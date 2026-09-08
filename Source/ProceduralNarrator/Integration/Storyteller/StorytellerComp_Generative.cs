using System;
using System.Collections.Generic;
using System.Globalization;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Tension;
using ProceduralNarrator.Core.Util;
using ProceduralNarrator.Integration.Defs;
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

        private bool runtimeReady;

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

            EnsureRuntime();
            EnsureProfileRuntime();

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

            // WYMOG: ScoreAll i ScorePass wolane DOKLADNIE RAZ na decyzje. Kolejne rundy petli
            // powtarzaja wylacznie SelectionPolicy.Select na juz ocenionej liscie. Powtorne
            // ocenianie (a) przeliczyloby czynniki tyle razy, ile rund, (b) nadpisaloby Factors,
            // gubiac slad tej rundy, ktora faktycznie zakonczyla decyzje. PASS tez nie jest
            // przeliczany miedzy rundami - gestosc zdarzen nie zmienia sie w obrebie jednej tury.
            List<ScoredCandidate> ocenieni = scorer.ScoreAll(kandydaci.Candidates, context);
            ScoredCandidate pass = scorer.ScorePass(context);

            // Kopia robocza: petla usuwa z niej kandydatow odrzuconych przez silnik gry, a lista
            // zwrocona przez ScoreAll ma pozostac nietknieta na potrzeby logu.
            var pula = new List<ScoredCandidate>(ocenieni);

            IncidentDef incydent;
            IncidentParms parms;
            List<string> odmowy;
            int rundy;
            NarratorDecision decyzja = RunSelectionLoop(target, context, pula, pass, rngSel,
                                                       out incydent, out parms, out odmowy, out rundy);

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
        /// Petla rund wyboru. Zwraca decyzje, a przez parametry wyjsciowe - gotowy incydent
        /// (albo null przy PASS), jego parametry, liste odmow silnika i liczbe zuzytych rund.
        ///
        /// Wydzielona z MakeIntervalIncidents, bo iterator nie moze miec parametrow out, a caly
        /// sens tej metody to zwrocenie kilku wielkosci naraz.
        ///
        /// DOWOD ZAKONCZENIA: kazda runda albo konczy petle (trafienie lub PASS), albo USUWA
        /// dokladnie jednego kandydata z puli. Pula jest skonczona, a Select na pustej puli zwraca
        /// PASS, wiec petla zatrzymalaby sie i bez licznika. maxSelectionRounds jest budzetem
        /// KOSZTU (CanFireNow bywa drogie), nie zabezpieczeniem poprawnosci.
        /// </summary>
        private NarratorDecision RunSelectionLoop(IIncidentTarget target, DecisionContext context,
                                                  List<ScoredCandidate> pula, ScoredCandidate pass,
                                                  IRandomSource rng,
                                                  out IncidentDef wybrany, out IncidentParms parms,
                                                  out List<string> odmowy, out int rundy)
        {
            wybrany = null;
            parms = null;
            odmowy = new List<string>();
            rundy = 0;

            // Straznik serii liczony RAZ na ture: polityka wyboru celowo nie widzi kontekstu
            // decyzji, bo ma byc funkcja czysta od puli.
            bool straznikSerii = SelectionPolicy.IsPassSuppressedByStreak(context, Props.pass);

            int maxRund = Props.maxSelectionRounds > 0
                ? Props.maxSelectionRounds
                : StorytellerCompProperties_Generative.DefaultMaxSelectionRounds;

            NarratorDecision decyzja = null;

            // STAN TURY, ustalany w rundzie pierwszej i niezmienny do konca tury.
            //
            // brama            - rozstrzygniecie "czy w ogole dzialac". Petla rund jest mechanizmem
            //                    NAPRAWCZYM po odmowie silnika, a nie ciagiem kolejnych decyzji
            //                    narracyjnych, wiec nie wolno jej losowac bramy raz za razem.
            // statystykiTury   - liczniki i ranking z PELNEJ puli. Odrzuc() kurczy pule robocza,
            //                    wiec bez zamrozenia mianownik metryk malalby z kazda odmowa,
            //                    a to obciazenie skorelowane z kontekstem.
            // losowaniaTury    - faktyczne zuzycie rng, sumowane po rundach. NIE liczymy go wzorem:
            //                    zrodlem prawdy jest to, co zaraportowal Select.
            GateOutcome brama = null;
            TurnStats statystykiTury = null;
            int losowaniaTury = 0;

            while (rundy < maxRund)
            {
                rundy++;

                // Pasmo near-best przelicza sie w KAZDEJ rundzie od nowa. To jest cala roznica
                // miedzy "powtorz wybor" a "zejdz po rankingu": po usunieciu lidera BestUtility
                // spada do wyniku drugiego, wiec BandThreshold opada razem z nim i wpuszcza
                // kandydatow, ktorzy wczesniej byli poza pasmem. Zamrozone pasmo losowaloby
                // z przedzialu zaczepionego o opcje, ktorej gra wlasnie odmowila.
                // Prog BEZWZGLEDNY (qualityCutoff) nie przelicza sie nigdy i to on, a nie pasmo,
                // jest gwarancja jakosci w kolejnych rundach.
                decyzja = policy.Select(pula, pass, rng, straznikSerii, brama, statystykiTury);
                decyzja.AttachTurnContext(scorer.LastPassDensity, context.History.ConsecutivePassCount);
                losowaniaTury += decyzja.RandomDraws;

                if (brama == null)
                {
                    // Runda pierwsza jest jedyna, ktora rozstrzyga brame i widzi pelna pule.
                    brama = decyzja.Gate;
                    statystykiTury = decyzja.TurnStats;
                }

                if (decyzja.IsPass)
                {
                    // Pula opustoszala przez ODMOWY SILNIKA, a nie dlatego, ze nigdy nic nie
                    // zawierala. To porazka silnika, nie decyzja narracyjna, i musi byc od niej
                    // odrozniona - inaczej metryka swiadomego milczenia liczy tez tury,
                    // w ktorych narrator chcial cos zrobic i nie mogl.
                    if (odmowy.Count > 0)
                    {
                        // Kazdy PASS zapadly PO odmowie silnika jest odrozniany od czystego.
                        // NoCandidates -> pula opustoszala WYLACZNIE przez odmowy.
                        // Competitive  -> PASS wygral, ale z pula okrojona przez silnik; to NIE
                        //                 jest swiadome milczenie, bo narrator chcial dzialac.
                        // Pozostale powody (AllVetoed, BelowCutoff) opisuja stan sprzed odmow
                        // i zostaja bez zmian - tam pula nie skurczyla sie przez silnik.
                        if (decyzja.PassReason == PassReason.NoCandidates)
                        {
                            decyzja.PassReason = PassReason.AllRefusedByGame;
                        }
                        else if (decyzja.PassReason == PassReason.Competitive)
                        {
                            // NIEOSIAGALNE od czasu przeniesienia bramy na poziom TURY, i to jest
                            // wlasnie zysk z tamtej zmiany: brama zapada w rundzie pierwszej, PRZED
                            // jakakolwiek odmowa silnika. Jesli powiedziala "dzialaj", to kazda
                            // pozniejsza cisza bierze sie z opustoszalej puli, czyli ma powod
                            // NoCandidates (mapowany wyzej na AllRefusedByGame), a nie Competitive.
                            // Galaz zostaje jako WYZWALACZ ALARMOWY: jej wykonanie oznacza, ze
                            // zamrozenie bramy przestalo dzialac i metryka swiadomego milczenia
                            // znowu jest zawyzana tam, gdzie gra duzo odmawia.
                            decyzja.PassReason = PassReason.CompetitiveAfterRefusal;
                            PNLog.Error("PassReason.CompetitiveAfterRefusal wystapil mimo bramy "
                                        + "rozstrzyganej raz na ture - zamrozenie bramy nie dziala. "
                                        + "Metryka swiadomego milczenia jest od tej tury obciazona.");
                        }
                    }
                    break;
                }

                ScoredCandidate zwyciezca = decyzja.Winner;
                ComposedEvent zdarzenie = zwyciezca == null ? null : zwyciezca.Event;

                if (zdarzenie == null)
                {
                    PNLog.Error("Zwyciezca rundy nie ma zlozonego zdarzenia - blad okablowania "
                                + "warstwy decyzyjnej. Kandydat wypada z puli.");
                    if (zwyciezca != null)
                    {
                        Odrzuc(pula, zwyciezca, odmowy, "?: kandydat bez zlozonego zdarzenia");
                    }
                    continue;
                }

                IncidentDef incydent = DefDatabase<IncidentDef>.GetNamedSilentFail(zdarzenie.ActionPayload);
                if (incydent == null)
                {
                    PNLog.Error("Klocek akcji wskazuje na nieistniejacy IncidentDef: " + zdarzenie.ActionPayload);
                    Odrzuc(pula, zwyciezca, odmowy, zdarzenie.ActionPayload + ": brak IncidentDef");
                    continue;
                }

                if (!incydent.TargetAllowed(target))
                {
                    Odrzuc(pula, zwyciezca, odmowy, incydent.defName + ": cel niedozwolony");
                    continue;
                }

                // IncidentParms budowane LENIWIE, wylacznie dla zwyciezcy DANEJ RUNDY.
                // StorytellerUtility.DefaultParmsNow (pod spodem GenerateParms) liczy punkty
                // zagrozenia z bogactwa, kolonistow i krzywych adaptacji - zbudowanie parms dla
                // calego rankingu oznaczaloby dzis 84 takie wywolania na ture zamiast jednego.
                // Scoring nie dotyka IncidentParms w ogole: intensywnosc jest cecha kompozycji,
                // a punkty sa jej TLUMACZENIEM na mechanike, potrzebnym dopiero przy odpaleniu.
                IncidentParms kandydackieParms = GenerateParms(incydent.category, target);
                kandydackieParms.points *= IntensityTable.PointsFactor(zdarzenie.Intensity);

                if (!incydent.Worker.CanFireNow(kandydackieParms))
                {
                    Odrzuc(pula, zwyciezca, odmowy, incydent.defName + ": CanFireNow=false");
                    continue;
                }

                wybrany = incydent;
                parms = kandydackieParms;
                break;
            }

            if (decyzja == null)
            {
                // Nieosiagalne po Sanitize (maxSelectionRounds >= 1), ale pusta decyzja wywrocilaby
                // log i linie danych, wiec budujemy zastepcza zamiast zwracac null.
                decyzja = AwaryjnaDecyzjaPass(pass, "budzet rund <= 0 - polityka nie zostala uruchomiona ani razu");
            }
            else if (wybrany == null && !decyzja.IsPass)
            {
                // Budzet rund wyczerpany, choc pula WCIAZ miala kandydatow. Trzeci rodzaj ciszy,
                // rozny i od decyzji o milczeniu, i od wyczerpania puli odmowami - diagnozuje
                // za ciasny budzet rund, a nie za luzne warunki twarde.
                decyzja.Winner = decyzja.PassCandidate ?? pass;
                decyzja.PassReason = PassReason.RoundBudgetExhausted;
                decyzja.PolicyTrace = (decyzja.PolicyTrace ?? string.Empty)
                                      + " | wyczerpano budzet rund wyboru ("
                                      + maxRund.ToString(CultureInfo.InvariantCulture) + ")";
            }

            // Laczne zuzycie losowosci w CALEJ turze, zsumowane z tego, co zaraportowaly kolejne
            // wywolania Select - a nie policzone wzorem. Po przeniesieniu bramy na poziom tury
            // rozklad jest taki: runda pierwsza 2 pobrania (brama + wybor), kazda kolejna 1
            // (sam wybor), czyli lacznie 1 + liczba rund. Wzor trzymamy w komentarzu, bo zrodlem
            // prawdy ma byc pomiar: gdyby ktos zmienil liczbe etapow, suma nadal bedzie zgodna,
            // a zaszyty wzor po cichu falszowalby kolumne badawcza - dokladnie tak, jak robilo to
            // poprzednie `= rundy`, ktore zanizalo zuzycie dwukrotnie.
            decyzja.RandomDraws = losowaniaTury;
            return decyzja;
        }

        /// <summary>
        /// Odrzucenie kandydata przez silnik gry. USUWA go z puli, a nie tylko oznacza flaga:
        /// Select jest funkcja czysta od puli, wiec przy samym oznaczeniu zwracalby w kazdej
        /// rundzie tego samego zwyciezce az do wyczerpania budzetu - a bez budzetu w nieskonczonosc.
        /// </summary>
        private static void Odrzuc(List<ScoredCandidate> pula, ScoredCandidate kandydat,
                                   List<string> odmowy, string powod)
        {
            kandydat.Rejected = RejectionStage.EngineRefused;
            pula.Remove(kandydat);
            odmowy.Add(powod);
        }

        private static NarratorDecision AwaryjnaDecyzjaPass(ScoredCandidate pass, string slad)
        {
            var decyzja = new NarratorDecision();
            decyzja.Winner = pass;
            decyzja.PassCandidate = pass;
            decyzja.Ranking = new List<ScoredCandidate>();
            if (pass != null)
            {
                decyzja.Ranking.Add(pass);
                decyzja.PassUtility = pass.Utility;
            }
            decyzja.PassReason = PassReason.RoundBudgetExhausted;
            decyzja.PolicyTrace = slad;
            return decyzja;
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
            if (tensionModel != null && string.Equals(activeProfileId, chcianyId, StringComparison.Ordinal))
            {
                return;
            }

            NarratorProfile profil = pamiec == null
                ? NarratorProfile.Fallback()
                : pamiec.ActiveProfile();

            activeProfileId = chcianyId;
            tensionModel = new TensionModel(profil.Tension, Props.contrast);

            // Wagi czynnikow zdarzeniowych pochodza z PROFILU, a nie z bloku <weights>
            // StorytellerDefa. Ten ostatni zostaje jako wartosc awaryjna i jako czesc kanarka
            // konfiguracji w logu startowym.
            scorer = new UtilityScorer(BuildEventFactors(), profil.Weights,
                                       UtilityScorer.BuildPassFactors(Props.pass), Props.pass,
                                       Props.vetoContextFitBelow);

            string problem;
            if (!scorer.Validate(out problem))
            {
                PNLog.Error("Konfiguracja scoringu (profil " + profil.Id + "): " + problem);
            }

            PNLog.Decision("Profil narratora aktywny: " + profil);
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
        private void EnsureRuntime()
        {
            if (runtimeReady)
            {
                return;
            }
            // Ustawiamy flage PRZED budowa, zeby ewentualny problem konfiguracji zostal zgloszony
            // raz, a nie przy kazdym interwale narratora przez cala rozgrywke.
            runtimeReady = true;

            List<Block> blocks;
            CompatibilityGraph graph;
            BlockCatalogLoader.Load(out blocks, out graph);

            composer = new EventComposer(blocks, graph);
            generator = new CandidateGenerator(composer);

            // Scorer NIE powstaje tutaj, tylko w EnsureProfileRuntime: jego wagi naleza
            // do profilu narratora, a profil jest znany dopiero po wczytaniu gry.
            // Tutaj zostaje wylacznie to, co jest wspolne dla wszystkich osobowosci.
            policy = new SelectionPolicy(Props.ToSelectionParameters());

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

            // Druga, niezalezna siec bezpieczenstwa obok PNStartup: gdyby ten comp zostal uzyty
            // w innym StorytellerDefie niz audytowany na starcie, cicha literowka w nazwie czynnika
            // (waga 0, martwy czynnik, zero komunikatow) nadal zostanie zgloszona.
            string problem;
            if (!scorer.Validate(out problem))
            {
                PNLog.Error("Konfiguracja scoringu: " + problem);
            }
            PNLog.Decision("Scoring: " + scorer.DescribeConfiguration());
            PNLog.Decision("Polityka wyboru: " + policy.Parameters.Describe()
                           + " maxSelectionRounds=" + Props.maxSelectionRounds.ToString(CultureInfo.InvariantCulture)
                           + " candidateBudget=" + Props.candidateBudget.ToString(CultureInfo.InvariantCulture));
        }
    }
}
