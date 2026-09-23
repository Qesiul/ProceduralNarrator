using System;
using System.Collections.Generic;
using System.Globalization;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Blackboard;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Tension;
using ProceduralNarrator.Core.Util;
using ProceduralNarrator.Integration.Arcs;
using ProceduralNarrator.Integration.Defs;
using ProceduralNarrator.Integration.Incidents;
using ProceduralNarrator.Integration.Persistence;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration.Storyteller
{
    /// <summary>
    /// Warstwa integracji (sekcja 5.1) - jedyny punkt styku rdzenia z API gry.
    /// Gra cyklicznie prosi ten komponent o wydarzenia; my zwracamy te ZLOZONE
    /// przez rdzen z klockow, zamiast wybierac gotowce z puli.
    ///
    /// KOLEJNOSC TURY (stan po polerowaniu etapu 4):
    ///   straznik zegara gry (wywolania spoza DoSingleTick nic nie robia)
    ///   -> bramka MTB -> WorldSnapshot (zamrozony raz)
    ///   -> TurnPlanner (Core): napiecie -> intencja + docelowa moc -> regula kryzysu -> DecisionContext
    ///   -> CandidateGenerator (cala przestrzen wariantow w granicach budzetu ocen)
    ///   -> dokladne sito (filtr trudnosci + prog punktow zagrozenia) PRZED scoringiem
    ///   -> UtilityScorer.ScoreAll + ScorePass (DOKLADNIE RAZ na ture)
    ///   -> TurnRunner (Core): faza 0 (prewerifikacja czola), brama raz na ture, rundy wyboru;
    ///      odmowa silnika OZNACZA cala akcje (zakres payloadu), nie usuwa kandydatow z listy
    ///   -> log czytelny (JEDEN komunikat na ture) + linia [PN-DATA]
    ///   -> RecordPass albo RecordEvent - zapis do historii PRZED yield return.
    ///
    /// Stare pole MaxCompositionAttempts ZNIKLO w kroku 3: poprzednia petla losowala kolejne
    /// kompozycje z nowego ziarna i przyjmowala pierwsza, ktora gra wpuscila - nie porownywala
    /// kandydatow ze soba w ogole.
    /// </summary>
    public class StorytellerComp_Generative : StorytellerComp
    {
        private const float TicksPerInterval = 1000f;
        private const float TicksPerDay = 60000f;

        /// <summary>
        /// Przesuniecie ziarna strumienia WYBORU wzgledem strumienia GENEROWANIA kandydatow
        /// (liczba pierwsza, zeby oba strumienie nie wpadaly w ten sam cykl).
        ///
        /// Dwa osobne strumienie sa wymogiem odtwarzalnosci ewaluacji: gdyby dzielily jeden
        /// generator, kazda zmiana liczby losowan po stronie generowania kandydatow (a ta zalezy
        /// od rozmiaru katalogu i od budzetu) przesuwalaby caly dalszy strumien i dwie serie
        /// rozniace sie jednym klockiem przestalyby byc porownywalne.
        /// </summary>
        private const int SelectionSeedSalt = 104729;

        private EventComposer composer;
        private CandidateGenerator generator;
        private UtilityScorer scorer;
        private SelectionPolicy policy;

        /// <summary>
        /// Awaryjna pamiec lokalna. Uzywana WYLACZNIE wtedy, gdy nie ma NarratorMemoryComponent -
        /// czyli w sytuacji, ktora nie powinna zajsc, bo Game.FillComponents() tworzy go przez
        /// refleksje w kazdej grze. Narrator dziala wtedy dalej, tylko bez trwalosci.
        ///
        /// Degradacja jest swiadoma: brak pamieci przez jedna sesje jest mniej szkodliwy niz
        /// wyjatek co 1000 tickow. Zeby jednak nie byla CICHA, towarzyszy jej jednorazowy Error -
        /// patrz ostrzezonoOBrakuKomponentu.
        /// </summary>
        private readonly Dictionary<int, EventHistory> historieAwaryjne = new Dictionary<int, EventHistory>();

        private bool ostrzezonoOBrakuKomponentu;

        /// <summary>
        /// Pamiec dla danej mapy. Od kroku 6 comp jej NIE POSIADA - tylko o nia pyta.
        ///
        /// Wlascicielem jest NarratorMemoryComponent, bo ten comp nie przezywa wczytania:
        /// Storyteller.ExposeData w fazie ResolvingCrossRefs wola InitializeStorytellerComps(),
        /// a ta robi Activator.CreateInstance(compClass), czyli buduje comp od zera z Defa.
        /// Kazde pole instancyjne przepada przy kazdym wczytaniu i przy kazdej zmianie narratora.
        ///
        /// Referencji do komponentu CELOWO NIE CACHUJEMY. GetComponent to skan liniowy listy,
        /// ale wolany raz na 1000 tickow jest darmowy, a cache przetrwalby wyjscie do menu
        /// i wczytanie innego zapisu - czyli wskazywalby na pamiec CUDZEJ rozgrywki.
        ///
        /// Rozdzial per mapa (klucz Map.uniqueID) i jego uzasadnienie mieszkaja teraz razem
        /// z pamiecia, w NarratorMemoryComponent.
        /// </summary>
        private EventHistory HistoryFor(Map map)
        {
            int klucz = map != null ? map.uniqueID : -1;

            NarratorMemoryComponent pamiec = Current.Game == null
                ? null
                : Current.Game.GetComponent<NarratorMemoryComponent>();

            if (pamiec != null)
            {
                return pamiec.HistoryFor(klucz);
            }

            if (!ostrzezonoOBrakuKomponentu)
            {
                ostrzezonoOBrakuKomponentu = true;
                PNLog.Error("BRAK NarratorMemoryComponent - pamiec narratora NIE PRZEZYJE zapisu gry. "
                            + "Szukaj w Player.log wpisu 'Could not instantiate a GameComponent of type "
                            + "ProceduralNarrator.Integration.Persistence.NarratorMemoryComponent' - najczestsza "
                            + "przyczyna to brak publicznego konstruktora przyjmujacego Game. "
                            + "Narrator dziala dalej na pamieci lokalnej, ktora ginie razem z procesem.");
            }

            EventHistory h;
            if (!historieAwaryjne.TryGetValue(klucz, out h))
            {
                h = new EventHistory();
                historieAwaryjne[klucz] = h;
            }
            return h;
        }

        /// <summary>
        /// Przebieg tury. Zyje w Core/, wiec walidator offline WYKONUJE ta petle, zamiast ja
        /// odwzorowywac - patrz uzasadnienie w TurnRunner. Nie zalezy od profilu (polityka,
        /// parametry PASS i budzet rund sa wspolne), wiec powstaje razem z reszta stalego runtime.
        /// </summary>
        private TurnRunner turnRunner;

        /// <summary>
        /// Odciski pytan zadanych silnikowi W BIEZACYM TICKU, kluczowane payloadem.
        /// Zyje na COMPIE, nie w turze, bo cache CanFireNowSub jest wspolny dla wszystkich tur
        /// w ticku - a przy wiecej niz jednej kolonii tur w ticku jest wiecej niz jedna.
        /// </summary>
        private readonly Dictionary<string, string> rejestrWerdyktow = new Dictionary<string, string>();

        /// <summary>Tick, ktorego dotyczy rejestrWerdyktow. Zmiana ticku czysci rejestr.</summary>
        private int rejestrTick = -1;

        // ---------------------------------------------------------------- STRAZNIK ZEGARA GRY
        // Wykrywa wywolania MakeIntervalIncidents spoza zegara gry - czyli z waniliowych narzedzi
        // debugowych (Future incidents i pokrewne). Szczegoly przy CzyWywolanieZewnetrzne.

        /// <summary>Klatka Unity i tick poprzedniego wywolania (kazdego, takze odrzuconego).</summary>
        private int straznikKlatka = -1;
        private int straznikTick = int.MinValue;

        /// <summary>Klatka uznana za zewnetrzna - kazde kolejne wywolanie w niej tez jest zewnetrzne.</summary>
        private int klatkaZewnetrzna = -1;

        /// <summary>Ostatni PRAWDZIWY interwal per mapa (Map.uniqueID -> tick).</summary>
        private readonly Dictionary<int, int> ostatniInterwalMapy = new Dictionary<int, int>();

        /// <summary>
        /// Wymusza przebudowe scorera i krzywej przy nastepnej turze. Wola to eksperyment po
        /// przywroceniu stanu: w trakcie eksperymentu linie "Profil narratora aktywny" i "Scoring:"
        /// ida do pliku symulacji, a przy ramieniu kontrolnym z profilem gry przebudowy po powrocie
        /// by nie bylo - Player.log zostalby bez opisu aktywnego profilu.
        /// </summary>
        internal void InvalidateProfileRuntime()
        {
            activeProfileId = null;
        }

        /// <summary>
        /// Czysci rejestr werdyktow i stan straznika zegara. Wola go wlasny eksperyment
        /// symulacyjny: na starcie kazdego ramienia (rejestr z poprzedniego ramienia nie moze
        /// odrzucac pytan nowego) i po przywroceniu stanu gry.
        /// </summary>
        /// <summary>Stan bezpiecznikow warstw (luki, fakty) - do zapamietania i przywrocenia przez symulator.</summary>
        internal struct Bezpieczniki
        {
            public bool Luki;
            public bool Fakty;
        }

        /// <summary>
        /// Symulator uzywa TEGO SAMEGO obiektu compa co gra, a bezpieczniki sa jego polami - bez
        /// zapamietania i przywrocenia wyjatek w jednym ramieniu wylaczalby warstwe w nastepnych
        /// ramionach i w prawdziwej grze po eksperymencie (przeglad S6).
        /// </summary>
        internal Bezpieczniki OdczytajBezpieczniki()
        {
            return new Bezpieczniki { Luki = arcsBroken, Fakty = faktyBroken };
        }

        internal void UstawBezpieczniki(Bezpieczniki b)
        {
            arcsBroken = b.Luki;
            faktyBroken = b.Fakty;
        }

        internal void ResetTickState()
        {
            rejestrWerdyktow.Clear();
            rejestrTick = -1;
            straznikKlatka = -1;
            straznikTick = int.MinValue;
            klatkaZewnetrzna = -1;
        }

        private bool runtimeReady;

        /// <summary>
        /// Narrator zostal wylaczony po nieudanej inicjalizacji. Osobna flaga od runtimeReady,
        /// bo to osobna sprawa: runtimeReady znaczy "czesc niezalezna od profilu jest zbudowana",
        /// a ta znaczy "nie probuj wiecej i nie spamuj logu". Zlozenie obu w jedna zmienna bylo
        /// zrodlem bledu, ktory ta zmiana naprawia.
        /// </summary>
        private bool runtimeBroken;

        /// <summary>
        /// Krzywa dramaturgiczna i profil, z ktorego powstala. Budowane leniwie i przebudowywane
        /// TYLKO wtedy, gdy zmieni sie profil - czyli w praktyce raz na rozgrywke, a przy seriach
        /// kontrolnych takze po wymuszeniu profilu akcja debugowa.
        ///
        /// Scorer jest tu razem z nimi, bo wagi czynnikow naleza do profilu: gdyby zostal
        /// zbudowany raz w EnsureRuntime, wymuszenie profilu zmienialoby krzywa, ale NIE wagi,
        /// i narrator dzialalby na hybrydzie dwoch osobowosci - roznicy nie dalo by sie wtedy
        /// przypisac zadnej z nich.
        /// </summary>
        private TensionModel tensionModel;

        private string activeProfileId;

        // ---------------------------------------------------------------- LUKI NARRACYJNE (krok 5)

        /// <summary>
        /// Silnik automatow lukow - wspolny dla sesji (katalog z Defow + blok &lt;arcs&gt;), budowany
        /// leniwie przy pierwszym wywolaniu. Stan lukow NIE zyje tutaj, tylko w NarratorMemoryComponent
        /// (ksiega per mapa) - comp nie przezywa wczytania, tak samo jak przy historii.
        /// </summary>
        private ArcDirector arcDirector;

        /// <summary>Warstwa lukow rzucila wyjatek - wylaczona do konca sesji (raport raz, bez spamu logu).</summary>
        private bool arcsBroken;

        /// <summary>
        /// Ramie eksperymentu "bez lukow": warstwa lukow nieobecna w turze (fokus null, zero obserwacji
        /// i potwierdzen po stronie lukow). Mierzy efekt drugiego rzedu lukow na tempo. Ustawia
        /// i zdejmuje wylacznie FutureIncidentsExperiment.
        /// </summary>
        internal bool ArcsDisabledForArm;

        private bool LukiAktywne
        {
            get { return arcDirector != null && !arcsBroken && !ArcsDisabledForArm && arcDirector.Params.enabled; }
        }

        private StorytellerCompProperties_Generative Props
        {
            get { return (StorytellerCompProperties_Generative)props; }
        }

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            var map = target as Map;
            if (map == null)
            {
                yield break;
            }

            // STRAZNIK ZEGARA - PRZED bramka MTB, bo ta juz zuzywa Rand gry.
            // Wlasny eksperyment (PNLog.InExperiment) przestawia zegar swiadomie i ma wlasny
            // zrzut stanu, wiec straznik go przepuszcza.
            if (!PNLog.InExperiment && CzyWywolanieZewnetrzne(map))
            {
                yield break;
            }

            // LUKI - OBSERWACJA PRZY KAZDYM WYWOLANIU (krok 5, decyzja autora nr 5), PRZED bramka
            // MTB: straznicy (reakcja na gracza) i limity czasu nie moga czekac na ture decyzji,
            // ktora przychodzi srednio co 2.5 dnia. Bez losowosci (kanarek w ArcTick).
            ArcTick(map);

            // FAKTY (krok 6) - JEDYNE miejsce, w ktorym slady zdarzen trafiaja do pamieci. PO lukach:
            // spoznione potwierdzenie luku buduje swiezy snapshot, a ten nie moze widziec sladu
            // zdarzenia, ktore wlasnie potwierdza (warunki widza swiat z poczatku tury - decyzja
            // autora). PRZED bramka MTB: fakty maja byc widoczne w snapshocie ewentualnej decyzji
            // w tym samym wywolaniu. Niezalezne od lukow i bez losowosci.
            FactTick(map);

            // Bramka tempa. mtbDays jest wyprowadzone z czestotliwosci podmienionych compow
            // Cassandry (0.14 + 0.06 + 0.21 = 0.40/dzien), zeby budzet wydarzen byl porownywalny.
            //
            // KROK 4 CELOWO JEJ NIE RUSZYL. Wczesniejszy komentarz zapowiadal, ze krzywa
            // dramaturgiczna zastapi te stala - odrzucone po policzeniu konsekwencji: ruchome
            // mtbDays kasuje parytet budzetu wobec Cassandry, czyli ten sam argument
            // metodologiczny, na ktorym stoi caly rozdzial o ewaluacji porownawczej.
            // Zamiast tego mtbDays zostaje SUFITEM, a krzywa decyduje, ile z niego narrator
            // zuzyje - przez brame PASS (PassScoringParams.weightIntentAlignment). Tempo jest
            // wiec zmienne, a porownanie nadal dotyczy tresci, a nie liczby zdarzen.
            if (!Rand.MTBEventOccurs(Props.mtbDays, TicksPerDay, TicksPerInterval))
            {
                yield break;
            }

            // STRAZNIK SCIEZKI DECYZYJNEJ - cala inicjalizacja siedzi WEWNATRZ TryEnsureRuntime,
            // zeby wyjatek z konstruktorow nie mial jak wyleciec z MakeIntervalIncidents.
            // Powod, dla ktorego to osobna metoda, a nie try/catch tutaj: C# nie pozwala na
            // yield return w bloku try z klauzula catch, wiec opakowanie inicjalizacji na
            // miejscu wymusiloby przebudowe calej metody-iteratora.
            if (!TryEnsureRuntime())
            {
                yield break;
            }

            int tick = CurrentTick();
            float gameDay = tick / TicksPerDay;

            // Stan swiata zamrazamy RAZ na cala ture. Wszystkie rundy oceniaja ten sam kontekst,
            // wiec ranking jest wewnetrznie spojny i da sie go w calosci odtworzyc z logu.
            EventHistory history = HistoryFor(map);

            // Pamiec narratora musi byc znana PRZED zamrozeniem snapshotu (krok 6): to snapshot
            // niesie ja dalej w postaci kanonicznej i tylko przez niego widza ja warunki.
            // Luki (krok 5): fokus tury z ksiegi mapy i intencji PO kryzysie. Warstwa nieobecna
            // (wylaczona, uszkodzona albo ramie "bez lukow") = fokus null = tura v6.
            NarratorMemoryComponent pamiecLukow = LukiAktywne && Current.Game != null
                ? Current.Game.GetComponent<NarratorMemoryComponent>()
                : null;
            ArcLedger ksiega = pamiecLukow == null ? null : pamiecLukow.LedgerFor(map.uniqueID);

            // Ksiega faktow (krok 6) NIE zalezy od lukow - fakty sa czescia blackboardu.
            FactLedger fakty = KsiegaFaktow(map);

            // Stan swiata zamrazamy RAZ na cala ture. Wszystkie rundy oceniaja ten sam kontekst,
            // wiec ranking jest wewnetrznie spojny i da sie go w calosci odtworzyc z logu.
            WorldSnapshot snapshot = WorldSnapshotBuilder.Build(map, history, ksiega, fakty, gameDay);

            // WARSTWA PLANOWANIA: napiecie -> intencja + docelowa moc -> regula kryzysu skrajnego
            // -> kontekst decyzji. Caly lancuch zyje w Core (TurnPlanner), wiec walidator offline
            // sprawdza go od snapshotu do DecisionContext.ExtremeCrisis - tutaj tylko jedno
            // wywolanie, bez wlasnej logiki (precedens: TurnRunner).
            // Uzytecznosc frakcji do wiazania (krok 5, S5): pelny waniliowy filtr zrodla napadu przy
            // punktach tury razy mnoznik mocy kandydata - te same punkty bazowe co sito ponizej.
            IFactionUsability uzytecznoscFrakcji = new FactionUsability(map, snapshot.ThreatPoints);
            TurnPlan plan = TurnPlanner.Plan(tensionModel, Props.crisis, history, snapshot, gameDay,
                                             ksiega == null ? null : arcDirector, ksiega, uzytecznoscFrakcji);
            TensionReading napiecie = plan.Tension;
            CrisisReading kryzys = plan.Crisis;
            IntentDecision zamiar = plan.Intent;
            DecisionContext context = plan.Context;
            EventRecipe recipe = BuildRecipe();

            // LOG CZYTELNY TURY ZBIERANY DO JEDNEGO KOMUNIKATU. Verse.Log wylacza sie po 1000
            // KOMUNIKATACH calego procesu (nie linii) - tura wypisywana linia po linii zuzywala
            // ok. 19 komunikatow, czyli sufit ok. 50 decyzji na sesje. Szczegoly w PNLog.
            var czytelne = new List<string>(24);
            czytelne.Add("Krzywa dramaturgiczna: " + napiecie.Trace + " -> " + zamiar.Trace
                         + (kryzys.Extreme ? string.Empty : " | " + kryzys.Trace));

            IRandomSource rngGen = new SeededRandom(tick);
            IRandomSource rngSel = new SeededRandom(unchecked(tick + SelectionSeedSalt));

            CandidateSet kandydaci = generator.Generate(recipe, snapshot, rngGen, Props.candidateBudget);
            if (kandydaci.Truncated)
            {
                // Dzis nieosiagalne (najwieksza akcja ma kilkadziesiat wariantow wobec TraversalCap
                // 20000 - dokladna liczbe pilnuje asercja walidatora, nie ten komentarz), ale flaga
                // bez konsumenta jest flaga martwa i nikt nie zauwazylby dnia, w ktorym zastrzeli.
                // Wtedy TotalVariants jest DOLNYM ograniczeniem, a nie wartoscia.
                PNLog.Warn("Enumeracja wariantow uderzyla w TraversalCap - pole przestrzen w [PN-DATA] "
                           + "jest dolnym ograniczeniem, a pokrycie gornym. " + kandydaci.Trace);
            }

            // DOKLADNE SITO PRZED SCORINGIEM: usuwamy kandydatow, ktorych gra i tak odrzuci
            // przez prog punktow zagrozenia. Cond_MinThreatPoints jest tylko sitem ZGRUBNYM,
            // bo warunek twardy dziala na poziomie klocka i nie zna koncowej intensywnosci
            // kompozycji - a to ona decyduje o mnozniku punktow. Szczegoly w IncidentParmsBuilder.
            //
            // Robimy to PRZED ocenianiem, bo kandydat nieosiagalny w puli moze wygrac runde,
            // zostac odrzucony przez CanFireNow i - nie trafiwszy do historii - wrocic na czolo
            // rankingu z maksymalna swiezoscia. To jest petla, ktora zjadala 64% rund.
            // NOWA LISTA, a nie filtrowanie w miejscu: kandydaci.Candidates jest zrodlem
            // kolumny "wygenerowanych", wiec skrocenie jej tutaj zabieraloby logowi jedyny
            // slad po dzialaniu sita.
            // Dwie przyczyny usuniecia, DWIE osobne linie logu. Linia "Sito punktow zagrozenia"
            // jest kryterium dlugu weryfikacyjnego 1 z CLAUDE.md - gdyby niosla takze filtr
            // trudnosci, na Peaceful (gdzie sito punktow jest dzis NIEOSIAGALNE: oba payloady
            // z progiem to ThreatBig, a filtr trudnosci odcina je wczesniej) potwierdzalaby dlug
            // zawsze falszywie. W [PN-DATA] obie przyczyny skladaja sie na roznice
            // wygenerowanych - kandydatow; rozdziela je wylacznie ten log czytelny.
            int odfiltrowanych;
            int odfiltrowanychTrudnosc;
            List<ComposedEvent> doOceny = IncidentParmsBuilder.OdfiltrujNieosiagalne(
                kandydaci.Candidates, snapshot.ThreatPoints, IncidentParmsBuilder.BigThreatsAllowedNow(),
                out odfiltrowanych, out odfiltrowanychTrudnosc);
            if (odfiltrowanychTrudnosc > 0)
            {
                czytelne.Add("Filtr trudnosci: usunieto "
                             + odfiltrowanychTrudnosc.ToString(CultureInfo.InvariantCulture)
                             + " kandydatow ThreatBig (allowBigThreats=false albo okno po metalowym piekle).");
            }
            if (odfiltrowanych - odfiltrowanychTrudnosc > 0)
            {
                czytelne.Add("Sito punktow zagrozenia: usunieto "
                             + (odfiltrowanych - odfiltrowanychTrudnosc).ToString(CultureInfo.InvariantCulture)
                             + " kandydatow nieosiagalnych przy "
                             + snapshot.ThreatPoints.ToString("0", CultureInfo.InvariantCulture)
                             + " punktach bazowych.");
            }

            // WYMOG: ScoreAll i ScorePass wolane DOKLADNIE RAZ na decyzje. Kolejne rundy petli
            // powtarzaja wylacznie SelectionPolicy.Select na juz ocenionej liscie. Powtorne
            // ocenianie (a) przeliczyloby czynniki tyle razy, ile rund, (b) nadpisaloby Factors,
            // gubiac slad tej rundy, ktora faktycznie zakonczyla decyzje. PASS tez nie jest
            // przeliczany miedzy rundami - gestosc zdarzen nie zmienia sie w obrebie jednej tury.
            List<ScoredCandidate> ocenieni = scorer.ScoreAll(doOceny, context);
            ScoredCandidate pass = scorer.ScorePass(context);

            // Kopia listy jest dzis SZCZATKOWA i zostaje wylacznie ochronnie: TurnRunner nie skraca
            // listy (odrzuconych OZNACZA, bo musza zostac w mianowniku i w rankingu), a flagi
            // i tak siedza na wspoldzielonych obiektach ScoredCandidate. Logger czyta ranking
            // z decyzji i statystyk tury, nie z tej listy. (Dawny komentarz "TurnRunner usuwa
            // z niej odrzuconych" opisywal ture sprzed prewerifikacji czola.)
            var pula = new List<ScoredCandidate>(ocenieni);

            // AKCEPTOR - jedyne miejsce, w ktorym przebieg tury dotyka API gry.
            //
            // Rdzen pyta "czy ten kandydat moze teraz wypalic?", a odpowiedz wymaga trzech
            // rzeczy, ktorych Core nie zna: rozwiazania IncidentDef, sprawdzenia tagu celu
            // i CanFireNow. Gotowy incydent z parametrami zostaje TUTAJ, w zmiennych lokalnych -
            // rdzen go nie transportuje, bo nie mialby jak opisac tych typow bez wciagania
            // API gry do Core.
            IncidentDef incydent = null;
            IncidentParms parms = null;

            CandidateAcceptor akceptor = delegate(ScoredCandidate kandydat, bool pytajSilnik,
                                                 out string powod)
            {
                powod = null;

                // KAZDE PYTANIE UNIEWAZNIA POPRZEDNIA ODPOWIEDZ. Odkad TurnRunner weryfikuje
                // czolo rankingu PRZED brama (faza 0), akceptor bywa wolany dla kandydata, ktory
                // tej tury nie wygra. Bez tego zerowania przygotowany wtedy incydent zostawalby
                // w zmiennych i mogl zostac odpalony zamiast wlasciwego zwyciezcy.
                incydent = null;
                parms = null;

                ComposedEvent zdarzenie = kandydat.Event;

                IncidentDef kandydacki = DefDatabase<IncidentDef>.GetNamedSilentFail(zdarzenie.ActionPayload);
                if (kandydacki == null)
                {
                    // Audyt startowy (AuditActionPayloads) lapie to juz w menu glownym, wiec
                    // tutaj jest to druga siec bezpieczenstwa - na wypadek Defa dolozonego
                    // przez inny mod po naszym audycie.
                    PNLog.Error("Klocek akcji wskazuje na nieistniejacy IncidentDef: "
                                + zdarzenie.ActionPayload);
                    powod = zdarzenie.ActionPayload + ": brak IncidentDef";
                    return AcceptorVerdict.RefusedByGame;
                }

                if (!kandydacki.TargetAllowed(target))
                {
                    powod = kandydacki.defName + ": cel niedozwolony";
                    return AcceptorVerdict.RefusedByGame;
                }

                // REGULA GRY Z TryExecute (S6; dekompilacja 1.5.4063, IncidentWorker.cs:169): incydent
                // z requireColonistsPresent przy braku wolnych kolonistow na mapie NIC nie robi, ale zwraca
                // sukces - a Storyteller.TryFire wpisuje wtedy lastFireTicks, wiec potwierdzenie wykonania
                // uznaloby go za WYKONANY (falszywe fakty, Scigani z komunikatem o kapsule, ktorej nie bylo).
                // CanFireNow tej flagi nie czyta (Uchodzcy = IncidentWorker_GiveQuest przepuszcza, gdy ktos
                // zyje w karawanie). Odwzorowanie reguly silnika, nie switch po nazwie - obejmuje kazdy Def
                // z ta flaga (dzis tylko RefugeePodCrash). PRZED pytaniem o CanFireNow: nie zuzywa cache'u gry.
                if (kandydacki.requireColonistsPresent && map.mapPawns.FreeColonistsSpawnedCount == 0)
                {
                    powod = kandydacki.defName + ": brak wolnych kolonistow na mapie (requireColonistsPresent)";
                    return AcceptorVerdict.RefusedByGame;
                }

                // IncidentParms budowane LENIWIE, wylacznie dla zwyciezcy DANEJ RUNDY.
                // StorytellerUtility.DefaultParmsNow (pod spodem GenerateParms) liczy punkty
                // zagrozenia z bogactwa, kolonistow i krzywych adaptacji - zbudowanie parms dla
                // calego rankingu oznaczaloby dzis 84 takie wywolania na ture zamiast jednego.
                // Scoring nie dotyka IncidentParms w ogole: intensywnosc jest cecha kompozycji,
                // a punkty sa jej TLUMACZENIEM na mechanike, potrzebnym dopiero przy odpaleniu.
                // Tlumaczenie kompozycji na mechanike ma JEDEN dom - IncidentParmsBuilder.
                // Tam tez zapisane jest, ktore pola IncidentParms nasz katalog realnie honoruje,
                // a ktore sa dla niego bezczynne (zmierzone dekompilacja, nie zalozone).
                // FRAKCJA LUKU (krok 5): tylko dla kandydata dopasowanego przez oczekiwanie
                // sameFaction, przy frakcji uzytecznej dla jego mocy (ArcFocus.FactionToBind).
                string frakcjaLukuId = context.ArcFocus == null ? null : context.ArcFocus.FactionToBind(zdarzenie);
                Faction frakcjaLuku = ArcObservationBuilder.ResolveFaction(frakcjaLukuId);
                IncidentParms kandydackieParms = IncidentParmsBuilder.Apply(
                    GenerateParms(kandydacki.category, target), zdarzenie, Props.useComposedLetter, frakcjaLuku);

                // PYTAMY SILNIK TYLKO WTEDY, GDY RDZEN O TO PROSI.
                //
                // CanFireNow buforuje wynik CanFireNowSub na parze (IncidentDef, tick), a nasza
                // tura miesci sie w jednym ticku. Drugie pytanie o ten sam incydent zwrocilo by
                // wiec werdykt policzony dla parametrow innego wariantu - i podalo go jako swiezy.
                // Rdzen pilnuje, zeby o kazda akcje zapytac najwyzej raz; tutaj tylko honorujemy
                // te decyzje. Pelne wyprowadzenie: CandidateAcceptor w Core/Decision/TurnRunner.
                //
                // Przy pytajSilnik == false wykonujemy sama arytmetyke parametrow, ktora jest
                // deterministyczna i nie moze zawiesc - wiec brak pytania nie oslabia niczego
                // poza tym, co i tak byloby odpowiedzia zbuforowana.
                if (pytajSilnik)
                {
                    // REJESTR WERDYKTOW NA TICK - jedyna obrona przed cache'em silnika
                    // wspoldzielonym miedzy TURAMI.
                    //
                    // Cache CanFireNowSub jest kluczowany para (IncidentDef, tick) - bez celu
                    // i bez parametrow. Storyteller.MakeIncidentsForInterval(comp, targets)
                    // iteruje przy tym po WSZYSTKICH celach, a kazda kolonia gracza daje tag
                    // Map_PlayerHome - wiec przy dwoch koloniach nasz comp wykonuje w jednym
                    // ticku DWIE pelne tury. Druga startuje z czysta ksiegowoscia TurnRunnera
                    // i dostaje od gry werdykt policzony dla PIERWSZEJ mapy.
                    //
                    // Odtworzone: mapa B przyjmowala kandydata, dla ktorego CanFireNowSub nie
                    // policzono ani razu, a wiersz danych raportowal ture jako w pelni
                    // zweryfikowana. Wszystkie trzynascie naszych CanFireNowSub jest mapozalezne.
                    //
                    // Rejestr zyje na compie, a nie w turze, bo problem jest miedzyturowy.
                    string zakres = zdarzenie.ActionPayload ?? "?";
                    string odciskPytania = zakres + "@" + (target == null ? "?" : target.GetHashCode()
                                               .ToString(CultureInfo.InvariantCulture))
                                           + "#" + IncidentParmsBuilder.ExecutionKey(zdarzenie, frakcjaLukuId);

                    if (rejestrTick != tick)
                    {
                        rejestrTick = tick;
                        rejestrWerdyktow.Clear();
                    }

                    string poprzedniOdcisk;
                    if (rejestrWerdyktow.TryGetValue(zakres, out poprzedniOdcisk)
                        && string.CompareOrdinal(poprzedniOdcisk, odciskPytania) != 0)
                    {
                        // O ten payload pytano juz w tym ticku, przy innym celu albo innych
                        // parametrach. Silnik odda TAMTA odpowiedz. Nie podajemy jej dalej.
                        powod = kandydacki.defName + ": werdykt niedostepny (pytano w tym ticku o "
                                + poprzedniOdcisk + ")";
                        return AcceptorVerdict.Unanswerable;
                    }

                    rejestrWerdyktow[zakres] = odciskPytania;

                    if (!kandydacki.Worker.CanFireNow(kandydackieParms))
                    {
                        powod = kandydacki.defName + ": CanFireNow=false";
                        return AcceptorVerdict.RefusedByGame;
                    }
                }

                incydent = kandydacki;
                parms = kandydackieParms;
                return AcceptorVerdict.Accepted;
            };

            // Selektor klucza parametrow wykonania. Rdzen uzywa go, zeby wiedziec, ktore warianty
            // obejmuje jedno pytanie do gry (te o identycznych parametrach), a ktore trzeba odlozyc
            // do nastepnej tury, bo cache CanFireNowSub nie pozwoli ich sprawdzic. Implementacja
            // stoi tuz pod IncidentParmsBuilder.Apply i ma sie zmieniac razem z nia.
            ExecutionKeySelector kluczWykonania =
                delegate(ScoredCandidate kandydat)
                {
                    return kandydat == null ? null : IncidentParmsBuilder.ExecutionKey(
                        kandydat.Event, context.ArcFocus == null ? null : context.ArcFocus.FactionToBind(kandydat.Event));
                };

            // ZAKRES CACHE'U = SAM PAYLOAD. Cache silnika jest kluczowany IncidentDefem, a nie
            // naszym klockiem akcji, wiec dwa rozne klocki o tym samym payloadzie dziela jeden
            // wpis. Dzis katalog takiego duplikatu nie ma; nic go jednak nie zabrania, a skutkiem
            // byloby przebicie obu warstw obrony naraz.
            ScopeKeySelector zakresCache =
                delegate(ScoredCandidate kandydat)
                {
                    if (kandydat == null || kandydat.Event == null) return null;
                    return kandydat.Event.ActionPayload;
                };

            TurnResult tura = turnRunner.Run(context, pula, pass, rngSel,
                                             scorer.LastPassDensity, akceptor,
                                             kluczWykonania, zakresCache);

            // Rdzen nie zna Verse.Log, wiec zbiera stany "to nie powinno sie zdarzyc" do listy.
            // Wypisujemy je jako BLEDY, bo kazdy oznacza usterke okablowania albo padniete
            // zamrozenie bramy - nie sa to informacje diagnostyczne.
            for (int i = 0; i < tura.Warnings.Count; i++)
            {
                PNLog.Error(tura.Warnings[i]);
            }

            NarratorDecision decyzja = tura.Decision;
            List<string> odmowy = tura.Refusals;
            int rundy = tura.Rounds;

            // Wykonalnosc wywnioskowana z rodzenstwa - do kolumny badawczej. Rdzen ustala fakt,
            // integracja go przenosi: NarratorDecision jedzie do loggera, TurnResult nie.
            decyzja.FeasibilityInferred = tura.FeasibilityInferred;
            decyzja.DeferredVariants = tura.DeferredVariants;
            decyzja.UnanswerableScopes = tura.UnanswerableScopes;

            // BRAMKA EMISJI IDZIE PO tura.Accepted, A NIE PO "incydent != null".
            //
            // Do czasu prewerifikacji czola oba warunki znaczyly to samo, bo akceptor byl wolany
            // wylacznie dla zwyciezcy rundy. Faza 0 to rozerwala: pyta o czolo PRZED brama, wiec
            // gdy brama wybierze cisze, w zmiennych siedzi gotowy incydent kandydata, ktory
            // niczego nie wygral. Sama bramka "incydent != null" odpalilaby go i PASS przestalby
            // istniec - awaria cicha, bo log pokazywalby poprawna decyzje o ciszy obok
            // wypalonego zdarzenia.
            //
            // tura.Accepted jest podnoszone WYLACZNIE bezposrednio po przyjeciu zwyciezcy, wiec
            // przy Accepted == true stan akceptora na pewno opisuje zwyciezce. Przy false nie
            // ma czego odpalac i zmienne czyscimy, zeby ten sam stan widzial takze logger.
            if (!tura.Accepted)
            {
                incydent = null;
                parms = null;
            }

            // KANAREK NA RELACJI, KTORA JEST JEDYNYM SLADEM SITA W DANYCH.
            //
            // Dokumentacja IncidentParmsBuilder obiecuje, ze w linii [PN-DATA] zachodzi
            //     wygenerowanych - kandydatow == liczba odfiltrowanych
            // i to jest powod, dla ktorego sito NIE dostalo wlasnej kolumny ani podbicia wersji
            // formatu. Obietnica bez straznika juz raz sie zestarzala po cichu: filtr mutowal
            // liste, z ktorej logger czyta pierwszy skladnik, wiec roznica wychodzila zawsze zero.
            //
            // Straznik porownuje to, co NAPRAWDE trafi do pliku (CountScored ze statystyk tury),
            // a nie lokalna dlugosc listy - lokalne porownanie bylo by tautologia i przespaloby
            // dokladnie ten blad, ktory tu wystapil.
            TurnStats statystykiTury = decyzja.TurnStats;
            if (statystykiTury != null)
            {
                int zalogowanaRoznica = (kandydaci.Candidates == null ? 0 : kandydaci.Candidates.Count)
                                        - statystykiTury.CountScored;
                if (zalogowanaRoznica != odfiltrowanych)
                {
                    PNLog.Error("Niezmiennik sita zlamany: wygenerowanych - kandydatow = "
                                + zalogowanaRoznica.ToString(CultureInfo.InvariantCulture)
                                + ", a sito usunelo "
                                + odfiltrowanych.ToString(CultureInfo.InvariantCulture)
                                + ". Mozliwe przyczyny sa DWIE i nalezy je rozroznic: albo miedzy "
                                + "generowaniem a scoringiem doszedl kolejny mechanizm usuwajacy "
                                + "kandydatow (wtedy roznica jest za duza i sito przestalo byc "
                                + "jedynym wyjasnieniem), albo generator wypuscil pustego kandydata "
                                + "- UtilityScorer.ScoreAll takie pomija. W obu przypadkach kolumna "
                                + "'kandydatow' przestala opisywac to, co glosi, wiec pochodne "
                                + "metryki (udzial weta, progu i pasma) maja zly mianownik.");
                }
            }

            // Log PRZED zapisem do historii: kontekst wypisany w logu ma opisywac stan, NA KTORYM
            // decyzja zapadla, a nie stan juz o nia powiekszony.
            LogDecision(czytelne, context, kandydaci, decyzja, incydent, parms, odmowy, rundy, map, tick,
                        tura.PreGateRefusals, tura.AcceptorCalls, napiecie, kryzys);

            if (incydent == null)
            {
                // PASS jest pelnoprawna decyzja i tez przesuwa czas narracyjny: licznik decyzji
                // rosnie (wiec wszystkie zdarzenia w historii sie odswiezaja), ale wpis do bufora
                // NIE powstaje - cisza nie jest zdarzeniem i zatrulaby rytm oraz gestosc.
                // O tym, czy cisza jest SWIADOMA (podnosi serie), rozstrzyga rdzen
                // (TurnResult.DeliberateSilence), a nie ta warstwa - bo to regula ksiegowania,
                // a walidator widzi tylko Core. Swiadoma jest wylacznie cisza Competitive poza
                // kryzysem skrajnym; cisza techniczna i cisza w kryzysie zostawiaja licznik bez
                // zmiany. DecisionCount rosnie zawsze, wiec starzenie swiezosci jest nietkniete.
                history.RecordPass(gameDay, tick, tura.DeliberateSilence);
                yield break;
            }

            // Historia dotykana DOKLADNIE RAZ w turze i PRZED yield return.
            // MakeIntervalIncidents jest iteratorem: kod za yield return wykona sie dopiero przy
            // kolejnym MoveNext, do ktorego konsument nie jest zobowiazany. Zapis po yield return
            // bylby zakladem o cudza petle, a przegrana objawia sie pusta historia i czynnikiem
            // swiezosci zamrozonym na wartosci neutralnej - czyli cicho.
            // Do zapisania w pracy: historia rejestruje INTENCJE narratora, nie potwierdzone
            // wykonanie (TryExecute moze pozniej zwrocic false). Wykonanie jest od kroku 5 MIERZONE
            // bez Harmony (lastFireTicks przed/po yield, [PN-EXEC]) - i od niego zaleza luki i fakty;
            // sama historia zostaje zapisem intencji (decyzja autora nr 6, dlug 7).
            if (!history.RecordEvent(decyzja.Winner.Event, gameDay, tick))
            {
                PNLog.Error("Nie udalo sie dopisac zdarzenia do historii - czynniki swiezosci "
                            + "i kontrastu strace ta ture. Kandydat: " + decyzja.Winner.Label);
            }

            // POTWIERDZENIE WYKONANIA (krok 5, decyzja autora nr 6) - bez Harmony. Wanilia wpisuje
            // tick do StoryState.lastFireTicks[def] WYLACZNIE po udanym CanFireNow i TryExecute
            // (Storyteller.TryFire -> Notify_IncidentFired), a nasz kod po yield return wykonuje
            // sie PO TryFire w tym samym ticku (leniwy lancuch iteratorow). Odczyt PRZED zapisujemy
            // takze w pamieci (Pending), gdyby iterator nie zostal wznowiony - wtedy potwierdzenie
            // domknie ArcTick przy nastepnym wywolaniu.
            int ostatniPrzed = OstatnieOdpalenie(map, incydent);
            if (ksiega != null)
            {
                ksiega.Pending = new PendingExecution
                {
                    Tick = tick,
                    GameDay = gameDay,
                    DecisionIndex = context.DecisionIndex,
                    IncidentDefName = incydent.defName,
                    // FactionId = frakcja, ktora MY ustawilismy (albo null) - do kolumny frakcjaZwiazana;
                    // faktyczna frakcje po TryExecute czyta PotwierdzWykonanie.
                    Event = ArcEventView.FromCandidate(decyzja.Winner.Event,
                        context.ArcFocus == null ? null : context.ArcFocus.FactionToBind(decyzja.Winner.Event)),
                    LastFireBefore = ostatniPrzed
                };
                // Pamiec decyzji (S6): pola snapshotu zalezne od historii, zanim RecordEvent je zmieni
                // (RecordEvent jest wyzej, ale snapshot zamrozono przed nim). Sciezka spozniona nalozy je
                // na swoj snapshot - warunki startu luku widza wtedy ten sam swiat co tutaj.
                ksiega.Pending.CaptureDecisionMemory(snapshot);
            }

            // FAKTY ZDARZENIA DO KOLEJKI (krok 6) - przed yield, z tego samego powodu co Pending
            // lukow: kod za yield moze sie nie wykonac. Do pamieci trafia dopiero po rozstrzygnieciu
            // wykonania, na poczatku nastepnego wywolania (FactTick).
            ZakolejkujFakty(map, fakty, decyzja.Winner.Event, tick, gameDay, context.DecisionIndex,
                            incydent.defName, ostatniPrzed);

            yield return new FiringIncident(incydent, this, parms);

            PotwierdzWykonanie(map, ksiega, history, snapshot, incydent, parms, ostatniPrzed, tick,
                               context.DecisionIndex, decyzja.Winner);
        }

        /// <summary>lastFireTicks[def] tej mapy albo -1, gdy incydent nigdy nie odpalal.</summary>
        private static int OstatnieOdpalenie(Map map, IncidentDef def)
        {
            int t;
            if (map != null && def != null && map.StoryState != null && map.StoryState.lastFireTicks != null
                && map.StoryState.lastFireTicks.TryGetValue(def, out t))
            {
                return t;
            }
            return -1;
        }

        /// <summary>
        /// Klasyfikacja wykonania, linia [PN-EXEC] (zawsze - odsetek wykonan to pomiar dlugu 7)
        /// i krok automatu lukow. Metoda zwykla, nie iterator: wyjatek tutaj nie moze zabic petli
        /// storytellera, wiec jest lapany i wylacza warstwe lukow (raport raz).
        /// </summary>
        private void PotwierdzWykonanie(Map map, ArcLedger ksiega, EventHistory history, WorldSnapshot snapshot,
                                        IncidentDef incydent, IncidentParms parms, int ostatniPrzed, int tick,
                                        int decyzjaNr, ScoredCandidate zwyciezca)
        {
            try
            {
                int ostatniPo = OstatnieOdpalenie(map, incydent);
                ExecStatus status = ExecutionConfirmation.Classify(ostatniPrzed, ostatniPo, tick, PNLog.InExperiment);

                // Faktyczna frakcja zdarzenia: nasz obiekt parms PO TryExecute (worker napadu
                // rozwiazuje ja w miejscu). Pewniejsze niz StoryState.lastRaidFaction, ktore
                // nadpisuje kazdy napad na mapie.
                string frakcja = parms == null ? null : ArcObservationBuilder.FactionId(parms.faction);
                string zrodloFrakcji = frakcja == null ? "-" : "parms";
                PendingExecution czekajace = ksiega == null ? null : ksiega.Pending;
                string zwiazana = czekajace == null || czekajace.Event == null ? null : czekajace.Event.FactionId;

                // EMULACJA FRAKCJI W SYMULATORZE (decyzja autora R4-4): bez TryExecute worker napadu
                // nie rozwiazuje frakcji, wiec luk wiazacy frakcje nigdy by sie tam nie otworzyl.
                // Tylko dla akcji niosacej frakcje, tylko w eksperymencie, jawnie oznaczone.
                if (frakcja == null && status == ExecStatus.Simulated && czekajace != null && czekajace.Event != null
                    && czekajace.Event.CarriesFaction)
                {
                    frakcja = ArcObservationBuilder.FactionId(
                        FactionBinding.EmulatedRaidFaction(map, parms == null ? 0f : parms.points));
                    zrodloFrakcji = frakcja == null ? "-" : "emulacja";
                }

                PNLog.Exec(map == null ? -1 : map.uniqueID, decyzjaNr, incydent == null ? null : incydent.defName,
                           zwyciezca == null ? null : zwyciezca.SortKey, ExecutionConfirmation.Label(status),
                           ostatniPrzed, ostatniPo, frakcja, zwiazana, tick, zrodloFrakcji, tick);

                // Fakty (krok 6): ten sam werdykt co luki, ale WLASNY try/catch wewnatrz metody -
                // wyjatek po stronie faktow nie moze wylaczyc lukow, a luki wylaczone nie moga
                // zatrzymac faktow (dlatego to wywolanie stoi PRZED wczesnym powrotem ponizej).
                PotwierdzFakty(map, decyzjaNr, tick, status);

                if (ksiega == null || arcDirector == null || !LukiAktywne)
                {
                    return;
                }
                ArcEventView wykonane = czekajace != null && czekajace.Event != null
                    ? czekajace.Event
                    : ArcEventView.FromCandidate(zwyciezca.Event, null);
                wykonane.FactionId = frakcja;
                ksiega.Pending = null;

                ArcObservation obs = ArcObservationBuilder.Build(map, ksiega, tick);
                List<ArcTransitionRecord> rekordy = arcDirector.OnExecuted(ksiega, wykonane, status, snapshot, obs);
                EmitArcRecords(map, history == null ? decyzjaNr : history.DecisionCount, rekordy);
            }
            catch (Exception e)
            {
                WylaczLuki("potwierdzenie wykonania", e);
            }
        }

        // =====================================================================================
        //  LUKI: obserwacja przy kazdym wywolaniu, spoznione potwierdzenia, emisja krokow
        // =====================================================================================

        /// <summary>
        /// Krok lukow przy KAZDYM wywolaniu compa: uzgodnienie ksiegi z katalogiem (Reconcile),
        /// domkniecie spoznionego potwierdzenia wykonania, obserwacja swiata, straznicy i limity.
        /// Nie losuje (kanarek Rand.iterations wokol obserwacji) i nie rzuca - wyjatek wylacza
        /// warstwe lukow z jednym raportem, narrator dziala dalej jak w v6.
        /// </summary>
        private void ArcTick(Map map)
        {
            if (arcsBroken || ArcsDisabledForArm || map == null || Current.Game == null)
            {
                return;
            }
            try
            {
                if (arcDirector == null)
                {
                    List<string> problemy;
                    // Bledy katalogu wypisal juz audyt startowy (PNStartup.AuditArcs) - tu bez powtorki.
                    arcDirector = new ArcDirector(ArcCatalogLoader.Load(out problemy), Props.arcs);
                }
                if (!arcDirector.Params.enabled)
                {
                    return;
                }
                NarratorMemoryComponent pamiec = Current.Game.GetComponent<NarratorMemoryComponent>();
                if (pamiec == null)
                {
                    return;
                }
                ArcLedger ksiega = pamiec.LedgerFor(map.uniqueID);
                int tick = CurrentTick();
                float dzien = tick / TicksPerDay;
                EventHistory history = HistoryFor(map);

                uint rand0 = RandCanary.Read();
                var rekordy = arcDirector.Reconcile(ksiega, tick, dzien);

                PendingExecution czekajace = ksiega.Pending;
                if (czekajace != null && czekajace.Tick < tick)
                {
                    // Iterator nie zostal wznowiony po naszym yield (np. wyjatek w TryFire) - domykamy
                    // teraz. Frakcja faktyczna jest nieznana (obiekt parms przepadl), wiec zdarzenie
                    // nie spelni sameFaction i nie otworzy luku wiazacego frakcje.
                    IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail(czekajace.IncidentDefName);
                    int teraz = OstatnieOdpalenie(map, def);
                    ExecStatus st = ExecutionConfirmation.ClassifyLate(teraz, czekajace);
                    PNLog.Exec(map.uniqueID, czekajace.DecisionIndex, czekajace.IncidentDefName, null,
                               ExecutionConfirmation.Label(st), czekajace.LastFireBefore, teraz, null,
                               czekajace.Event == null ? null : czekajace.Event.FactionId, tick, "-", czekajace.Tick);
                    ArcEventView ev = czekajace.Event ?? new ArcEventView();
                    ev.FactionId = null;
                    ksiega.Pending = null;
                    // TEN SAM SWIAT PAMIECI CO NA SCIEZCE NORMALNEJ (S6 - przeglad adwersarialny). Snapshot
                    // zbudowany tu od nowa widzialby historie JUZ z tym zdarzeniem (RecordEvent idzie przed
                    // yield): inny wiek tematow, "dni od ostatniego zdarzenia" ok. 0,017 zamiast dni, a przy
                    // pelnym buforze - bez najstarszego wpisu. Dlatego: pola historii z pamieci decyzji
                    // (Pending), fakty oceniane w dniu DECYZJI i przez KsiegaFaktow (bezpiecznik faktow
                    // obowiazuje tak samo jak na sciezce normalnej). Fakty TEGO zdarzenia nie sa widoczne,
                    // bo FactTick biegnie PO ArcTick. Zostaje roznica swiadoma (CLAUDE.md 2.4): zywy swiat
                    // (bogactwo, koloniscy, frakcje) i zegar obserwacji sa z T+1000. Stara linia P (sprzed S6)
                    // nie ma pamieci decyzji - wtedy snapshot jak dawniej, z dniem biezacym.
                    float dzienSwiata = czekajace.HasDecisionMemory ? czekajace.GameDay : dzien;
                    WorldSnapshot swiat = WorldSnapshotBuilder.Build(map, history, ksiega, KsiegaFaktow(map), dzienSwiata);
                    czekajace.ApplyDecisionMemoryTo(swiat);
                    rekordy.AddRange(arcDirector.OnExecuted(ksiega, ev, st, swiat,
                                                            ArcObservationBuilder.Build(map, ksiega, tick)));
                }

                ArcObservation obs = ArcObservationBuilder.Build(map, ksiega, tick);
                rekordy.AddRange(arcDirector.Observe(ksiega, obs));
                RandCanary.Check(rand0, "ArcTick");

                EmitArcRecords(map, history.DecisionCount, rekordy);
            }
            catch (Exception e)
            {
                WylaczLuki("obserwacja", e);
            }
        }

        /// <summary>
        /// Linie [PN-ARC], komunikaty w grze i JEDEN komunikat logu czytelnego na wywolanie
        /// (limit 1000 komunikatow Verse.Log - patrz PNLog).
        /// </summary>
        private static void EmitArcRecords(Map map, int decyzjaNr, List<ArcTransitionRecord> rekordy)
        {
            if (rekordy == null || rekordy.Count == 0)
            {
                return;
            }
            var czytelne = new List<string>(rekordy.Count);
            foreach (ArcTransitionRecord r in rekordy)
            {
                PNLog.Arc(map == null ? -1 : map.uniqueID, decyzjaNr, r);
                ArcMessages.Show(r);
                czytelne.Add(r.ToString() + (string.IsNullOrEmpty(r.Message) ? string.Empty : " \"" + r.Message + "\""));
            }
            PNLog.Decision("Luki (mapa " + (map == null ? "?" : map.uniqueID.ToString(CultureInfo.InvariantCulture))
                           + "): " + string.Join(" | ", czytelne.ToArray()));
        }

        private void WylaczLuki(string gdzie, Exception e)
        {
            if (arcsBroken)
            {
                return;
            }
            arcsBroken = true;
            PNLog.Error("WARSTWA LUKOW rzucila wyjatek (" + gdzie + ") i jest WYLACZONA do wczytania zapisu "
                        + "albo zmiany narratora (comp powstaje wtedy od nowa). "
                        + "Narrator dziala dalej bez lukow (jak v6); kolumny lukow beda puste.\n" + e);
        }

        // =====================================================================================
        //  FAKTY (krok 6): kolejka przed yield, potwierdzenie po nim, zastosowanie w nastepnym
        //  wywolaniu. Wlasny bezpiecznik - niezalezny od lukow w obie strony.
        // =====================================================================================

        /// <summary>Warstwa faktow rzucila wyjatek - wylaczona do wczytania zapisu albo zmiany narratora (raport raz).</summary>
        private bool faktyBroken;

        /// <summary>Ksiega faktow mapy albo null (brak komponentu pamieci, warstwa wylaczona).</summary>
        private FactLedger KsiegaFaktow(Map map)
        {
            if (faktyBroken || map == null || Current.Game == null)
            {
                return null;
            }
            NarratorMemoryComponent pamiec = Current.Game.GetComponent<NarratorMemoryComponent>();
            return pamiec == null ? null : pamiec.FactsFor(map.uniqueID);
        }

        /// <summary>
        /// Rozstrzygniecie kolejki faktow na poczatku wywolania compa: potwierdzone wykonanie -
        /// fakty do ksiegi z dniem DECYZJI; niepotwierdzone (iterator nie wrocil) - spozniona
        /// klasyfikacja przez lastFireTicks, ten sam kod co luki. Bez Verse.Rand.
        /// </summary>
        private void FactTick(Map map)
        {
            try
            {
                FactLedger fakty = KsiegaFaktow(map);
                if (fakty == null || fakty.Pending == null)
                {
                    return;
                }
                int tick = CurrentTick();
                List<FactEvent> zdarzenia = fakty.ResolvePending(tick, nazwa => OstatnieOdpalenie(
                    map, string.IsNullOrEmpty(nazwa) ? null : DefDatabase<IncidentDef>.GetNamedSilentFail(nazwa)));
                EmitFactRecords(map, tick, zdarzenia);
            }
            catch (Exception e)
            {
                WylaczFakty("rozstrzygniecie kolejki", e);
            }
        }

        /// <summary>
        /// Stawia fakty zwyciezcy w kolejce. Deklaracje sa KOPIOWANE, a nie wspoldzielone z obiektami
        /// z Defow - kolejka zyje w zapisie gry i nie moze zmieniac sie razem z katalogiem.
        /// </summary>
        private void ZakolejkujFakty(Map map, FactLedger fakty, ComposedEvent zdarzenie, int tick, float dzien,
                                     int decyzjaNr, string incydent, int ostatniPrzed)
        {
            if (fakty == null || zdarzenie == null || zdarzenie.Blocks == null)
            {
                return;
            }
            try
            {
                var zapisy = new List<FactWrite>();
                foreach (Block b in zdarzenie.Blocks)
                {
                    if (b == null || b.FactsOnExecute == null)
                    {
                        continue;
                    }
                    foreach (FactWrite w in b.FactsOnExecute)
                    {
                        if (w != null)
                        {
                            zapisy.Add(new FactWrite { key = w.key, value = w.value, accumulate = w.accumulate,
                                                       lifespanDays = w.lifespanDays });
                        }
                    }
                }
                if (zapisy.Count == 0)
                {
                    return;
                }

                bool nadpisano;
                List<FactEvent> odrzucone = fakty.Queue(new PendingFacts
                {
                    Tick = tick,
                    Day = dzien,
                    DecisionIndex = decyzjaNr,
                    IncidentDefName = incydent,
                    LastFireBefore = ostatniPrzed,
                    Writes = zapisy
                }, out nadpisano);
                if (nadpisano)
                {
                    // Nie powinno sie zdarzyc: kazde wywolanie zaczyna sie od FactTick. Jesli jednak
                    // tak - fakty poprzedniego zdarzenia przepadly, i to musi byc widac.
                    PNLog.Warn("Kolejka faktow mapy " + map.uniqueID.ToString(CultureInfo.InvariantCulture)
                               + " nie byla pusta przy nowej decyzji - fakty poprzedniego zdarzenia przepadly.");
                }
                EmitFactRecords(map, tick, odrzucone);
            }
            catch (Exception e)
            {
                WylaczFakty("kolejkowanie", e);
            }
        }

        /// <summary>Potwierdzenie na sciezce normalnej (ten sam tick co decyzja) - tylko oznacza kolejke.</summary>
        private void PotwierdzFakty(Map map, int decyzjaNr, int tick, ExecStatus status)
        {
            try
            {
                FactLedger fakty = KsiegaFaktow(map);
                if (fakty == null)
                {
                    return;
                }
                FactEvent odrzucenie = fakty.ConfirmPending(decyzjaNr, tick, status);
                if (odrzucenie != null)
                {
                    EmitFactRecords(map, tick, new List<FactEvent> { odrzucenie });
                }
            }
            catch (Exception e)
            {
                WylaczFakty("potwierdzenie", e);
            }
        }

        /// <summary>Linie [PN-FACT] i JEDEN komunikat logu czytelnego na wywolanie (limit Verse.Log).</summary>
        private static void EmitFactRecords(Map map, int tick, List<FactEvent> zdarzenia)
        {
            if (zdarzenia == null || zdarzenia.Count == 0)
            {
                return;
            }
            int mapa = map == null ? -1 : map.uniqueID;
            var czytelne = new List<string>(zdarzenia.Count);
            foreach (FactEvent e in zdarzenia)
            {
                PNLog.Fact(mapa, tick, e);
                czytelne.Add(e.KindLabel() + " " + (string.IsNullOrEmpty(e.Key) ? "(cale zdarzenie)" : e.Key)
                             + (float.IsNaN(e.Value) ? string.Empty : "=" + e.Value.ToString("0.##", CultureInfo.InvariantCulture))
                             + " (" + (e.Reason ?? "-") + ")");
            }
            PNLog.Decision("Fakty (mapa " + mapa.ToString(CultureInfo.InvariantCulture) + "): "
                           + string.Join(" | ", czytelne.ToArray()));
        }

        private void WylaczFakty(string gdzie, Exception e)
        {
            if (faktyBroken)
            {
                return;
            }
            faktyBroken = true;
            PNLog.Error("WARSTWA FAKTOW rzucila wyjatek (" + gdzie + ") i jest WYLACZONA do wczytania zapisu "
                        + "albo zmiany narratora (comp powstaje wtedy od nowa). "
                        + "Narrator i luki dzialaja dalej; warunki faktowe beda niespelnione (pusta pamiec "
                        + "faktow zabiera klocki i luki z puli, nie wpuszcza ich bez pokrycia).\n" + e);
        }

        /// <summary>
        /// Straty kolonistow per mapa (Guard_ColonistsLost) - bez Harmony: wanilia wola ten hook
        /// dla KAZDEGO compa storytellera (Storyteller.Notify_PawnEvent), przy zgonie z
        /// DoKillSideEffects i przy porwaniu z PreKidnapped - w obu chwilach pionek ma jeszcze
        /// frakcje gracza, wiec IsColonist dziala (dekompilacja 1.5.4063, Pawn.cs).
        /// </summary>
        public override void Notify_PawnEvent(Pawn p, AdaptationEvent ev, DamageInfo? dinfo = null)
        {
            if (p == null || (ev != AdaptationEvent.Died && ev != AdaptationEvent.Kidnapped) || !p.IsColonist)
            {
                return;
            }
            // Symulator nie zabija pionkow; zdarzenie w jego trakcie byloby zdarzeniem prawdziwej
            // gry, a ksiegi sa wtedy podmienione na kopie ramienia.
            if (PNLog.InExperiment || Current.Game == null)
            {
                return;
            }
            NarratorMemoryComponent pamiec = Current.Game.GetComponent<NarratorMemoryComponent>();
            if (pamiec == null)
            {
                return;
            }
            // STRATA LICZY SIE DLA KOLONII (decyzja autora 2026-09-23, przeglad S6): zgon albo porwanie
            // poza mapa domowa (zasadzka, mapa zadania, kontratak na baze frakcji) trafial do osobnej
            // ksiegi niewidocznej dla lukow domu, a w karawanie (MapHeld == null) nie liczyl sie wcale.
            // Przy JEDNEJ kolonii strata idzie do jej mapy; przy kilku nie da sie wskazac wlasciwej -
            // wtedy nie jest liczona (znane ograniczenie, CLAUDE.md 2.4) i log mowi to raz.
            Map mapa = p.MapHeld;
            if (mapa == null || !mapa.IsPlayerHome)
            {
                Map dom = JedynaMapaDomowa();
                if (dom == null)
                {
                    if (!strataPozaDomemZgloszona)
                    {
                        strataPozaDomemZgloszona = true;
                        PNLog.Warn("Strata kolonisty poza mapa domowa przy " + (Find.Maps == null ? 0 : Find.Maps.Count(m => m.IsPlayerHome))
                                   .ToString(CultureInfo.InvariantCulture)
                                   + " koloniach - nie da sie wskazac kolonii, wiec nie jest liczona dla lukow (ostrzezenie raz na sesje).");
                    }
                    return;
                }
                mapa = dom;
            }
            pamiec.LedgerFor(mapa.uniqueID).ColonistLosses++;
        }

        private bool strataPozaDomemZgloszona;

        /// <summary>Jedyna mapa domowa gracza albo null (brak albo kilka kolonii).</summary>
        private static Map JedynaMapaDomowa()
        {
            if (Find.Maps == null)
            {
                return null;
            }
            Map jedyna = null;
            foreach (Map m in Find.Maps)
            {
                if (m == null || !m.IsPlayerHome)
                {
                    continue;
                }
                if (jedyna != null)
                {
                    return null;
                }
                jedyna = m;
            }
            return jedyna;
        }


        /// <summary>
        /// Slad decyzji: czesc czytelna ([PN]) plus jeden wiersz danych ([PN-DATA]).
        /// Wypisujemy pelny ranking ze sladem rozbicia na czynniki, bo bez odrzuconych znika
        /// mianownik metryk z sekcji 12 koncepcji - nie da sie policzyc, ile razy weto zadzialalo
        /// ani jak szeroka byla stawka.
        ///
        /// Czesc czytelna idzie JEDNYM komunikatem wieloliniowym razem z liniami zebranymi
        /// wczesniej w turze (krzywa, filtry) - limit Verse.Log liczy komunikaty, nie linie.
        /// </summary>
        private void LogDecision(List<string> czytelne, DecisionContext context, CandidateSet kandydaci,
                                 NarratorDecision decyzja,
                                 IncidentDef incydent, IncidentParms parms, List<string> odmowy,
                                 int rundy, Map map, int tick,
                                 int preGateRefusals, int acceptorCalls,
                                 TensionReading napiecie, CrisisReading kryzys)
        {
            string runda = " | runda " + rundy.ToString(CultureInfo.InvariantCulture)
                           + "/" + Props.maxSelectionRounds.ToString(CultureInfo.InvariantCulture);

            var linie = czytelne ?? new List<string>();

            if (incydent != null)
            {
                ComposedEvent zdarzenie = decyzja.Winner.Event;
                linie.Add(
                    "ZLOZONO [" + string.Join(" + ", zdarzenie.Blocks.ConvertAll(b => b.ToString()).ToArray()) + "]"
                    + " -> " + incydent.defName
                    + " | " + zdarzenie.Theme + "/" + zdarzenie.Valence + "/" + zdarzenie.Scale
                    + " | intensywnosc=" + zdarzenie.Intensity
                    // NASYCENIE SKALI, wypisywane TYLKO gdy zaszlo. Bez tego cala zmiana C3
                    // (surowa suma przed klamrowaniem) byla widoczna wylacznie dla walidatora
                    // offline, bo ComposedEvent.Trace nie ma w projekcie ANI JEDNEGO czytelnika -
                    // warstwa integracji loguje CandidateSet.Trace i FitTrace, nie ten slad.
                    // Znalezione przegladem adwersarialnym: deklarowanym celem C3 bylo, zeby
                    // przyciecie wkladu przestalo byc niewidoczne W GRZE, a nie tylko w tescie.
                    + (zdarzenie.IntensityClamped
                       ? " (suma wkladow=" + zdarzenie.IntensityRawSum.ToString(CultureInfo.InvariantCulture)
                         + " -> ograniczona przez skale)"
                       : string.Empty)
                    + " punkty=" + parms.points.ToString("0", CultureInfo.InvariantCulture)
                    + " | wynik=" + Fmt(decyzja.Winner.Utility)
                    + " p=" + Fmt(decyzja.Winner.SelectionProbability)
                    + runda);
            }
            else
            {
                linie.Add(
                    "PASS (powod=" + decyzja.PassReason + ")"
                    + " wynik=" + Fmt(decyzja.PassUtility)
                    + " bestTury=" + Fmt(decyzja.BestUtility)
                    + " pasmo=" + Fmt(decyzja.BandThreshold)
                    + runda);
            }

            linie.Add("  kontekst: " + context);
            linie.Add("  kompozycja: " + (kandydaci.Trace ?? kandydaci.DataLogFragment()));

            foreach (string linia in decyzja.ToRankingLines())
            {
                linie.Add("  " + linia);
            }

            if (odmowy.Count > 0)
            {
                // Od kroku 3 odmowy silnika sa materialem na przyszly czynnik "logiczna zasadnosc
                // w kontekscie": kazda z nich mowi, ze warunek twardy byl luzniejszy niz wymagania
                // IncidentWorkera.
                linie.Add("  odmowy silnika: " + string.Join("; ", odmowy.ToArray()));
            }

            if (incydent != null)
            {
                linie.Add("  dopasowanie: " + decyzja.Winner.Event.FitTrace);
                linie.Add("  opis: " + decyzja.Winner.Event.Description);
            }

            PNLog.Decision(string.Join("\n", linie.ToArray()));

            PNLog.Data(tick, map == null ? -1 : map.uniqueID, context, kandydaci, decyzja,
                       odmowy.Count, preGateRefusals, acceptorCalls, napiecie, kryzys);
        }

        /// <summary>
        /// Czy to wywolanie przyszlo SPOZA zegara gry - z waniliowego narzedzia debugowego.
        ///
        /// PROBLEM. Waniliowe StorytellerUtility.DebugGetFutureIncidents (i cztery inne sciezki
        /// debugowe znalezione skanem IL) wolaja nasz comp w petli, przestawiajac zegar gry
        /// o 1000 tickow na iteracje. Nasz comp przechodzi przy tym pelna sciezke decyzyjna
        /// i ZAPISUJE decyzje do trwalej pamieci - a wanilia przywraca potem tylko wlasny stan
        /// (i to z bledem: StoryState "przywraca" przez CopyTo do tego samego obiektu). Zapisanie
        /// gry po takim tescie utrwalalo kilkadziesiat fikcyjnych decyzji z datami z przyszlosci.
        ///
        /// ROZWIAZANIE: fail-closed. Wywolanie uznane za zewnetrzne nie robi NIC - nie losuje,
        /// nie liczy, nie pisze do pamieci ani do logu danych; jeden Warn na klatke. Do testow
        /// sluzy wlasna akcja "PN: test przyszlych incydentow", ktora robi zrzut i przywraca stan.
        ///
        /// KRYTERIUM GLOWNE - KOTWICA W ZEGARZE GRY (NarratorMemoryComponent.ZegarGry). W zwyklej
        /// grze narrator jest wolany wylacznie z DoSingleTick, PRZED GameComponentTick, wiec widzi
        /// zegar rowny tick - 1 (przy fastEcology tick - 2000). Kazde inne wywolanie jest
        /// zewnetrzne. Przeglad etapu 4 wykazal, ze sama heurystyka ponizej zawodzila przed
        /// minDaysPassed: wanilia nie wola compa przed 5. dniem, wiec pierwsza iteracja petli
        /// debugowej, ktora go osiagala, przechodzila jako prawdziwa, a jej tick z przyszlosci
        /// blokowal potem PRAWDZIWY interwal.
        ///
        /// KRYTERIA ZAPASOWE, gdy zegar nie jest znany (brak komponentu pamieci) - wystarczy jedno:
        ///   1. klatka Unity juz uznana za zewnetrzna;
        ///   2. tick spoza siatki 1000 - prawdziwy Storyteller.StorytellerTick wola compy
        ///      wylacznie przy TicksGame % 1000 == 0;
        ///   3. skok zegara w obrebie jednej klatki - gra przetwarza najwyzej 2 * TickRateMultiplier
        ///      (maks. 30) tickow na klatke, wiec dwa rozne interwaly w jednej klatce to petla
        ///      debugowa;
        ///   4. interwal tej mapy juz przetworzony (tick &lt;= ostatni) - lapie pierwsza iteracje
        ///      petli debugowej wtedy, gdy gra stoi na pauzie automatycznej dokladnie na ticku
        ///      wielokrotnosci 1000 (list zdarzenia ThreatBig), czyli w najczestszym scenariuszu,
        ///      w ktorym samo kryterium 2 zawodzi.
        ///
        /// Z kotwica zegara: falszywych negatywow brak (petla debugowa nie przechodzi przez
        /// DoSingleTick), a falszywe pozytywy tylko w JEDNYM znanym przypadku: deweloperskie
        /// narzedzia przesuwajace zegar bez DoSingleTick ("Increment time" o 1 h / 6 h / 1 d...,
        /// "Storywatcher tick 1 day"). Pierwszy prawdziwy tick po nich nie nastepuje po zegarze,
        /// wiec jesli trafi w siatke 1000 (ok. 1/1000 na uzycie, zero po pauzie na ticku siatki),
        /// przepada jedna tura z jednym ostrzezeniem - razem z turami innych map w tej klatce.
        /// Lagodzenie straznika pod ten przypadek byloby ryzykowniejsze niz koszt. Bez kotwicy
        /// (tylko kryteria zapasowe): pierwsza iteracja petli debugowej, ktora osiaga compa, moze
        /// przejsc, gdy jest pierwszym wywolaniem w klatce na ticku siatki - wtedy moze podjac jedna
        /// decyzje (bramka MTB ok. 0.7% na interwal); nastepne lapie kryterium 3.
        ///
        /// ostatniInterwalMapy nie jest zapisywany dla wywolan zewnetrznych, wiec nie ma jak
        /// zablokowac prawdziwego interwalu tickiem z przyszlosci.
        /// </summary>
        private bool CzyWywolanieZewnetrzne(Map map)
        {
            int tick = CurrentTick();
            int klatka = UnityEngine.Time.frameCount;
            string powod = null;

            NarratorMemoryComponent pamiecZegar = Current.Game == null
                ? null
                : Current.Game.GetComponent<NarratorMemoryComponent>();
            int zegar = pamiecZegar == null ? int.MinValue : pamiecZegar.ZegarGry;

            int ostatni;
            if (klatka == klatkaZewnetrzna)
            {
                powod = null; // klatka juz zgloszona - bez kolejnego ostrzezenia
            }
            else if (zegar != int.MinValue)
            {
                int oczekiwany = zegar + (DebugSettings.fastEcology ? 2000 : 1);
                if (tick == oczekiwany && ostatniInterwalMapy.TryGetValue(map.uniqueID, out ostatni) && tick <= ostatni)
                {
                    // DRUGIE wywolanie compa dla tej samej mapy w tym samym ticku (S6): kolejka faktow
                    // potwierdzonego zdarzenia zostalaby nadpisana bez linii [PN-FACT]. Wanilia tak nie
                    // wola (StorytellerTick raz na tick) - to mogl by zrobic tylko obcy mod.
                    powod = "interwal mapy " + map.uniqueID.ToString(CultureInfo.InvariantCulture)
                            + " juz przetworzony w tym ticku (" + tick.ToString(CultureInfo.InvariantCulture)
                            + ") - drugie wywolanie compa";
                }
                else if (tick == oczekiwany)
                {
                    straznikKlatka = klatka;
                    straznikTick = tick;
                    ostatniInterwalMapy[map.uniqueID] = tick;
                    return false;
                }
                else
                {
                    powod = "tick " + tick.ToString(CultureInfo.InvariantCulture)
                            + " nie nastepuje po zegarze gry " + zegar.ToString(CultureInfo.InvariantCulture)
                            + " (wywolanie spoza TickManager.DoSingleTick)";
                }
            }
            else if (tick % (int)TicksPerInterval != 0)
            {
                powod = "tick " + tick.ToString(CultureInfo.InvariantCulture) + " spoza siatki 1000";
            }
            else if (klatka == straznikKlatka && tick != straznikTick)
            {
                powod = "skok zegara w jednej klatce (" + straznikTick.ToString(CultureInfo.InvariantCulture)
                        + " -> " + tick.ToString(CultureInfo.InvariantCulture) + ")";
            }
            else if (ostatniInterwalMapy.TryGetValue(map.uniqueID, out ostatni) && tick <= ostatni)
            {
                powod = "interwal mapy " + map.uniqueID.ToString(CultureInfo.InvariantCulture)
                        + " juz przetworzony (tick " + tick.ToString(CultureInfo.InvariantCulture)
                        + " <= " + ostatni.ToString(CultureInfo.InvariantCulture) + ")";
            }
            else
            {
                straznikKlatka = klatka;
                straznikTick = tick;
                ostatniInterwalMapy[map.uniqueID] = tick;
                return false;
            }

            straznikKlatka = klatka;
            straznikTick = tick;
            if (klatka != klatkaZewnetrzna)
            {
                klatkaZewnetrzna = klatka;
                PNLog.Warn("Wywolanie narratora SPOZA zegara gry (" + powod + ") - najpewniej waniliowe "
                           + "narzedzie debugowe (Future incidents i pokrewne). Narrator nic nie zwraca "
                           + "i nie rusza pamieci, zeby test nie utrwalil fikcyjnych decyzji. Do testow "
                           + "uzyj akcji debugowej 'PN: test przyszlych incydentow'.");
            }
            return true;
        }

        /// <summary>
        /// Ograniczenia kompozycji dla tej tury - dzis wylacznie wymagany tag akcji.
        ///
        /// Intencja i docelowa moc NIE ida przez przepis, tylko przez DecisionContext (TurnPlanner),
        /// bo konsumuje je scoring. Przepis NIE FILTRUJE kandydatow po intencji: utility AI ma
        /// wazyc, a nie wykluczac - filtrowanie odcinaloby kandydatow, zanim ktokolwiek policzy
        /// ich uzytecznosc, wiec ze sladu decyzji nie dalo by sie odczytac, ILE narrator poswiecil,
        /// zeby posluchac krzywej. (Do drugiego przegladu etapu 4 przepis mial jeszcze martwe pola
        /// intencji i mocy bez zadnego czytelnika - patrz EventRecipe.)
        /// </summary>
        private EventRecipe BuildRecipe()
        {
            return new EventRecipe
            {
                RequiredActionTag = Props.requiredActionTag
            };
        }

        /// <summary>
        /// Buduje scorer i krzywa pod AKTUALNY profil; przebudowuje je, gdy profil sie zmienil.
        ///
        /// Porownanie po defName, a nie po referencji: NarratorProfileCatalog.Resolve zwraca
        /// swieza kopie przy kazdym wywolaniu (celowo - patrz tam), wiec porownanie referencji
        /// przebudowywaloby wszystko co ture.
        /// </summary>
        private void EnsureProfileRuntime()
        {
            NarratorMemoryComponent pamiec = Current.Game == null
                ? null
                : Current.Game.GetComponent<NarratorMemoryComponent>();

            string chcianyId = pamiec == null ? string.Empty : pamiec.ProfileId;

            // SZYBKIE WYJSCIE SPRAWDZA WSZYSTKIE POLA, ZA KTORE TA METODA ODPOWIADA.
            //
            // Wczesniej patrzylo wylacznie na tensionModel i identyfikator profilu - a to jest
            // dokladnie ten wzorzec, ktory zamienia awarie jednorazowa w TRWALA. Wystarczylo,
            // zeby konstruktor UtilityScorer rzucil po udanym zbudowaniu krzywej: pole
            // activeProfileId bylo juz ustawione, tensionModel juz niepusty, wiec przy kazdej
            // nastepnej turze metoda wracala tutaj natychmiast i scorer zostawal null
            // na zawsze. Straznik na pelnym zestawie pol usuwa cala te klase bledow.
            if (scorer != null && tensionModel != null
                && string.Equals(activeProfileId, chcianyId, StringComparison.Ordinal))
            {
                return;
            }

            NarratorProfile profil = pamiec == null
                ? NarratorProfile.Fallback(Props.weights)
                : pamiec.ActiveProfile(Props.weights);

            // PUBLIKACJA ATOMOWA: budujemy do zmiennych lokalnych i przypisujemy do pol dopiero
            // wtedy, gdy OBA obiekty powstaly. Gdyby drugi konstruktor rzucil, pola zostaja
            // nietkniete - czyli albo na poprzednim, spojnym profilu, albo na null, ktore
            // wychwyci straznik w TryEnsureRuntime. Zaden z tych stanow nie jest mieszanka
            // dwoch osobowosci.
            //
            // Wagi czynnikow zdarzeniowych pochodza z PROFILU, a nie z bloku <weights>
            // StorytellerDefa. Ten ostatni zasila WYLACZNIE profil awaryjny (brak komponentu
            // pamieci albo profil nieistniejacy w katalogu) - do drugiego przegladu etapu 4 byl
            // tak opisywany, ale sciezka awaryjna brala inicjalizatory C#, wiec zmiana bloku
            // w XML nie miala zadnego skutku mimo wpisu "wagi awaryjne" w [PN-CONFIG].
            var nowyModel = new TensionModel(profil.Tension, Props.contrast);
            var nowyScorer = new UtilityScorer(BuildEventFactors(), profil.Weights,
                                               UtilityScorer.BuildPassFactors(Props.pass), Props.pass,
                                               Props.vetoContextFitBelow);

            tensionModel = nowyModel;
            scorer = nowyScorer;
            activeProfileId = chcianyId;

            // Walidacja i opis sa TUTAJ, bezposrednio po konstrukcji, i to nie jest kwestia
            // stylu. Kazde inne miejsce zaklada kolejnosc wywolan, ktora da sie po cichu
            // odwrocic przy nastepnej refaktoryzacji - dokladnie tak, jak stalo sie w kroku 4.
            //
            // Druga, niezalezna siec bezpieczenstwa obok PNStartup: gdyby ten comp zostal uzyty
            // w innym StorytellerDefie niz audytowany na starcie, cicha literowka w nazwie
            // czynnika (waga 0, martwy czynnik, zero komunikatow) nadal zostanie zgloszona.
            string problem;
            if (!scorer.Validate(out problem))
            {
                PNLog.Error("Konfiguracja scoringu (profil " + profil.Id + "): " + problem);
            }

            PNLog.Decision("Profil narratora aktywny: " + profil);
            PNLog.Decision("Scoring: " + scorer.DescribeConfiguration());
        }

        /// <summary>
        /// KOLEJNOSC REJESTRACJI CZYNNIKOW JEST CZESCIA FORMATU DANYCH BADAWCZYCH:
        /// [contextFit, freshness, dramaticContrast, intentAlignment]. Ustala kolejnosc wierszy
        /// sladu w logu czytelnym i musi byc stala miedzy rozgrywkami - jej zmiana uniewaznia
        /// porownywalnosc wczesniej zebranych serii.
        ///
        /// Factor_DramaticContrast dostaje to samo strojenie z XML co TensionModel - wspolne okno,
        /// lambda i mapowanie osi. Kontrast liczy z nich rytm BEZ zaniku, napiecie - obciazenie
        /// z zanikiem kazdego wpisu (ComputeAgedLoad); patrz konstruktor TensionModel.
        /// </summary>
        private IScoringFactor[] BuildEventFactors()
        {
            return new IScoringFactor[]
            {
                new Factor_ContextFit(),
                new Factor_Freshness(),
                new Factor_DramaticContrast(Props.contrast),
                new Factor_IntentAlignment()
            };
        }

        /// <summary>
        /// Znacznik czasu tury. Sluzy jednoczesnie za ziarno strumienia generowania kandydatow
        /// i - po przesunieciu o SelectionSeedSalt - za ziarno strumienia wyboru.
        /// </summary>
        private static int CurrentTick()
        {
            return Find.TickManager != null ? Find.TickManager.TicksGame : 0;
        }

        private static string Fmt(float v)
        {
            return v.ToString("0.000", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// JEDYNE wejscie do inicjalizacji ze sciezki decyzyjnej. Zwraca false, gdy narrator
        /// nie jest gotowy podjac decyzji - wolajacy ma wtedy po prostu pominac ture.
        ///
        /// Trzy rzeczy, ktore ta metoda gwarantuje, a ktorych sam straznik na polach NIE dawal:
        ///   1. WYJATEK Z KONSTRUKTOROW NIE WYCHODZI NA ZEWNATRZ. Straznik sprawdzajacy pola
        ///      po wywolaniu obu metod Ensure lapal wylacznie pole zostawione na null; gdyby
        ///      BlockCatalogLoader albo ktorykolwiek konstruktor rzucil, wykonanie nigdy by
        ///      do niego nie doszlo i wyjatek zabilby ture tak samo jak przedtem.
        ///   2. AWARIA JEST ZGLASZANA RAZ. Verse.Log ma twardy limit 1000 wiadomosci na sesje
        ///      (patrz komentarz w PNLog), a narrator dziala co 1000 tickow - blad powtarzany
        ///      co ture wyczerpalby ten limit w kilkadziesiat minut gry i wygasil CALY log
        ///      czytelny, takze komunikaty innych modow.
        ///   3. PO AWARII NARRATOR JEST WYLACZONY, A NIE PROBUJE W KOLKO. Nasze awarie
        ///      inicjalizacji maja ksztalt konfiguracyjny (zly XML, nierozwiazany Class=),
        ///      czyli sa TRWALE - ponawianie nie ma jak pomoc, a kosztuje wyjatek na ture.
        ///      Naprawa wymaga poprawienia plikow i ponownego uruchomienia gry, bo RimWorld
        ///      czyta Defy i DLL wylacznie przy starcie procesu.
        /// </summary>
        private bool TryEnsureRuntime()
        {
            if (runtimeBroken)
            {
                return false;
            }

            try
            {
                // Dwie fazy, rozdzielone wedlug JEDNEGO kryterium: czy rzecz zalezy od profilu.
                //   EnsureRuntime        - raz na sesje: katalog klockow, kompozytor, generator,
                //                          polityka wyboru. Niezalezne od osobowosci.
                //   EnsureProfileRuntime - co ture, ale przebudowuje TYLKO przy zmianie profilu:
                //                          scorer (wagi z profilu) i krzywa napiecia.
                // Miedzy tymi metodami nie ma zaleznosci - EnsureProfileRuntime czyta wylacznie
                // Props i komponent pamieci - wiec kolejnosc wywolan jest obojetna.
                EnsureRuntime();
                EnsureProfileRuntime();
            }
            catch (Exception e)
            {
                runtimeBroken = true;
                PNLog.Error("INICJALIZACJA NARRATORA RZUCILA WYJATEK. Narrator jest wylaczony "
                            + "do wczytania zapisu albo zmiany narratora i nie wyprodukuje zadnego wydarzenia; waniliowe "
                            + "compy dzialaja dalej. Napraw konfiguracje i uruchom gre ponownie "
                            + "(RimWorld czyta Defy i DLL tylko przy starcie procesu).\n" + e);
                return false;
            }

            if (composer == null || generator == null || policy == null
                || turnRunner == null || scorer == null || tensionModel == null)
            {
                // Inicjalizacja nie rzucila, ale zostawila dziure. To znaczy, ze ktoras metoda
                // Ensure ma sciezke wyjscia pomijajaca przypisanie pola - blad w kodzie, nie
                // w konfiguracji. Zglaszamy z lista brakujacych skladnikow i wylaczamy sie,
                // zamiast czekac na NullReferenceException kilka linii dalej.
                runtimeBroken = true;
                PNLog.Error("Narrator nie jest kompletnie zainicjalizowany - wylaczony do wczytania "
                            + "zapisu albo zmiany narratora. Brakuje: "
                            + (composer == null ? "composer " : string.Empty)
                            + (generator == null ? "generator " : string.Empty)
                            + (policy == null ? "policy " : string.Empty)
                            + (turnRunner == null ? "turnRunner " : string.Empty)
                            + (scorer == null ? "scorer " : string.Empty)
                            + (tensionModel == null ? "tensionModel " : string.Empty)
                            + "- patrz bledy wyzej w logu.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Leniwe zlozenie czesci potoku NIEZALEZNEJ od profilu. Wykonuje sie raz na sesje, przy
        /// pierwszej decyzji - a nie w konstruktorze, bo w chwili tworzenia komponentu DefDatabase
        /// moze jeszcze nie byc gotowa.
        /// </summary>
        private void EnsureRuntime()
        {
            if (runtimeReady)
            {
                return;
            }

            List<Block> blocks;
            CompatibilityGraph graph;
            BlockCatalogLoader.Load(out blocks, out graph);

            // BUDUJEMY DO ZMIENNYCH LOKALNYCH, POLA PRZYPISUJEMY NA KONCU.
            // Publikacja czesciowa zostawialaby comp w stanie "polowa pol ustawiona", ktory
            // jest trudniejszy do zdiagnozowania niz brak inicjalizacji w ogole.
            var nowyComposer = new EventComposer(blocks, graph);
            var nowyGenerator = new CandidateGenerator(nowyComposer);

            // Scorer NIE powstaje tutaj, tylko w EnsureProfileRuntime: jego wagi naleza
            // do profilu narratora, a profil jest znany dopiero po wczytaniu gry.
            // Tutaj zostaje wylacznie to, co jest wspolne dla wszystkich osobowosci.
            var nowaPolityka = new SelectionPolicy(Props.ToSelectionParameters());
            var nowyRunner = new TurnRunner(nowaPolityka, Props.pass, Props.maxSelectionRounds);

            composer = nowyComposer;
            generator = nowyGenerator;
            policy = nowaPolityka;
            turnRunner = nowyRunner;

            // FLAGA NA SAMYM KONCU, PO UDANEJ BUDOWIE.
            // Wczesniej stala na poczatku metody, z uzasadnieniem "zeby problem konfiguracji
            // zostal zgloszony raz, a nie przy kazdym interwale". Cel byl sluszny, srodek zly:
            // przy wyjatku w srodku budowy flaga zostawala podniesiona, wiec kolejne tury
            // omijaly inicjalizacje i comp zostawal TRWALE z polami na null. Za jednorazowosc
            // zgloszenia odpowiada teraz runtimeBroken, czyli osobny mechanizm dla osobnej
            // sprawy - a ta flaga znaczy dokladnie to, co glosi jej nazwa.
            runtimeReady = true;

            if (blocks.Count == 0)
            {
                PNLog.Error("Katalog klockow PUSTY - narrator nie zlozy zadnego wydarzenia. "
                            + "Patrz diagnostyka startowa wyzej w logu.");
            }
            else
            {
                PNLog.Decision("Katalog klockow zaladowany: " + blocks.Count + " klockow, "
                               + graph.ForbiddenEdgeCount + " zabronionych krawedzi.");
            }

            // UWAGA HISTORYCZNA - w tym miejscu stala walidacja scorera i rzucala
            // NullReferenceException przy pierwszej decyzji kazdej rozgrywki.
            //
            // Do kroku 3 scorer powstawal kilka linii wyzej, wiec walidacja obok niego byla
            // poprawna. Krok 4 przeniosl jego budowe do EnsureProfileRuntime (wagi naleza do
            // PROFILU, znanego dopiero po wczytaniu gry), ale walidacje tu zostawil - na polu,
            // ktore w tym momencie bylo jeszcze null.
            //
            // Objaw byl mylacy: flaga runtimeReady stala WTEDY na poczatku metody, wiec przy
            // nastepnym interwale EnsureRuntime wracalo od razu, scorer powstawal
            // w EnsureProfileRuntime i narrator dzialal dalej. Kosztem byla jedna przepadnieta
            // decyzja i TRWALE brakujace linie diagnostyki startowej - czyli mod, ktory wyglada
            // na sprawny, tylko po cichu zgubil pierwsza ture i wlasny audyt konfiguracji.
            //
            // Naprawa jest w trzech miejscach naraz, bo blad mial trzy niezalezne przyczyny:
            // walidacja przeniesiona TAM, GDZIE POWSTAJE OBIEKT (EnsureProfileRuntime); flaga
            // przesunieta na koniec udanej budowy (wyzej); cala inicjalizacja opakowana
            // w TryEnsureRuntime, zeby wyjatek nie wychodzil na sciezke decyzyjna.
            PNLog.Decision("Polityka wyboru: " + policy.Parameters.Describe()
                           + " maxSelectionRounds=" + Props.maxSelectionRounds.ToString(CultureInfo.InvariantCulture)
                           + " candidateBudget=" + Props.candidateBudget.ToString(CultureInfo.InvariantCulture));
        }
    }
}
