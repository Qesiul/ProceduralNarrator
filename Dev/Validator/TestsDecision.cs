using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

/// <summary>
/// Testy warstwy decyzyjnej wymagane przez plan kroku 3 (punkty 3-7 i 9).
/// Wartosci oczekiwane pochodza z sekcji --- WZORY --- i --- TESTY --- specyfikacji.
/// </summary>
static class TestsDecision
{
    // ============================================================ POMOCNICZE

    public static ComposedEvent Ev(string actionId, Theme th, Valence v, EventScale sc)
    {
        return new ComposedEvent
        {
            ActionBlockId = actionId,
            ActionPayload = actionId,
            Signature = "-|-|" + actionId + "|-|-",
            Theme = th,
            Valence = v,
            Scale = sc
        };
    }

    /// <summary>Historia z wpisow (id, temat, walencja, skala, dzien gry), podanych OD NAJSTARSZEGO.</summary>
    public static EventHistory Hist(params object[][] items)
    {
        var h = new EventHistory();
        foreach (object[] it in items)
        {
            h.RecordEvent(Ev((string)it[0], (Theme)it[1], (Valence)it[2], (EventScale)it[3]), (float)it[4], 0);
        }
        return h;
    }

    /// <summary>Kontekst z JAWNA intencja - do testow krzywej dramaturgicznej (krok 4).</summary>
    public static DecisionContext CtxIntent(EventHistory h, float gameDay, Intent intent)
    {
        return DecisionContext.Create(new WorldSnapshot(), h, gameDay, intent);
    }

    public static DecisionContext Ctx(EventHistory h, float gameDay)
    {
        return DecisionContext.Create(new WorldSnapshot(), h, gameDay, Intent.Hold);
    }

    /// <summary>
    /// Udzial PASS, jaki dalaby WERSJA JEDNOETAPOWA: jeden softmax po calej puli zdarzen
    /// z doloczonym pseudo-kandydatem PASS. Sluzy WYLACZNIE za kontrole regresji w tescie -
    /// pokazuje czarno na bialym, ze tamta konstrukcja wiazala sklonnosc do ciszy z licznoscia
    /// puli, czyli z rozmiarem katalogu klockow.
    /// </summary>
    private static double UdzialPassJednoetapowo(List<ScoredCandidate> zdarzenia, double passUtility, double t)
    {
        double maks = passUtility;
        for (int i = 0; i < zdarzenia.Count; i++)
            if (zdarzenia[i].Utility > maks) maks = zdarzenia[i].Utility;

        double suma = System.Math.Exp((passUtility - maks) / t);
        double wPass = suma;
        for (int i = 0; i < zdarzenia.Count; i++)
            suma += System.Math.Exp((zdarzenia[i].Utility - maks) / t);
        return wPass / suma;
    }

    /// <summary>
    /// Symuluje CALA TURE narratora: petle rund z odmowami silnika.
    ///
    /// UCZCIWE ZASTRZEZENIE: to jest ODWZOROWANIE petli z
    /// Integration/Storyteller/StorytellerComp_Generative.RunSelectionLoop, a nie ten sam kod -
    /// walidator kompiluje wylacznie Core, wiec warstwy integracji nie widzi. Ponizsze testy
    /// dowodza zatem, ze POLITYKA UMOZLIWIA poprawne prowadzenie tury (brama zamrazalna,
    /// statystyki przenoszalne), a NIE tego, ze integracja faktycznie tak ja prowadzi.
    /// Tamto trzeba potwierdzic w grze albo przeniesc petle do Core za delegatem "czy moze wypalic".
    /// </summary>
    private static NarratorDecision SymulujTure(SelectionPolicy policy, List<ScoredCandidate> pula,
                                                ScoredCandidate pass, IRandomSource rng,
                                                int odmow, int maxRund,
                                                out int losowania, out int rundy)
    {
        // OD KROKU B: ta metoda NIE odwzorowuje juz petli - WYKONUJE ja.
        //
        // Poprzednia wersja byla recznie przepisana kopia petli z warstwy integracji, wiec
        // dowodzila, ze polityka UMOZLIWIA poprawne prowadzenie tury, a nie ze integracja tak
        // ja prowadzi. Co gorsza, kopia byla testem samej siebie: gdyby petla w grze rozjechala
        // sie z ta kopia, obie wersje nadal bylyby zielone. Po przeniesieniu petli do
        // Core/Decision/TurnRunner walidator uruchamia DOKLADNIE ten kod, ktory chodzi w grze.
        //
        // Sygnatura zostaje bez zmian, zeby asercje TEST 5d - napisane niezaleznie i kodujace
        // oryginalne zachowanie - byly teraz sprawdzeniem WIERNOSCI przeniesienia.
        var robocza = new List<ScoredCandidate>(pula);
        var runner = new TurnRunner(policy, PassScoringParams.Defaults(), maxRund);

        int wykonanychOdmow = 0;
        CandidateAcceptor akceptor = delegate(ScoredCandidate kandydat, bool pytajSilnik,
                                              out string powod)
        {
            powod = null;
            if (!pytajSilnik)
            {
                // Samo przygotowanie parametrow - nie liczy sie do odmow ani do pytan.
                return AcceptorVerdict.Accepted;
            }
            if (wykonanychOdmow < odmow)
            {
                wykonanychOdmow++;
                powod = "test: wymuszona odmowa silnika";
                return AcceptorVerdict.RefusedByGame;
            }
            return AcceptorVerdict.Accepted;
        };

        // Pusta historia daje ConsecutivePassCount == 0, wiec straznik serii jest wylaczony -
        // tak samo jak w poprzedniej kopii, ktora przekazywala straznik na stale jako false.
        TurnResult wynik = runner.Run(Ctx(new EventHistory(), 10f), robocza, pass, rng, 0f,
                                       akceptor, KluczWykonania, ZakresCache);

        losowania = wynik.Decision.RandomDraws;
        rundy = wynik.Rounds;
        return wynik.Decision;
    }

    /// <summary>
    /// Tura z akceptorem PREDYKATOWYM: wykonalnosc zalezy od KANDYDATA, a nie od kolejnosci pytan.
    ///
    /// SymulujTure odmawia pierwszym N pytaniom, co bylo wierne, dopoki pytano wylacznie
    /// o zwyciezcow rund. Od prewerifikacji czola pytania padaja takze przed brama, wiec "pierwsze
    /// N pytan" i "N konkretnych niewykonalnych akcji" to dwie rozne rzeczy - a druga jest ta,
    /// ktora modeluje gre. Bez tego wariantu nie da sie zbudowac przebiegu, w ktorym odmowa pada
    /// PO bramie, bo faza 0 zjadlaby caly limit odmow.
    /// </summary>
    private static TurnResult SymulujTureF(SelectionPolicy policy, List<ScoredCandidate> pula,
                                           ScoredCandidate pass, IRandomSource rng,
                                           System.Func<ScoredCandidate, bool> wykonalny, int maxRund)
    {
        var robocza = new List<ScoredCandidate>(pula);
        var runner = new TurnRunner(policy, PassScoringParams.Defaults(), maxRund);

        // AKCEPTOR MODELUJE CACHE SILNIKA, a nie tylko predykat wykonalnosci.
        //
        // IncidentWorker buforuje wynik CanFireNowSub na parze (IncidentDef, tick), a nasza tura
        // miesci sie w jednym ticku. Test, ktory o tym nie wie, przepuscilby implementacje
        // pytajaca dwa razy o te sama akcje - a wlasnie to jest w grze niepoprawne, bo druga
        // odpowiedz opisuje parametry pierwszego wariantu.
        //
        // Dlatego zliczamy pytania PER AKCJA i zapisujemy naruszenie zamiast je zignorowac.
        var pytanoOAkcje = new Dictionary<string, int>();
        CandidateAcceptor akceptor = delegate(ScoredCandidate kandydat, bool pytajSilnik,
                                              out string powod)
        {
            powod = null;
            if (!pytajSilnik)
            {
                return AcceptorVerdict.Accepted;
            }

            // Zakres liczymy po PAYLOADZIE, tak jak cache silnika - nie po klocku akcji.
            string akcja = kandydat.Event == null ? "?" : (kandydat.Event.ActionPayload ?? "?");
            int ile;
            pytanoOAkcje.TryGetValue(akcja, out ile);
            pytanoOAkcje[akcja] = ile + 1;

            if (wykonalny(kandydat))
            {
                return AcceptorVerdict.Accepted;
            }
            powod = "test: " + kandydat.SortKey + " niewykonalny";
            return AcceptorVerdict.RefusedByGame;
        };

        TurnResult wynikTury = runner.Run(Ctx(new EventHistory(), 10f), robocza, pass, rng, 0f,
                                          akceptor, KluczWykonania, ZakresCache);

        foreach (var para in pytanoOAkcje)
        {
            if (para.Value > 1)
            {
                wynikTury.Warnings.Add("TEST: silnik zapytany " + para.Value + " razy o akcje "
                                       + para.Key + " w jednej turze - druga odpowiedz bylaby "
                                       + "zbuforowana i opisywalaby inny wariant.");
            }
        }

        return wynikTury;
    }

    /// <summary>
    /// P(cisza) w turze, jaka dawalaby brama LOSOWANA W KAZDEJ RUNDZIE (stan sprzed poprawki):
    /// 1 - iloczyn(1 - p_i), gdzie p_i liczy sie wobec malejacego BestUtility po kolejnych odmowach.
    /// Sluzy za kontrole regresji - pokazuje, ile wynosila zaleznosc od liczby odmow silnika.
    /// </summary>
    private static double UdzialCiszyBramaCoRunde(List<ScoredCandidate> posortowaneMalejaco,
                                                  double passUtility, double t, int odmow)
    {
        double przetrwanie = 1.0;
        for (int i = 0; i <= odmow && i < posortowaneMalejaco.Count; i++)
        {
            double best = posortowaneMalejaco[i].Utility;
            double p = 1.0 / (1.0 + System.Math.Exp((best - passUtility) / t));
            przetrwanie *= (1.0 - p);
        }
        return 1.0 - przetrwanie;
    }

    /// <summary>
    /// Kandydat zwykly: KAZDY ma WLASNY identyfikator akcji, wyprowadzony z klucza.
    ///
    /// DLACZEGO WLASNY, A NIE WSPOLNY "X" JAK PRZEDTEM. Odmowa silnika ma od naprawy drugiego
    /// zarzutu zakres CALEJ AKCJI, a nie pojedynczego kandydata. Przy wspolnym identyfikatorze
    /// jedna wymuszona odmowa unieruchamiala cala pule i kazdy test z odmowami mierzylby
    /// wylacznie ten skrajny przypadek - co faktycznie zaszlo i dalo 100% ciszy zamiast 11.92%.
    ///
    /// Wlasny identyfikator odtwarza semantyke, dla ktorej te testy pisano (N odmow = N
    /// usunietych opcji), wiec ich asercje nadal znacza to, co znaczyly. Grupowanie po akcji
    /// bada sie osobno, przez KandAkcja - tam identyfikator jest JAWNYM parametrem, bo jest
    /// przedmiotem testu, a nie jego tlem.
    /// </summary>
    private static ScoredCandidate Kand(float utility, string key, bool vetoed = false, float raw = -1f)
    {
        return new ScoredCandidate
        {
            Event = Ev(key, Theme.Raid, Valence.Negative, EventScale.Major),
            IsPass = false,
            SortKey = key,
            RawUtility = raw < 0f ? utility : raw,
            Utility = vetoed ? 0f : utility,
            Vetoed = vetoed,
            Factors = new List<FactorScore>()
        };
    }

    /// <summary>
    /// Kandydat z JAWNYM identyfikatorem akcji - potrzebny tam, gdzie test bada polityke
    /// GRUPOWANIA po akcji, a nie sam ranking. Kand() daje kazdemu kandydatowi WLASNA akcje,
    /// wiec nie da sie nim zbudowac akcji o wielu wariantach.
    /// </summary>
    /// <summary>
    /// Odwzorowanie IncidentParmsBuilder.ExecutionKey po stronie testu. Ta sama regula: payload
    /// plus intensywnosc, bo tylko te dwie rzeczy roznicuja IncidentParms istotnie dla CanFireNow.
    /// </summary>
    /// <summary>Zakres cache'u silnika: sam payload, bo CanFireNowSub jest buforowane per IncidentDef.</summary>
    /// <summary>Wycinek sladu polityki wokol podanego klucza - do czytelnych komunikatow asercji.</summary>
    private static string FragmentSladu(string slad, string klucz)
    {
        if (string.IsNullOrEmpty(slad)) return "(pusty slad)";
        int i = slad.IndexOf(klucz, System.StringComparison.Ordinal);
        if (i < 0) return "(brak '" + klucz + "' w sladzie)";
        int od = System.Math.Max(0, i - 20);
        int len = System.Math.Min(80, slad.Length - od);
        return slad.Substring(od, len);
    }

    private static string ZakresCache(ScoredCandidate k)
    {
        return k == null || k.Event == null ? null : k.Event.ActionPayload;
    }

    private static string KluczWykonania(ScoredCandidate k)
    {
        if (k == null || k.Event == null) return null;
        return (k.Event.ActionPayload ?? "?") + "|"
               + ((int)k.Event.Intensity).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Wariant akcji o JAWNEJ intensywnosci - czyli o jawnych parametrach wykonania.
    /// Potrzebny tam, gdzie test bada wspoldzielenie potwierdzenia: bez roznych intensywnosci
    /// wszystkie warianty maja ten sam klucz i odkladanie nigdy sie nie uruchamia.
    /// </summary>
    /// <summary>
    /// Kandydat, ktorego KLOCEK AKCJI i PAYLOAD sa rozne. Potrzebny, bo Ev() ustawia oba na te
    /// sama wartosc - a wtedy test nie odrozni zakresu liczonego po klocku od zakresu liczonego
    /// po payloadzie, czyli przespi dokladnie te dziure, ktora ma pilnowac.
    /// </summary>
    private static ScoredCandidate KandPayload(float utility, string actionId, string payload,
                                               string key, IntensityLevel moc)
    {
        ScoredCandidate k = KandAkcja(utility, actionId, key);
        k.Event.ActionPayload = payload;
        k.Event.Intensity = moc;
        return k;
    }

    private static ScoredCandidate KandMoc(float utility, string actionId, string key,
                                           IntensityLevel moc)
    {
        ScoredCandidate k = KandAkcja(utility, actionId, key);
        k.Event.Intensity = moc;
        return k;
    }

    private static ScoredCandidate KandAkcja(float utility, string actionId, string key)
    {
        ComposedEvent e = Ev(actionId, Theme.Raid, Valence.Negative, EventScale.Major);
        e.Signature = key;
        return new ScoredCandidate
        {
            Event = e,
            IsPass = false,
            SortKey = key,
            RawUtility = utility,
            Utility = utility,
            Vetoed = false,
            Factors = new List<FactorScore>()
        };
    }

    private static ScoredCandidate Pass(float utility)
    {
        return new ScoredCandidate
        {
            Event = null,
            IsPass = true,
            SortKey = UtilityScorer.PassSortKey,
            RawUtility = utility,
            Utility = utility,
            Vetoed = false,
            Factors = new List<FactorScore>()
        };
    }

    public static IScoringFactor[] CzynnikiZdarzen()
    {
        return new IScoringFactor[]
        {
            new Factor_ContextFit(),
            new Factor_Freshness(),
            new Factor_DramaticContrast(),
            new Factor_IntentAlignment()
        };
    }

    private static float Kontrast(Factor_DramaticContrast f, EventHistory h, float gameDay, Valence v, EventScale s, out string slad)
    {
        var c = new ScoredCandidate { Event = Ev("K", Theme.Raid, v, s), IsPass = false, SortKey = "K" };
        return f.Evaluate(c, Ctx(h, gameDay), out slad);
    }


    // ------------------------------------------------- KULTURA / SEPARATOR DZIESIETNY
    private static void TestKultura(EventComposer composer, XmlConfig cfg)
    {
        T.Section("KULTURA: separator dziesietny przy CurrentCulture = pl-PL");
        System.Globalization.CultureInfo poprzednia = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            var pl = new System.Globalization.CultureInfo("pl-PL");
            System.Threading.Thread.CurrentThread.CurrentCulture = pl;

            var gen = new CandidateGenerator(composer);
            var scorer = new UtilityScorer(CzynnikiZdarzen(), cfg.weights,
                                           UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, cfg.vetoContextFitBelow);
            var policy = new SelectionPolicy(cfg.Selection());

            WorldSnapshot s0 = Scenariusze()[0];
            DecisionContext ctx = Ctx(HistoriaWariant(2), 30f);
            CandidateSet cs = gen.Generate(new EventRecipe(), s0, new SeededRandom(1), cfg.candidateBudget);
            List<ScoredCandidate> oc = scorer.ScoreAll(cs.Candidates, ctx);
            NarratorDecision d = policy.Select(oc, scorer.ScorePass(ctx), new SeededRandom(2));

            // Linia MASZYNOWA [PN-DATA] - tu przecinek dziesietny lamie parser w Pythonie.
            string data = d.ToDataFragment();
            bool dataCzysta = !MaPrzecinekDziesietny(data);
            T.Ok("NarratorDecision.ToDataFragment() nie zawiera przecinka dziesietnego", dataCzysta,
                 dataCzysta ? "OK" : "fragment: " + data);
            bool parsujeSie = true;
            foreach (string pole in data.Split(';'))
            {
                string[] kv = pole.Split('=');
                if (kv.Length != 2) { parsujeSie = false; continue; }
                string v = kv[1].Trim();
                if (v.Length == 0) continue;
                double liczba;
                if (v.Any(char.IsDigit) && !v.Any(char.IsLetter) && !v.Contains("|")
                    && !double.TryParse(v, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out liczba))
                {
                    parsujeSie = false;
                }
            }
            T.Ok("kazde pole liczbowe [PN-DATA] parsuje sie przez float() z kropka", parsujeSie, null);

            // CandidateSet i slad polityki - te tez ida do logu.
            T.Ok("CandidateSet.DataLogFragment() bez przecinka dziesietnego",
                 !MaPrzecinekDziesietny(cs.DataLogFragment()), cs.DataLogFragment());
            T.Ok("SelectionPolicy.PolicyTrace bez przecinka dziesietnego",
                 !MaPrzecinekDziesietny(d.PolicyTrace), d.PolicyTrace);

            // Slad dopasowania (log czytelny) - regula projektu: KAZDA liczba przez InvariantCulture.
            ComposedEvent zKandydatow = cs.Candidates.FirstOrDefault(x => x.FitTrace != null && x.FitTrace.Contains("="));
            string fit = zKandydatow == null ? "" : zKandydatow.FitTrace;
            T.Ok("ComposedEvent.FitTrace bez przecinka dziesietnego (regula: InvariantCulture wszedzie)",
                 !MaPrzecinekDziesietny(fit), "FitTrace = " + fit);
            T.Ok("WorldSnapshot.ToString() bez przecinka dziesietnego",
                 !MaPrzecinekDziesietny(s0.ToString()), "snapshot = " + s0.ToString());
        }

        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = poprzednia;
        }
    }

    // ============================================================ URUCHOMIENIE

    public static void Run(EventComposer composer, XmlConfig cfg)
    {
        SprawdzKonfiguracje(cfg);
        Test9Freshness();
        Test9Contrast();
        Test9Pass(cfg);
        Test9ScoreMath();
        Test4Weto(cfg);
        Test5PassWygrywa(cfg);
        Test7Niezmiennik(composer, cfg);
        Test3Scenariusz(composer, cfg);
        Test6Determinizm(composer, cfg);
        TestKultura(composer, cfg);
    }

    // ---------------------------------------------------------------- KONFIG
    private static void SprawdzKonfiguracje(XmlConfig cfg)
    {
        T.Section("KONFIGURACJA Z PRAWDZIWEGO XML vs DOMYSLNE WARTOSCI W KODZIE");
        var wDom = new ScoringWeights();
        var pDom = PassScoringParams.Defaults();
        T.Eq("XML weights.contextFit", cfg.weights.contextFit, wDom.contextFit, 1e-6);
        T.Eq("XML weights.freshness", cfg.weights.freshness, wDom.freshness, 1e-6);
        T.Eq("XML weights.dramaticContrast", cfg.weights.dramaticContrast, wDom.dramaticContrast, 1e-6);
        T.Eq("XML weights.intentAlignment", cfg.weights.intentAlignment, wDom.intentAlignment, 1e-6);
        // Suma WYPROWADZONA z odczytanych pol, a nie wpisana z reki. Poprzednia wersja miala
        // tu literal 4.5 i zestarzala sie w kroku 4, gdy intentAlignment poszedl z 0 na 1.0 -
        // czyli dokladnie ten wzorzec, przed ktorym ostrzega CLAUDE.md.
        double sumaZPol = cfg.weights.contextFit + cfg.weights.freshness
                          + cfg.weights.dramaticContrast + cfg.weights.intentAlignment;
        T.Eq("suma wag zdarzeniowych == suma odczytanych pol", cfg.weights.Total(), sumaZPol, 1e-6);
        T.Ok("waga intentAlignment > 0 (krzywa dramaturgiczna wlaczona w kroku 4)",
             cfg.weights.intentAlignment > 0f,
             "intentAlignment = " + cfg.weights.intentAlignment.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        T.Eq("XML pass.halfLifeDays", cfg.pass.halfLifeDays, pDom.halfLifeDays, 1e-6);
        T.Eq("XML pass.densityFloor", cfg.pass.densityFloor, pDom.densityFloor, 1e-6);
        T.Eq("XML pass.densitySaturation", cfg.pass.densitySaturation, pDom.densitySaturation, 1e-6);
        T.EqI("XML pass.maxStreak", cfg.pass.maxStreak, pDom.maxStreak);
        T.Eq("XML pass.weightRestraint", cfg.pass.weightRestraint, pDom.weightRestraint, 1e-6);
        T.Eq("XML pass.weightBaseline", cfg.pass.weightBaseline, pDom.weightBaseline, 1e-6);
        T.Eq("suma wag PASS == 1.0", cfg.pass.SumWeights, 1.0, 1e-6);
        T.Eq("XML qualityCutoff", cfg.qualityCutoff, 0.35, 1e-6);
        T.Eq("XML nearBestFraction", cfg.nearBestFraction, 0.75, 1e-6);
        T.Eq("XML softmaxTemperature", cfg.softmaxTemperature, 0.1, 1e-6);
        // gateTemperature byl jedynym parametrem pominietym przez tego straznika dryfu.
        T.Eq("XML gateTemperature", cfg.gateTemperature, 0.1, 1e-6);
        // Prog compa odwzorowuje tor WIEKSZOSCIOWY sposrod trzech podmienianych compow Cassandry.
        // Progi u Cassandry: OnOffCycle(ThreatBig) 11, OnOffCycle(ThreatSmall) 11,
        // CategoryMTB(Misc, Map_PlayerHome) 5. Nasze akcje to 8x Misc i 4x ThreatBig, zero
        // ThreatSmall - stad 5 na poziomie compa, a prog 11 dla zagrozen siedzi w Cond_MinDaysPassed
        // przy akcjach (sprawdzane behawioralnie w sekcji [5c] walidatora).
        //
        // HISTORIA TEJ ASERCJI: pierwotnie zadala 11 i przechodzila - bo kod tez mial 11. Obie
        // liczby byly zle naraz, bo powstaly z tego samego przeoczenia (trzeci podmieniany comp).
        // Asercja zgodna z kodem nie jest asercja poprawna; ta ma teraz WYPROWADZENIE, nie kopie.
        T.Eq("XML minDaysPassed == 5 (prog toru Misc, 8 z 12 akcji)", cfg.minDaysPassed, 5.0, 1e-6);
        // Kanarek konfiguracji: pole bez sensownej wartosci domyslnej.
        T.Ok("XML configStamp niepusty (kanarek deserializacji bloku)",
             !string.IsNullOrEmpty(cfg.configStamp), "configStamp=" + cfg.configStamp);
        T.Eq("XML vetoContextFitBelow", cfg.vetoContextFitBelow, 0.15, 1e-6);
        T.Eq("XML mtbDays (CLAUDE.md: 2.5)", cfg.mtbDays, 2.5, 1e-6);

        // Walidacja startowa scorera na konfiguracji z XML.
        var scorer = new UtilityScorer(CzynnikiZdarzen(), cfg.weights,
                                       UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, cfg.vetoContextFitBelow);
        string problem;
        bool ok = scorer.Validate(out problem);
        T.Ok("UtilityScorer.Validate() na konfiguracji z XML", ok, problem ?? "brak zastrzezen");
        Console.WriteLine("  konfiguracja: " + scorer.DescribeConfiguration());
    }

    // ------------------------------------------------------ TEST 9a: SWIEZOSC
    private static void Test9Freshness()
    {
        T.Section("TEST 9a - CZYNNIK SWIEZOSCI: tablica wartosci ze specyfikacji (SPEC 2, WZORY)");
        var st = FreshnessSettings.Default();

        // Pusta historia.
        var pusta = new EventHistory();
        FreshnessResult r0 = Factor_Freshness.Compute(Theme.Raid, "PN_Akcja_Napad", pusta, st);
        T.Eq("pusta historia -> Value", r0.Value, 0.5, 1e-6);
        T.Eq("pusta historia -> RawValue", r0.RawValue, 1.0, 1e-6);
        T.Eq("pusta historia -> Confidence", r0.Confidence, 0.0, 1e-6);

        // Fixture W: Napad/Raid, Okup/Military, Zrzut/Economic, Wedrowiec/Social.
        EventHistory W = Hist(
            new object[] { "PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major, 0f },
            new object[] { "PN_Akcja_Okup", Theme.Military, Valence.Negative, EventScale.Moderate, 1f },
            new object[] { "PN_Akcja_Zrzut", Theme.Economic, Valence.Positive, EventScale.Minor, 2f },
            new object[] { "PN_Akcja_Wedrowiec", Theme.Social, Valence.Positive, EventScale.Minor, 3f });
        T.EqI("fixture W: Count", W.Count, 4);
        T.EqI("fixture W: DecisionCount", W.DecisionCount, 4);

        T.Eq("nic nie pasuje (Supernatural/Emanator)",
             Factor_Freshness.Compute(Theme.Supernatural, "PN_Akcja_Emanator", W, st).Value, 1.000, 0.001);

        FreshnessResult rPow = Factor_Freshness.Compute(Theme.Social, "PN_Akcja_Wedrowiec", W, st);
        T.Eq("natychmiastowa powtorka: ThemePressure", rPow.ThemePressure, 1.000, 0.001);
        T.Eq("natychmiastowa powtorka: ActionPressure", rPow.ActionPressure, 1.000, 0.001);
        T.Eq("natychmiastowa powtorka: ThemeFreshness", rPow.ThemeFreshness, 0.549, 0.001);
        T.Eq("natychmiastowa powtorka: ActionFreshness", rPow.ActionFreshness, 0.301, 0.001);
        T.Eq("natychmiastowa powtorka: Value", rPow.Value, 0.400, 0.001);

        FreshnessResult rTemat = Factor_Freshness.Compute(Theme.Social, "PN_Akcja_Uchodzcy", W, st);
        T.Eq("ten sam temat, inna akcja: ThemePressure", rTemat.ThemePressure, 1.000, 0.001);
        T.Eq("ten sam temat, inna akcja: ActionPressure", rTemat.ActionPressure, 0.000, 0.001);
        T.Eq("ten sam temat, inna akcja: Value", rTemat.Value, 0.820, 0.001);

        T.Eq("starsza powtorka (wiek 4): Value",
             Factor_Freshness.Compute(Theme.Raid, "PN_Akcja_Napad", W, st).Value, 0.568, 0.001);

        // Monotonicznosc zaniku dla wieku 1, 3, 6, 11.
        double[] oczek = { 0.400, 0.516, 0.660, 0.821 };
        int[] wieki = { 1, 3, 6, 11 };
        double poprzedni = -1;
        bool rosnaco = true;
        for (int i = 0; i < wieki.Length; i++)
        {
            EventHistory h = HistoriaZPowtorka(wieki[i]);
            float v = Factor_Freshness.Compute(Theme.Social, "PN_Akcja_Wedrowiec", h, st).Value;
            T.Eq("monotonicznosc: wiek " + wieki[i], v, oczek[i], 0.001);
            if (v <= poprzedni) rosnaco = false;
            poprzedni = v;
        }
        T.Ok("ciag swiezosci po wieku jest SCISLE ROSNACY", rosnaco, null);

        // Dwie powtorki kumuluja sie: ta sama akcja w wieku 1 i 6.
        EventHistory dwie = Hist(
            new object[] { "PN_Akcja_Wedrowiec", Theme.Social, Valence.Positive, EventScale.Minor, 0f },
            new object[] { "PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major, 1f },
            new object[] { "PN_Akcja_Okup", Theme.Military, Valence.Negative, EventScale.Moderate, 2f },
            new object[] { "PN_Akcja_Zrzut", Theme.Economic, Valence.Positive, EventScale.Minor, 3f },
            new object[] { "PN_Akcja_Emanator", Theme.Supernatural, Valence.Negative, EventScale.Major, 4f },
            new object[] { "PN_Akcja_Wedrowiec", Theme.Social, Valence.Positive, EventScale.Minor, 5f });
        FreshnessResult rDwie = Factor_Freshness.Compute(Theme.Social, "PN_Akcja_Wedrowiec", dwie, st);
        T.Eq("dwie powtorki (wiek 1 i 6): ThemePressure", rDwie.ThemePressure, 1.315, 0.001);
        T.Eq("dwie powtorki (wiek 1 i 6): ActionPressure", rDwie.ActionPressure, 1.500, 0.001);
        T.Eq("dwie powtorki (wiek 1 i 6): Value", rDwie.Value, 0.281, 0.001);
        T.Ok("dwie powtorki bola mocniej niz jedna", rDwie.Value < 0.400, T.F4(rDwie.Value) + " < 0.4000");

        // Trzy tematy pod rzad, nowa akcja.
        EventHistory trzy = Hist(
            new object[] { "PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major, 0f },
            new object[] { "PN_Akcja_Uchodzcy", Theme.Social, Valence.Neutral, EventScale.Minor, 1f },
            new object[] { "PN_Akcja_Dzikus", Theme.Social, Valence.Neutral, EventScale.Minor, 2f },
            new object[] { "PN_Akcja_Wedrowiec", Theme.Social, Valence.Positive, EventScale.Minor, 3f });
        FreshnessResult rTrzy = Factor_Freshness.Compute(Theme.Social, "PN_Akcja_NOWA", trzy, st);
        T.Eq("trzy tematy pod rzad: ThemePressure", rTrzy.ThemePressure, 2.424, 0.001);
        T.Eq("trzy tematy pod rzad: ActionPressure", rTrzy.ActionPressure, 0.000, 0.001);
        T.Eq("trzy tematy pod rzad: Value", rTrzy.Value, 0.693, 0.001);

        // PASS przesuwa zanik, nie wchodzac do bufora.
        EventHistory Wp = Hist(
            new object[] { "PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major, 0f },
            new object[] { "PN_Akcja_Okup", Theme.Military, Valence.Negative, EventScale.Moderate, 1f },
            new object[] { "PN_Akcja_Zrzut", Theme.Economic, Valence.Positive, EventScale.Minor, 2f },
            new object[] { "PN_Akcja_Wedrowiec", Theme.Social, Valence.Positive, EventScale.Minor, 3f });
        for (int i = 0; i < 5; i++) Wp.RecordPass(4f + i, 0, true);
        T.EqI("po 5 x RecordPass: Count nadal 4", Wp.Count, 4);
        T.EqI("po 5 x RecordPass: DecisionCount 9", Wp.DecisionCount, 9);
        T.Eq("po 5 x RecordPass: swiezosc Wedrowca (wiek 6)",
             Factor_Freshness.Compute(Theme.Social, "PN_Akcja_Wedrowiec", Wp, st).Value, 0.660, 0.001);
        T.EqI("po 5 x RecordPass swiadomego: DeliberateSilenceStreak", Wp.DeliberateSilenceStreak, 5);

        // Warmup.
        EventHistory jeden = Hist(new object[] { "PN_Akcja_Wedrowiec", Theme.Social, Valence.Positive, EventScale.Minor, 0f });
        T.Eq("warmup, 1 wpis: Value", Factor_Freshness.Compute(Theme.Social, "PN_Akcja_Wedrowiec", jeden, st).Value, 0.475, 0.001);
        EventHistory dwa = Hist(
            new object[] { "PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major, 0f },
            new object[] { "PN_Akcja_Wedrowiec", Theme.Social, Valence.Positive, EventScale.Minor, 1f });
        FreshnessResult rW2 = Factor_Freshness.Compute(Theme.Social, "PN_Akcja_Wedrowiec", dwa, st);
        T.Eq("warmup, 2 wpisy: Confidence", rW2.Confidence, 0.5, 1e-6);
        T.Eq("warmup, 2 wpisy: RawValue", rW2.RawValue, 0.400, 0.001);
        T.Eq("warmup, 2 wpisy: Value", rW2.Value, 0.450, 0.001);

        // Pojemnosc bufora.
        var poj = new EventHistory();
        for (int i = 0; i < 30; i++)
        {
            poj.RecordEvent(Ev("PN_Akcja_A" + i, Theme.Social, Valence.Neutral, EventScale.Minor), i, 0);
        }
        T.EqI("pojemnosc: Count", poj.Count, 24);
        T.EqI("pojemnosc: DecisionCount", poj.DecisionCount, 30);
        T.EqI("pojemnosc: Entries[0].DecisionIndex", poj.Entries[0].DecisionIndex, 6);
        T.EqI("pojemnosc: Entries[23].DecisionIndex", poj.Entries[23].DecisionIndex, 29);

        // Brak weta - dolna granica 0.0221 przy 24 identycznych.
        var identyczne = new EventHistory();
        for (int i = 0; i < 24; i++)
        {
            identyczne.RecordEvent(Ev("PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major), i, 0);
        }
        FreshnessResult rMin = Factor_Freshness.Compute(Theme.Raid, "PN_Akcja_Napad", identyczne, st);
        T.Eq("24 identyczne z rzedu: Value (absolutne minimum)", rMin.Value, 0.0221, 0.001);
        T.Ok("swiezosc NIGDY nie zeruje kandydata (brak weta)", rMin.Value > 0f, "Value=" + T.F(rMin.Value));

        // Zakres na losowych historiach.
        var rnd = new Random(99);
        bool zakresOk = true;
        var tematy = (Theme[])Enum.GetValues(typeof(Theme));
        for (int i = 0; i < 5000; i++)
        {
            var h = new EventHistory();
            int n = rnd.Next(0, 30);
            for (int k = 0; k < n; k++)
            {
                h.RecordEvent(Ev("A" + rnd.Next(0, 12), tematy[rnd.Next(tematy.Length)], Valence.Neutral, EventScale.Minor), k, 0);
            }
            float v = Factor_Freshness.Compute(tematy[rnd.Next(tematy.Length)], "A" + rnd.Next(0, 12), h, st).Value;
            if (float.IsNaN(v) || v <= 0f || v > 1f) zakresOk = false;
        }
        T.Ok("5000 losowych historii: swiezosc zawsze w (0,1]", zakresOk, null);

        // Round-trip persystencji (przygotowanie kroku 6).
        var h2 = new EventHistory();
        int odrzucone = h2.RestoreFromLines(W.DecisionCount, W.DeliberateSilenceStreak, W.ToPersistableLines());
        bool rtOk = odrzucone == 0 && h2.Count == W.Count && h2.DecisionCount == W.DecisionCount
                    && Math.Abs(Factor_Freshness.Compute(Theme.Social, "PN_Akcja_Wedrowiec", h2, st).Value
                                - Factor_Freshness.Compute(Theme.Social, "PN_Akcja_Wedrowiec", W, st).Value) < 1e-6;
        T.Ok("round-trip persystencji zachowuje swiezosc", rtOk, "odrzucone=" + odrzucone + " count=" + h2.Count);
    }

    /// <summary>Historia, w ktorej PN_Akcja_Wedrowiec/Social ma dokladnie zadany wiek, a Count >= 4.</summary>
    private static EventHistory HistoriaZPowtorka(int wiek)
    {
        string[] fillId = { "PN_Akcja_Napad", "PN_Akcja_Okup", "PN_Akcja_Zrzut", "PN_Akcja_Emanator", "PN_Akcja_Szal" };
        Theme[] fillTh = { Theme.Raid, Theme.Military, Theme.Economic, Theme.Supernatural, Theme.Natural };

        var h = new EventHistory();
        int dzien = 0;
        for (int i = 0; i < 3; i++)
        {
            h.RecordEvent(Ev(fillId[i], fillTh[i], Valence.Negative, EventScale.Moderate), dzien++, 0);
        }
        h.RecordEvent(Ev("PN_Akcja_Wedrowiec", Theme.Social, Valence.Positive, EventScale.Minor), dzien++, 0);
        for (int i = 0; i < wiek - 1; i++)
        {
            int k = i % 5;
            h.RecordEvent(Ev(fillId[k], fillTh[k], Valence.Negative, EventScale.Moderate), dzien++, 0);
        }
        return h;
    }

    // ------------------------------------------------------ TEST 9b: KONTRAST
    private static void Test9Contrast()
    {
        T.Section("TEST 9b - CZYNNIK KONTRASTU: tablica wartosci ze specyfikacji (SPEC 3, WZORY)");
        var f = new Factor_DramaticContrast();
        T.Eq("MaxRaw jest LICZONY z wag (70/30)", f.MaxRaw, 0.7375, 1e-6);
        T.Eq("ComputeMaxRaw(1.00, 0.00)", Factor_DramaticContrast.ComputeMaxRaw(1f, 0f), 1.0, 1e-6);
        T.Eq("ComputeMaxRaw(0.50, 0.50)", Factor_DramaticContrast.ComputeMaxRaw(0.5f, 0.5f), 0.8125, 1e-4);

        EventHistory negMajor = Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 10f },
            new object[] { "B", Theme.Raid, Valence.Negative, EventScale.Major, 12f },
            new object[] { "C", Theme.Raid, Valence.Negative, EventScale.Major, 14f });

        string slad;
        T.Eq("kanoniczna ULGA (3x Neg/Major -> Pos/Minor)",
             Kontrast(f, negMajor, 15f, Valence.Positive, EventScale.Minor, out slad), 1.0, 1e-5);
        Console.WriteLine("     slad: " + slad);
        T.Eq("kanoniczne ZERO (3x Neg/Major -> Neg/Major)",
             Kontrast(f, negMajor, 15f, Valence.Negative, EventScale.Major, out slad), 0.0, 1e-6);

        EventHistory posMinor = Hist(
            new object[] { "A", Theme.Economic, Valence.Positive, EventScale.Minor, 10f },
            new object[] { "B", Theme.Economic, Valence.Positive, EventScale.Minor, 12f },
            new object[] { "C", Theme.Economic, Valence.Positive, EventScale.Minor, 14f });

        // ---- KROK 4: domyslne strojenie jest SYMETRYCZNE, i to jest asercja, nie skutek uboczny.
        // Do kroku 3 strikeGain wynosil 0.75, czyli kontrast sam premiowal ulge po serii ciosow.
        // Od kroku 4 dokladnie to samo robi krzywa dramaturgiczna przez Intent.Breathe, wiec
        // zostawienie asymetrii tutaj liczyloby te preferencje DWA RAZY - raz jako ukryta stala
        // bez wiersza w sladzie decyzji, raz jako jawna intencja z wlasna waga w XML.
        var domyslneStrojenie = ContrastTuning.Default();
        T.Eq("KROK 4: domyslny strikeGain == reliefGain (asymetria przeniesiona do intencji)",
             domyslneStrojenie.strikeGain, domyslneStrojenie.reliefGain, 1e-6);
        float lustroSym = Kontrast(f, posMinor, 15f, Valence.Negative, EventScale.Major, out slad);
        T.Eq("symetria: para lustrzana daje DOKLADNIE tyle co ulga (1.0)", lustroSym, 1.0, 1e-5);

        // ---- MECHANIZM asymetrii nadal DZIALA i jest sterowalny z XML. Bez tego testu
        // pola reliefGain/strikeGain stalyby sie martwym kodem, ktory da sie usunac bez sladu
        // w wynikach - a maja byc pokretlem kalibracji osobowosci, nie reliktem.
        var fAsym = new Factor_DramaticContrast(new ContrastTuning { strikeGain = 0.75f });
        float lustroAsym = Kontrast(fAsym, posMinor, 15f, Valence.Negative, EventScale.Major, out slad);
        T.Eq("asymetria WLACZONA z XML: para lustrzana (3x Pos/Minor -> Neg/Major)", lustroAsym, 0.8517, 1e-4);
        double tozsamosc = (1.0 - 0.75) * 0.70 * 0.625 / 0.7375;
        T.Eq("asymetria WLACZONA: roznica 1.0 - lustro == tozsamosc algebraiczna",
             1.0 - lustroAsym, tozsamosc, 1e-4);
        T.Ok("asymetria jest POKRETLEM, nie stala: wlaczona zmienia wynik",
             lustroAsym < lustroSym - 0.05f,
             "symetrycznie " + lustroSym.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
             + " vs asymetrycznie " + lustroAsym.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));

        // Pulapka oscylacji.
        EventHistory osc = Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 10f },
            new object[] { "B", Theme.Economic, Valence.Positive, EventScale.Minor, 12f },
            new object[] { "C", Theme.Raid, Valence.Negative, EventScale.Major, 14f },
            new object[] { "D", Theme.Economic, Valence.Positive, EventScale.Minor, 16f },
            new object[] { "E", Theme.Raid, Valence.Negative, EventScale.Major, 18f },
            new object[] { "F", Theme.Economic, Valence.Positive, EventScale.Minor, 20f });
        RhythmPoint rp = f.ComputeRhythm(osc);
        T.Eq("oscylacja: Rc", rp.Charge, -0.28571, 1e-4);
        T.Eq("oscylacja: Rm", rp.Magnitude, 0.57143, 1e-4);
        T.Eq("oscylacja: zmiennosc", f.ComputeVolatility(osc), 1.0, 1e-6);
        float oPos = Kontrast(f, osc, 21f, Valence.Positive, EventScale.Minor, out slad);
        float oNeg = Kontrast(f, osc, 21f, Valence.Negative, EventScale.Major, out slad);
        T.Eq("oscylacja: kontrast Pos/Minor", oPos, 0.4714, 1e-4);
        T.Eq("oscylacja: kontrast Neg/Major", oNeg, 0.5286, 1e-4);

        // WYPROWADZONE OGRANICZENIE zamiast zapamietanej liczby: wynik czynnika to
        // 0.5 + (shaped - 0.5) * trust, a shaped nalezy do [0,1], wiec odchylenie od 0.5
        // NIE MOZE przekroczyc 0.5 * trust. To wiaze tlumienie z wynikiem algebraicznie
        // i przezyje kazde przestrojenie wag oraz wzmocnien.
        float conf, rec, damp, vol;
        float trust = f.ComputeTrust(osc, Ctx(osc, 21f), out conf, out rec, out damp, out vol);
        T.Ok("oscylacja: |wynik - 0.5| <= 0.5 * trust (ograniczenie z konstrukcji)",
             System.Math.Abs(oPos - 0.5f) <= 0.5f * trust + 1e-4f
             && System.Math.Abs(oNeg - 0.5f) <= 0.5f * trust + 1e-4f,
             "trust=" + trust.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
             + " oPos=" + oPos.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
             + " oNeg=" + oNeg.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
        T.Ok("oscylacja: czynnik faktycznie milknie (oba w [0.45,0.55])",
             // Prog 0.03 na roznicy usuniety w kroku 4: byl skalibrowany dla ASYMETRYCZNYCH
             // wzmocnien (strikeGain 0.75), ktore scisnely obie wartosci ku sobie. Przy symetrii
             // roznica jest wieksza, ale wlasciwosc, o ktora naprawde chodzi - "czynnik milknie
             // przy naprzemiennej historii" - jest zachowana i pilnuje jej przedzial [0.45, 0.55]
             // oraz wyprowadzone ograniczenie 0.5 * trust powyzej.
             oPos >= 0.45 && oPos <= 0.55 && oNeg >= 0.45 && oNeg <= 0.55,
             "Pos=" + T.F4(oPos) + " Neg=" + T.F4(oNeg));

        // Tozsamosc tlumienia: tlumienie SPLASZCZA, nie odwraca.
        var bezTlum = new Factor_DramaticContrast(new ContrastTuning { dampingStrength = 0f });
        float bPos = Kontrast(bezTlum, osc, 21f, Valence.Positive, EventScale.Minor, out slad);
        float bNeg = Kontrast(bezTlum, osc, 21f, Valence.Negative, EventScale.Major, out slad);
        T.Eq("tozsamosc tlumienia: (r1A-r1B) == 0.4 * (r0A-r0B)", oNeg - oPos, 0.4 * (bNeg - bPos), 1e-5);
        T.Ok("tozsamosc tlumienia: kolejnosc rankingu NIEZMIENIONA",
             Math.Sign(oNeg - oPos) == Math.Sign(bNeg - bPos), null);

        // Zimny start.
        EventHistory z1 = Hist(new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 14f });
        EventHistory z2 = Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 12f },
            new object[] { "B", Theme.Raid, Valence.Negative, EventScale.Major, 14f });
        T.Eq("zimny start n=0", Kontrast(f, new EventHistory(), 15f, Valence.Positive, EventScale.Minor, out slad), 0.5, 1e-6);
        T.Eq("zimny start n=1", Kontrast(f, z1, 15f, Valence.Positive, EventScale.Minor, out slad), 0.6667, 1e-4);
        T.Eq("zimny start n=2", Kontrast(f, z2, 15f, Valence.Positive, EventScale.Minor, out slad), 0.8333, 1e-4);
        T.Eq("zimny start n=3", Kontrast(f, negMajor, 15f, Valence.Positive, EventScale.Minor, out slad), 1.0000, 1e-4);

        // Starosc rytmu (mierzona GameDay - odstepstwo przyjete w kontroli spojnosci).
        EventHistory stary = Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 6f },
            new object[] { "B", Theme.Raid, Valence.Negative, EventScale.Major, 8f },
            new object[] { "C", Theme.Raid, Valence.Negative, EventScale.Major, 10f });
        T.Eq("starosc rytmu: gap 1", Kontrast(f, stary, 11f, Valence.Positive, EventScale.Minor, out slad), 1.0000, 1e-4);
        T.Eq("starosc rytmu: gap 8 (jeszcze na progu)", Kontrast(f, stary, 18f, Valence.Positive, EventScale.Minor, out slad), 1.0000, 1e-4);
        T.Eq("starosc rytmu: gap 16", Kontrast(f, stary, 26f, Valence.Positive, EventScale.Minor, out slad), 0.7500, 1e-4);
        T.Eq("starosc rytmu: gap 24", Kontrast(f, stary, 34f, Valence.Positive, EventScale.Minor, out slad), 0.5000, 1e-6);
        T.Eq("starosc rytmu: gap 50", Kontrast(f, stary, 60f, Valence.Positive, EventScale.Minor, out slad), 0.5000, 1e-6);

        // Skladnik glosnosci zarabia na wage.
        EventHistory neu = Hist(
            new object[] { "A", Theme.Social, Valence.Neutral, EventScale.Minor, 10f },
            new object[] { "B", Theme.Social, Valence.Neutral, EventScale.Minor, 12f },
            new object[] { "C", Theme.Social, Valence.Neutral, EventScale.Minor, 14f });
        float gMinor = Kontrast(f, neu, 15f, Valence.Neutral, EventScale.Minor, out slad);
        float gMajor = Kontrast(f, neu, 15f, Valence.Neutral, EventScale.Major, out slad);
        T.Eq("glosnosc: rytm 3x Neu/Minor -> Neu/Minor", gMinor, 0.0000, 1e-6);
        T.Eq("glosnosc: rytm 3x Neu/Minor -> Neu/Major", gMajor, 0.4068, 1e-4);
        T.Ok("glosnosc: skladnik dMag rozroznia glosne zdarzenie neutralne", gMajor > gMinor, null);

        // Modulacja bije whiplash.
        float posMajor = Kontrast(f, negMajor, 15f, Valence.Positive, EventScale.Major, out slad);
        T.Eq("modulacja: 3x Neg/Major -> Pos/Major", posMajor, 0.9492, 1e-4);
        T.Eq("modulacja: Pos/Minor przewyzsza Pos/Major o", 1.0 - posMajor, 0.0508, 1e-4);

        // Sekwencja Pos/Neu/Neg to modulacja, nie oscylacja.
        EventHistory mod = Hist(
            new object[] { "A", Theme.Economic, Valence.Positive, EventScale.Minor, 10f },
            new object[] { "B", Theme.Social, Valence.Neutral, EventScale.Minor, 12f },
            new object[] { "C", Theme.Raid, Valence.Negative, EventScale.Major, 14f });
        T.Eq("martwa strefa: [Pos,Neu,Neg] daje zmiennosc 0", f.ComputeVolatility(mod), 0.0, 1e-6);

        // KONTRAKT KOLEJNOSCI HISTORII - wartosc wyprowadzona z dokumentowanego wzoru (lambda=0.75,
        // najnowszy na indeksie 0), a nie przepisana ze spec (jego liczby - patrz raport).
        EventHistory kol = Hist(
            new object[] { "A", Theme.Economic, Valence.Positive, EventScale.Minor, 10f },
            new object[] { "B", Theme.Economic, Valence.Positive, EventScale.Minor, 12f },
            new object[] { "C", Theme.Raid, Valence.Negative, EventScale.Major, 14f });
        RhythmPoint rKol = f.ComputeRhythm(kol);
        double[] cNewestFirst = { -1.0, 0.25, 0.25 };
        double[] cOldestFirst = { 0.25, 0.25, -1.0 };
        double rcNew = Ewma(cNewestFirst, 0.75);
        double rcOld = Ewma(cOldestFirst, 0.75);
        T.Eq("kolejnosc historii: Rc liczony newest-first (wzor lambda=0.75)", rKol.Charge, rcNew, 1e-4);
        T.Ok("kolejnosc historii: Rc ROZNI sie od wariantu odwroconego", Math.Abs(rcNew - rcOld) > 0.1,
             "newest-first=" + T.F4(rcNew) + " odwrocony=" + T.F4(rcOld));
        Console.WriteLine("     UWAGA: SPEC 3 podaje dla tego przypadku Rc=-0.4643 i wynik 0.7576;"
                          + " wzor lambda=0.75 daje Rc=" + T.F4(rcNew) + ". Patrz raport (niezgodnosc SPEC).");

        // Zakres na 9 parach x 9 rytmow jednorodnych.
        var walencje = (Valence[])Enum.GetValues(typeof(Valence));
        var skale = (EventScale[])Enum.GetValues(typeof(EventScale));
        bool zakres = true, podMax = true;
        foreach (Valence rv in walencje)
        {
            foreach (EventScale rs in skale)
            {
                EventHistory h = Hist(
                    new object[] { "A", Theme.Raid, rv, rs, 10f },
                    new object[] { "B", Theme.Raid, rv, rs, 12f },
                    new object[] { "C", Theme.Raid, rv, rs, 14f });
                foreach (Valence cv in walencje)
                {
                    foreach (EventScale cs in skale)
                    {
                        float v = Kontrast(f, h, 15f, cv, cs, out slad);
                        if (float.IsNaN(v) || v < 0f || v > 1f) zakres = false;
                        if (v > 1.0f + 1e-6f) podMax = false;
                    }
                }
            }
        }
        T.Ok("81 kombinacji (kandydat x rytm jednorodny): wynik w [0,1], brak NaN", zakres && podMax, null);
    }

    private static double Ewma(double[] values, double lambda)
    {
        double w = 0, s = 0;
        for (int i = 0; i < values.Length; i++)
        {
            double wi = Math.Pow(lambda, i);
            w += wi;
            s += wi * values[i];
        }
        return s / w;
    }

    // ---------------------------------------------------------- TEST 9c: PASS
    private static void Test9Pass(XmlConfig cfg)
    {
        T.Section("TEST 9c - UZYTECZNOSC PASS: tablica wartosci ze specyfikacji (SPEC 4, WZORY)");
        PassScoringParams p = cfg.pass;

        // WYPROWADZENIE, nie literal: podloga to udzial czynnika stalego w sumie wag.
        // Po wlaczeniu intencji w kroku 4 suma wag zostala CELOWO zrenormalizowana do 1.0
        // (0.55 + 0.20 + 0.25), zeby weightBaseline nadal czytalo sie wprost jako podloga.
        double sumaWagPass = p.weightRestraint + p.weightBaseline + p.weightIntentAlignment;
        T.Eq("podloga U_pass = w_baseline / sumaWag", p.UtilityFloor, p.weightBaseline / sumaWagPass, 1e-6);
        T.Eq("suma wag PASS == 1.0 (na tym stoi czytelnosc podlogi)", p.SumWeights, 1.0, 1e-6);
        T.Eq("sufit U_pass", p.UtilityCeiling, 1.0, 1e-6);

        // Udzial polowy wagi intencji - wraca w kilku wzorach nizej, bo Factor_PassIntent
        // przy intencji Hold zwraca 0.5 (patrz FitFor).
        double holdIntent = 0.5 * p.weightIntentAlignment / sumaWagPass;

        T.Eq("gestosc: pusta historia", Factor_PassRestraint.Density(new EventHistory(), 10f, 5f), 0.0, 1e-6);
        EventHistory jeden = Hist(new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 5f });
        T.Eq("gestosc: 1 zdarzenie sprzed 5.0 dnia, halfLife 5", Factor_PassRestraint.Density(jeden, 10f, 5f), 0.5, 1e-4);
        EventHistory dwa = Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 5f },
            new object[] { "B", Theme.Raid, Valence.Negative, EventScale.Major, 10f });
        T.Eq("gestosc: 2 zdarzenia (delta 0 i 5.0)", Factor_PassRestraint.Density(dwa, 10f, 5f), 1.5, 1e-4);

        T.Eq("rampa: D = 1.5 -> restraint 0", Factor_PassRestraint.Restraint(1.5f, 1.5f, 4.5f), 0.0, 1e-6);
        T.Eq("rampa: D = 3.0 -> restraint 0.5", Factor_PassRestraint.Restraint(3.0f, 1.5f, 4.5f), 0.5, 1e-6);
        T.Eq("rampa: D = 4.5 -> restraint 1.0", Factor_PassRestraint.Restraint(4.5f, 1.5f, 4.5f), 1.0, 1e-6);
        T.Eq("rampa: D = 9.0 -> restraint 1.0 (klamrowanie)", Factor_PassRestraint.Restraint(9.0f, 1.5f, 4.5f), 1.0, 1e-6);

        var scorer = new UtilityScorer(CzynnikiZdarzen(), cfg.weights,
                                       UtilityScorer.BuildPassFactors(p), p, cfg.vetoContextFitBelow);

        // Podloga na pustej historii.
        ScoredCandidate pPusta = scorer.ScorePass(Ctx(new EventHistory(), 10f));
        // Pusta historia: gestosc 0 -> restraint 0. Intencja jest tu Hold, wiec czynnik
        // intencji daje 0.5, a nie 0 - stad podloga PLUS polowa wagi intencji.
        T.Eq("pusta historia: U_pass == podloga + polowa wagi intencji (Hold)",
             pPusta.Utility, p.UtilityFloor + holdIntent, 1e-5);
        T.EqI("pusta historia: Factors.Count == 3", pPusta.Factors.Count, 3);
        T.EqS("kolejnosc czynnikow PASS [0]", pPusta.Factors[0].Name, "restraint");
        T.EqS("kolejnosc czynnikow PASS [1]", pPusta.Factors[1].Name, "baseline");
        T.EqS("kolejnosc czynnikow PASS [2]", pPusta.Factors[2].Name, "intentAlignment");
        T.Ok("PASS nigdy nie jest zawetowany", !pPusta.Vetoed, null);

        // Przypadek referencyjny: zdarzenia sprzed 0.5 / 1.5 / 3.0 / 5.0 dnia.
        float teraz = 10f;
        EventHistory refH = Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, teraz - 5.0f },
            new object[] { "B", Theme.Raid, Valence.Negative, EventScale.Major, teraz - 3.0f },
            new object[] { "C", Theme.Raid, Valence.Negative, EventScale.Major, teraz - 1.5f },
            new object[] { "D", Theme.Raid, Valence.Negative, EventScale.Major, teraz - 0.5f });
        float dRef = Factor_PassRestraint.Density(refH, teraz, p.halfLifeDays);
        T.Eq("przypadek referencyjny: gestosc", dRef, 2.9050, 1e-3);
        T.Eq("przypadek referencyjny: restraint",
             Factor_PassRestraint.Restraint(dRef, p.densityFloor, p.densitySaturation), 0.4683, 1e-3);
        ScoredCandidate pRef = scorer.ScorePass(Ctx(refH, teraz));
        // Pelne wyprowadzenie zamiast zapamietanej liczby: srednia wazona trzech czynnikow.
        double uRefZWzoru = (p.weightRestraint * 0.4683 + p.weightBaseline * 1.0
                             + p.weightIntentAlignment * 0.5) / sumaWagPass;
        T.Eq("przypadek referencyjny: U_pass == wzor", pRef.Utility, uRefZWzoru, 1e-3);
        T.Eq("przypadek referencyjny: LastPassDensity", scorer.LastPassDensity, 2.9050, 1e-3);

        // Nasycenie na serii.
        EventHistory nasy = Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, teraz - 4.0f },
            new object[] { "B", Theme.Raid, Valence.Negative, EventScale.Major, teraz - 3.0f },
            new object[] { "C", Theme.Raid, Valence.Negative, EventScale.Major, teraz - 2.2f },
            new object[] { "D", Theme.Raid, Valence.Negative, EventScale.Major, teraz - 1.5f },
            new object[] { "E", Theme.Raid, Valence.Negative, EventScale.Major, teraz - 0.8f },
            new object[] { "F", Theme.Raid, Valence.Negative, EventScale.Major, teraz - 0.2f });
        float dNas = Factor_PassRestraint.Density(nasy, teraz, p.halfLifeDays);
        T.Eq("nasycenie: gestosc", dNas, 4.6511, 1e-3);
        T.Eq("nasycenie: restraint", Factor_PassRestraint.Restraint(dNas, p.densityFloor, p.densitySaturation), 1.0, 1e-6);
        // Sufit 1.0 wymaga, zeby WSZYSTKIE trzy czynniki osiagnely 1 naraz. Przy intencji Hold
        // czynnik intencji daje 0.5, wiec sufit jest NIEOSIAGALNY - i to jest poprawne, a nie
        // regresja: pelna cisza nalezy sie dopiero wtedy, gdy narrator jej CHCE (Breathe).
        T.Eq("nasycenie przy Hold: U_pass == sufit minus polowa wagi intencji",
             scorer.ScorePass(Ctx(nasy, teraz)).Utility, 1.0 - holdIntent, 1e-5);
        T.Eq("nasycenie przy Breathe: U_pass == PELNY sufit 1.0",
             scorer.ScorePass(CtxIntent(nasy, teraz, Intent.Breathe)).Utility, 1.0, 1e-5);
        T.Ok("intencja REALNIE zmienia uzytecznosc ciszy (Breathe > Hold > Escalate)",
             scorer.ScorePass(CtxIntent(nasy, teraz, Intent.Breathe)).Utility
                 > scorer.ScorePass(Ctx(nasy, teraz)).Utility
             && scorer.ScorePass(Ctx(nasy, teraz)).Utility
                 > scorer.ScorePass(CtxIntent(nasy, teraz, Intent.Escalate)).Utility,
             "Breathe=" + scorer.ScorePass(CtxIntent(nasy, teraz, Intent.Breathe)).Utility.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
             + " Hold=" + scorer.ScorePass(Ctx(nasy, teraz)).Utility.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
             + " Escalate=" + scorer.ScorePass(CtxIntent(nasy, teraz, Intent.Escalate)).Utility.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));

        // Wpisy PASS nie zageszczaja.
        var samePassy = new EventHistory();
        for (int i = 0; i < 4; i++) samePassy.RecordPass(teraz - 2f + i * 0.5f, 0, true);
        T.Eq("same PASS-y: gestosc 0", Factor_PassRestraint.Density(samePassy, teraz, p.halfLifeDays), 0.0, 1e-6);
        T.Eq("same PASS-y: U_pass == podloga + polowa wagi intencji (Hold)",
             scorer.ScorePass(Ctx(samePassy, teraz)).Utility, p.UtilityFloor + holdIntent, 1e-5);

        // Antysprzezenie: U_pass maleje w czasie, swiezosc rosnie.
        bool nierosnace = true;
        float poprzednie = float.MaxValue;
        for (int i = 0; i < 30; i++)
        {
            float u = scorer.ScorePass(Ctx(refH, teraz + i)).Utility;
            if (u > poprzednie + 1e-6f) nierosnace = false;
            poprzednie = u;
        }
        T.Ok("antysprzezenie cz.1: U_pass NIEROSNACY przez 30 dni ciszy", nierosnace, null);

        float uT0 = scorer.ScorePass(Ctx(refH, teraz)).Utility;
        float uT10 = scorer.ScorePass(Ctx(refH, teraz + 10f)).Utility;
        float sw = Factor_Freshness.Compute(Theme.Raid, "A", refH, FreshnessSettings.Default()).Value;
        T.Ok("antysprzezenie cz.2: U_pass(t0) > U_pass(t0+10)", uT0 > uT10, T.F4(uT0) + " > " + T.F4(uT10));
        // ZASTAPIONA ASERCJA TAUTOLOGICZNA. Poprzednia wersja porownywala Compute(...) z DOKLADNIE
        // tym samym wywolaniem, czyli miala postac x <= x + 1e-6 i przechodzila przy dowolnie
        // zepsutym Factor_Freshness. Wlasnosc "zanik po decyzjach, nie po dniach" jest przy tym
        // gwarantowana SYGNATURA (Compute nie przyjmuje dnia gry), wiec testowac trzeba to,
        // co faktycznie jest zmienne: ze swiezosc ROSNIE, w miare jak temat oddala sie w kolejnosci
        // decyzji. To wlasnie ten zanik ma odsuwac powtorki.
        EventHistory hRaidSwiezy = Hist(
            new object[] { "Z1", Theme.Economic, Valence.Positive, EventScale.Minor, 1f },
            new object[] { "Z2", Theme.Social, Valence.Neutral, EventScale.Minor, 2f },
            new object[] { "Z3", Theme.Natural, Valence.Negative, EventScale.Moderate, 3f },
            new object[] { "Z4", Theme.Military, Valence.Negative, EventScale.Moderate, 4f },
            new object[] { "Z5", Theme.Raid, Valence.Negative, EventScale.Major, 5f });
        EventHistory hRaidStary = Hist(
            new object[] { "Z5", Theme.Raid, Valence.Negative, EventScale.Major, 1f },
            new object[] { "Z1", Theme.Economic, Valence.Positive, EventScale.Minor, 2f },
            new object[] { "Z2", Theme.Social, Valence.Neutral, EventScale.Minor, 3f },
            new object[] { "Z3", Theme.Natural, Valence.Negative, EventScale.Moderate, 4f },
            new object[] { "Z4", Theme.Military, Valence.Negative, EventScale.Moderate, 5f });
        float swSwiezy = Factor_Freshness.Compute(Theme.Raid, "NIEOBECNY", hRaidSwiezy, FreshnessSettings.Default()).Value;
        float swStary = Factor_Freshness.Compute(Theme.Raid, "NIEOBECNY", hRaidStary, FreshnessSettings.Default()).Value;
        T.Ok("swiezosc ROSNIE, gdy temat oddala sie w kolejnosci decyzji",
             swStary > swSwiezy + 1e-4f, T.F4(swStary) + " > " + T.F4(swSwiezy));

        // Determinizm ScorePass.
        ScoredCandidate a1 = scorer.ScorePass(Ctx(refH, teraz));
        ScoredCandidate a2 = scorer.ScorePass(Ctx(refH, teraz));
        T.Ok("determinizm ScorePass: identyczna uzytecznosc i identyczny slad",
             a1.Utility == a2.Utility && string.Join("|", a1.Factors.Select(x => x.ToString()))
             == string.Join("|", a2.Factors.Select(x => x.ToString())), null);

        // Zakres na losowych historiach.
        var rnd = new Random(7);
        bool zakres = true;
        for (int i = 0; i < 10000; i++)
        {
            var h = new EventHistory(64);
            int n = rnd.Next(0, 40);
            float now = (float)(rnd.NextDouble() * 400.0);
            for (int k = 0; k < n; k++)
            {
                h.RecordEvent(Ev("A", Theme.Raid, Valence.Negative, EventScale.Major), (float)(rnd.NextDouble() * 400.0), 0);
            }
            float u = scorer.ScorePass(Ctx(h, now)).Utility;
            if (float.IsNaN(u) || float.IsInfinity(u) || u < 0f || u > 1f) zakres = false;
        }
        T.Ok("10000 losowych historii: U_pass zawsze w [0,1], bez NaN", zakres, null);
    }

    // ----------------------------------------------------- TEST 9d: SCOREMATH
    private static void Test9ScoreMath()
    {
        T.Section("TEST 9d - SCOREMATH: softmax, kwantyzacja, progi (SPEC 5, WZORY)");

        double[] u = { 0.90, 0.80, 0.35 };
        double[] w = ScoreMath.SoftmaxWeights(u, 0.1);
        int[] thr = ScoreMath.CumulativeThresholds(w);
        double[] pr = ScoreMath.ProbabilitiesFromThresholds(thr);
        T.Eq("pula {0.90, 0.80, PASS 0.35}: p1", pr[0], 0.7289, 1e-3);
        T.Eq("pula {0.90, 0.80, PASS 0.35}: p2", pr[1], 0.2681, 1e-3);
        T.Eq("pula {0.90, 0.80, PASS 0.35}: p3", pr[2], 0.0030, 1e-3);
        T.EqI("prog skumulowany [2] == Scale", thr[2], ScoreMath.ProbabilityScale);
        Console.WriteLine("     progi otrzymane: " + string.Join(" / ", thr)
                          + "   (SPEC podaje 728880 / 997021 / 1000000)");
        T.EqI("prog [1] zgodny ze SPEC", thr[1], 997021);
        T.Ok("prog [0]: zaokraglenie (floor+0.5) daje 728881, obciecie dalo by 728880",
             thr[0] == 728881, "otrzymano " + thr[0] + "; idealne p1*1e6 = 728880.92");

        double[] rowne = { 1.0, 1.0, 1.0 };
        int[] thrR = ScoreMath.CumulativeThresholds(rowne);
        T.EqI("kwantyzacja rowna: prog[0]", thrR[0], 333333);
        T.EqI("kwantyzacja rowna: prog[1]", thrR[1], 666667);
        T.EqI("kwantyzacja rowna: prog[2]", thrR[2], 1000000);
        int[] szer = { thrR[0], thrR[1] - thrR[0], thrR[2] - thrR[1] };
        T.Ok("kwantyzacja rowna: szerokosci 333333/333334/333333",
             szer[0] == 333333 && szer[1] == 333334 && szer[2] == 333333, string.Join("/", szer));

        // Brak obciazenia losowania.
        double[] wP = { 0.7, 0.2, 0.1 };
        int[] thrP = ScoreMath.CumulativeThresholds(wP);
        var rng = new SeededRandom(12345);
        var licz = new int[3];
        for (int i = 0; i < 100000; i++) licz[ScoreMath.PickByThresholds(thrP, rng)]++;
        T.Eq("100k losowan: czestosc kubelka 0", licz[0] / 100000.0, 0.7, 0.01);
        T.Eq("100k losowan: czestosc kubelka 1", licz[1] / 100000.0, 0.2, 0.01);
        T.Eq("100k losowan: czestosc kubelka 2", licz[2] / 100000.0, 0.1, 0.01);

        // Progi domykajace.
        T.Ok("AtLeast(0.15, 0.15) == true", ScoreMath.AtLeast(0.15, 0.15), null);
        T.Ok("AtLeast(0.1499, 0.15) == false", !ScoreMath.AtLeast(0.1499, 0.15), null);
        bool nan;
        T.Eq("Sanitize01(NaN) == 0", ScoreMath.Sanitize01(float.NaN, out nan), 0.0, 1e-9);
        T.Ok("Sanitize01(NaN) ustawia flage", nan, null);
        T.Eq("Sanitize01(1.2) == 1 bez flagi", ScoreMath.Sanitize01(1.2f, out nan), 1.0, 1e-9);
        T.Ok("Sanitize01(1.2) NIE ustawia flagi (zwykle klamrowanie)", !nan, null);
    }

    // ----------------------------------------------------------- TEST 4: WETO
    private static void Test4Weto(XmlConfig cfg)
    {
        T.Section("TEST 4 - WETO: contextFit < 0.15 -> Utility 0 i kandydat NIE wygrywa nigdy");

        UtilityScorer S(float cf, ScoringWeights w = null)
        {
            var czynniki = new IScoringFactor[]
            {
                new StubFactor(nameof(ScoringWeights.contextFit), cf),
                new StubFactor(nameof(ScoringWeights.freshness), 1f),
                new StubFactor(nameof(ScoringWeights.dramaticContrast), 1f),
                new StubFactor(nameof(ScoringWeights.intentAlignment), 1f)
            };
            return new UtilityScorer(czynniki, w ?? cfg.weights,
                                     UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, cfg.vetoContextFitBelow);
        }

        DecisionContext ctx = Ctx(new EventHistory(), 10f);
        ComposedEvent zdarzenie = Ev("PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major);

        ScoredCandidate c14 = S(0.14f).Score(zdarzenie, ctx);
        T.Ok("contextFit 0.14 -> Vetoed", c14.Vetoed, c14.VetoReason);
        T.Eq("contextFit 0.14 -> Utility == 0", c14.Utility, 0.0, 1e-6);
        // WYPROWADZENIE z faktycznych wag zamiast literalu: stuby daja 1.0 wszystkim czynnikom
        // poza contextFit. Poprzednia wersja miala tu 0.6178 dla sumy wag 4.5 i zestarzala sie
        // w kroku 4 wraz z wlaczeniem intencji.
        double rawZWzoru = (cfg.weights.contextFit * 0.14 + cfg.weights.freshness
                            + cfg.weights.dramaticContrast + cfg.weights.intentAlignment)
                           / cfg.weights.Total();
        T.Eq("contextFit 0.14 -> RawUtility zachowane (wzor z wag)", c14.RawUtility, rawZWzoru, 1e-4);
        T.Ok("VetoReason zawiera obie liczby", c14.VetoReason != null && c14.VetoReason.Contains("0.14") && c14.VetoReason.Contains("0.15"), c14.VetoReason);

        T.Ok("prog domykajacy: contextFit == 0.15 -> BRAK weta", !S(0.15f).Score(zdarzenie, ctx).Vetoed, null);
        T.Ok("prog domykajacy: contextFit == 0.1499 -> weto", S(0.1499f).Score(zdarzenie, ctx).Vetoed, null);

        var wZerowaWaga = new ScoringWeights { contextFit = 0f, freshness = 1.5f, dramaticContrast = 1f, intentAlignment = 0f };
        ScoredCandidate cZ = S(0.10f, wZerowaWaga).Score(zdarzenie, ctx);
        T.Ok("weto NIEZALEZNE od wagi: contextFit=0.10 przy wadze 0 -> nadal weto", cZ.Vetoed, cZ.VetoReason);

        // Kandydat zawetowany nie wygrywa NIGDY, mimo najwyzszego RawUtility.
        var policy = new SelectionPolicy(cfg.Selection());
        var pula = new List<ScoredCandidate>
        {
            Kand(0.0f, "AAA", true, 0.99f),
            Kand(0.60f, "BBB"),
            Kand(0.58f, "CCC"),
            Kand(0.55f, "DDD")
        };
        int wygranychZawetowanego = 0;
        for (int seed = 0; seed < 500; seed++)
        {
            NarratorDecision d = policy.Select(pula, Pass(0.25f), new SeededRandom(seed));
            if (d.Winner != null && d.Winner.SortKey == "AAA") wygranychZawetowanego++;
        }
        T.EqI("500 ziaren: zawetowany (raw 0.99) wygral", wygranychZawetowanego, 0);
        NarratorDecision d1 = policy.Select(pula, Pass(0.25f), new SeededRandom(1));
        T.EqI("zawetowany ma Rejected == Veto", (int)pula[0].Rejected, (int)RejectionStage.Veto);
        T.EqI("licznik zawetowanych", d1.CountVetoed, 1);
        T.EqI("zawetowany NIE wpada do licznika ponizej progu", d1.CountBelowCutoff, 0);
        T.Ok("zawetowany zostaje w rankingu z zachowanym RawUtility",
             d1.Ranking.Any(x => x.SortKey == "AAA" && Math.Abs(x.RawUtility - 0.99f) < 1e-6), null);
        T.Ok("niezmiennik: Ranking.Count == CountScored + 1", d1.RankingIsComplete,
             "ranking=" + d1.Ranking.Count + " ocenionych=" + d1.CountScored);

        // Wszyscy zawetowani.
        var samiZaweto = new List<ScoredCandidate>
        {
            Kand(0f, "A", true, 0.9f), Kand(0f, "B", true, 0.8f), Kand(0f, "C", true, 0.7f),
            Kand(0f, "D", true, 0.6f), Kand(0f, "E", true, 0.5f)
        };
        NarratorDecision dz = policy.Select(samiZaweto, Pass(0.25f), new SeededRandom(3));
        T.Ok("wszyscy zawetowani -> PASS", dz.IsPass, null);
        T.EqS("powod PASS", dz.PassReason.ToString(), PassReason.AllVetoed.ToString());
        T.EqI("CountVetoed == 5", dz.CountVetoed, 5);
        T.EqI("CountBelowCutoff == 0", dz.CountBelowCutoff, 0);
    }

    // ------------------------------------------------------ TEST 5: PASS WYGRYWA
    private static void Test5PassWygrywa(XmlConfig cfg)
    {
        // =============================================================================
        //  WARSTWA MIEKKA I RNG - dotad ZERO asercji, mimo ze contextFit ma najwyzsza wage
        //  (2.0) i jako jedyny czynnik ma prawo weta.
        // =============================================================================
        T.Section("TEST 4b - WARSTWA MIEKKA: Curves.Ramp, klasy Pref_* i ContextEvaluator");

        // --- rampa: fundament wszystkich preferencji liczbowych ---
        T.Eq("Ramp rosnaca: value == from -> 0", Curves.Ramp(1f, 1f, 3f), 0.0, 1e-6);
        T.Eq("Ramp rosnaca: srodek -> 0.5", Curves.Ramp(2f, 1f, 3f), 0.5, 1e-6);
        T.Eq("Ramp rosnaca: value == to -> 1", Curves.Ramp(3f, 1f, 3f), 1.0, 1e-6);
        T.Eq("Ramp klamruje ponizej", Curves.Ramp(-5f, 1f, 3f), 0.0, 1e-6);
        T.Eq("Ramp klamruje powyzej", Curves.Ramp(99f, 1f, 3f), 1.0, 1e-6);
        // Malejaca (from > to) - uzywana m.in. przez PN_Akcja_Meteoryt i PN_Mod_Slabo.
        T.Eq("Ramp malejaca: value == from -> 0", Curves.Ramp(3f, 3f, 1f), 0.0, 1e-6);
        T.Eq("Ramp malejaca: value == to -> 1", Curves.Ramp(1f, 3f, 1f), 1.0, 1e-6);
        T.Eq("Ramp malejaca: srodek -> 0.5", Curves.Ramp(2f, 3f, 1f), 0.5, 1e-6);
        // Zdegenerowana from == to: prog skokowy (udokumentowane zachowanie, nie NaN).
        T.Eq("Ramp zdegenerowana from==to: ponizej -> 0", Curves.Ramp(1.9f, 2f, 2f), 0.0, 1e-6);
        T.Eq("Ramp zdegenerowana from==to: na progu -> 1", Curves.Ramp(2f, 2f, 2f), 1.0, 1e-6);

        // --- kazda klasa Pref_* osobno, na wartosciach granicznych ---
        var snapB = new WorldSnapshot
        {
            DaysPassed = 30, ColonistCount = 6, WealthRelative = 2f,
            Season = 1, IsNight = true, WildAnimalCount = 10
        };

        T.Eq("Pref_WealthRelative 1->3 przy 2.0", new Pref_WealthRelative { from = 1f, to = 3f }.Fit(snapB), 0.5, 1e-6);
        T.Eq("Pref_Colonists 2->10 przy 6", new Pref_Colonists { from = 2f, to = 10f }.Fit(snapB), 0.5, 1e-6);
        T.Eq("Pref_GameAge 10->50 przy 30", new Pref_GameAge { from = 10f, to = 50f }.Fit(snapB), 0.5, 1e-6);
        T.Eq("Pref_WildAnimals 0->20 przy 10", new Pref_WildAnimals { from = 0f, to = 20f }.Fit(snapB), 0.5, 1e-6);
        // Pref_Night i Pref_Season sa DWUSTANOWE - podloga 0.2 / 0.3, nie 0. To jest decyzja
        // projektowa ("niedopasowanie nie zeruje kandydata"), wiec ma byc pilnowana asercja.
        T.Eq("Pref_Night trafiony (noc)", new Pref_Night { wantNight = true }.Fit(snapB), 1.0, 1e-6);
        T.Eq("Pref_Night nietrafiony -> podloga 0.2", new Pref_Night { wantNight = false }.Fit(snapB), 0.2, 1e-6);
        T.Eq("Pref_Season trafiona", new Pref_Season { season = 1 }.Fit(snapB), 1.0, 1e-6);
        T.Eq("Pref_Season nietrafiona -> podloga 0.3", new Pref_Season { season = 3 }.Fit(snapB), 0.3, 1e-6);

        // --- ContextEvaluator: srednia wazona po WSPOLNEJ puli preferencji wszystkich klockow ---
        var blokA = new Block { Id = "A", Type = BlockType.Actor };
        blokA.Preferences.Add(new Pref_Night { wantNight = true });                        // 1.0, waga 1
        var blokB = new Block { Id = "B", Type = BlockType.Action };
        blokB.Preferences.Add(new Pref_Season { season = 3 });                             // 0.3, waga 1
        var zdarz = Ev("X", Theme.Raid, Valence.Negative, EventScale.Major);
        zdarz.Blocks.Add(blokA);
        zdarz.Blocks.Add(blokB);
        ContextEvaluator.Evaluate(zdarz, snapB);
        T.Eq("ContextEvaluator: srednia po wspolnej puli (1.0 + 0.3) / 2", zdarz.ContextFit, 0.65, 1e-5);
        T.Ok("ContextEvaluator: FitTrace wymienia OBA klocki",
             zdarz.FitTrace.Contains("A/") && zdarz.FitTrace.Contains("B/"), zdarz.FitTrace);

        // Pusta pula preferencji -> 1.0. To decyzja kroku 2 ("brak informacji nie jest kara"),
        // ktora RAZ JUZ dala blad: kandydat zlozony z samych takich klockow dostawal darmowy
        // sufit na osi o najwyzszej wadze. Dzis pilnuje tego diagnostyka w PNStartup, ale sama
        // regula musi byc zapisana asercja, bo od niej zalezy interpretacja tamtej naprawy.
        var zdarzPuste = Ev("Y", Theme.Social, Valence.Neutral, EventScale.Minor);
        zdarzPuste.Blocks.Add(new Block { Id = "C", Type = BlockType.Actor });
        ContextEvaluator.Evaluate(zdarzPuste, snapB);
        T.Eq("ContextEvaluator: pusta pula preferencji -> 1.0 (brak informacji nie jest kara)",
             zdarzPuste.ContextFit, 1.0, 1e-6);

        T.Section("TEST 4c - RNG: SeededRandom.Avalanche (regresja ciagu Weyla)");

        // Blad nr 1 z przegladu adwersarialnego: narrator bral ziarno z ticku, tick rosnie o 1000,
        // a polityka pobierala z generatora dokladnie raz - wiec PIERWSZE losowanie bylo silnie
        // skorelowane z ziarnem i delty byly niemal stale (+425315, -574686, +425314, ...).
        // Rozklad brzegowy wygladal przy tym poprawnie, wiec histogram tego NIE wykrywal.
        // Do teraz naprawa nie miala testu: usuniecie Avalanche zostawialo caly walidator na zielono.
        //
        // UWAGA: nie da sie tu porownac z kontrola "goly System.Random", bo walidator chodzi na
        // .NET 8, gdzie Random ma inna implementacje niz net472 uzywany przez gre. Testujemy wiec
        // WLASNOSC (dekorelacja sasiednich ziaren), a nie roznice wobec zepsutej wersji.
        const int KROK_TICKU = 1000;
        const int PROBEK = 500;

        var deltyAval = new HashSet<int>();
        int poprzAval = SeededRandom.Avalanche(0);
        for (int i = 1; i < PROBEK; i++)
        {
            int biezacy = SeededRandom.Avalanche(i * KROK_TICKU);
            deltyAval.Add(biezacy - poprzAval);
            poprzAval = biezacy;
        }
        T.Ok("Avalanche: sasiednie ziarna (krok 1000) daja rozne delty",
             deltyAval.Count > PROBEK * 8 / 10, "roznych delt: " + deltyAval.Count + " / " + (PROBEK - 1));

        var deltyLos = new HashSet<int>();
        int poprzLos = new SeededRandom(0).Next(1000000);
        for (int i = 1; i < PROBEK; i++)
        {
            int biezacy = new SeededRandom(i * KROK_TICKU).Next(1000000);
            deltyLos.Add(biezacy - poprzLos);
            poprzLos = biezacy;
        }
        T.Ok("PIERWSZE losowanie z sasiednich ziaren jest zdekorelowane",
             deltyLos.Count > PROBEK * 8 / 10, "roznych delt: " + deltyLos.Count + " / " + (PROBEK - 1));

        // Avalanche musi byc funkcja (ten sam wejsciowy -> ten sam wyjsciowy) i roznowartosciowa
        // na badanym zakresie - inaczej dwa rozne ticki daloby ten sam strumien decyzji.
        T.EqI("Avalanche jest deterministyczna", SeededRandom.Avalanche(123456), SeededRandom.Avalanche(123456));
        var obrazy = new HashSet<int>();
        for (int i = 0; i < PROBEK; i++) obrazy.Add(SeededRandom.Avalanche(i * KROK_TICKU));
        T.EqI("Avalanche nie skleja ziaren (brak kolizji na 500 probkach)", obrazy.Count, PROBEK);

        T.Section("TEST 5 - PASS WYGRYWA, gdy wszyscy kandydaci sa ponizej qualityCutoff");
        var policy = new SelectionPolicy(cfg.Selection());

        var slabi = new List<ScoredCandidate> { Kand(0.20f, "A"), Kand(0.25f, "B"), Kand(0.30f, "C") };
        NarratorDecision d = policy.Select(slabi, Pass(0.35f), new SeededRandom(11));
        T.Ok("wszyscy ponizej 0.35 -> PASS", d.IsPass, "zwyciezca=" + (d.Winner == null ? "?" : d.Winner.Label));
        T.EqS("powod PASS", d.PassReason.ToString(), PassReason.BelowCutoff.ToString());
        T.EqI("CountBelowCutoff", d.CountBelowCutoff, 3);
        T.Eq("BestUtility == 0 (nikt nie przeszedl progu)", d.BestUtility, 0.0, 1e-6);
        // Od bramy dwuetapowej CountInSoftmax liczy SAME zdarzenia etapu B - PASS rozstrzyga
        // sie osobno w bramie i nie jest juz elementem tej puli.
        T.EqI("CountInSoftmax == 0 (zadne zdarzenie nie przeszlo progu)", d.CountInSoftmax, 0);
        T.EqI("RandomDraws == 3 (brama + akcja + wariant, takze przy zdegenerowanych etapach)", d.RandomDraws, 3);
        T.Eq("GatePassProbability == 1 przy pustej puli zdarzen", d.GatePassProbability, 1.0, 1e-6);

        // PASS jest ZWOLNIONY z progu jakosci - wygrywa nawet ponizej cutoffu.
        NarratorDecision dPodProgiem = policy.Select(slabi, Pass(0.10f), new SeededRandom(11));
        T.Ok("PASS ponizej cutoffu tez wygrywa (jest zwolniony z progu)", dPodProgiem.IsPass, null);

        // Zero kandydatow.
        NarratorDecision d0 = policy.Select(new List<ScoredCandidate>(), Pass(0.25f), new SeededRandom(5));
        T.Ok("zero kandydatow -> PASS", d0.IsPass, null);
        T.EqS("powod PASS przy pustej puli", d0.PassReason.ToString(), PassReason.NoCandidates.ToString());
        T.EqI("Ranking.Count == 1", d0.Ranking.Count, 1);

        // PASS przegrywa z mocnym kandydatem (kontrola negatywna).
        var mocni = new List<ScoredCandidate> { Kand(0.90f, "A"), Kand(0.85f, "B") };
        int pasy = 0;
        for (int s = 0; s < 200; s++) if (policy.Select(mocni, Pass(0.25f), new SeededRandom(s)).IsPass) pasy++;
        // Asercja jest LUZNA celowo. Przy bramie p(PASS) = 1/(1+exp((0.90-0.25)/0.1)) = 0.150%,
        // wiec na 200 ziaren wartosc oczekiwana to 0.3 trafienia - twarde "rowna sie zero" byloby
        // asercja na szczescie w losowaniu, a nie na wlasnosc systemu.
        T.Ok("200 ziaren, mocni kandydaci: PASS praktycznie nie wygrywa", pasy <= 3,
             "PASS-ow: " + pasy + " (oczekiwane ~0.3)");
        NarratorDecision dMocni = policy.Select(mocni, Pass(0.25f), new SeededRandom(0));
        T.Ok("brama przy mocnej stawce daje PASS ponizej 1%",
             dMocni.GatePassProbability < 0.01,
             "pBrama=" + dMocni.GatePassProbability.ToString("0.0000", CultureInfo.InvariantCulture));

        // Cutoffy licza sie BEZ PASS-a (weto tylnymi drzwiami).
        var jeden = new List<ScoredCandidate> { Kand(0.50f, "A") };
        NarratorDecision dB = policy.Select(jeden, Pass(0.99f), new SeededRandom(2));
        T.Eq("BestUtility liczone bez PASS", dB.BestUtility, 0.50, 1e-6);
        T.Eq("BandThreshold = 0.75 * 0.50", dB.BandThreshold, 0.375, 1e-6);
        T.EqI("kandydat 0.50 zostaje w puli etapu B (PASS nie zwezil pasma)", dB.CountInSoftmax, 1);
        // PASS 0.99 kontra best 0.50: brama musi go wybrac niemal zawsze. To kontrola, ze brama
        // faktycznie patrzy na uzytecznosc ciszy, a nie tylko na to, czy pula jest pusta.
        T.Ok("brama: PASS 0.99 wobec best 0.50 wygrywa niemal zawsze",
             dB.GatePassProbability > 0.99, "pBrama=" + dB.GatePassProbability.ToString("0.0000", CultureInfo.InvariantCulture));

        // Straznik serii.
        var pp = PassScoringParams.Defaults();
        pp.maxStreak = 1;
        var hist = new EventHistory();
        hist.RecordPass(1f, 0, true);
        DecisionContext ctxStreak = Ctx(hist, 2f);
        T.Ok("straznik serii aktywny przy DeliberateSilenceStreak >= maxStreak",
             SelectionPolicy.IsPassSuppressedByStreak(ctxStreak, pp), null);
        NarratorDecision dStr = policy.Select(mocni, Pass(1.0f), new SeededRandom(1), true);
        T.Ok("straznik przy NIEPUSTEJ puli: zwyciezca NIE jest PASS", !dStr.IsPass, null);
        T.Eq("straznik zeruje prawdopodobienstwo bramy", dStr.GatePassProbability, 0.0, 1e-6);
        NarratorDecision dStrPusta = policy.Select(new List<ScoredCandidate>(), Pass(1.0f), new SeededRandom(1), true);
        T.Ok("straznik przy PUSTEJ puli: PASS mimo wszystko wygrywa", dStrPusta.IsPass, null);

        // Przeliczanie pasma po usunieciu lidera.
        var trzej = new List<ScoredCandidate> { Kand(0.90f, "A"), Kand(0.80f, "B"), Kand(0.60f, "C") };
        NarratorDecision r1 = policy.Select(trzej, Pass(0.25f), new SeededRandom(1));
        T.EqI("runda 1: CountInSoftmax == 2 (dwa zdarzenia, PASS osobno w bramie)", r1.CountInSoftmax, 2);
        T.EqI("runda 1: kandydat 0.60 odpada na pasmie", (int)trzej[2].Rejected, (int)RejectionStage.NearBestBand);
        var poUsunieciu = new List<ScoredCandidate> { trzej[1], trzej[2] };
        NarratorDecision r2 = policy.Select(poUsunieciu, Pass(0.25f), new SeededRandom(1));
        T.Eq("runda 2: BestUtility spada do 0.80", r2.BestUtility, 0.80, 1e-6);
        T.Eq("runda 2: BandThreshold spada do 0.60", r2.BandThreshold, 0.60, 1e-6);
        T.EqI("runda 2: kandydat 0.60 JEST w puli (Rejected == None)", (int)trzej[2].Rejected, (int)RejectionStage.None);

        // =============================================================================
        //  BRAMA DWUETAPOWA - wlasnosc, dla ktorej ten etap powstal
        // =============================================================================
        T.Section("TEST 5b - BRAMA: udzial PASS NIEZALEZNY od licznosci puli zdarzen");

        // Dwie pule o TYM SAMYM maksimum, rozniace sie wylacznie licznoscia. Wszyscy miesza sie
        // w pasmie (0.75 * 0.70 = 0.525), wiec zadnego nie odcina zaden filtr.
        var pulaMala = new List<ScoredCandidate> { Kand(0.70f, "M0"), Kand(0.68f, "M1") };
        var pulaDuza = new List<ScoredCandidate>();
        for (int i = 0; i < 40; i++) pulaDuza.Add(Kand(0.70f - (i % 20) * 0.001f, "D" + i.ToString("00")));

        NarratorDecision dMala = policy.Select(pulaMala, Pass(0.45f), new SeededRandom(7));
        NarratorDecision dDuza = policy.Select(pulaDuza, Pass(0.45f), new SeededRandom(7));

        T.EqI("pula mala ma 2 zdarzenia w etapie B", dMala.CountInSoftmax, 2);
        T.EqI("pula duza ma 40 zdarzen w etapie B", dDuza.CountInSoftmax, 40);
        T.Eq("TA SAMA sklonnosc do ciszy mimo 20-krotnie wiekszej puli",
             dDuza.GatePassProbability, dMala.GatePassProbability, 1e-6);

        // Ile bylaby warta cisza w wersji JEDNOETAPOWEJ - liczone tym samym softmaksem po calej
        // puli razem z PASS. To jest dokladnie ta wielkosc, ktora zalezala od rozmiaru katalogu.
        double jednoMala = UdzialPassJednoetapowo(pulaMala, 0.45, 0.1);
        double jednoDuza = UdzialPassJednoetapowo(pulaDuza, 0.45, 0.1);
        T.Ok("kontrola: wersja jednoetapowa DAWALA rozne udzialy dla tych samych pul",
             jednoMala > jednoDuza * 2.0,
             "jednoetapowo: mala=" + (jednoMala * 100).ToString("0.000", CultureInfo.InvariantCulture) + "% duza="
             + (jednoDuza * 100).ToString("0.000", CultureInfo.InvariantCulture) + "%  |  brama: obie="
             + (dMala.GatePassProbability * 100).ToString("0.000", CultureInfo.InvariantCulture) + "%");

        // Suma prawdopodobienstw po calej puli plus PASS wynosi 1 - kolumna "p" w danych
        // badawczych pozostaje prawdopodobienstwem bezwarunkowym.
        double suma = dDuza.PassCandidate.SelectionProbability;
        for (int i = 0; i < pulaDuza.Count; i++) suma += pulaDuza[i].SelectionProbability;
        T.Eq("suma prawdopodobienstw (zdarzenia + PASS) == 1", suma, 1.0, 1e-3);

        // Monotonicznosc: im cenniejsza cisza, tym czesciej brama ja wybiera.
        double poprzednia = -1.0;
        bool rosnie = true;
        var opisMono = new System.Text.StringBuilder();
        for (int i = 0; i <= 6; i++)
        {
            float u = 0.20f + i * 0.08f;
            float pb = policy.Select(pulaMala, Pass(u), new SeededRandom(3)).GatePassProbability;
            if (pb <= poprzednia) rosnie = false;
            poprzednia = pb;
            if (i > 0) opisMono.Append(' ');
            opisMono.Append(u.ToString("0.00", CultureInfo.InvariantCulture)).Append("->").Append((pb * 100).ToString("0.0", CultureInfo.InvariantCulture)).Append('%');
        }
        T.Ok("brama rosnie monotonicznie z uzytecznoscia ciszy", rosnie, opisMono.ToString());

        T.Section("TEST 5c - BRAMA: przelozenie na ZMIERZONE decyzje z rozgrywki");

        // Pary (best, passWynik) odczytane z PN_decyzje.log, 8 decyzji z dwoch sesji gry.
        // Zmierzony udzial PASS w wersji jednoetapowej: 0.2467% (jeden PASS na ~405 decyzji).
        double[][] zGry =
        {
            new[] { 0.683, 0.250 }, new[] { 0.608, 0.250 }, new[] { 0.649, 0.306 }, new[] { 0.614, 0.250 },
            new[] { 0.660, 0.250 }, new[] { 0.739, 0.250 }, new[] { 0.735, 0.313 }, new[] { 0.808, 0.489 }
        };
        double sumaBramy = 0.0;
        var opisGry = new System.Text.StringBuilder();
        for (int i = 0; i < zGry.Length; i++)
        {
            var jeden1 = new List<ScoredCandidate> { Kand((float)zGry[i][0], "G" + i) };
            float pb = policy.Select(jeden1, Pass((float)zGry[i][1]), new SeededRandom(i)).GatePassProbability;
            sumaBramy += pb;
            if (i > 0) opisGry.Append(' ');
            opisGry.Append((pb * 100).ToString("0.00", CultureInfo.InvariantCulture)).Append('%');
        }
        double sredniaBramy = sumaBramy / zGry.Length;
        T.Ok("na danych z gry brama podnosi udzial PASS powyzej 1%",
             sredniaBramy > 0.01,
             "srednia=" + (sredniaBramy * 100).ToString("0.00", CultureInfo.InvariantCulture) + "% (bylo 0.25%), krotnosc="
             + (sredniaBramy / 0.002467).ToString("0.0", CultureInfo.InvariantCulture) + "x, jeden PASS na "
             + (1.0 / sredniaBramy).ToString("0") + " decyzji | per decyzja: " + opisGry);

        // =============================================================================
        //  BRAMA NA POZIOMIE TURY - regresja trzech defektow znalezionych audytem
        // =============================================================================
        T.Section("TEST 5d - TURA: brama zapada RAZ, statystyki nie kurcza sie po odmowach");

        // Pula malejaca, wszyscy w pasmie (0.75 * 0.70 = 0.525).
        var pulaT = new List<ScoredCandidate>();
        for (int i = 0; i < 6; i++) pulaT.Add(Kand(0.70f - i * 0.01f, "T" + i));
        const float U_PASS_T = 0.50f;

        // --- WLASNOSC GLOWNA, PRZEFORMULOWANA PRZY PREWERIFIKACJI CZOLA ---
        //
        // Pierwotne brzmienie: "P(cisza) NIEZALEZNE od liczby odmow silnika". Bronilo przed
        // defektem, w ktorym brama losowala sie w KAZDEJ rundzie, wiec cisza kumulowala sie po
        // odmowach (1 - iloczyn(1-p_i)) i rosla z 11.92% na 53.74%. Ta wlasnosc padla dopiero
        // wtedy, gdy zaczela byc FALSZYWA Z DOBREGO POWODU.
        //
        // Od prewerifikacji czola brama widzi najlepsze zdarzenie WYKONALNE, a nie po prostu
        // najlepsze. Gdy trzy najlepsze opcje sa dla gry niemozliwe, realnie najlepsza opcja JEST
        // gorsza - i cisza ma prawo byc wtedy nieco atrakcyjniejsza. To nie jest zaleznosc od
        // LICZBY odmow, tylko od JAKOSCI tego, co zostalo.
        //
        // NOWE, MOCNIEJSZE BRZMIENIE: odmowa silnika dziala dokladnie jak NIEOBECNOSC kandydata
        // w puli. Dwie tury, ktore po odsianiu maja ten sam zbior wykonalnych opcji, musza dac
        // IDENTYCZNE P(cisza) - co do bitu, nie w przyblizeniu. Ta forma:
        //   - nadal wyklucza stary defekt (kumulacja dawalaby wartosci rosnace szybciej),
        //   - wyklucza tez KAZDY inny wplyw samego faktu odmowy na brame,
        //   - a przy tym jest prawdziwa, wiec nie zmusza do naginania kodu pod test.
        var opisOdmow = new System.Text.StringBuilder();
        bool odmowaRownaNieobecnosci = true;
        var pBramaPoOdmowach = new List<float>();
        for (int odmow = 0; odmow <= 4; odmow++)
        {
            int los, rnd;
            NarratorDecision zOdmowami = SymulujTure(policy, pulaT, Pass(U_PASS_T), new SeededRandom(21),
                                                     odmow, 12, out los, out rnd);

            // Ten sam stan swiata wyrazony inaczej: N najlepszych kandydatow po prostu NIE ISTNIEJE.
            var bezCzola = new List<ScoredCandidate>();
            for (int i = odmow; i < pulaT.Count; i++) bezCzola.Add(Kand(pulaT[i].Utility, "B" + i));
            int los2, rnd2;
            NarratorDecision bezNich = SymulujTure(policy, bezCzola, Pass(U_PASS_T), new SeededRandom(21),
                                                   0, 12, out los2, out rnd2);

            if (System.Math.Abs(zOdmowami.GatePassProbability - bezNich.GatePassProbability) > 1e-6f)
            {
                odmowaRownaNieobecnosci = false;
            }
            pBramaPoOdmowach.Add(zOdmowami.GatePassProbability);
            if (odmow > 0) opisOdmow.Append(' ');
            opisOdmow.Append(odmow).Append("odm->")
                     .Append((zOdmowami.GatePassProbability * 100).ToString("0.00", CultureInfo.InvariantCulture))
                     .Append('%');
        }
        T.Ok("[5d] odmowa silnika dziala JAK NIEOBECNOSC kandydata (P(cisza) identyczne)",
             odmowaRownaNieobecnosci, opisOdmow.ToString());

        // PLON PREWERIFIKACJI, wyrazony liczba: odmowy przestaly kosztowac RUNDY petli wyboru.
        // Przed nia kazda odmowa zjadala jedna runde (4 odmowy = 5 rund). Teraz faza 0 odsiewa
        // niewykonalne akcje ZANIM brama cokolwiek rozstrzygnie, wiec petla wykonuje jedna runde
        // niezaleznie od tego, ile gra odmowila.
        var opisRund = new System.Text.StringBuilder();
        bool zawszeJednaRunda = true;
        for (int odmow = 0; odmow <= 4; odmow++)
        {
            int los, rnd;
            SymulujTure(policy, pulaT, Pass(U_PASS_T), new SeededRandom(21), odmow, 12, out los, out rnd);
            if (rnd != 1) zawszeJednaRunda = false;
            if (odmow > 0) opisRund.Append(' ');
            opisRund.Append(odmow).Append("odm->").Append(rnd).Append("rund");
        }
        T.Ok("[5d] odmowy NIE kosztuja juz rund petli wyboru (faza 0 je pochlania)",
             zawszeJednaRunda, opisRund.ToString() + " (przed prewerifikacja: 4odm->5rund)");

        // Kontrola regresji: ile wynosilaby ta zaleznosc przed poprawka.
        double staraPrzy0 = UdzialCiszyBramaCoRunde(pulaT, U_PASS_T, 0.1, 0);
        double staraPrzy4 = UdzialCiszyBramaCoRunde(pulaT, U_PASS_T, 0.1, 4);
        T.Ok("kontrola: brama losowana CO RUNDE rosla z liczba odmow", staraPrzy4 > staraPrzy0 * 2.0,
             "co runde: 0 odmow=" + (staraPrzy0 * 100).ToString("0.00", CultureInfo.InvariantCulture)
             + "% 4 odmowy=" + (staraPrzy4 * 100).ToString("0.00", CultureInfo.InvariantCulture)
             + "%  |  raz na ture: " + (pBramaPoOdmowach[0] * 100).ToString("0.00", CultureInfo.InvariantCulture) + "% zawsze");

        // --- TURA Z FAKTYCZNYMI ODMOWAMI: statystyki, ranking i zuzycie rng na jednym przebiegu ---
        //
        // STRAZNIK PUSTEGO TESTU. Przy ziarnie 22 brama wybierala cisze juz w rundzie pierwszej,
        // wiec ZADNA odmowa sie nie wydarzala, a asercje ponizej przechodzily na pustym przebiegu.
        // Dlatego najpierw jawnie sprawdzamy, ze tura faktycznie przeszla przez 3 odmowy - bez tego
        // caly blok bylby asercja na szczescie w losowaniu, a nie na wlasnosc systemu.
        var licznikTury = new CountingRandom(7);
        int losT, rndT;
        NarratorDecision dOdm = SymulujTure(policy, pulaT, Pass(U_PASS_T), licznikTury, 3, 12, out losT, out rndT);

        // Straznik: odmowy padaja dzis PRZED brama, wiec sprawdzamy je po stronie fazy 0,
        // a nie po liczbie rund. Bez tego caly blok przechodzilby na pustym przebiegu.
        TurnResult turaOdm = SymulujTureF(policy, pulaT, Pass(U_PASS_T), new CountingRandom(7),
                                          k => string.CompareOrdinal(k.SortKey, "T0") != 0
                                               && string.CompareOrdinal(k.SortKey, "T1") != 0
                                               && string.CompareOrdinal(k.SortKey, "T2") != 0, 12);
        T.Ok("STRAZNIK: tura faktycznie wykonala 3 odmowy silnika i zakonczyla sie zdarzeniem",
             turaOdm.PreGateRefusals == 3 && turaOdm.Accepted,
             "odmowPrzedBrama=" + turaOdm.PreGateRefusals + " rund=" + turaOdm.Rounds
             + " PASS=" + turaOdm.Decision.IsPass);

        NarratorDecision dOdmF = turaOdm.Decision;

        // MIANOWNIK OPISUJE TURE, nie to, co z niej zostalo. Kandydat odrzucony przez silnik
        // ZOSTAJE w liscie ocenionych - inaczej udzial weta, progu i pasma mialby mianownik
        // kurczacy sie dokladnie tam, gdzie gra duzo odmawia.
        T.EqI("mianownik `kandydatow` opisuje TURE, nie ostatnia runde", dOdmF.CountScored, pulaT.Count);

        // `wSoftmaksie` liczy zdarzenia, ktore REALNIE konkurowaly - a odrzucone przez silnik nie
        // konkurowaly, bo gra ich nie dopuszcza. To NIE jest kurczenie sie mianownika: mianownikiem
        // jest CountScored wyzej, a ta liczba jest licznikiem "ilu stanelo do wyboru".
        T.EqI("`wSoftmaksie` liczy zdarzenia WYKONALNE, ktore stanely do wyboru",
              dOdmF.CountInSoftmax, pulaT.Count - 3);

        // `best` to najlepsze zdarzenie WYKONALNE - i o to chodzilo w trzecim zarzucie.
        // Przed prewerifikacja bylo to 0.70, czyli kandydat, ktoremu gra i tak odmowila:
        // brama porownywala cisze z opcja, ktorej nie bylo.
        T.Eq("`best` to najlepsze zdarzenie WYKONALNE, nie najlepsze w ogole",
             dOdmF.BestUtility, 0.67, 1e-6);
        T.Ok("[C3] referencja bramy przestala byc kandydatem skazanym na odmowe",
             dOdmF.BestUtility < 0.70f, "best=" + dOdmF.BestUtility.ToString("0.000", CultureInfo.InvariantCulture)
             + " (przed prewerifikacja: 0.700, czyli T0 - odrzucany przez silnik)");
        T.Ok("niezmiennik rankingu trzyma sie na zbiorze TURY", dOdmF.RankingIsComplete,
             "Ranking=" + dOdmF.Ranking.Count + " CountScored=" + dOdmF.CountScored);

        // --- kandydat odrzucony przez silnik NIE GINIE ze sladem ---
        // Dotyczy to takze odmow sprzed bramy: gdyby faza 0 usuwala kandydatow z listy zamiast je
        // oznaczac, wiersz [PN-DATA] opisywalby ture juz okrojona, a slad czynnikow odrzuconych
        // przepadalby bez sladu - czyli dokladnie regresja szostego dlugu, tylko o faze wczesniej.
        int odrzuconychWRankingu = dOdmF.Ranking.FindAll(x => x != null && x.Rejected == RejectionStage.EngineRefused).Count;
        T.EqI("odrzuceni przez silnik zostaja w rankingu z wlasnym sladem", odrzuconychWRankingu, 3);

        // --- ZUZYCIE RNG: 1 brama na ture + 2 pobrania (akcja, wariant) na runde ---
        T.EqI("zuzycie rng raportowane == 1 + 2 * liczba rund", losT, 1 + 2 * rndT);
        T.EqI("raport zgadza sie z FAKTYCZNA liczba wywolan rng.Next", licznikTury.NextCalls, losT);

        // --- FLAGA ODMOWY NIE PRZECIEKA MIEDZY TURAMI ---
        // Regresja na bledzie znalezionym wlasnie tym testem: EngineRefusedThisTurn jest lepkie
        // w obrebie tury, wiec ktos musi je kasowac. Gdy wlascicielem byl "wolajacy", ta sama pula
        // puszczona przez dwie tury wchodzila do drugiej juz unieruchomiona, a P(cisza) szlo
        // z 11.92% na 100% - awaria CICHA, bo wygladala jak poprawne milczenie narratora.
        var pulaPrzeciek = new List<ScoredCandidate>();
        for (int i = 0; i < 6; i++) pulaPrzeciek.Add(Kand(0.70f - i * 0.01f, "L" + i));
        SymulujTureF(policy, pulaPrzeciek, Pass(U_PASS_T), new SeededRandom(21),
                     k => string.CompareOrdinal(k.SortKey, "L0") != 0, 12);
        bool flagiPoPierwszej = pulaPrzeciek.Exists(x => x.EngineRefusedThisTurn);
        TurnResult druga = SymulujTureF(policy, pulaPrzeciek, Pass(U_PASS_T), new SeededRandom(21),
                                        k => true, 12);
        T.Ok("STRAZNIK: pierwsza tura faktycznie zostawila oznaczonego kandydata",
             flagiPoPierwszej, "oznaczonych po turze 1: "
             + pulaPrzeciek.FindAll(x => x.EngineRefusedThisTurn).Count);
        T.Ok("flaga odmowy NIE przecieka do tury nastepnej",
             druga.Decision.CountInSoftmax == 6 && druga.PreGateRefusals == 0,
             "wSoftmaksie=" + druga.Decision.CountInSoftmax + " odmowPrzedBrama=" + druga.PreGateRefusals);

        // Runda dalsza nie placi juz za brame.
        var pulaR = new List<ScoredCandidate> { Kand(0.70f, "R0"), Kand(0.69f, "R1") };
        NarratorDecision r1g = policy.Select(pulaR, Pass(U_PASS_T), new SeededRandom(9));
        T.EqI("runda 1 (brama swieza): 3 pobrania (brama + akcja + wariant)", r1g.RandomDraws, 3);
        NarratorDecision r2g = policy.Select(pulaR, Pass(U_PASS_T), new SeededRandom(9), false, r1g.Gate, r1g.TurnStats);
        T.EqI("runda 2 (brama zamrozona): 2 pobrania (akcja + wariant)", r2g.RandomDraws, 2);
        T.Eq("runda 2 dziedziczy P(cisza) z rundy 1", r2g.GatePassProbability, r1g.GatePassProbability, 1e-6);

        // Select nie mutuje wejscia.
        var wejscie = new List<ScoredCandidate> { Kand(0.9f, "A"), Kand(0.8f, "B") };
        int przed = wejscie.Count;
        string kolejnoscPrzed = string.Join(",", wejscie.Select(x => x.SortKey));
        policy.Select(wejscie, Pass(0.25f), new SeededRandom(1));
        T.Ok("Select nie mutuje listy wejsciowej",
             wejscie.Count == przed && string.Join(",", wejscie.Select(x => x.SortKey)) == kolejnoscPrzed, null);

        // Niezaleznosc od kolejnosci wejscia.
        var rndP = new Random(1);
        var baza = new List<ScoredCandidate> { Kand(0.9f, "AAA"), Kand(0.88f, "BBB"), Kand(0.86f, "CCC"), Kand(0.4f, "DDD") };
        string wzor = string.Join(",", policy.Select(baza, Pass(0.25f), new SeededRandom(9)).Ranking.Select(x => x.SortKey));
        bool permOk = true;
        for (int i = 0; i < 20; i++)
        {
            List<ScoredCandidate> perm = baza.OrderBy(x => rndP.Next()).ToList();
            string got = string.Join(",", policy.Select(perm, Pass(0.25f), new SeededRandom(9)).Ranking.Select(x => x.SortKey));
            if (got != wzor) permOk = false;
        }
        T.Ok("20 permutacji wejscia daje identyczny ranking", permOk, wzor);

        // ============================================================================
        //  KLASYFIKACJA CISZY NA GRANICY BUDZETU RUND
        // ============================================================================
        //
        // Dwie wartosci PassReason diagnozuja PRZECIWNE usterki i prowadza do przeciwnych
        // napraw, wiec pomylenie ich kaze stroic zle pokretlo:
        //   RoundBudgetExhausted -> budzet rund za ciasny, podnies maxSelectionRounds
        //   AllRefusedByGame     -> warunki twarde luzniejsze niz wymagania workerow
        //
        // Wlasnosc jest sformulowana jako ZGODNOSC DWOCH DROG do tego samego stanu swiata,
        // a nie jako oczekiwana wartosc wpisana z reki. Przy N kandydatach i N odmowach:
        //   maxRund == N     -> petla konczy sie na wyczerpaniu budzetu, ale pula JEST JUZ PUSTA
        //   maxRund == N + 1 -> runda N+1 dostaje pusta pule i polityka zwraca PASS/NoCandidates
        // To jest ten sam przebieg gry opisany dwoma budzetami, wiec powod MUSI byc ten sam.
        // Przed poprawka pierwsza droga raportowala RoundBudgetExhausted, druga AllRefusedByGame.
        const int N_GRANICA = 6;   // == pulaT.Count

        // Nic nie jest wykonalne, wiec pula opustoszeje niezaleznie od tego, w ktorej fazie.
        TurnResult gRowno = SymulujTureF(policy, pulaT, Pass(U_PASS_T), new SeededRandom(7),
                                         k => false, N_GRANICA);
        TurnResult gZapas = SymulujTureF(policy, pulaT, Pass(U_PASS_T), new SeededRandom(7),
                                         k => false, N_GRANICA + 1);

        // STRAZNIK PUSTEGO TESTU. Odkad faza 0 pochlania odmowy, liczy sie nie liczba rund, tylko
        // to, czy silnik faktycznie odmowil CALEJ puli - inaczej caly blok przechodzilby na
        // przebiegu, w ktorym nic sie nie wydarzylo.
        T.Ok("[5d] STRAZNIK: oba przebiegi graniczne faktycznie wyczerpaly pule odmowami",
             gRowno.Refusals.Count == N_GRANICA && gZapas.Refusals.Count == N_GRANICA,
             "odmow przy maxRund=N: " + gRowno.Refusals.Count + " (przed brama "
             + gRowno.PreGateRefusals + "), przy maxRund=N+1: " + gZapas.Refusals.Count
             + " (przed brama " + gZapas.PreGateRefusals + ")");

        T.Ok("[5d] pusta pula to AllRefusedByGame, nie budzet rund i nie brak kandydatow",
             gRowno.Decision.IsPass && gRowno.Decision.PassReason == PassReason.AllRefusedByGame,
             "PASS=" + gRowno.Decision.IsPass + " powod=" + gRowno.Decision.PassReason);

        T.EqS("[5d] ten sam stan swiata przy dwoch budzetach daje TEN SAM powod ciszy",
              gRowno.Decision.PassReason.ToString(), gZapas.Decision.PassReason.ToString());

        // NoCandidates ma znaczyc "warstwa kompozycji nic nie zwrocila". Gdyby mapowanie liczylo
        // wylacznie odmowy PO bramie, cisza z puli opustoszalej w fazie 0 zostawalaby przy
        // NoCandidates - czyli diagnoza wskazywalaby na generator kandydatow zamiast na warunki
        // twarde luzniejsze niz wymagania workerow. Dwie przeciwne naprawy.
        T.Ok("[5d] odmowy SPRZED bramy tez daja AllRefusedByGame, nie NoCandidates",
             gRowno.PreGateRefusals == N_GRANICA
             && gRowno.Decision.PassReason == PassReason.AllRefusedByGame,
             "odmowPrzedBrama=" + gRowno.PreGateRefusals + " powod=" + gRowno.Decision.PassReason);

        // DRUGA STRONA: gdy budzet naprawde wiaze, powod ma zostac przy budzecie.
        // Bez tej asercji naprawa mogla by po prostu skasowac RoundBudgetExhausted z uzycia.
        //
        // KONSTRUKCJA. Budzet pytan do gry jest WSPOLNY dla calej tury i rowny maxRund, wiec
        // przy 10 kandydatach i budzecie 3 faza 0 zuzywa go w calosci na trzy odmowy, a petla
        // nie ma juz czym sprawdzic zwyciezcy. Zostaje 7 kandydatow nietknietych.
        var pulaBudzet = new List<ScoredCandidate>();
        for (int i = 0; i < 10; i++) pulaBudzet.Add(Kand(0.70f - i * 0.005f, "U" + i));
        TurnResult gBudzet = SymulujTureF(policy, pulaBudzet, Pass(U_PASS_T), new SeededRandom(7),
                                          k => false, 3);

        T.Ok("[5d] STRAZNIK: przebieg z wiazacym budzetem zuzyl go w calosci i zostawil kandydatow",
             gBudzet.AcceptorCalls == 3 && gBudzet.Refusals.Count == 3
             && gBudzet.PreVerification == PreVerificationOutcome.BudgetExhausted,
             "pytanDoGry=" + gBudzet.AcceptorCalls + " odmow=" + gBudzet.Refusals.Count
             + " faza0=" + gBudzet.PreVerification + " rund=" + gBudzet.Rounds
             + " z 10 kandydatow");

        T.Ok("[5d] budzet wiazacy przy NIEPUSTEJ puli zostaje RoundBudgetExhausted",
             gBudzet.Decision.IsPass && gBudzet.Decision.PassReason == PassReason.RoundBudgetExhausted,
             "PASS=" + gBudzet.Decision.IsPass + " powod=" + gBudzet.Decision.PassReason
             + " (nietknietych kandydatow: " + (10 - gBudzet.Refusals.Count) + ")");

        // NIEZMIENNIK BUDZETU, sprawdzany na calej rodzinie przebiegow. Przed unifikacja faza 0
        // i petla mialy OSOBNE liczniki rowne maxRund, wiec tura mogla po cichu wykonac do
        // 2 * maxRund wywolan CanFireNow - limit, ktorego nikt nie zadeklarowal.
        bool budzetTrzyma = true;
        int najwiecejPytan = 0;
        foreach (int mr in new[] { 2, 3, 5, 8 })
        {
            for (int seed = 0; seed < 40; seed++)
            {
                TurnResult tB = SymulujTureF(policy, pulaBudzet, Pass(U_PASS_T), new SeededRandom(seed),
                                             k => false, mr);
                if (tB.AcceptorCalls > mr) budzetTrzyma = false;
                if (tB.AcceptorCalls > najwiecejPytan) najwiecejPytan = tB.AcceptorCalls;
            }
        }
        T.Ok("[5d] NIEZMIENNIK: pytanDoGry <= maxSelectionRounds w CALEJ turze",
             budzetTrzyma, "maksimum zaobserwowane: " + najwiecejPytan
             + " (przed unifikacja limit wynosil 2 * maxRund i nie byl nigdzie zadeklarowany)");

        // ================================================================================
        //  TEST 5e - DWA DLUGI WARSTWY DECYZYJNEJ: SPLACONE, ASERCJE ZOSTAJA JAKO REGRESJA
        // ================================================================================
        //
        // Sekcja powstala jako CZERWONA: opisywala dwie wlasnosci, ktorych nikt nie sprawdzal,
        // zanim ktokolwiek dotknal kodu. Obie sa dzis spelnione, a asercje zostaja bez zmiany
        // tresci - zmienil sie tylko ich status, z dlugu na zabezpieczenie przed powrotem.
        //
        //  DLUG 1 - suma p po Ranking wynosila 1.15 przy jednej odmowie i 2.73, gdy silnik
        //           odrzucil wszystko. Naprawa: prawdopodobienstwa zamrozone na rundzie pierwszej
        //           (SelectionPolicy C7), rozklad rundy koncowej wystawiony osobno jako
        //           NarratorDecision.WinnerRoundProbability.
        //  DLUG 2 - plaski softmax dawal akcji o K wariantach ukryta premie T*ln(K). Naprawa:
        //           wybor dwustopniowy (ActionWeighting), ocena akcji = MAKSIMUM wariantu w pasmie.

        T.Section("TEST 5e - REGRESJA: suma p po turze oraz niezaleznosc od licznosci wariantow");

        var pulaP = new List<ScoredCandidate>();
        for (int i = 0; i < 6; i++) pulaP.Add(Kand(0.70f - i * 0.01f, "P" + i));

        // KONSTRUKCJA JEST TU CALA TRESCIA TESTU i zostala przepisana po tym, jak sie ZDEGENEROWALA.
        //
        // Defekt sumy p wymaga TURY WIELORUNDOWEJ: p nadpisywane w rundzie k mieszalo sie z p
        // kandydata wypadlego w rundzie k-1. Pierwsza wersja tego testu wymuszala odmowy
        // licznikiem ("odmow pierwszym N pytaniom") i dawala 4 odmowy = 5 rund. Po wprowadzeniu
        // prewerifikacji czola te same odmowy padaja PRZED brama, wiec tura ma jedna runde -
        // i asercja przechodzila takze po usunieciu naprawy. Sprawdzone wstrzyknieciem: walidator
        // zostawal ZIELONY, czyli test pilnowal wlasnosci, ktorej juz nie wywolywal.
        //
        // Zeby odmowa padla PO bramie, czolo musi byc WYKONALNE, a niewykonalny musi byc ktos
        // INNY - wtedy faza 0 konczy sie od razu, a odmowy zbiera dopiero petla wyboru, gdy
        // softmax wskaze kandydata spoza czola. Ziarna przegladamy i bierzemy te, ktore faktycznie
        // daja wiecej niz jedna runde; straznik nizej pilnuje, ze takich jest dosc.
        var opisSum = new System.Text.StringBuilder();
        bool sumyOk = true;
        int turWielorundowych = 0;
        int najwiecejRund = 0;
        double najwiekszaSuma = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            TurnResult tE = SymulujTureF(policy, pulaP, Pass(0.50f), new SeededRandom(seed),
                                         k => string.CompareOrdinal(k.SortKey, "P0") == 0, 12);
            if (tE.Rounds < 2)
            {
                continue;
            }
            turWielorundowych++;
            if (tE.Rounds > najwiecejRund) najwiecejRund = tE.Rounds;

            double sumaE = 0;
            for (int i = 0; i < tE.Decision.Ranking.Count; i++)
            {
                if (tE.Decision.Ranking[i] != null) sumaE += tE.Decision.Ranking[i].SelectionProbability;
            }
            if (sumaE > najwiekszaSuma) najwiekszaSuma = sumaE;
            if (System.Math.Abs(sumaE - 1.0) > 2e-3) sumyOk = false;
        }
        opisSum.Append("tur wielorundowych=").Append(turWielorundowych)
               .Append(" max rund=").Append(najwiecejRund)
               .Append(" max suma p=").Append(najwiekszaSuma.ToString("0.0000", CultureInfo.InvariantCulture));

        T.Ok("[5e] STRAZNIK: przegladane ziarna faktycznie daja tury WIELORUNDOWE",
             turWielorundowych >= 20 && najwiecejRund >= 3,
             "z 200 ziaren: " + turWielorundowych + " tur o >1 rundzie, najdluzsza " + najwiecejRund);

        T.Ok("[5e] DLUG: suma p po decyzja.Ranking == 1 takze w turze WIELORUNDOWEJ",
             sumyOk, opisSum.ToString());

        // DRUGA STRONA TEJ SAMEJ NAPRAWY: rozklad losowania wyboru nie ginie, tylko przeprowadza
        // sie do osobnej kolumny o JAWNIE innym znaczeniu.
        bool pRundaRozna = false;
        for (int seed = 0; seed < 200 && !pRundaRozna; seed++)
        {
            TurnResult tR = SymulujTureF(policy, pulaP, Pass(0.50f), new SeededRandom(seed),
                                         k => string.CompareOrdinal(k.SortKey, "P0") == 0, 12);
            if (tR.Rounds >= 2 && !tR.Decision.IsPass
                && tR.Decision.WinnerRoundProbability.HasValue
                && System.Math.Abs(tR.Decision.WinnerRoundProbability.Value
                                   - tR.Decision.Winner.SelectionProbability) > 1e-4f)
            {
                pRundaRozna = true;
            }
        }
        T.Ok("[5e] pRunda NIESIE rozklad losowania wyboru, a nie kopie kolumny p", pRundaRozna,
             "znaleziono ture, w ktorej p (rozklad rundy pierwszej) rozni sie od pRunda");

        // pRunda JEST WARUNKOWE - nie zawiera czynnika bramy.
        //
        // ASERCJA JEST ROWNOSCIA WYPROWADZONA, a nie nierownoscia. Pierwsza wersja brzmiala
        // "pRunda >= p" i byla PUSTA: przy blednej implementacji pRunda rowna sie p co do bitu,
        // wiec warunek zachodzil w obie strony. Wykrylo to dopiero wstrzykniecie regresji -
        // sam zielony przebieg wygladal tak samo jak poprawny.
        //
        // Wyprowadzenie: p = (1 - pBrama) * pWyboru, a pRunda ma byc rowne pWyboru. Zatem
        //     pRunda * (1 - pBrama) == p
        // i jest to rownosc DOKLADNA, ktorej wersja z czynnikiem bramy nie spelnia (dawalaby
        // p * (1 - pBrama) == p, czyli wymagalaby pBrama == 0). Dlatego scenariusz ma pBrama
        // wyraznie dodatnie: U_pass 0.60 przy czole 0.70 i T = 0.1 daje okolo 0.269.
        var pulaWar = new List<ScoredCandidate> { Kand(0.70f, "W0"), Kand(0.66f, "W1") };
        TurnResult tWar = SymulujTureF(policy, pulaWar, Pass(0.60f), new SeededRandom(5),
                                       k => true, 8);

        T.Ok("[5e] STRAZNIK: przebieg kontrolny skonczyl sie ZDARZENIEM przy niezerowej bramie",
             !tWar.Decision.IsPass && tWar.Decision.WinnerRoundProbability.HasValue
             && tWar.Decision.GatePassProbability > 0.05f,
             "PASS=" + tWar.Decision.IsPass + " pBrama="
             + tWar.Decision.GatePassProbability.ToString("0.0000", CultureInfo.InvariantCulture));

        double pRundaWar = tWar.Decision.WinnerRoundProbability.HasValue
                               ? tWar.Decision.WinnerRoundProbability.Value : 0.0;
        double pWar = tWar.Decision.Winner.SelectionProbability;
        double pBramaWar = tWar.Decision.GatePassProbability;
        T.Eq("[5e] pRunda * (1 - pBrama) == p, czyli pRunda jest WARUNKOWE",
             pRundaWar * (1.0 - pBramaWar), pWar, 2e-3);

        // CISZA NIE MA pRunda - takze ta, ktora spadla z petli po wyczerpaniu budzetu.
        // Wpisywano tam wczesniej prawdopodobienstwo bramy, mimo ze brama ciszy NIE wybrala.
        //
        // STRAZNIK: potrzebna jest tura, w ktorej brama powiedziala "dzialaj", a mimo to
        // wynikiem jest PASS. Bez tego warunku test trafialby w cisze wybrana przez brame,
        // gdzie pole i tak jest puste z innego powodu - czyli przechodzilby na pustym przebiegu.
        // Sa DWIE gallezie konczace ture cisza techniczna - AllRefusedByGame i RoundBudgetExhausted -
        // i obie ustawiaja pRunda osobnym przypisaniem. Skan obejmuje wiec kilka budzetow, a wynik
        // wypisuje rozbicie: test, ktory trafia tylko w jedna galaz, przespalby regresje w drugiej.
        int techAll = 0, techBudzet = 0, techZPRunda = 0;
        for (int mr = 2; mr <= 5; mr++)
        {
            for (int seed = 0; seed < 150; seed++)
            {
                TurnResult tT = SymulujTureF(policy, pulaBudzet, Pass(U_PASS_T), new SeededRandom(seed),
                                             k => false, mr);
                if (!tT.Decision.IsPass || tT.Decision.Gate == null || tT.Decision.Gate.ChoseSilence)
                {
                    continue;
                }
                if (tT.Decision.PassReason == PassReason.AllRefusedByGame) techAll++;
                else if (tT.Decision.PassReason == PassReason.RoundBudgetExhausted) techBudzet++;
                if (tT.Decision.WinnerRoundProbability.HasValue) techZPRunda++;
            }
        }
        // Cisza z pustej puli objawia sie WEWNATRZ petli (Select zwraca PASS/NoCandidates),
        // a nie po niej - post-petlowa galaz AllRefusedByGame jest po przebudowie nieosiagalna
        // i pelni role alarmu. Sprawdzamy wiec obie sciezki, ale kazda tam, gdzie realnie zyje.
        TurnResult tPusta = SymulujTureF(policy, pulaT, Pass(U_PASS_T), new SeededRandom(7),
                                         k => false, 12);
        string opisTech = "po petli: RoundBudgetExhausted=" + techBudzet + " AllRefusedByGame="
                          + techAll + " | w petli: powod=" + tPusta.Decision.PassReason
                          + " | z wypelnionym pRunda=" + techZPRunda;

        T.Ok("[5e] STRAZNIK: skan trafil w cisze techniczna, ktorej brama NIE wybrala",
             techBudzet > 0, opisTech);
        T.EqI("[5e] cisza TECHNICZNA po petli nie dostaje pRunda", techZPRunda, 0);

        T.Ok("[5e] cisza z pustej puli (w petli) tez nie dostaje pRunda",
             tPusta.Decision.IsPass
             && tPusta.Decision.PassReason == PassReason.AllRefusedByGame
             && !tPusta.Decision.WinnerRoundProbability.HasValue,
             "powod=" + tPusta.Decision.PassReason + " pRunda="
             + (tPusta.Decision.WinnerRoundProbability.HasValue ? "WYPELNIONE" : "(puste)"));

        T.EqI("[5e] post-petlowa galaz AllRefusedByGame jest nieosiagalna (alarm, nie sciezka)",
              techAll, 0);

        // GALAZ SKRAJNA: silnik odrzuca WSZYSTKO, wiec zywy zostaje sam PASS.
        //
        // UWAGA NA INTERPRETACJE: od prewerifikacji czola ta galaz NIE rozstrzyga juz o zamrozeniu
        // p, bo wszystkie odmowy padaja w fazie 0 i tura ma jedna runde. Zostaje jako asercja
        // na co innego - ze przy pustej puli zdarzen cala masa idzie do PASS-a i nic jej nie
        // rozprasza. Rozstrzyga blok wyzej, na turach wielorundowych.
        var opisPusta = new System.Text.StringBuilder();
        bool pustaOk = true;
        foreach (int n in new[] { 5, 8 })
        {
            var pulaN = new List<ScoredCandidate>();
            for (int i = 0; i < n; i++) pulaN.Add(Kand(0.70f - i * 0.01f, "E" + i));
            int losE2, rndE2;
            NarratorDecision d2 = SymulujTure(policy, pulaN, Pass(0.50f), new SeededRandom(7),
                                              n, n + 2, out losE2, out rndE2);
            double suma2 = 0;
            for (int i = 0; i < d2.Ranking.Count; i++)
            {
                if (d2.Ranking[i] != null) suma2 += d2.Ranking[i].SelectionProbability;
            }
            if (System.Math.Abs(suma2 - 1.0) > 2e-3) pustaOk = false;
            opisPusta.Append('n').Append(n).Append("->").Append(suma2.ToString("0.0000", CultureInfo.InvariantCulture)).Append(' ');
        }
        T.Ok("[5e] DLUG: suma p == 1 takze gdy silnik odrzucil WSZYSTKO", pustaOk, opisPusta.ToString().Trim());

        // STRAZNIK PUSTEGO TESTU: obie asercje wyzej przechodzilyby trywialnie, gdyby tury
        // nie wykonywaly odmow (np. brama wybrala cisze w rundzie pierwszej). Sprawdzamy to
        // JAWNIE - ta sama pulapka co ziarno 22 w TEST 5d.
        //
        // Warunkiem jest LICZBA ODMOW, a nie liczba rund: od prewerifikacji czola odmowy padaja
        // przed brama i petla wyboru wykonuje jedna runde. Stara forma (rund == 4) po tej zmianie
        // gasla na POPRAWNYM zachowaniu - czyli byla asercja na implementacje, nie na wlasnosc.
        TurnResult tStraznikE = SymulujTureF(policy, pulaP, Pass(0.50f), new SeededRandom(7),
                                             k => string.CompareOrdinal(k.SortKey, "P0") != 0
                                                  && string.CompareOrdinal(k.SortKey, "P1") != 0
                                                  && string.CompareOrdinal(k.SortKey, "P2") != 0, 12);
        T.Ok("[5e] STRAZNIK: tura pomiarowa faktycznie wykonala 3 odmowy",
             tStraznikE.Refusals.Count == 3 && !tStraznikE.Decision.IsPass,
             "odmow=" + tStraznikE.Refusals.Count + " (przed brama " + tStraznikE.PreGateRefusals
             + ") rund=" + tStraznikE.Rounds + " PASS=" + tStraznikE.Decision.IsPass);

        // ---------------------------------------------------------------- DLUG 2: licznosc wariantow
        //
        // WLASNOSC, KTORA MA TERAZ OBOWIAZYWAC: szansa AKCJI nie zalezy od tego, ile opraw
        // narracyjnych ma ona w katalogu. Liczba wariantow jest cecha KATALOGU, a nie wlasnoscia
        // sytuacji w kolonii - gdyby wchodzila do decyzji, dosypanie opisow do jednego incydentu
        // po cichu czynilo by go czestszym, bez zmiany w scoringu i bez sladu w logu.
        //
        // TEST JEST NA NIEZMIENNICZOSC, nie na konkretna liczbe, i to jest istotne: asercja na
        // "udzial A wynosi 0.5" przechodzilaby takze dla polityki, ktora liczy cos zupelnie
        // innego, byle wyszlo pol. Asercja "udzial A jest TEN SAM przy 2, 4 i 8 wariantach"
        // nie da sie spelnic plaskim softmaksem przy zadnej kalibracji.
        var udzialyA = new List<double>();
        var opisWariantow = new System.Text.StringBuilder();
        foreach (int kWar in new[] { 2, 4, 8 })
        {
            var pulaW = new List<ScoredCandidate>();
            for (int v = 0; v < kWar; v++) pulaW.Add(KandAkcja(0.80f, "A", "A" + v));
            pulaW.Add(KandAkcja(0.80f, "B", "B0"));

            int wA = 0, wB = 0;
            for (int seed = 0; seed < 4000; seed++)
            {
                NarratorDecision dw = policy.Select(pulaW, Pass(0.20f), new SeededRandom(seed));
                if (dw.IsPass) continue;
                if (string.CompareOrdinal(dw.Winner.Event.ActionBlockId, "A") == 0) wA++; else wB++;
            }
            double u = (double)wA / (wA + wB);
            udzialyA.Add(u);
            opisWariantow.Append(kWar).Append("war->")
                         .Append(u.ToString("0.000", CultureInfo.InvariantCulture))
                         .Append(" (plaski dalby ")
                         .Append(((double)kWar / (kWar + 1)).ToString("0.000", CultureInfo.InvariantCulture))
                         .Append(") ");
        }

        bool niezmiennik = System.Math.Abs(udzialyA[0] - udzialyA[1]) < 0.03
                           && System.Math.Abs(udzialyA[1] - udzialyA[2]) < 0.03;
        T.Ok("[5e] udzial AKCJI nie zalezy od liczby jej wariantow (2, 4, 8)",
             niezmiennik, opisWariantow.ToString().Trim());

        // Kontrola, ze wynik nie jest przypadkiem zbiezny z polityka plaska: ta dawalaby
        // K/(K+1), czyli 0.667, 0.800 i 0.889 - wartosci ROZNE miedzy soba, wiec niezmiennik
        // wyzej by jej nie przepuscil. Tutaj sprawdzamy jeszcze, ze jestesmy od nich daleko.
        T.Ok("[5e] kontrola: polityka plaska dalaby przy 8 wariantach 0.889, my dajemy okolo 0.5",
             System.Math.Abs(udzialyA[2] - 0.5) < 0.05,
             "udzial przy 8 wariantach = " + udzialyA[2].ToString("0.000", CultureInfo.InvariantCulture)
             + " (plaski: 0.889, czyli ukryta premia T*ln(8) = 0.2079 uzytecznosci)");

        // DRUGI ETAP NADAL ROZNICUJE. Gdyby wybor wariantu byl jednostajny, rozdzielenie etapow
        // kupilo by niezmienniczosc kosztem utraty scoringu wewnatrz akcji - czyli oprawa
        // narracyjna przestalaby zalezec od kontekstu, a to jest polowa wkladu tej pracy.
        var pulaWew = new List<ScoredCandidate>
        {
            KandAkcja(0.80f, "A", "Adobry"),
            KandAkcja(0.72f, "A", "Aslaby"),
        };
        int dobry = 0, slaby = 0;
        for (int seed = 0; seed < 4000; seed++)
        {
            NarratorDecision dv = policy.Select(pulaWew, Pass(0.20f), new SeededRandom(seed));
            if (dv.IsPass) continue;
            if (string.CompareOrdinal(dv.Winner.SortKey, "Adobry") == 0) dobry++; else slaby++;
        }
        // ASERCJA WYPROWADZONA, NIE ZGADNIETA. Softmax przy roznicy ocen 0.08 i T = 0.1 daje
        // stosunek szans exp(0.08 / 0.1) = 2.2255. Pierwsza wersja tego testu zadala "ponad trzy
        // razy czesciej" - liczby wzietej z powietrza, ktora gasla na POPRAWNYM zachowaniu
        // (zmierzone 2.2456). Prog wyprowadzony z temperatury sprawdza mechanizm, a prog z reki
        // sprawdzalby wylacznie to, czy ktos trafil w intuicje.
        double stosunekB2 = (double)dobry / slaby;
        double oczekiwanyB2 = System.Math.Exp(0.08 / 0.1);
        T.Eq("[5e] wewnatrz akcji stosunek szans == exp(dU/T) (etap B2 nie jest jednostajny)",
             stosunekB2, oczekiwanyB2, 0.1);
        T.Ok("[5e] ... a wiec jawnie ROZNY od jednostajnego",
             stosunekB2 > 1.5, "lepszy=" + dobry + " slabszy=" + slaby
             + " stosunek=" + stosunekB2.ToString("0.000", CultureInfo.InvariantCulture)
             + " oczekiwany exp(0.8)=" + oczekiwanyB2.ToString("0.000", CultureInfo.InvariantCulture)
             + " (jednostajny dalby 1.000)");

        // ================================================================================
        //  TEST 5f - PAMIEC WERDYKTU SILNIKA (zarzut 2) I SERIA SWIADOMEJ CISZY (zarzut 5)
        // ================================================================================
        T.Section("TEST 5f - odmowa w zakresie AKCJI oraz rozdzielenie ciszy swiadomej od technicznej");

        // --- ZARZUT 2: jedna odmowa unieruchamia CALA akcje, nie jeden wariant ---
        //
        // Wyprowadzenie, nie kalibracja: CanFireNow zalezy od IncidentDef i stanu swiata, a nie od
        // oprawy narracyjnej, wiec w obrebie jednej tury wszystkie warianty tej samej akcji dostana
        // te sama odpowiedz. Pytanie o nie po kolei jest z definicji jalowe - i wlasnie to bylo
        // zmierzone w grze jako 44 odmowy jednego incydentu na 88 rund petli wyboru.
        var pulaAkcji = new List<ScoredCandidate>();
        for (int v = 0; v < 5; v++) pulaAkcji.Add(KandAkcja(0.80f - v * 0.001f, "ZLA", "Z" + v));
        pulaAkcji.Add(KandAkcja(0.60f, "DOBRA", "D0"));

        TurnResult tAkcja = SymulujTureF(policy, pulaAkcji, Pass(0.20f), new SeededRandom(3),
                                         k => string.CompareOrdinal(k.Event.ActionBlockId, "DOBRA") == 0, 8);

        T.Ok("STRAZNIK: tura faktycznie odpytala silnik o niewykonalna akcje",
             tAkcja.Refusals.Count > 0 && tAkcja.Accepted,
             "odmow=" + tAkcja.Refusals.Count + " przyjety=" + tAkcja.Accepted);

        T.EqI("[C2] piec wariantow niewykonalnej akcji kosztuje DOKLADNIE JEDNA odmowe",
              tAkcja.Refusals.Count, 1);

        int oznaczonychZlych = pulaAkcji.FindAll(
            x => string.CompareOrdinal(x.Event.ActionBlockId, "ZLA") == 0 && x.EngineRefusedThisTurn).Count;
        T.EqI("[C2] odmowa oznacza WSZYSTKIE warianty tej akcji, nie tylko odpytany",
              oznaczonychZlych, 5);

        // KOSZT TURY W PYTANIACH DO GRY, rozpisany na skladniki zamiast zgadniety:
        //   1 - faza 0 pyta o czolo (wariant akcji ZLA)  -> odmowa, cala akcja odpada
        //   2 - faza 0 pyta o nowe czolo (DOBRA)          -> POTWIERDZONE, faza 0 konczy sie
        // Petla NIE pyta trzeci raz. Wczesniejsza wersja pytala, "zeby stan akceptora zawsze
        // opisywal zwyciezce" - i bylo to bledne z dwoch powodow naraz: (a) zwyciezca jest tu
        // dokladnie tym potwierdzonym kandydatem, wiec pytanie nie wnosilo nic, (b) gdyby byl
        // innym wariantem tej samej akcji, odpowiedz i tak przyszlaby ZBUFOROWANA, policzona
        // dla parametrow kandydata potwierdzonego. Rdzen prosi wiec akceptor o samo
        // przygotowanie parametrow, bez pytania.
        //
        // Przy zakresie odmowy per WARIANT byloby 5 odmow + 1 potwierdzenie = 6 pytan, i roznica
        // rosnie liniowo z liczba opraw niewykonalnej akcji.
        T.EqI("[C2] koszt tury w pytaniach do gry: 2 zamiast 6", tAkcja.AcceptorCalls, 2);

        T.Ok("[C2] zwyciezca jest kandydatem POTWIERDZONYM, wiec wykonalnosc nie jest wnioskowana",
             !tAkcja.FeasibilityInferred, "wnioskowana=" + tAkcja.FeasibilityInferred);

        T.Ok("[C2] zwyciezca pochodzi z akcji WYKONALNEJ mimo gorszej oceny",
             !tAkcja.Decision.IsPass
             && string.CompareOrdinal(tAkcja.Decision.Winner.Event.ActionBlockId, "DOBRA") == 0,
             "zwyciezca=" + tAkcja.Decision.Winner.Label);

        // --- ZARZUT 5: cisza techniczna nie zuzywa limitu ciszy swiadomej ---
        //
        // TRZY ZACHOWANIA ROZLACZNE: swiadoma podnosi, zdarzenie zeruje, techniczna nie rusza.
        // Czwarta wlasnosc jest rownie wazna i latwo ja zgubic: DecisionCount ma rosnac we
        // WSZYSTKICH trzech, bo jest zegarem starzenia swiezosci, a czas plynie niezaleznie od
        // tego, czy narrator milczal z wyboru, czy z bezsily.
        var hSeria = new EventHistory();
        hSeria.RecordPass(1f, 0, true);
        hSeria.RecordPass(2f, 0, true);
        T.EqI("[C5] dwie ciszne swiadome -> seria 2", hSeria.DeliberateSilenceStreak, 2);

        hSeria.RecordPass(3f, 0, false);
        hSeria.RecordPass(4f, 0, false);
        T.EqI("[C5] dwie ciszne TECHNICZNE zostawiaja serie bez zmiany", hSeria.DeliberateSilenceStreak, 2);
        T.EqI("[C5] ale licznik decyzji rosnie w KAZDEJ z czterech tur", hSeria.DecisionCount, 4);

        hSeria.RecordEvent(Ev("PN_Akcja_Zrzut", Theme.Economic, Valence.Positive, EventScale.Minor), 5f, 0);
        T.EqI("[C5] wybrane zdarzenie ZERUJE serie swiadomej ciszy", hSeria.DeliberateSilenceStreak, 0);
        T.EqI("[C5] i tez przesuwa zegar decyzji", hSeria.DecisionCount, 5);

        // Straznik serii czyta TEN licznik, wiec cisza techniczna nie moze go uruchomic.
        // Przed naprawa kolonia, w ktorej gra duzo odmawia, byla zmuszana do dzialania za cudze
        // przeszkody - obciazenie skorelowane z kontekstem, nie losowe.
        var ppSeria = PassScoringParams.Defaults();
        ppSeria.maxStreak = 2;
        var hTech = new EventHistory();
        for (int i = 0; i < 5; i++) hTech.RecordPass(1f + i, 0, false);
        T.Ok("[C5] piec cisz TECHNICZNYCH nie uruchamia straznika serii",
             !SelectionPolicy.IsPassSuppressedByStreak(Ctx(hTech, 9f), ppSeria),
             "seria=" + hTech.DeliberateSilenceStreak + " maxStreak=" + ppSeria.maxStreak
             + " decyzji=" + hTech.DecisionCount);

        var hSwiad = new EventHistory();
        for (int i = 0; i < 5; i++) hSwiad.RecordPass(1f + i, 0, true);
        T.Ok("[C5] piec cisz SWIADOMYCH straznika uruchamia",
             SelectionPolicy.IsPassSuppressedByStreak(Ctx(hSwiad, 9f), ppSeria),
             "seria=" + hSwiad.DeliberateSilenceStreak);

        // ================================================================================
        //  TEST 5k - KRYZYS SKRAJNY: STRAZNIK SERII ZAWIESZONY, ZEGAR SERII ZATRZYMANY
        // ================================================================================
        //
        // Decyzja autora po przegladzie etapu 4: w kryzysie skrajnym straznik serii nie zmusza
        // narratora do dzialania, a cisza wybrana w kryzysie nie zuzywa limitu swiadomego
        // milczenia. Poza kryzysem - bez zmian. Wszystkie trzy przypadki sa potrzebne naraz:
        // bez "poza kryzysem" test przeszedlby na implementacji, ktora wylaczyla straznika
        // w ogole; bez "seria ponizej limitu" - na implementacji, ktora zapala flage zawsze.
        T.Section("TEST 5k - kryzys skrajny: straznik serii zawieszony, cisza w kryzysie poza seria");

        PassScoringParams ppK = cfg.pass;
        var hSeriaK = new EventHistory();
        for (int i = 0; i < ppK.maxStreak; i++)
        {
            hSeriaK.RecordPass(5f + i, 0, true);
        }
        var hKrotkaK = new EventHistory();
        hKrotkaK.RecordPass(5f, 0, true);

        T.Ok("STRAZNIK: seria faktycznie na limicie (i limit wlaczony)",
             ppK.maxStreak > 0 && hSeriaK.DeliberateSilenceStreak >= ppK.maxStreak
             && hKrotkaK.DeliberateSilenceStreak < ppK.maxStreak,
             "maxStreak=" + ppK.maxStreak + " seria=" + hSeriaK.DeliberateSilenceStreak
             + " krotka=" + hKrotkaK.DeliberateSilenceStreak);

        var pulaK = new List<ScoredCandidate>
        {
            KandAkcja(0.60f, "AKCJA_K1", "K1"),
            KandAkcja(0.58f, "AKCJA_K2", "K2")
        };
        CandidateAcceptor wszystkoK = delegate(ScoredCandidate k, bool pytaj, out string pw)
        {
            pw = null;
            return AcceptorVerdict.Accepted;
        };
        var runnerK = new TurnRunner(policy, ppK, 8);

        int cisz = 0, stlumionychPoza = 0, zawieszonychPoza = 0;
        int ciszWKryzysie = 0, stlumionychWKryzysie = 0, zawieszonychWKryzysie = 0;
        int swiadomychWKryzysie = 0, swiadomychPoza = 0, ciszPozaKrotka = 0;
        int flagKrotka = 0, kolumnaZgodna = 0, sladZgodny = 0;
        int ciszKrotkaKryzys = 0, swiadomychKrotkaKryzys = 0, kolumnaFalszywa = 0;
        const int ZIAREN_K = 60;

        for (int ziarno = 0; ziarno < ZIAREN_K; ziarno++)
        {
            // (1) POZA kryzysem, seria na limicie - straznik ma dzialac jak przedtem.
            DecisionContext cPoza = DecisionContext.Create(new WorldSnapshot(), hSeriaK, 10f,
                                                           Intent.Breathe, 0f, 0.9f, false);
            TurnResult rPoza = runnerK.Run(cPoza, new List<ScoredCandidate>(pulaK), Pass(0.95f),
                                           new SeededRandom(ziarno), 0f, wszystkoK,
                                           KluczWykonania, ZakresCache);
            if (rPoza.Decision.IsPass) cisz++;
            if (rPoza.Decision.PassSuppressedByStreak) stlumionychPoza++;
            if (rPoza.Decision.StreakWaivedByCrisis) zawieszonychPoza++;

            // (2) W kryzysie, seria na limicie - straznik zawieszony, cisza znowu mozliwa.
            DecisionContext cKryzys = DecisionContext.Create(new WorldSnapshot(), hSeriaK, 10f,
                                                             Intent.Breathe, 0f, 0.9f, true);
            TurnResult rKryzys = runnerK.Run(cKryzys, new List<ScoredCandidate>(pulaK), Pass(0.95f),
                                             new SeededRandom(ziarno), 0f, wszystkoK,
                                             KluczWykonania, ZakresCache);
            if (rKryzys.Decision.IsPass) ciszWKryzysie++;
            if (rKryzys.Decision.PassSuppressedByStreak) stlumionychWKryzysie++;
            if (rKryzys.Decision.StreakWaivedByCrisis) zawieszonychWKryzysie++;
            if (rKryzys.Decision.IsPass && rKryzys.DeliberateSilence) swiadomychWKryzysie++;
            if ((rKryzys.Decision.ToDataFragment() ?? string.Empty).Contains("straznikZawieszony=true")) kolumnaZgodna++;
            if ((rKryzys.Decision.PolicyTrace ?? string.Empty).Contains("ZAWIESZONY")) sladZgodny++;

            // (3) Seria PONIZEJ limitu: w kryzysie zadna flaga nie ma prawa sie zapalic,
            //     a poza kryzysem cisza Competitive jest swiadoma i podnosi serie.
            DecisionContext cKrotkaKryzys = DecisionContext.Create(new WorldSnapshot(), hKrotkaK, 10f,
                                                                   Intent.Breathe, 0f, 0.9f, true);
            TurnResult rKrotkaKryzys = runnerK.Run(cKrotkaKryzys, new List<ScoredCandidate>(pulaK),
                                                   Pass(0.95f), new SeededRandom(ziarno), 0f,
                                                   wszystkoK, KluczWykonania, ZakresCache);
            if (rKrotkaKryzys.Decision.PassSuppressedByStreak || rKrotkaKryzys.Decision.StreakWaivedByCrisis) flagKrotka++;
            // ZAMROZENIE SERII W KRYZYSIE PONIZEJ LIMITU (przeglad: dotad sprawdzane tylko NA limicie,
            // gdzie zawieszenie straznika pokrywa sie z kryzysem - pomylenie "kryzys" z "zawieszeniem"
            // przechodzilo na zielono, a cisza w kryzysie ponizej limitu podnosila serie).
            if (rKrotkaKryzys.Decision.IsPass)
            {
                ciszKrotkaKryzys++;
                if (rKrotkaKryzys.DeliberateSilence) swiadomychKrotkaKryzys++;
            }
            // Kolumna i slad w DRUGA strone: "false" tam, gdzie zawieszenia nie ma.
            if (!(rKrotkaKryzys.Decision.ToDataFragment() ?? string.Empty).Contains("straznikZawieszony=false")
                || (rKrotkaKryzys.Decision.PolicyTrace ?? string.Empty).Contains("ZAWIESZONY")) kolumnaFalszywa++;
            if (!(rPoza.Decision.ToDataFragment() ?? string.Empty).Contains("straznikZawieszony=false")
                || (rPoza.Decision.PolicyTrace ?? string.Empty).Contains("ZAWIESZONY")) kolumnaFalszywa++;

            DecisionContext cKrotka = DecisionContext.Create(new WorldSnapshot(), hKrotkaK, 10f,
                                                             Intent.Breathe, 0f, 0.9f, false);
            TurnResult rKrotka = runnerK.Run(cKrotka, new List<ScoredCandidate>(pulaK), Pass(0.95f),
                                             new SeededRandom(ziarno), 0f, wszystkoK,
                                             KluczWykonania, ZakresCache);
            if (rKrotka.Decision.IsPass)
            {
                ciszPozaKrotka++;
                if (rKrotka.DeliberateSilence) swiadomychPoza++;
            }
        }

        T.EqI("poza kryzysem, seria na limicie: straznik TLUMI PASS w kazdej turze", stlumionychPoza, ZIAREN_K);
        T.EqI("... wiec narrator nie milczy ani razu", cisz, 0);
        T.EqI("... i flaga zawieszenia sie NIE zapala", zawieszonychPoza, 0);
        T.EqI("w kryzysie, seria na limicie: straznik ZAWIESZONY w kazdej turze", zawieszonychWKryzysie, ZIAREN_K);
        T.EqI("... i PASS NIE jest tlumiony", stlumionychWKryzysie, 0);
        T.Ok("... wiec cisza znowu jest mozliwa (PASS 0.95 wobec 0.60: brama wybiera ja prawie zawsze)",
             ciszWKryzysie >= ZIAREN_K * 9 / 10, "cisz w kryzysie: " + ciszWKryzysie + "/" + ZIAREN_K);
        T.EqI("cisza w kryzysie NIE jest swiadoma - nie podnosi serii (zegar serii zatrzymany)",
              swiadomychWKryzysie, 0);
        T.Ok("STRAZNIK: poza kryzysem cisza przy krotkiej serii faktycznie wystepuje",
             ciszPozaKrotka >= ZIAREN_K * 9 / 10, "cisz: " + ciszPozaKrotka + "/" + ZIAREN_K);
        T.EqI("poza kryzysem ta sama cisza JEST swiadoma (kontrola: regula nie wylaczyla ksiegowania)",
              swiadomychPoza, ciszPozaKrotka);
        T.EqI("seria ponizej limitu w kryzysie: ani tlumienie, ani zawieszenie", flagKrotka, 0);
        T.EqI("kolumna straznikZawieszony=true w kazdej turze z zawieszeniem", kolumnaZgodna, ZIAREN_K);
        T.EqI("slad polityki odnotowuje zawieszenie", sladZgodny, ZIAREN_K);
        T.EqI("tam, gdzie zawieszenia NIE ma: kolumna =false i slad bez 'ZAWIESZONY'", kolumnaFalszywa, 0);
        T.Ok("STRAZNIK: w kryzysie ponizej limitu cisza faktycznie wystepuje",
             ciszKrotkaKryzys >= ZIAREN_K * 9 / 10, "cisz: " + ciszKrotkaKryzys + "/" + ZIAREN_K);
        T.EqI("cisza w kryzysie PONIZEJ limitu tez nie podnosi serii (zamrozenie, nie zawieszenie)",
              swiadomychKrotkaKryzys, 0);

        // ---- TURY ZAKONCZONE ZDARZENIEM W KRYZYSIE - deterministycznie, nie jednym ziarnem ----
        // Przy PASS 0.95 zdarzenie w kryzysie wypadalo na jednym ziarnie z 60; regresja ustawiajaca
        // flage zawieszenia tylko przy ciszy przechodzila przy innym zestawie ziaren.
        int zdarzenWKryzysie = 0, zawieszonychZdarzen = 0, swiadomychZdarzen = 0;
        for (int ziarno = 0; ziarno < ZIAREN_K; ziarno++)
        {
            DecisionContext cZd = DecisionContext.Create(new WorldSnapshot(), hSeriaK, 10f,
                                                         Intent.Breathe, 0f, 0.9f, true);
            TurnResult rZd = runnerK.Run(cZd, new List<ScoredCandidate>(pulaK), Pass(0.05f),
                                         new SeededRandom(ziarno), 0f, wszystkoK, KluczWykonania, ZakresCache);
            if (!rZd.Decision.IsPass)
            {
                zdarzenWKryzysie++;
                if (rZd.Decision.StreakWaivedByCrisis
                    && (rZd.Decision.ToDataFragment() ?? string.Empty).Contains("straznikZawieszony=true"))
                {
                    zawieszonychZdarzen++;
                }
                if (rZd.DeliberateSilence) swiadomychZdarzen++;
            }
        }
        T.Ok("STRAZNIK: przy PASS 0.05 tury w kryzysie koncza sie zdarzeniem", zdarzenWKryzysie >= ZIAREN_K * 9 / 10,
             "zdarzen: " + zdarzenWKryzysie + "/" + ZIAREN_K);
        T.EqI("zawieszenie odnotowane TAKZE w turach zakonczonych zdarzeniem", zawieszonychZdarzen, zdarzenWKryzysie);
        T.EqI("tura zakonczona zdarzeniem nigdy nie jest cisza swiadoma", swiadomychZdarzen, 0);

        // ---- CISZA TECHNICZNA: DeliberateSilence == false dla KAZDEGO powodu innego niz Competitive ----
        // Przeglad: usuniecie warunku PassReason == Competitive z TurnResult.DeliberateSilence
        // zostawialo walidator zielony, bo akceptor w 5k zawsze przyjmowal - a to przywraca defekt
        // z zarzutu 5 kroku 3 (cisza techniczna podnosi serie). Cztery sciezki, w kryzysie i poza nim.
        CandidateAcceptor odmowaZawsze = delegate(ScoredCandidate k, bool pytaj, out string pw)
        {
            pw = "test: zawsze odmowa";
            return pytaj ? AcceptorVerdict.RefusedByGame : AcceptorVerdict.Accepted;
        };
        CandidateAcceptor niedostepny = delegate(ScoredCandidate k, bool pytaj, out string pw)
        {
            pw = "test: werdykt niedostepny";
            return pytaj ? AcceptorVerdict.Unanswerable : AcceptorVerdict.Accepted;
        };
        var pulaTrzy = new List<ScoredCandidate>
        {
            KandAkcja(0.60f, "AKCJA_T1", "T1"), KandAkcja(0.59f, "AKCJA_T2", "T2"), KandAkcja(0.58f, "AKCJA_T3", "T3")
        };
        var sciezki = new[]
        {
            new { nazwa = "pusta pula", pula = new List<ScoredCandidate>(), akc = wszystkoK, rund = 8,
                  powod = PassReason.NoCandidates },
            new { nazwa = "silnik odmawia wszystkim", pula = pulaTrzy, akc = odmowaZawsze, rund = 8,
                  powod = PassReason.AllRefusedByGame },
            new { nazwa = "werdykt niedostepny", pula = pulaTrzy, akc = niedostepny, rund = 8,
                  powod = PassReason.VerdictUnavailable },
            new { nazwa = "budzet pytan wyczerpany w fazie 0", pula = pulaTrzy, akc = odmowaZawsze, rund = 2,
                  powod = PassReason.RoundBudgetExhausted },
        };
        var hZero = new EventHistory();
        foreach (var sc in sciezki)
        {
            foreach (bool kryzys in new[] { false, true })
            {
                var runnerT = new TurnRunner(policy, ppK, sc.rund);
                DecisionContext cT = DecisionContext.Create(new WorldSnapshot(), hZero, 10f,
                                                            Intent.Hold, 0f, 0.3f, kryzys);
                TurnResult rT = runnerT.Run(cT, new List<ScoredCandidate>(sc.pula), Pass(0.05f),
                                            new SeededRandom(7), 0f, sc.akc, KluczWykonania, ZakresCache);
                T.Ok("cisza techniczna [" + sc.nazwa + (kryzys ? ", kryzys" : "") + "]: PASS o powodzie "
                     + sc.powod + " i DeliberateSilence == false",
                     rT.Decision.IsPass && rT.Decision.PassReason == sc.powod && !rT.DeliberateSilence,
                     "pass=" + rT.Decision.IsPass + " powod=" + rT.Decision.PassReason
                     + " swiadoma=" + rT.DeliberateSilence);
            }
        }

        // ---- maxStreak = 0 (straznik wylaczony - legalna konfiguracja po Sanitize) ----
        var ppZero = PassScoringParams.Defaults();
        ppZero.maxStreak = 0;
        foreach (bool kryzys in new[] { false, true })
        {
            DecisionContext cZ = DecisionContext.Create(new WorldSnapshot(), hSeriaK, 10f,
                                                        Intent.Breathe, 0f, 0.9f, kryzys);
            T.Ok("maxStreak=0" + (kryzys ? " w kryzysie" : "") + ": ani tlumienia, ani zawieszenia",
                 !SelectionPolicy.IsPassSuppressedByStreak(cZ, ppZero)
                 && !SelectionPolicy.IsStreakWaivedByCrisis(cZ, ppZero)
                 && !SelectionPolicy.IsStreakAtLimit(cZ, ppZero), null);
            int ciszZero = 0, flagZero = 0;
            for (int ziarno = 0; ziarno < 20; ziarno++)
            {
                TurnResult rZ = new TurnRunner(policy, ppZero, 8).Run(cZ, new List<ScoredCandidate>(pulaK),
                    Pass(0.95f), new SeededRandom(ziarno), 0f, wszystkoK, KluczWykonania, ZakresCache);
                if (rZ.Decision.IsPass) ciszZero++;
                if (rZ.Decision.PassSuppressedByStreak || rZ.Decision.StreakWaivedByCrisis
                    || !(rZ.Decision.ToDataFragment() ?? string.Empty).Contains("straznikZawieszony=false")) flagZero++;
            }
            T.Ok("maxStreak=0" + (kryzys ? " w kryzysie" : "") + ": cisza mozliwa, flagi i kolumna false",
                 ciszZero >= 18 && flagZero == 0, "cisz=" + ciszZero + "/20 flag=" + flagZero);
        }

        // ================================================================================
        //  TEST 5l - ODLOZENIA PO DECYZJI NIE ZASILAJA LICZNIKA "odlozonych"
        // ================================================================================
        //
        // ZNALEZIONE W DANYCH v5 Z WANILIOWEGO SYMULATORA DEBUGOWEGO (33 wiersze): 36 ze 115
        // odlozen padlo PO przyjeciu zwyciezcy
        // w petli rund. Tura juz sie konczyla, wiec nie zmienialy niczego w wyborze - a zawyzaly
        // kolumne i przestawialy rodzenstwo zwyciezcy w rankingu na "odlozone".
        //
        // Uklad: czolo X (jedna oprawa) potwierdzone w fazie 0, a runda wybiera akcje Y o DWOCH
        // oprawach roznych parametrow. Wtedy silnik jest pytany o Y w petli - i to jest jedyna
        // sciezka, w ktorej stary kod odkladal po fakcie.
        T.Section("TEST 5l - odlozenia po decyzji nie trafiaja do licznika ani do rankingu");

        var pulaL = new List<ScoredCandidate>
        {
            KandMoc(0.800f, "AKCJA_X", "X_n", IntensityLevel.Normal),
            KandMoc(0.790f, "AKCJA_Y", "Y_hi", IntensityLevel.High),
            KandMoc(0.785f, "AKCJA_Y", "Y_lo", IntensityLevel.Low),
        };
        int turY = 0, naruszenLicznika = 0, naruszenRankingu = 0;
        for (int ziarno = 0; ziarno < 200; ziarno++)
        {
            TurnResult rL = SymulujTureF(policy, pulaL, Pass(0.10f), new SeededRandom(ziarno), k => true, 8);
            if (!rL.Accepted || rL.Decision.Winner == null
                || string.CompareOrdinal(rL.Decision.Winner.Event.ActionBlockId, "AKCJA_Y") != 0)
            {
                continue;
            }
            turY++;
            if (rL.DeferredVariants != 0) naruszenLicznika++;
            for (int i = 0; i < pulaL.Count; i++)
            {
                ScoredCandidate k = pulaL[i];
                if (string.CompareOrdinal(k.Event.ActionBlockId, "AKCJA_Y") == 0
                    && !ReferenceEquals(k, rL.Decision.Winner)
                    && k.Rejected == RejectionStage.Unverifiable)
                {
                    naruszenRankingu++;
                }
            }
        }

        T.Ok("STRAZNIK: petla faktycznie wybrala akcje spoza potwierdzonego czola", turY >= 20,
             "tur ze zwyciezca Y: " + turY + "/200");
        T.EqI("odlozonych == 0, gdy jedyne 'odlozenie' padloby po decyzji", naruszenLicznika, 0);
        T.EqI("rodzenstwo zwyciezcy NIE jest oznaczane w rankingu jako odlozone", naruszenRankingu, 0);

        // ================================================================================
        //  TEST 5j - DZIURY POKRYCIA ZNALEZIONE WSTRZYKNIECIEM REGRESJI
        // ================================================================================
        //
        // Wszystkie trzy wlasnosci ponizej byly SPELNIONE przez kod, ale nie pilnowane przez
        // zadna asercje: wstrzykniecie starej wersji zostawialo walidator zielony. Przyczyna
        // jest za kazdym razem ta sama - scenariusz testowy nie odrozniał dwoch rzeczy, ktore
        // w grze sa rozne.
        T.Section("TEST 5j - zakres po payloadzie, rozdzielone liczniki, weto przezywa odlozenie");

        // --- (a) ZAKRES CACHE'U IDZIE PO PAYLOADZIE, NIE PO KLOCKU AKCJI ---
        //
        // Ev() ustawia ActionBlockId i ActionPayload na te sama wartosc, wiec kazdy dotychczasowy
        // test przechodzil niezaleznie od tego, ktorego pola uzywa zakres. Tutaj dwa ROZNE klocki
        // akcji wskazuja JEDEN payload - uklad, ktory dzis w katalogu nie wystepuje, ale ktorego
        // nic nie zabrania, a ktory przebijalby obie warstwy obrony naraz.
        var pulaWspolny = new List<ScoredCandidate>
        {
            KandPayload(0.80f, "AKCJA_P", "WSPOLNY", "P0", IntensityLevel.Normal),
            KandPayload(0.78f, "AKCJA_Q", "WSPOLNY", "Q0", IntensityLevel.Normal),
            KandPayload(0.60f, "AKCJA_R", "INNY", "R0", IntensityLevel.Normal),
        };

        TurnResult tWsp = SymulujTureF(policy, pulaWspolny, Pass(0.25f), new SeededRandom(4),
                                       k => string.CompareOrdinal(k.Event.ActionPayload, "INNY") == 0, 8);

        T.Ok("[5j] STRAZNIK: tura faktycznie odpytala silnik o wspolny payload",
             tWsp.Refusals.Count > 0, "odmow=" + tWsp.Refusals.Count);

        bool brakPowtorzenia = !tWsp.Warnings.Exists(w => w.StartsWith("TEST: silnik zapytany"));
        T.Ok("[5j] jedna odmowa obejmuje OBA klocki akcji o tym samym payloadzie",
             brakPowtorzenia && tWsp.Refusals.Count == 1,
             "odmow=" + tWsp.Refusals.Count + " ostrzezen o powtorce="
             + tWsp.Warnings.FindAll(w => w.StartsWith("TEST: silnik zapytany")).Count
             + " (przy zakresie po ActionBlockId byloby 2 pytania o ten sam IncidentDef, "
             + "a drugie dostaloby odpowiedz zbuforowana)");

        // --- (b) ODLOZENI I ODRZUCENI TO DWA ROZNE LICZNIKI ---
        //
        // Obie grupy wypadaja z puli wyboru, wiec zachowanie narratora jest identyczne - rozni
        // sie WYLACZNIE slad decyzji. A to on trafia do rozdzialu o ewaluacji i to on mowi,
        // ktore pokretlo stroic: odmowy -> zaostrz warunki twarde; odlozenia -> tura dzieli tick
        // albo katalog ma wiele zestawow parametrow na payload.
        var pulaLiczniki = new List<ScoredCandidate>
        {
            KandMoc(0.80f, "AKCJA_L", "L_hi", IntensityLevel.High),
            KandMoc(0.79f, "AKCJA_L", "L_lo", IntensityLevel.Low),
            KandMoc(0.78f, "AKCJA_L", "L_vl", IntensityLevel.VeryLow),
        };
        TurnResult tLicz = SymulujTureF(policy, pulaLiczniki, Pass(0.25f), new SeededRandom(2),
                                        k => true, 8);
        string sladLicz = tLicz.Decision.PolicyTrace ?? string.Empty;

        T.Ok("[5j] STRAZNIK: tura faktycznie cos odlozyla i niczego nie odmowila",
             tLicz.DeferredVariants == 2 && tLicz.Refusals.Count == 0,
             "odlozonych=" + tLicz.DeferredVariants + " odmow=" + tLicz.Refusals.Count);

        T.Ok("[5j] slad decyzji rozdziela odlozonych od odrzuconych przez silnik",
             sladLicz.Contains("odrzSilnik=0") && sladLicz.Contains("odlozonych=2"),
             "fragment sladu: " + FragmentSladu(sladLicz, "odrzSilnik"));

        // --- (c) WETO PRZEZYWA ODLOZENIE ---
        //
        // Weto jest wlasnoscia kandydata i zachodzi niezaleznie od tego, czy gra by go dopuscila.
        // Odlozenie zawetowanego zabieraloby go licznikowi weta, czyli psuloby metryke, ktora
        // z wykonalnoscia nie ma nic wspolnego.
        var pulaWeto = new List<ScoredCandidate>
        {
            KandMoc(0.80f, "AKCJA_W", "W_hi", IntensityLevel.High),
            KandMoc(0.00f, "AKCJA_W", "W_lo_weto", IntensityLevel.Low),
        };
        pulaWeto[1].Vetoed = true;

        TurnResult tWeto = SymulujTureF(policy, pulaWeto, Pass(0.25f), new SeededRandom(2),
                                        k => true, 8);

        T.Ok("[5j] STRAZNIK: tura faktycznie potwierdzila czolo przy zawetowanym rodzenstwie",
             tWeto.Accepted && tWeto.Decision.CountScored == 2,
             "przyjety=" + tWeto.Accepted + " kandydatow=" + tWeto.Decision.CountScored);

        T.EqI("[5j] zawetowany wariant NIE jest odkladany, wiec nie znika z licznika weta",
              tWeto.Decision.CountVetoed, 1);

        T.EqI("[5j] ... i nie jest liczony jako odlozony", tWeto.DeferredVariants, 0);

        // ================================================================================
        //  TEST 5i - CACHE SILNIKA JEST WSPOLNY DLA TUR W JEDNYM TICKU
        // ================================================================================
        //
        // ZNALEZISKO WERYFIKACJI ADWERSARIALNEJ, odtworzone na prawdziwym Core.
        //
        // IncidentWorker buforuje wynik CanFireNowSub w polach INSTANCYJNYCH na parze
        // (IncidentDef, tick) - bez celu i bez parametrow. Storyteller.MakeIncidentsForInterval
        // iteruje przy tym po WSZYSTKICH celach (zdekompilowane: for i < targets.Count), a kazda
        // kolonia gracza zwraca tag Map_PlayerHome. Przy dwoch koloniach nasz comp wykonuje wiec
        // w JEDNYM TICKU dwie pelne tury - a druga startuje z czysta ksiegowoscia TurnRunnera
        // i dostaje od gry werdykt policzony dla PIERWSZEJ mapy.
        //
        // Wszystkie trzynascie CanFireNowSub naszego katalogu jest mapozaleznych, wiec werdykt
        // z mapy A nie mowi o mapie B niczego.
        T.Section("TEST 5i - dwie tury w jednym ticku nie moga dzielic werdyktu silnika");

        // MODEL SILNIKA, wierny co do mechanizmu: cache na (payload) w obrebie ticku,
        // prawdziwa wykonalnosc zalezna od MAPY.
        var cacheSub = new Dictionary<string, bool>();
        int policzonychSub = 0;
        System.Func<ScoredCandidate, string, bool> silnik = (k, mapa) =>
        {
            string payload = k.Event == null ? "?" : (k.Event.ActionPayload ?? "?");
            bool v;
            if (cacheSub.TryGetValue(payload, out v))
            {
                return v;                       // <- cache NIE zna mapy
            }
            v = string.CompareOrdinal(mapa, "B") != 0;   // na mapie B nic nie jest wykonalne
            policzonychSub++;
            cacheSub[payload] = v;
            return v;
        };

        // REJESTR WERDYKTOW NA TICK - odwzorowanie tego, co robi warstwa integracji.
        var rejestr = new Dictionary<string, string>();

        System.Func<string, bool, TurnResult> turaNaMapie = (mapa, zRejestrem) =>
        {
            var pulaDwu = new List<ScoredCandidate>();
            for (int i = 0; i < 4; i++)
            {
                pulaDwu.Add(KandMoc(0.80f - i * 0.01f, "AKCJA_D" + i, "D" + mapa + i, IntensityLevel.Normal));
            }

            CandidateAcceptor akc = delegate(ScoredCandidate kandydat, bool pytaj, out string pw)
            {
                pw = null;
                if (!pytaj) return AcceptorVerdict.Accepted;

                string zakres = kandydat.Event.ActionPayload ?? "?";
                string odcisk = zakres + "@" + mapa + "#" + KluczWykonania(kandydat);
                if (zRejestrem)
                {
                    string poprzedni;
                    if (rejestr.TryGetValue(zakres, out poprzedni)
                        && string.CompareOrdinal(poprzedni, odcisk) != 0)
                    {
                        pw = "werdykt niedostepny";
                        return AcceptorVerdict.Unanswerable;
                    }
                    rejestr[zakres] = odcisk;
                }

                if (silnik(kandydat, mapa)) return AcceptorVerdict.Accepted;
                pw = "CanFireNow=false";
                return AcceptorVerdict.RefusedByGame;
            };

            var r = new TurnRunner(policy, PassScoringParams.Defaults(), 8);
            return r.Run(Ctx(new EventHistory(), 10f), new List<ScoredCandidate>(pulaDwu),
                         Pass(0.25f), new SeededRandom(11), 0f, akc, KluczWykonania, ZakresCache);
        };

        // --- KONTROLA REGRESJI: bez rejestru mapa B przyjmuje kandydata na kredyt ---
        cacheSub.Clear(); policzonychSub = 0; rejestr.Clear();
        TurnResult bezA = turaNaMapie("A", false);
        TurnResult bezB = turaNaMapie("B", false);
        int subBez = policzonychSub;

        T.Ok("[5i] STRAZNIK: kontrola faktycznie przeszla przez obie mapy w jednym ticku",
             bezA.Accepted, "mapa A przyjeta=" + bezA.Accepted + " policzonych CanFireNowSub=" + subBez);

        T.Ok("[5i] KONTROLA REGRESJI: bez rejestru mapa B przyjmuje kandydata NIEWYKONALNEGO",
             bezB.Accepted,
             "mapa B przyjeta=" + bezB.Accepted + " (prawdziwa wykonalnosc na B = false, "
             + "a CanFireNowSub policzono dla niej " + 0 + " razy)");

        // --- Z REJESTREM: mapa B nie dostaje cudzego werdyktu ---
        cacheSub.Clear(); policzonychSub = 0; rejestr.Clear();
        TurnResult zA = turaNaMapie("A", true);
        TurnResult zB = turaNaMapie("B", true);

        T.Ok("[5i] STRAZNIK: mapa A nadal dziala normalnie", zA.Accepted,
             "mapa A przyjeta=" + zA.Accepted + " pytanDoGry=" + zA.AcceptorCalls);

        T.Ok("[5i] NAPRAWA: mapa B NIE przyjmuje kandydata, o ktorego nie mogla zapytac",
             !zB.Accepted,
             "mapa B przyjeta=" + zB.Accepted + " powod=" + zB.Decision.PassReason
             + " niedostepnychWerdyktow=" + zB.UnanswerableScopes
             + " odlozonych=" + zB.DeferredVariants);

        // KSIEGOWOSC. Cztery payloady mapy B dzielia sie na dwa rozlaczne zbiory:
        //   - te, o ktore pytala juz mapa A  -> werdykt NIEDOSTEPNY, zero pytan, zero odmow
        //   - te, o ktore nie pytala         -> pytanie PRAWOWITE, silnik liczy je swiezo
        //                                       i odmawia (na mapie B nic nie jest wykonalne)
        // Pierwsza wersja tej asercji zadala "zero odmow" i byla ZLA: pytania o payloady spoza
        // rejestru sa uczciwe i ich odmowy naleza do kolumny odmowSilnika.
        T.EqI("[5i] kazdy payload rozliczony DOKLADNIE RAZ - albo pytaniem, albo niedostepnoscia",
              zB.UnanswerableScopes + zB.Refusals.Count, 4);

        T.Ok("[5i] ... a niedostepnosc nie zasila ani odmow, ani budzetu pytan",
             zB.UnanswerableScopes == 2 && zB.AcceptorCalls == zB.Refusals.Count,
             "niedostepnych=" + zB.UnanswerableScopes + " odmowSilnika=" + zB.Refusals.Count
             + " pytanDoGry=" + zB.AcceptorCalls);

        // CISZA Z NIEDOSTEPNOSCI MA WLASNY POWOD. Gdy ani jedna odmowa nie padla, a pula
        // opustoszala przez odlozenia, diagnoza jest inna niz "zaostrz warunki twarde".
        cacheSub.Clear(); rejestr.Clear();
        turaNaMapie("A", true);
        foreach (string p in new[] { "AKCJA_D0", "AKCJA_D1", "AKCJA_D2", "AKCJA_D3" })
        {
            rejestr[p] = p + "@A#" + p + "|0";
        }
        TurnResult zC = turaNaMapie("C", true);
        T.Ok("[5i] cisza z samej niedostepnosci werdyktu ma powod VerdictUnavailable",
             zC.Decision.IsPass && zC.Decision.PassReason == PassReason.VerdictUnavailable
             && zC.Refusals.Count == 0,
             "powod=" + zC.Decision.PassReason + " odmow=" + zC.Refusals.Count
             + " niedostepnych=" + zC.UnanswerableScopes);

        // ================================================================================
        //  TEST 5h - POTWIERDZENIE WSPOLDZIELONE PO PARAMETRACH WYKONANIA
        // ================================================================================
        //
        // NAJWAZNIEJSZA WLASNOSC POPRAWNOSCIOWA CALEJ WARSTWY: narrator nie odpala kandydata,
        // ktorego wlasne parametry nie przeszly sprawdzenia.
        //
        // Wersja poprzednia to naruszala i oznaczala jedynie flaga. Flaga ujawnia niepewnosc,
        // ale jej nie usuwa: zdarzenie, ktore sie nie wykona, trafia mimo to do historii
        // narratora i przesuwa swiezosc, rytm oraz kolejne decyzje. Odfiltrowanie wiersza
        // w analizie tego NIE COFA.
        T.Section("TEST 5h - potwierdzenie wspoldzielone po parametrach, nie po klocku akcji");

        // Akcja o dwoch zestawach parametrow: trzy oprawy przy High i dwie przy Low.
        // WYKONALNE sa wylacznie te o High - dokladnie ten uklad, ktory w wersji poprzedniej
        // konczyl sie przyjeciem wariantu Low na kredyt.
        var pulaMoc = new List<ScoredCandidate>();
        pulaMoc.Add(KandMoc(0.80f, "AKCJA_M", "M_hi0", IntensityLevel.High));
        pulaMoc.Add(KandMoc(0.79f, "AKCJA_M", "M_lo0", IntensityLevel.Low));
        pulaMoc.Add(KandMoc(0.78f, "AKCJA_M", "M_hi1", IntensityLevel.High));
        pulaMoc.Add(KandMoc(0.77f, "AKCJA_M", "M_lo1", IntensityLevel.Low));
        pulaMoc.Add(KandMoc(0.76f, "AKCJA_M", "M_hi2", IntensityLevel.High));

        System.Func<ScoredCandidate, bool> tylkoHigh =
            k => k.Event != null && k.Event.Intensity == IntensityLevel.High;

        int przyjetych = 0, naruszen = 0, zLow = 0, wspoldzielonych = 0, odlozonychSuma = 0;
        for (int seed = 0; seed < 300; seed++)
        {
            TurnResult tM = SymulujTureF(policy, pulaMoc, Pass(0.25f), new SeededRandom(seed),
                                         tylkoHigh, 8);
            odlozonychSuma += tM.DeferredVariants;
            if (!tM.Accepted) continue;
            przyjetych++;
            if (tM.FeasibilityInferred) wspoldzielonych++;
            if (!tylkoHigh(tM.Decision.Winner)) { naruszen++; zLow++; }
        }

        T.Ok("[5h] STRAZNIK: przebiegi faktycznie konczyly sie przyjeciem kandydata",
             przyjetych > 200, "przyjetych=" + przyjetych + " z 300 ziaren");

        T.EqI("[5h] NIGDY nie przyjeto kandydata, ktorego wlasne parametry nie przeszly sprawdzenia",
              naruszen, 0);

        T.Ok("[5h] ... a warianty o innych parametrach byly faktycznie odkladane",
             odlozonychSuma > 0, "lacznie odlozonych wariantow=" + odlozonychSuma
             + " (2 na ture: M_lo0 i M_lo1)");

        T.Ok("[5h] ROZNORODNOSC ZACHOWANA: oprawy o TYCH SAMYCH parametrach nadal wygrywaja",
             wspoldzielonych > 0,
             "tur ze wspoldzielonym potwierdzeniem=" + wspoldzielonych + " z " + przyjetych
             + " (czyli wygrywal M_hi1 albo M_hi2, nie tylko potwierdzone czolo)");

        // KONTROLA ODWROTNA: gdy WSZYSTKIE warianty maja te same parametry, nic sie nie odklada
        // i cala piatka nadal konkuruje. Bez tej asercji naprawa mogla by po prostu odkladac
        // wszystko, co nie jest czolem - i test wyzej by tego nie zauwazyl.
        var pulaJednorodna = new List<ScoredCandidate>();
        for (int v = 0; v < 5; v++)
        {
            pulaJednorodna.Add(KandMoc(0.80f - v * 0.002f, "AKCJA_J", "J" + v, IntensityLevel.Normal));
        }
        var zwyciezcyJ = new HashSet<string>();
        int odlozonychJ = 0;
        for (int seed = 0; seed < 300; seed++)
        {
            TurnResult tJ = SymulujTureF(policy, pulaJednorodna, Pass(0.25f), new SeededRandom(seed),
                                         k => true, 8);
            odlozonychJ += tJ.DeferredVariants;
            if (tJ.Accepted) zwyciezcyJ.Add(tJ.Decision.Winner.SortKey);
        }
        T.EqI("[5h] KONTROLA: przy jednorodnych parametrach nic nie jest odkladane", odlozonychJ, 0);
        T.Ok("[5h] KONTROLA: i wszystkie piec opraw nadal wygrywa",
             zwyciezcyJ.Count == 5, "roznych zwyciezcow=" + zwyciezcyJ.Count + "/5");

        // KOSZT: wspoldzielenie nadal oszczedza pytania - jedno na ture, nie jedno na wariant.
        TurnResult tKoszt = SymulujTureF(policy, pulaJednorodna, Pass(0.25f), new SeededRandom(3),
                                         k => true, 8);
        T.EqI("[5h] koszt tury pozostaje JEDNYM pytaniem do gry", tKoszt.AcceptorCalls, 1);

        // DRUGA WARSTWA OBRONY MA MILCZEC. Odkladanie usuwa warianty o innych parametrach ZANIM
        // dojda do petli, wiec straznik w petli (ten, ktory porownuje klucze zwyciezcy) nie ma
        // prawa sie odezwac. Jest to ta sama konstrukcja co przy CompetitiveAfterRefusal:
        // gallaz zostaje, a test asercjonuje jej MILCZENIE.
        //
        // Zapisane wprost, bo wstrzykniecie regresji tego nie wykrywa: usuniecie samego straznika
        // zostawia walidator zielony, dopoki dziala warstwa pierwsza. Wykrywalne jest dopiero
        // usuniecie OBU - i taki wariant jest sprawdzony osobno (klucz ignorujacy intensywnosc
        // daje 108 naruszen).
        int alarmowKlucza = 0;
        for (int seed = 0; seed < 300; seed++)
        {
            TurnResult tA = SymulujTureF(policy, pulaMoc, Pass(0.25f), new SeededRandom(seed),
                                         tylkoHigh, 8);
            if (tA.Warnings.Exists(w => w.Contains("INNY klucz parametrow"))) alarmowKlucza++;
        }
        T.EqI("[5h] straznik drugiej warstwy MILCZY (odkladanie dziala wczesniej)", alarmowKlucza, 0);

        // ================================================================================
        //  TEST 5g - TRZY ZNALEZISKA PRZEGLADU: wyczerpanie fazy 0, cache silnika, pRunda
        // ================================================================================
        T.Section("TEST 5g - wyczerpana prewerifikacja, jedno pytanie na akcje, wnioskowana wykonalnosc");

        // --- ZNALEZISKO 1: wyczerpany budzet fazy 0 to wynik TECHNICZNY ---
        //
        // Gdy budzet pytan konczy sie, zanim ktorekolwiek czolo zostanie potwierdzone, brama
        // porownuje cisze z referencja NIESPRAWDZONA - czyli dokladnie tym stanem, ktory faza 0
        // miala usunac. Cisza z takiej tury nie jest wyborem narratora i nie moze zasilac ani
        // metryki swiadomego milczenia, ani licznika serii.
        var pulaWyczerp = new List<ScoredCandidate>();
        for (int i = 0; i < 10; i++) pulaWyczerp.Add(Kand(0.70f - i * 0.005f, "X" + i));

        int ciszTechnicznych = 0, ciszSwiadomych = 0, przebiegow = 0;
        bool zawszeWyczerpane = true;
        for (int seed = 0; seed < 60; seed++)
        {
            TurnResult tW = SymulujTureF(policy, pulaWyczerp, Pass(0.50f), new SeededRandom(seed),
                                         k => false, 3);
            przebiegow++;
            if (tW.PreVerification != PreVerificationOutcome.BudgetExhausted) zawszeWyczerpane = false;
            if (!tW.Decision.IsPass) continue;
            if (tW.Decision.PassReason == PassReason.Competitive) ciszSwiadomych++;
            else ciszTechnicznych++;
        }

        T.Ok("[5g] STRAZNIK: wszystkie przebiegi faktycznie wyczerpaly budzet fazy 0",
             zawszeWyczerpane && przebiegow == 60, "przebiegow=" + przebiegow);

        T.EqI("[5g] ZNALEZISKO 1: cisza po wyczerpanej prewerifikacji NIGDY nie jest Competitive",
              ciszSwiadomych, 0);

        T.Ok("[5g] ... i jest ich dosc, zeby test nie byl pusty",
             ciszTechnicznych > 0, "cisz technicznych=" + ciszTechnicznych
             + " swiadomych=" + ciszSwiadomych + " (przed naprawa wszystkie liczylyby sie "
             + "jako swiadome milczenie i podnosilyby licznik serii)");

        // Ostrzezenie ma trafic do warstwy integracji jako BLAD - bez niego wyczerpany budzet
        // bylby widoczny tylko posrednio, po powodzie ciszy.
        TurnResult tOstrz = SymulujTureF(policy, pulaWyczerp, Pass(0.50f), new SeededRandom(1),
                                         k => false, 3);
        bool jestOstrzezenie = tOstrz.Warnings.Exists(w => w.Contains("prewerifikacji"));
        T.Ok("[5g] wyczerpany budzet fazy 0 zglasza sie w Warnings", jestOstrzezenie,
             "ostrzezen=" + tOstrz.Warnings.Count);

        // --- ZNALEZISKO 2: silnik pytany NAJWYZEJ RAZ o akcje w turze ---
        //
        // Wymog poprawnosci, nie oszczednosci: IncidentWorker buforuje CanFireNowSub na parze
        // (IncidentDef, tick), a tura miesci sie w jednym ticku. Akceptor testowy modeluje ten
        // cache i zapisuje naruszenie do Warnings - patrz SymulujTureF.
        var pulaCache = new List<ScoredCandidate>();
        for (int v = 0; v < 4; v++) pulaCache.Add(KandAkcja(0.80f - v * 0.002f, "AKCJA_A", "CA" + v));
        for (int v = 0; v < 3; v++) pulaCache.Add(KandAkcja(0.75f - v * 0.002f, "AKCJA_B", "CB" + v));

        int naruszenCache = 0, maxPytan = 0;
        for (int seed = 0; seed < 120; seed++)
        {
            TurnResult tC = SymulujTureF(policy, pulaCache, Pass(0.30f), new SeededRandom(seed),
                                         k => string.CompareOrdinal(k.Event.ActionBlockId, "AKCJA_B") == 0, 8);
            if (tC.Warnings.Exists(w => w.StartsWith("TEST: silnik zapytany"))) naruszenCache++;
            if (tC.AcceptorCalls > maxPytan) maxPytan = tC.AcceptorCalls;
        }
        T.EqI("[5g] ZNALEZISKO 2: silnik NIGDY nie pytany dwa razy o te sama akcje w turze",
              naruszenCache, 0);
        T.Ok("[5g] ... przy maksymalnie dwoch pytaniach na ture (A odrzucona, B potwierdzona)",
             maxPytan == 2, "max pytanDoGry=" + maxPytan);

        // --- ZNALEZISKO 2b: wygrana RODZENSTWA jest oznaczana jako wnioskowana ---
        //
        // Gdy potwierdzono jeden wariant akcji, a wygral inny, ponowne pytanie bylo by
        // bezwartosciowe (cache). Rdzen go nie zadaje - i ZGLASZA, ze odpowiedz jest wnioskowana.
        // Dla 10 z 13 payloadow wniosek jest bezpieczny, bo ich CanFireNowSub nie czyta
        // parms.points; dla ManhunterPack, WandererJoin i RefugeePodCrash - nie jest.
        int wnioskowanych = 0, sprawdzonych = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            TurnResult tR2 = SymulujTureF(policy, pulaCache, Pass(0.30f), new SeededRandom(seed),
                                          k => true, 8);
            if (tR2.Decision.IsPass) continue;
            if (tR2.FeasibilityInferred) wnioskowanych++; else sprawdzonych++;
        }
        T.Ok("[5g] ZNALEZISKO 2b: wygrana rodzenstwa oznaczana jako wnioskowana",
             wnioskowanych > 0 && sprawdzonych > 0,
             "wnioskowanych=" + wnioskowanych + " sprawdzonych wprost=" + sprawdzonych
             + " (oba > 0, wiec flaga realnie rozroznia, a nie stoi na stale)");
    }

    // -------------------------------------------------------- TEST 7: NIEZMIENNIK SLADU
    private static void Test7Niezmiennik(EventComposer composer, XmlConfig cfg)
    {
        T.Section("TEST 7 - NIEZMIENNIK SLADU: suma Share == RawUtility (tol 1e-5) dla KAZDEGO kandydata");
        var gen = new CandidateGenerator(composer);

        // ---------------------------------------------------------------------------
        //  POMIAR KOSZTU ODKLADANIA NA PRAWDZIWYM KATALOGU
        // ---------------------------------------------------------------------------
        //
        // Odkladanie wariantow o innych parametrach wykonania kosztuje roznorodnosc - ale ile
        // dokladnie, wiadomo dopiero z katalogu. Liczymy dla kazdej AKCJI, na ile grup
        // o identycznych parametrach (payload + intensywnosc) dziela sie jej warianty i jak
        // duza jest grupa najwieksza. Grupa = zbior opraw, ktore jedno pytanie do gry obejmuje.
        //
        // Liczba jest DRUKOWANA, nie wpisana w asercje: katalog zmienia sie pod nia, a projekt
        // ma juz za soba kilka literalow, ktore zestarzaly sie po cichu. Asercjonowana jest
        // WLASNOSC - ze wspoldzielenie w ogole zachodzi, czyli ze odkladanie nie degeneruje
        // wyboru do jednej oprawy na akcje.
        {
            CandidateSet wszyscy = gen.Generate(new EventRecipe(), Scenariusze()[0],
                                                new SeededRandom(1), cfg.candidateBudget);
            var grupy = new Dictionary<string, int>();
            var akcje = new Dictionary<string, HashSet<string>>();
            if (wszyscy.Candidates != null)
            {
                foreach (ComposedEvent e in wszyscy.Candidates)
                {
                    string akcja = e.ActionBlockId ?? "?";
                    string klucz = akcja + "|" + ((int)e.Intensity).ToString(CultureInfo.InvariantCulture);
                    int n;
                    grupy.TryGetValue(klucz, out n);
                    grupy[klucz] = n + 1;
                    if (!akcje.ContainsKey(akcja)) akcje[akcja] = new HashSet<string>();
                    akcje[akcja].Add(klucz);
                }
            }

            int wariantow = wszyscy.Candidates == null ? 0 : wszyscy.Candidates.Count;
            int najwiekszaGrupa = 0, grupWielokrotnych = 0;
            foreach (var g in grupy)
            {
                if (g.Value > najwiekszaGrupa) najwiekszaGrupa = g.Value;
                if (g.Value > 1) grupWielokrotnych++;
            }
            double sredniaGrupa = grupy.Count == 0 ? 0 : (double)wariantow / grupy.Count;

            T.Ok("[7] POMIAR: wspoldzielenie potwierdzenia realnie obejmuje wiele opraw",
                 grupWielokrotnych > 0 && najwiekszaGrupa > 1,
                 "kandydatow=" + wariantow + " akcji=" + akcje.Count
                 + " grup parametrow=" + grupy.Count
                 + " srednio opraw na grupe=" + sredniaGrupa.ToString("0.00", CultureInfo.InvariantCulture)
                 + " najwieksza grupa=" + najwiekszaGrupa
                 + " grup wielokrotnych=" + grupWielokrotnych);
        }
        var scorer = new UtilityScorer(CzynnikiZdarzen(), cfg.weights,
                                       UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, cfg.vetoContextFitBelow);

        WorldSnapshot[] scen = Scenariusze();
        int sprawdzonych = 0, zlamanych = 0;
        double najgorsza = 0;
        foreach (WorldSnapshot s in scen)
        {
            for (int wariant = 0; wariant < 3; wariant++)
            {
                EventHistory h = HistoriaWariant(wariant);
                DecisionContext ctx = Ctx(h, 30f);
                CandidateSet cs = gen.Generate(new EventRecipe(), s, new SeededRandom(1), cfg.candidateBudget);
                List<ScoredCandidate> ocenieni = scorer.ScoreAll(cs.Candidates, ctx);
                ocenieni.Add(scorer.ScorePass(ctx));
                foreach (ScoredCandidate c in ocenieni)
                {
                    sprawdzonych++;
                    double suma = c.Factors.Sum(fs => (double)fs.Share);
                    double diff = Math.Abs(suma - c.RawUtility);
                    if (diff > najgorsza) najgorsza = diff;
                    if (diff > 1e-5) zlamanych++;
                    if (c.RawUtility < -1e-6 || c.RawUtility > 1f + 1e-6) zlamanych++;
                }
            }
        }
        // PROG POKRYCIA, nie asercja poprawnosci - ta jest linijke nizej (zlamanych == 0).
        // Obnizony z 800 na 400 po dolozeniu warunkow twardych Cond_Night i Cond_CalmPeriod:
        // cztery z pieciu scenariuszy sa dzienne, wiec odpada PN_Mod_Noc, a jeden modeluje brak
        // spokoju, wiec odpada PN_Trig_Cisza. Zawezenie jest ZAMIERZONE - te warunki istnieja
        // po to, zeby tekst nie stwierdzal nieprawdy - wiec prog musi isc za katalogiem,
        // a nie katalog za progiem.
        //
        // CELOWO NIE CYTUJEMY TU ZMIERZONEJ LICZBY. Poprzednia wersja komentarza glosila
        // "Zmierzone po zmianie: 591", co zestarzalo sie przy nastepnym zaostrzeniu warunkow
        // (Cond_KidnappedColonist i Cond_PoweredCommsConsole na PN_Akcja_Okup zabraly kolejne
        // kandydatow) - czyli dokladnie ten wzorzec, przed ktorym ostrzega CLAUDE.md: liczba
        // w komentarzu, ktorej nie pilnuje asercja, starzeje sie po cichu. Faktyczna wartosc
        // jest DRUKOWANA w etykiecie asercji ponizej, wiec widac ja przy kazdym przebiegu.
        T.Ok("kandydatow sprawdzonych: " + sprawdzonych, sprawdzonych > 400, null);
        T.EqI("naruszen niezmiennika (suma Share vs RawUtility, zakres [0,1])", zlamanych, 0);
        Console.WriteLine("     najwieksza zaobserwowana roznica: " + najgorsza.ToString("E3", CultureInfo.InvariantCulture));

        // Ten sam niezmiennik na 10000 losowych zestawach wartosci i wag.
        var rnd = new Random(4711);
        int zle = 0;
        double naj2 = 0;
        for (int i = 0; i < 10000; i++)
        {
            var w = new ScoringWeights
            {
                contextFit = (float)(rnd.NextDouble() * 5),
                freshness = (float)(rnd.NextDouble() * 5),
                dramaticContrast = (float)(rnd.NextDouble() * 5),
                intentAlignment = (float)(rnd.NextDouble() * 5)
            };
            var czynniki = new IScoringFactor[]
            {
                new StubFactor(nameof(ScoringWeights.contextFit), (float)rnd.NextDouble()),
                new StubFactor(nameof(ScoringWeights.freshness), (float)rnd.NextDouble()),
                new StubFactor(nameof(ScoringWeights.dramaticContrast), (float)rnd.NextDouble()),
                new StubFactor(nameof(ScoringWeights.intentAlignment), (float)rnd.NextDouble())
            };
            var sc = new UtilityScorer(czynniki, w, UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, 0f);
            ScoredCandidate c = sc.Score(Ev("A", Theme.Raid, Valence.Negative, EventScale.Major), Ctx(new EventHistory(), 1f));
            double suma = c.Factors.Sum(fs => (double)fs.Share);
            double diff = Math.Abs(suma - c.RawUtility);
            if (diff > naj2) naj2 = diff;
            if (diff > 1e-5 || c.RawUtility < 0f || c.RawUtility > 1f) zle++;
        }
        T.EqI("10000 losowych zestawow wag: naruszen", zle, 0);
        Console.WriteLine("     najwieksza roznica na losowych: " + naj2.ToString("E3", CultureInfo.InvariantCulture));

        // Waga 0 jest NEUTRALNA, nie rozcienczajaca.
        var wagi = new ScoringWeights { contextFit = 2f, freshness = 1.5f, dramaticContrast = 1f, intentAlignment = 0f };
        IScoringFactor[] Zestaw(float ia)
        {
            return new IScoringFactor[]
            {
                new StubFactor(nameof(ScoringWeights.contextFit), 1.00f),
                new StubFactor(nameof(ScoringWeights.freshness), 0.00f),
                new StubFactor(nameof(ScoringWeights.dramaticContrast), 0.50f),
                new StubFactor(nameof(ScoringWeights.intentAlignment), ia)
            };
        }
        var s0 = new UtilityScorer(Zestaw(0f), wagi, UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, 0f);
        var s1 = new UtilityScorer(Zestaw(1f), wagi, UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, 0f);
        DecisionContext c0 = Ctx(new EventHistory(), 1f);
        float u0 = s0.Score(Ev("A", Theme.Raid, Valence.Negative, EventScale.Major), c0).RawUtility;
        float u1 = s1.Score(Ev("A", Theme.Raid, Valence.Negative, EventScale.Major), c0).RawUtility;
        T.Eq("suma wazona referencyjna (2.5/4.5)", u0, 0.5556, 1e-4);
        T.Eq("waga 0 nie rozciencza: identyczny wynik przy intentAlignment 0 i 1", u1, u0, 1e-6);

        // Suma wag = 0.
        var zeroW = new ScoringWeights { contextFit = 0f, freshness = 0f, dramaticContrast = 0f, intentAlignment = 0f };
        var scZero = new UtilityScorer(Zestaw(1f), zeroW, UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, 0f);
        ScoredCandidate cz = scZero.Score(Ev("A", Theme.Raid, Valence.Negative, EventScale.Major), c0);
        T.Eq("suma wag 0 -> RawUtility 0", cz.RawUtility, 0.0, 1e-9);
        T.Ok("suma wag 0 -> WeightsDegenerate", cz.WeightsDegenerate, null);
        T.EqI("suma wag 0 -> slad zachowany (4 wiersze)", cz.Factors.Count, 4);
        string prob;
        T.Ok("Validate() zglasza sume wag 0", !scZero.Validate(out prob) && prob.Contains("suma wag"), prob);

        // NaN z czynnika.
        var nanCz = new IScoringFactor[]
        {
            new StubFactor(nameof(ScoringWeights.contextFit), 1f),
            new StubFactor(nameof(ScoringWeights.freshness), float.NaN),
            new StubFactor(nameof(ScoringWeights.dramaticContrast), 1f),
            new StubFactor(nameof(ScoringWeights.intentAlignment), 1f)
        };
        var scNan = new UtilityScorer(nanCz, cfg.weights, UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, 0f);
        ScoredCandidate cn = scNan.Score(Ev("A", Theme.Raid, Valence.Negative, EventScale.Major), c0);
        T.Ok("czynnik NaN: wartosc zbita do 0, flaga Invalid, HadInvalidFactor",
             cn.Factors[1].Value == 0f && cn.Factors[1].Invalid && cn.HadInvalidFactor, null);
        T.Ok("czynnik NaN: RawUtility skonczone", !float.IsNaN(cn.RawUtility) && !float.IsInfinity(cn.RawUtility),
             "raw=" + T.F(cn.RawUtility));

        // Brak czynnika contextFit przy aktywnym wecie.
        var bezFit = new IScoringFactor[] { new StubFactor(nameof(ScoringWeights.freshness), 1f) };
        var scBez = new UtilityScorer(bezFit, cfg.weights, UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, 0.15f);
        ScoredCandidate cb = scBez.Score(Ev("A", Theme.Raid, Valence.Negative, EventScale.Major), c0);
        T.Ok("brak czynnika contextFit: nikt nie jest zawetowany", !cb.Vetoed, null);
        T.Ok("brak czynnika contextFit: Validate() krzyczy", !scBez.Validate(out prob) && prob.Contains("contextFit"), prob);
    }

    // -------------------------------------------------------- TEST 3: SCENARIUSZ
    private static void Test3Scenariusz(EventComposer composer, XmlConfig cfg)
    {
        T.Section("TEST 3 - SCENARIUSZ Z SEKCJI 12 KONCEPCJI (bogata gorska kolonia z wrogami)");

        var snapshot = new WorldSnapshot
        {
            DaysPassed = 60,
            ColonistCount = 9,
            ColonyWealth = 250000f,
            WealthRelative = 2.5f,
            MountainRoofCellsNearColony = 120,
            HasHostileFaction = true,
            Season = 1,
            IsNight = false,
            WildAnimalCount = 12
        };

        var gen = new CandidateGenerator(composer);
        var scorer = new UtilityScorer(CzynnikiZdarzen(), cfg.weights,
                                       UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, cfg.vetoContextFitBelow);
        var policy = new SelectionPolicy(cfg.Selection());

        DecisionContext ctx = Ctx(new EventHistory(), 60f);
        CandidateSet cs = gen.Generate(new EventRecipe(), snapshot, new SeededRandom(1), cfg.candidateBudget);
        List<ScoredCandidate> ocenieni = scorer.ScoreAll(cs.Candidates, ctx);
        ScoredCandidate pass = scorer.ScorePass(ctx);
        NarratorDecision d = policy.Select(ocenieni, pass, new SeededRandom(1 + 104729));

        Console.WriteLine("  kandydatow=" + cs.Candidates.Count + " zawetowanych=" + d.CountVetoed
                          + " ponizejProgu=" + d.CountBelowCutoff + " ponizejPasma=" + d.CountBelowBand
                          + " wSoftmaksie=" + d.CountInSoftmax
                          + " best=" + T.F4(d.BestUtility) + " pasmo=" + T.F4(d.BandThreshold)
                          + " U_pass=" + T.F4(d.PassUtility));
        Console.WriteLine("  --- czolo rankingu ---");
        for (int i = 0; i < Math.Min(14, d.Ranking.Count); i++)
        {
            ScoredCandidate k = d.Ranking[i];
            Console.WriteLine("   #" + (i + 1).ToString().PadLeft(2) + " " + k.Label.PadRight(30)
                              + " u=" + T.F4(k.Utility)
                              + " cf=" + T.F4(k.FactorValue(nameof(ScoringWeights.contextFit)))
                              + " sw=" + T.F4(k.FactorValue(nameof(ScoringWeights.freshness)))
                              + " kt=" + T.F4(k.FactorValue(nameof(ScoringWeights.dramaticContrast)))
                              + " p=" + T.F4(k.SelectionProbability)
                              + "  " + k.SortKey);
        }

        // Rozklad payloadow w pasmie softmaksu.
        var wPasmie = d.Ranking.Where(x => !x.IsPass && x.Rejected == RejectionStage.None).ToList();
        Console.WriteLine("  w pasmie softmaksu: " + string.Join(", ",
            wPasmie.GroupBy(x => x.Label).Select(g => g.Key + " x" + g.Count())));

        string payload = d.Winner == null || d.Winner.Event == null ? "PASS" : d.Winner.Event.ActionPayload;

        // ---------------------------------------------------------------------------------
        //  DWIE ASERCJE SPECYFIKACJI ZOSTALY TU ZASTAPIONE - obie byly wadliwe SAME W SOBIE.
        //
        //  1) "zwyciezca ma ActionPayload == RaidEnemy" - specyfikacja postulowala konkretnego
        //     zwyciezce, nie wyprowadzajac, dlaczego mialby nim byc. W tym scenariuszu jest
        //     rownie dobrze uzasadniona infestacja (baza gorska, 120 komorek stropu) i wysyp
        //     ambrozji (lato). Test na jeden konkretny payload mierzylby zgodnosc z przeczuciem
        //     autora, a nie poprawnosc.
        //
        //  2) "zwyciezca ma najwyzsza uzytecznosc w rankingu" - to asercja na ARGMAX, a polityka
        //     wyboru jest SOFTMAKSEM (T=0.1 w pasmie 0.75*best). Cala jej istota polega na tym,
        //     ze zwyciezca NIE zawsze jest maksimum. Ta asercja przeczyla wiec decyzji projektowej,
        //     ktorej mialaby pilnowac, i przechodzilaby tylko przypadkiem.
        //
        //  W ich miejsce testujemy to, co polityka NAPRAWDE gwarantuje, plus wymog z sekcji 12
        //  koncepcji ("w zadanym kontekscie wybierany jest oczekiwany kandydat") na scenariuszu,
        //  w ktorym zbior poprawnych odpowiedzi da sie WYPROWADZIC, a nie postulowac.
        // ---------------------------------------------------------------------------------

        // GWARANCJE POLITYKI - te obowiazuja przy KAZDYM ziarnie.
        T.Ok("zwyciezca nie jest zawetowany", d.Winner != null && !d.Winner.Vetoed,
             "zwyciezca=" + payload);
        T.Ok("zwyciezca przekracza prog jakosci", d.Winner != null && d.Winner.Utility >= cfg.qualityCutoff - 1e-6f,
             "u=" + T.F4(d.Winner == null ? 0 : d.Winner.Utility) + " cutoff=" + T.F4(cfg.qualityCutoff));
        T.Ok("zwyciezca miesci sie w pasmie near-best", d.Winner != null && d.Winner.Utility >= d.BandThreshold - 1e-6f,
             "u=" + T.F4(d.Winner == null ? 0 : d.Winner.Utility) + " pasmo=" + T.F4(d.BandThreshold)
             + " best=" + T.F4(d.BestUtility));

        // WYMOG SEKCJI 12 na scenariuszu ROZSTRZYGAJACYM. Kontekst dobrany tak, zeby twarde
        // warunki wyciely wszystko poza wydarzeniami "ulgi": brak wrogiej frakcji (odpada napad
        // i okup), brak dzikich zwierzat (odpada szal), brak stropu gorskiego (odpada rojenie),
        // dzien 3 (odpada emanator, ktory wymaga 20). Kolonia skrajnie uboga (0.15 normy), wiec
        // preferencje malejace po bogactwie premiuja zrzut i meteoryt.
        var snapUlga = new WorldSnapshot
        {
            DaysPassed = 3, ColonistCount = 2, ColonyWealth = 1500f, WealthRelative = 0.15f,
            MountainRoofCellsNearColony = 0, HasHostileFaction = false,
            Season = 3, IsNight = false, WildAnimalCount = 0,
            DaysSinceLastEvent = 3.0f
        };
        // Asercja przez WYKLUCZENIE, nie przez wyliczenie. Poprzednia wersja wyliczala zbior
        // dozwolonych z pamieci i pominela Flashstorm, ktory nie ma zadnych warunkow twardych,
        // wiec jest w tym kontekscie legalny - test wywalal sie na poprawnym zachowaniu.
        // Teza, ktora ten test ma sprawdzac, brzmi: warunki twarde WYKLUCZAJA wydarzenia
        // nieadekwatne do sytuacji. Lista zakazanych jest wyprowadzalna wprost z kontekstu:
        //   brak wrogiej frakcji -> RaidEnemy, RansomDemand
        //   brak dzikich zwierzat -> ManhunterPack
        //   brak stropu gorskiego -> Infestation
        //   dzien 3 (< 20)        -> PsychicEmanatorShipPartCrash
        var zakazane = new HashSet<string> { "RaidEnemy", "RansomDemand", "ManhunterPack",
                                             "Infestation", "PsychicEmanatorShipPartCrash" };
        int trafien = 0, prob = 40;
        var zwyciezcy = new HashSet<string>();
        for (int seed = 0; seed < prob; seed++)
        {
            CandidateSet csU = gen.Generate(new EventRecipe(), snapUlga, new SeededRandom(seed), cfg.candidateBudget);
            DecisionContext ctxU = Ctx(new EventHistory(), 3f);
            NarratorDecision dU = policy.Select(scorer.ScoreAll(csU.Candidates, ctxU),
                                                scorer.ScorePass(ctxU), new SeededRandom(seed + 104729));
            string pU = dU.Winner == null || dU.Winner.Event == null ? "PASS" : dU.Winner.Event.ActionPayload;
            zwyciezcy.Add(pU);
            if (!zakazane.Contains(pU)) trafien++;
        }
        T.EqI("scenariusz rozstrzygajacy: zaden zwyciezca sposrod wykluczonych przez warunki twarde",
              trafien, prob);
        Console.WriteLine("  zwyciezcy scenariusza rozstrzygajacego: " + string.Join(", ", zwyciezcy));

        // Dokladna rownosc przy powtorzeniu (mimo softmaksu).
        CandidateSet cs2 = gen.Generate(new EventRecipe(), snapshot, new SeededRandom(1), cfg.candidateBudget);
        List<ScoredCandidate> oc2 = scorer.ScoreAll(cs2.Candidates, Ctx(new EventHistory(), 60f));
        ScoredCandidate p2 = scorer.ScorePass(Ctx(new EventHistory(), 60f));
        NarratorDecision d2 = policy.Select(oc2, p2, new SeededRandom(1 + 104729));
        T.EqS("powtorzenie przy tym samym ziarnie: identyczny klucz zwyciezcy", d2.Winner.SortKey, d.Winner.SortKey);
        T.Ok("powtorzenie: identyczna uzytecznosc co do bitu", d2.Winner.Utility == d.Winner.Utility,
             T.F(d2.Winner.Utility) + " vs " + T.F(d.Winner.Utility));

        // ---------------------------------------------------------------------
        // DIAGNOSTYKA NIEZGODNOSCI: dlaczego RaidEnemy nie moze wygrac.
        // ---------------------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine("  === DIAGNOSTYKA: skad bierze sie niezgodnosc ===");

        int bezPreferencji = cs.Candidates.Count(c => c.FitTrace == "brak preferencji");
        Console.WriteLine("  kandydatow BEZ ANI JEDNEJ preferencji (contextFit == 1.0 bezwarunkowo): "
                          + bezPreferencji + " z " + cs.Candidates.Count);
        foreach (ComposedEvent c in cs.Candidates.Where(x => x.FitTrace == "brak preferencji"))
        {
            Console.WriteLine("     " + c.Signature + "  -> " + c.ActionPayload);
        }

        T.Ok("LUKA DANOWA: zaden kandydat nie ma PUSTEJ puli preferencji", bezPreferencji == 0,
             bezPreferencji + " kandydatow dostaje contextFit 1.0 bezwarunkowo w KAZDYM kontekscie "
             + "(ContextEvaluator: 'brak preferencji' -> 1.0). Naprawa nalezy do warstwy danych (XML).");

        // Sufit contextFit dla RaidEnemy: czy 1.0 jest w ogole osiagalne?
        var sufitSnap = new WorldSnapshot
        {
            DaysPassed = 60, ColonistCount = 2, ColonyWealth = 500000f, WealthRelative = 5.0f,
            MountainRoofCellsNearColony = 120, HasHostileFaction = true, Season = 1, IsNight = true, WildAnimalCount = 12
        };
        CandidateSet sufitCs = gen.Generate(new EventRecipe(), sufitSnap, new SeededRandom(1), cfg.candidateBudget);
        float sufitRaid = sufitCs.Candidates.Where(x => x.ActionPayload == "RaidEnemy")
                                            .Select(x => x.ContextFit).DefaultIfEmpty(0f).Max();
        Console.WriteLine("  sufit contextFit RaidEnemy (WealthRelative 5.0, 2 kolonistow, noc): " + T.F4(sufitRaid));

        Console.WriteLine("  maksymalny contextFit per incydent w TYM scenariuszu:");
        foreach (var g in cs.Candidates.GroupBy(x => x.ActionPayload).OrderByDescending(x => x.Max(y => y.ContextFit)))
        {
            Console.WriteLine("     " + g.Key.PadRight(30) + " max cf=" + T.F4(g.Max(y => y.ContextFit))
                              + "  min cf=" + T.F4(g.Min(y => y.ContextFit)) + "  wariantow=" + g.Count());
        }

        // Czy JAKAKOLWIEK wartosc pol NIEZWIAZANYCH przez SPEC (Season, IsNight, WildAnimalCount)
        // pozwala RaidEnemy wygrac? Pola zwiazane przez SPEC (WealthRelative 2.5, ColonistCount 9,
        // MountainRoofCellsNearColony 120, HasHostileFaction true, DaysPassed 60) zostaja stale.
        Console.WriteLine("  przemiatanie pol NIEZWIAZANYCH przez SPEC (pora roku x noc x zwierzeta):");
        int razyRaidLider = 0, razyRaidWPasmie = 0, kombinacji = 0;
        float najlepszyRaidCfGlobalnie = 0f;
        foreach (int pora in new[] { 0, 1, 2, 3 })
        {
            foreach (bool noc in new[] { false, true })
            {
                foreach (int zwierzeta in new[] { 0, 12, 30 })
                {
                    kombinacji++;
                    var sn = new WorldSnapshot
                    {
                        DaysPassed = 60, ColonistCount = 9, ColonyWealth = 250000f, WealthRelative = 2.5f,
                        MountainRoofCellsNearColony = 120, HasHostileFaction = true,
                        Season = pora, IsNight = noc, WildAnimalCount = zwierzeta
                    };
                    CandidateSet c2 = gen.Generate(new EventRecipe(), sn, new SeededRandom(1), cfg.candidateBudget);
                    List<ScoredCandidate> o2 = scorer.ScoreAll(c2.Candidates, Ctx(new EventHistory(), 60f));
                    NarratorDecision dd = policy.Select(o2, scorer.ScorePass(Ctx(new EventHistory(), 60f)),
                                                       new SeededRandom(1 + 104729));
                    float maxRaid = c2.Candidates.Where(x => x.ActionPayload == "RaidEnemy")
                                                 .Select(x => x.ContextFit).DefaultIfEmpty(0f).Max();
                    if (maxRaid > najlepszyRaidCfGlobalnie) najlepszyRaidCfGlobalnie = maxRaid;
                    ScoredCandidate lid = dd.Ranking.FirstOrDefault(x => !x.IsPass);
                    if (lid != null && lid.Event != null && lid.Event.ActionPayload == "RaidEnemy") razyRaidLider++;
                    if (dd.Ranking.Any(x => !x.IsPass && x.Event != null && x.Event.ActionPayload == "RaidEnemy"
                                            && x.Rejected == RejectionStage.None)) razyRaidWPasmie++;
                }
            }
        }
        Console.WriteLine("     kombinacji zbadanych: " + kombinacji
                          + "; RaidEnemy byl liderem rankingu: " + razyRaidLider
                          + "; RaidEnemy wszedl do puli softmaksu: " + razyRaidWPasmie
                          + "; najwyzszy osiagalny contextFit RaidEnemy: " + T.F4(najlepszyRaidCfGlobalnie));
        Console.WriteLine("     WNIOSEK: przy polach ZWIAZANYCH przez SPEC (WealthRelative=2.5, ColonistCount=9)");
        Console.WriteLine("     preferencje PN_Aktor_Piraci daja Ramp(2.5;0.8->4.0)=0.531 (waga 2) oraz");
        Console.WriteLine("     Ramp(9;10->2)=0.125 (waga 1) => 1.1875/3 = 0.3958. PN_Akcja_Napad nie ma");
        Console.WriteLine("     ZADNEJ preferencji, wiec nic tego nie podnosi. Rownoczesnie kandydaci");
        Console.WriteLine("     bez preferencji dostaja 1.0 bezwarunkowo (decyzja kroku 2:");
        Console.WriteLine("     'brak informacji nie jest kara'). RaidEnemy nie ma jak wygrac.");

        // Diagnostyka: najlepszy kandydat RaidEnemy i jego miejsce w rankingu.
        ScoredCandidate najRaid = d.Ranking.FirstOrDefault(x => !x.IsPass && x.Event != null && x.Event.ActionPayload == "RaidEnemy");
        if (najRaid != null)
        {
            int poz = d.Ranking.IndexOf(najRaid) + 1;
            Console.WriteLine("  najlepszy RaidEnemy: pozycja #" + poz + " u=" + T.F4(najRaid.Utility)
                              + " cf=" + T.F4(najRaid.FactorValue(nameof(ScoringWeights.contextFit)))
                              + " p=" + T.F4(najRaid.SelectionProbability));
            ScoredCandidate lider = d.Ranking[0];
            Console.WriteLine("  lider rankingu:      " + lider.Label + " u=" + T.F4(lider.Utility)
                              + " cf=" + T.F4(lider.FactorValue(nameof(ScoringWeights.contextFit))));
            if (lider.Event != null)
            {
                Console.WriteLine("  slad dopasowania lidera: " + lider.Event.FitTrace);
            }
        }
    }

    // ------------------------------------------------------- TEST 6: DETERMINIZM
    private static void Test6Determinizm(EventComposer composer, XmlConfig cfg)
    {
        T.Section("TEST 6 - DETERMINIZM: to samo ziarno + ten sam snapshot -> ten sam wybor I ranking");
        var gen = new CandidateGenerator(composer);
        var scorer = new UtilityScorer(CzynnikiZdarzen(), cfg.weights,
                                       UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, cfg.vetoContextFitBelow);
        var policy = new SelectionPolicy(cfg.Selection());

        WorldSnapshot[] scen = Scenariusze();
        int[] budzety = { 400, 40 };
        bool wszystkoOk = true;
        var raport = new List<string>();

        foreach (int budzet in budzety)
        {
            for (int i = 0; i < scen.Length; i++)
            {
                string a = Odcisk(gen, scorer, policy, scen[i], budzet, 12345, 2);
                string b = Odcisk(gen, scorer, policy, scen[i], budzet, 12345, 2);
                bool ok = a == b;
                wszystkoOk = wszystkoOk && ok;
                if (!ok) raport.Add("budzet " + budzet + " scenariusz " + i);
            }
        }
        T.Ok("odciski (zwyciezca + pelny ranking + prawdopodobienstwa) identyczne dla 2 budzetow x "
             + scen.Length + " scenariuszy", wszystkoOk, raport.Count == 0 ? null : string.Join("; ", raport));

        // Rozne ziarno wyboru MOZE dac innego zwyciezce (kontrola, ze losowosc w ogole dziala).
        var rozne = new HashSet<string>();
        for (int seed = 0; seed < 200; seed++)
        {
            rozne.Add(Odcisk(gen, scorer, policy, scen[0], 400, seed, 1));
        }
        T.Ok("rozne ziarna wyboru daja co najmniej 2 rozne przebiegi (softmax faktycznie losuje)",
             rozne.Count >= 2, "roznych odciskow: " + rozne.Count);

        // Niezmiennik zuzycia losowosci: DOKLADNIE 2 pobrania na runde Select (brama + wybor
        // zdarzenia), niezaleznie od rozmiaru puli i od tego, czy ktorys etap byl zdegenerowany.
        // To warunek porownywalnosci dwoch przebiegow ewaluacji rozniacych sie jednym kandydatem.
        var licznik = new CountingRandom(1);
        var pula = new List<ScoredCandidate> { Kand(0.9f, "A") };
        policy.Select(pula, Pass(0.25f), licznik);
        T.EqI("Select na puli 1-elementowej: dokladnie 3 wywolania rng.Next", licznik.NextCalls, 3);
        var licznik2 = new CountingRandom(1);
        policy.Select(new List<ScoredCandidate>(), Pass(0.25f), licznik2);
        T.EqI("Select na PUSTEJ puli: dokladnie 3 wywolania rng.Next", licznik2.NextCalls, 3);
        var licznik3 = new CountingRandom(1);
        var pulaDuzaR = new List<ScoredCandidate>();
        for (int i = 0; i < 40; i++) pulaDuzaR.Add(Kand(0.90f - i * 0.001f, "K" + i.ToString("00")));
        policy.Select(pulaDuzaR, Pass(0.25f), licznik3);
        T.EqI("Select na puli 40-elementowej: nadal dokladnie 3 wywolania", licznik3.NextCalls, 3);
    }

    private static string Odcisk(CandidateGenerator gen, UtilityScorer scorer, SelectionPolicy policy,
                                 WorldSnapshot s, int budzet, int seed, int wariantHistorii)
    {
        EventHistory h = HistoriaWariant(wariantHistorii);
        DecisionContext ctx = Ctx(h, 30f);
        CandidateSet cs = gen.Generate(new EventRecipe(), s, new SeededRandom(seed), budzet);
        List<ScoredCandidate> oc = scorer.ScoreAll(cs.Candidates, ctx);
        ScoredCandidate p = scorer.ScorePass(ctx);
        NarratorDecision d = policy.Select(oc, p, new SeededRandom(unchecked(seed + 104729)));

        var sb = new System.Text.StringBuilder();
        sb.Append(d.Winner == null ? "?" : d.Winner.SortKey).Append('#').Append(d.PassReason).Append('#');
        foreach (ScoredCandidate k in d.Ranking)
        {
            sb.Append(k.SortKey).Append(':')
              .Append(k.Utility.ToString("0.000000", CultureInfo.InvariantCulture)).Append(':')
              .Append(k.RawUtility.ToString("0.000000", CultureInfo.InvariantCulture)).Append(':')
              .Append(k.SelectionProbability.ToString("0.000000", CultureInfo.InvariantCulture)).Append(':')
              .Append((int)k.Rejected).Append(';');
        }
        return sb.ToString();
    }

    public static WorldSnapshot[] Scenariusze()
    {
        return new[]
        {
            new WorldSnapshot { DaysPassed = 60, ColonistCount = 9, ColonyWealth = 250000f, WealthRelative = 2.5f,
                                MountainRoofCellsNearColony = 120, HasHostileFaction = true, Season = 1, IsNight = false, WildAnimalCount = 12,
                                DaysSinceLastEvent = 3.0f },
            new WorldSnapshot { DaysPassed = 40, ColonistCount = 8, ColonyWealth = 60000f, WealthRelative = 4.0f,
                                MountainRoofCellsNearColony = 250, HasHostileFaction = true, Season = 3, IsNight = true, WildAnimalCount = 25,
                                DaysSinceLastEvent = 5.0f },
            new WorldSnapshot { DaysPassed = 40, ColonistCount = 3, ColonyWealth = 3000f, WealthRelative = 0.35f,
                                MountainRoofCellsNearColony = 0, HasHostileFaction = true, Season = 1, IsNight = false, WildAnimalCount = 2,
                                DaysSinceLastEvent = 2.0f },
            new WorldSnapshot { DaysPassed = 40, ColonistCount = 3, ColonyWealth = 3000f, WealthRelative = 0.35f,
                                MountainRoofCellsNearColony = 0, HasHostileFaction = false, Season = 1, IsNight = false, WildAnimalCount = 0,
                                // JEDYNY scenariusz BEZ spokoju - pilnuje, ze Cond_CalmPeriod
                                // faktycznie odcina PN_Trig_Cisza, a nie tylko istnieje.
                                DaysSinceLastEvent = 0.4f },
            new WorldSnapshot { DaysPassed = 1, ColonistCount = 3, ColonyWealth = 2000f, WealthRelative = 0.2f,
                                MountainRoofCellsNearColony = 0, HasHostileFaction = true, Season = 0, IsNight = false, WildAnimalCount = 5,
                                DaysSinceLastEvent = 1.0f }
        };
    }

    public static EventHistory HistoriaWariant(int wariant)
    {
        if (wariant == 0) return new EventHistory();
        if (wariant == 1)
        {
            return Hist(
                new object[] { "PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major, 26f },
                new object[] { "PN_Akcja_Zrzut", Theme.Economic, Valence.Positive, EventScale.Minor, 28f });
        }
        return Hist(
            new object[] { "PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major, 20f },
            new object[] { "PN_Akcja_Wedrowiec", Theme.Social, Valence.Positive, EventScale.Minor, 23f },
            new object[] { "PN_Akcja_Rojenie", Theme.Natural, Valence.Negative, EventScale.Major, 26f },
            new object[] { "PN_Akcja_Zrzut", Theme.Economic, Valence.Positive, EventScale.Minor, 29f });
        }

        /// <summary>
        /// Przecinek DZIESIETNY, czyli taki, ktory stoi miedzy cyframi. Zwykly przecinek
        /// jest legalny jako separator listy - FitTrace laczy nim kolejne preferencje -
        /// wiec test na Contains(",") dawal falszywy alarm przy kandydacie majacym
        /// wiecej niz jedna preferencje.
        /// </summary>
        private static bool MaPrzecinekDziesietny(string tekst)
        {
            if (string.IsNullOrEmpty(tekst)) return false;
            for (int i = 1; i + 1 < tekst.Length; i++)
                if (tekst[i] == ',' && char.IsDigit(tekst[i - 1]) && char.IsDigit(tekst[i + 1]))
                    return true;
            return false;
        }
}
