using System.Collections.Generic;
using System.Globalization;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;
using ProceduralNarrator.Integration.Defs;
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
        /// Pamiec zdarzen narratora, OSOBNA DLA KAZDEJ MAPY (klucz: Map.uniqueID).
        ///
        /// Dlaczego nie jedna wspolna: StorytellerComp istnieje JEDEN na cala gre, nie jeden
        /// na mape. Przy drugiej kolonii wspolny bufor psulby trzy rzeczy naraz, kazda po cichu:
        ///   swiezosc - zdarzenie na kolonii A "zuzywaloby" temat dla kolonii B, wiec narrator
        ///              unikalby na drugiej mapie tego, czego uzyl na pierwszej, bez powodu,
        ///   kontrast - rytm mieszalby dwa niezalezne ciagi, wiec "po serii katastrof" znaczyloby
        ///              katastrofy w zupelnie innym miejscu,
        ///   gestosc PASS - licznik decyzji roslby dwa razy szybciej, wiec narrator uznalby, ze
        ///              jest gesto, i zaczalby milczec na OBU mapach.
        /// Kazda kolonia prowadzi wlasna narracje, wiec kazda ma wlasna pamiec.
        ///
        /// OGRANICZENIE KROKU 3: slownik zyje w instancji komponentu, wiec nie przezywa save/load
        /// (StorytellerComp jest odtwarzany z Defa). Zdejmuje to krok 6, przenoszac pamiec do
        /// GameComponent z ExposeData - i wtedy ten slownik przenosi sie tam w calosci, bez zmiany
        /// ksztaltu. Kazda decyzja loguje mape, histWpisow i histDecyzji wlasnie po to, zeby utrata
        /// pamieci byla widoczna od pierwszego wiersza danych, a nie dopiero po zebraniu serii.
        /// </summary>
        private readonly Dictionary<int, EventHistory> histories = new Dictionary<int, EventHistory>();

        /// <summary>
        /// Pamiec dla danej mapy; tworzona przy pierwszej decyzji na tej mapie.
        /// Map.uniqueID, a NIE Map.Index: indeks jest pozycja na liscie map i przesuwa sie,
        /// gdy gracz porzuci kolonie, wiec pamiec przeskoczylaby wtedy na inna mape.
        /// </summary>
        private EventHistory HistoryFor(Map map)
        {
            int klucz = map != null ? map.uniqueID : -1;
            EventHistory h;
            if (!histories.TryGetValue(klucz, out h))
            {
                h = new EventHistory();
                histories[klucz] = h;
                if (histories.Count > 1)
                {
                    PNLog.Decision("Nowa pamiec narratora dla mapy " + klucz.ToString(CultureInfo.InvariantCulture)
                                   + " (map z wlasna pamiecia: " + histories.Count.ToString(CultureInfo.InvariantCulture)
                                   + "). Kazda kolonia prowadzi osobna narracje.");
                }
            }
            return h;
        }

        private bool runtimeReady;

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

            // Bramka tempa - bez zmian wzgledem kroku 2. mtbDays jest wyprowadzone z czestotliwosci
            // podmienionych compow Cassandry, zeby budzet wydarzen byl porownywalny; krok 4
            // zastapi te stala krzywa dramaturgiczna.
            if (!Rand.MTBEventOccurs(Props.mtbDays, TicksPerDay, TicksPerInterval))
            {
                yield break;
            }

            EnsureRuntime();

            int tick = CurrentTick();
            float gameDay = tick / TicksPerDay;

            // Stan swiata zamrazamy RAZ na cala ture. Wszystkie rundy oceniaja ten sam kontekst,
            // wiec ranking jest wewnetrznie spojny i da sie go w calosci odtworzyc z logu.
            EventHistory history = HistoryFor(map);
            WorldSnapshot snapshot = WorldSnapshotBuilder.Build(map, history, gameDay);
            EventRecipe recipe = BuildRecipe();
            DecisionContext context = DecisionContext.Create(snapshot, history, gameDay, recipe.Intent);

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
                decyzja = policy.Select(pula, pass, rng, straznikSerii);
                decyzja.AttachTurnContext(scorer.LastPassDensity, context.History.ConsecutivePassCount);

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
                            decyzja.PassReason = PassReason.CompetitiveAfterRefusal;
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

            // Select liczy JEDNO pobranie z rng na runde, wiec laczne zuzycie losowosci w turze
            // rowna sie liczbie rund. Tylko warstwa integracji zna te liczbe, wiec ona domyka
            // niezmiennik "losowan == liczba rund, niezaleznie od rozmiaru puli".
            decyzja.RandomDraws = rundy;
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
        /// Miejsce wpiecia warstwy planowania. Intencja jest tu ustawiana w JEDNYM miejscu
        /// i na stale na Hold - krok 4 podmieni te stala na wyjscie krzywej dramaturgicznej.
        /// Przepis NIE filtruje kandydatow po intencji: utility AI ma wazyc, a nie wykluczac,
        /// inaczej slad decyzji nie pokazywalby, ile narrator poswiecil, zeby posluchac krzywej.
        /// </summary>
        private EventRecipe BuildRecipe()
        {
            return new EventRecipe
            {
                RequiredActionTag = Props.requiredActionTag,
                TargetIntensity = 1f,
                Intent = Intent.Hold
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

            // KOLEJNOSC REJESTRACJI CZYNNIKOW JEST CZESCIA FORMATU DANYCH BADAWCZYCH:
            // [contextFit, freshness, dramaticContrast, intentAlignment]. Ustala kolejnosc
            // wierszy sladu w logu czytelnym i musi byc stala miedzy rozgrywkami - jej zmiana
            // uniewaznia porownywalnosc wczesniej zebranych serii.
            // intentAlignment jest wpiety JUZ TERAZ z waga 0: czynnik o wadze zerowej wnosi 0
            // do licznika i 0 do mianownika normalizacji, wiec jest dokladnie neutralny, a nie
            // rozcienczajacy - dzieki temu format logu bedzie identyczny w kroku 3 i 4.
            var czynnikiZdarzen = new IScoringFactor[]
            {
                new Factor_ContextFit(),
                new Factor_Freshness(),
                new Factor_DramaticContrast(),
                new Factor_IntentAlignment()
            };

            scorer = new UtilityScorer(czynnikiZdarzen, Props.weights,
                                       UtilityScorer.BuildPassFactors(Props.pass), Props.pass,
                                       Props.vetoContextFitBelow);
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
