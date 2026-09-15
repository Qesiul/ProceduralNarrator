using System;
using System.Collections.Generic;
using System.Globalization;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Tension;
using ProceduralNarrator.Core.Util;
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
    /// KOLEJNOSC TURY (krok 3, zastapila petle prob z kroku 2):
    ///   bramka MTB -> WorldSnapshot (zamrozony raz) -> DecisionContext
    ///   -> CandidateGenerator (cala przestrzen wariantow w granicach budzetu ocen)
    ///   -> UtilityScorer.ScoreAll + ScorePass (DOKLADNIE RAZ na ture)
    ///   -> petla rund: SelectionPolicy.Select -> CanFireNow -> ewentualne usuniecie z puli
    ///   -> zapis do historii PRZED yield return -> log czytelny + linia [PN-DATA].
    ///
    /// Stare pole MaxCompositionAttempts ZNIKLO. Poprzednia petla losowala kolejne kompozycje
    /// z nowego ziarna i przyjmowala pierwsza, ktora gra wpuscila - czyli nie porownywala
    /// kandydatow ze soba w ogole. Teraz kandydaci sa oceniani wszyscy naraz, a "proba"
    /// zamienila sie w runde rankingu: po odmowie silnika wypada dokladnie jeden kandydat,
    /// a pasmo near-best przelicza sie od nowa wzgledem najlepszej DOSTEPNEJ opcji.
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
            WorldSnapshot snapshot = WorldSnapshotBuilder.Build(map, history, gameDay);

            // WARSTWA PLANOWANIA (krok 4): napiecie -> intencja + docelowa moc.
            // Liczona PO snapshocie, bo czlon sytuacyjny czyta powalonych i poziom zagrozenia.
            TensionReading napiecie = tensionModel.Compute(history, snapshot, gameDay);
            IntentDecision zamiar = IntentSelector.Select(napiecie.Tension, tensionModel.Parameters);

            EventRecipe recipe = BuildRecipe(zamiar);
            DecisionContext context = DecisionContext.Create(snapshot, history, gameDay,
                                                             recipe.Intent, recipe.TargetIntensity,
                                                             napiecie.Tension);

            PNLog.Decision("Krzywa dramaturgiczna: " + napiecie.Trace + " -> " + zamiar.Trace);

            IRandomSource rngGen = new SeededRandom(tick);
            IRandomSource rngSel = new SeededRandom(unchecked(tick + SelectionSeedSalt));

            CandidateSet kandydaci = generator.Generate(recipe, snapshot, rngGen, Props.candidateBudget);
            if (kandydaci.Truncated)
            {
                // Dzis nieosiagalne (najwieksza akcja ma 16 wariantow wobec TraversalCap 20000),
                // ale flaga bez konsumenta jest flaga martwa i nikt nie zauwazylby dnia, w ktorym
                // zastrzeli. Wtedy TotalVariants jest DOLNYM ograniczeniem, a nie wartoscia.
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
            int odfiltrowanych;
            List<ComposedEvent> doOceny = IncidentParmsBuilder.OdfiltrujNieosiagalne(
                kandydaci.Candidates, snapshot.ThreatPoints, out odfiltrowanych);
            if (odfiltrowanych > 0)
            {
                PNLog.Decision("Sito punktow zagrozenia: usunieto "
                               + odfiltrowanych.ToString(CultureInfo.InvariantCulture)
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

            // Kopia robocza: TurnRunner usuwa z niej kandydatow odrzuconych przez silnik gry,
            // a lista zwrocona przez ScoreAll ma pozostac nietknieta na potrzeby logu.
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

            CandidateAcceptor akceptor = delegate(ScoredCandidate kandydat, out string powod)
            {
                powod = null;
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
                    return false;
                }

                if (!kandydacki.TargetAllowed(target))
                {
                    powod = kandydacki.defName + ": cel niedozwolony";
                    return false;
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
                IncidentParms kandydackieParms = IncidentParmsBuilder.Apply(
                    GenerateParms(kandydacki.category, target), zdarzenie, Props.useComposedLetter);

                if (!kandydacki.Worker.CanFireNow(kandydackieParms))
                {
                    powod = kandydacki.defName + ": CanFireNow=false";
                    return false;
                }

                incydent = kandydacki;
                parms = kandydackieParms;
                return true;
            };

            TurnResult tura = turnRunner.Run(context, pula, pass, rngSel,
                                             scorer.LastPassDensity, akceptor);

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
            LogDecision(context, kandydaci, decyzja, incydent, parms, odmowy, rundy, map, tick);

            if (incydent == null)
            {
                // PASS jest pelnoprawna decyzja i tez przesuwa czas narracyjny: licznik decyzji
                // rosnie (wiec wszystkie zdarzenia w historii sie odswiezaja), ale wpis do bufora
                // NIE powstaje - cisza nie jest zdarzeniem i zatrulaby rytm oraz gestosc.
                history.RecordPass(gameDay, tick);
                yield break;
            }

            // Historia dotykana DOKLADNIE RAZ w turze i PRZED yield return.
            // MakeIntervalIncidents jest iteratorem: kod za yield return wykona sie dopiero przy
            // kolejnym MoveNext, do ktorego konsument nie jest zobowiazany. Zapis po yield return
            // bylby zakladem o cudza petle, a przegrana objawia sie pusta historia i czynnikiem
            // swiezosci zamrozonym na wartosci neutralnej - czyli cicho.
            // Do zapisania w pracy: historia rejestruje INTENCJE narratora, nie potwierdzone
            // wykonanie (TryExecute moze pozniej zwrocic false). Domkniecie petli faktycznym
            // wynikiem tury to krok 6 i hak Harmony na IncidentWorker.TryExecute.
            if (!history.RecordEvent(decyzja.Winner.Event, gameDay, tick))
            {
                PNLog.Error("Nie udalo sie dopisac zdarzenia do historii - czynniki swiezosci "
                            + "i kontrastu strace ta ture. Kandydat: " + decyzja.Winner.Label);
            }

            yield return new FiringIncident(incydent, this, parms);
        }


        /// <summary>
        /// Slad decyzji: czesc czytelna ([PN]) plus jeden wiersz danych ([PN-DATA]).
        /// Wypisujemy pelny ranking ze sladem rozbicia na czynniki, bo bez odrzuconych znika
        /// mianownik metryk z sekcji 12 koncepcji - nie da sie policzyc, ile razy weto zadzialalo
        /// ani jak szeroka byla stawka.
        /// </summary>
        private void LogDecision(DecisionContext context, CandidateSet kandydaci, NarratorDecision decyzja,
                                 IncidentDef incydent, IncidentParms parms, List<string> odmowy,
                                 int rundy, Map map, int tick)
        {
            string runda = " | runda " + rundy.ToString(CultureInfo.InvariantCulture)
                           + "/" + Props.maxSelectionRounds.ToString(CultureInfo.InvariantCulture);

            if (incydent != null)
            {
                ComposedEvent zdarzenie = decyzja.Winner.Event;
                PNLog.Decision(
                    "ZLOZONO [" + string.Join(" + ", zdarzenie.Blocks.ConvertAll(b => b.ToString()).ToArray()) + "]"
                    + " -> " + incydent.defName
                    + " | " + zdarzenie.Theme + "/" + zdarzenie.Valence + "/" + zdarzenie.Scale
                    + " | intensywnosc=" + zdarzenie.Intensity
                    + " punkty=" + parms.points.ToString("0", CultureInfo.InvariantCulture)
                    + " | wynik=" + Fmt(decyzja.Winner.Utility)
                    + " p=" + Fmt(decyzja.Winner.SelectionProbability)
                    + runda);
            }
            else
            {
                PNLog.Decision(
                    "PASS (powod=" + decyzja.PassReason + ")"
                    + " wynik=" + Fmt(decyzja.PassUtility)
                    + " best=" + Fmt(decyzja.BestUtility)
                    + " pasmo=" + Fmt(decyzja.BandThreshold)
                    + runda);
            }

            PNLog.Decision("  kontekst: " + context);
            PNLog.Decision("  kompozycja: " + (kandydaci.Trace ?? kandydaci.DataLogFragment()));

            foreach (string linia in decyzja.ToRankingLines())
            {
                PNLog.Decision("  " + linia);
            }

            if (odmowy.Count > 0)
            {
                // Od kroku 3 odmowy silnika sa materialem na przyszly czynnik "logiczna zasadnosc
                // w kontekscie": kazda z nich mowi, ze warunek twardy byl luzniejszy niz wymagania
                // IncidentWorkera.
                PNLog.Decision("  odmowy silnika: " + string.Join("; ", odmowy.ToArray()));
            }

            if (incydent != null)
            {
                PNLog.Decision("  dopasowanie: " + decyzja.Winner.Event.FitTrace);
                PNLog.Decision("  opis: " + decyzja.Winner.Event.Description);
            }

            PNLog.Data(tick, map == null ? -1 : map.uniqueID, context, kandydaci, decyzja, odmowy.Count);
        }

        /// <summary>
        /// Miejsce wpiecia warstwy planowania - od kroku 4 wypelnione wyjsciem krzywej
        /// dramaturgicznej zamiast stala Hold.
        ///
        /// Przepis NADAL NIE FILTRUJE kandydatow po intencji i to sie nie zmienilo: utility AI
        /// ma wazyc, a nie wykluczac. Filtrowanie odcinaloby kandydatow, zanim ktokolwiek
        /// policzy ich uzytecznosc, wiec ze sladu decyzji nie dalo by sie odczytac, ILE narrator
        /// poswiecil, zeby posluchac krzywej - a to jest jedna z ciekawszych wielkosci
        /// do rozdzialu o ewaluacji.
        /// </summary>
        private EventRecipe BuildRecipe(IntentDecision zamiar)
        {
            return new EventRecipe
            {
                RequiredActionTag = Props.requiredActionTag,
                TargetIntensity = zamiar == null ? 0f : zamiar.TargetIntensity,
                Intent = zamiar == null ? Intent.Hold : zamiar.Intent
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
                ? NarratorProfile.Fallback()
                : pamiec.ActiveProfile();

            // PUBLIKACJA ATOMOWA: budujemy do zmiennych lokalnych i przypisujemy do pol dopiero
            // wtedy, gdy OBA obiekty powstaly. Gdyby drugi konstruktor rzucil, pola zostaja
            // nietkniete - czyli albo na poprzednim, spojnym profilu, albo na null, ktore
            // wychwyci straznik w TryEnsureRuntime. Zaden z tych stanow nie jest mieszanka
            // dwoch osobowosci.
            //
            // Wagi czynnikow zdarzeniowych pochodza z PROFILU, a nie z bloku <weights>
            // StorytellerDefa. Ten ostatni zostaje jako wartosc awaryjna i jako czesc kanarka
            // konfiguracji w logu startowym.
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
        /// Factor_DramaticContrast dostaje strojenie z XML, zeby liczylo rytm identycznie
        /// jak TensionModel.
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
        /// Leniwe zlozenie calego potoku decyzyjnego. Wykonuje sie raz na sesje, przy pierwszej
        /// decyzji - a nie w konstruktorze, bo w chwili tworzenia komponentu DefDatabase moze
        /// jeszcze nie byc gotowa.
        /// </summary>
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
                            + "do konca tej sesji i nie wyprodukuje zadnego wydarzenia; waniliowe "
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
                PNLog.Error("Narrator nie jest kompletnie zainicjalizowany - wylaczony do konca "
                            + "sesji. Brakuje: "
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
