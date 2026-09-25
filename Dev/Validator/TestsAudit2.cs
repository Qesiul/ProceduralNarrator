using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Tension;
using ProceduralNarrator.Core.Util;

/// <summary>
/// DRUGI PRZEGLAD ETAPU 4 (2026-09-22) - testy domykajace luki w pokryciu, ktore przeglad
/// wykazal wstrzyknieciami: kod byl poprawny, ale zadna asercja go nie pilnowala.
///
///   10i - TurnPlanner: KAZDE wejscie Plan() ma wykrywacz (dotad tylko flaga kryzysu)
///   10j - docelowa moc: skladnik mocy, intensitySpan profili, porzadek zadanych ladunkow,
///         skrajne ladunki wyprowadzone z WCZYTANEGO katalogu
///   10k - Clone/Sanitize profili i wag przez refleksje, profil awaryjny z wagami z XML
///   12  - kodek pamieci narratora: round-trip pole po polu (a nie przez ponowne Encode),
///         linie wadliwe, autonaprawy stanu sprzecznego
///   5m  - klasyfikacja ciszy po petli: rundy zjedzone przez niedostepne werdykty
/// </summary>
static class TestsAudit2
{
    public static void Run(XmlConfig cfg, List<Block> blocks)
    {
        TestPlannerWejscia(cfg);
        TestDocelowaMoc(cfg, blocks);
        TestKlonyProfili(cfg);
        TestKodekPamieci();
        TestCiszaPoPetli(cfg);
    }

    private static string F(double v)
    {
        return v.ToString("0.000", CultureInfo.InvariantCulture);
    }

    private static WorldSnapshot Swiat(int kolonistow, int powalonych, DangerLevel danger)
    {
        return new WorldSnapshot
        {
            DaysPassed = 30,
            ColonistCount = kolonistow,
            ColonistsOnMap = kolonistow,
            AcuteDownedCount = powalonych,
            Danger = danger,
            WealthRelative = 1f
        };
    }

    private static TensionModel Model(TensionParams p)
    {
        return new TensionModel(p, ContrastTuning.Default());
    }

    // =====================================================================================
    //  TEST 10i - TurnPlanner: kazde wejscie Plan() ma wykrywacz
    // =====================================================================================
    private static void TestPlannerWejscia(XmlConfig cfg)
    {
        T.Section("TEST 10i - TURNPLANNER: parametry kryzysu, krzywa profilu, historia i dzien gry");

        CrisisParams kp = cfg.crisis.Clone();
        kp.Sanitize();
        WorldSnapshot trzyNaSzesc = Swiat(6, 3, DangerLevel.None);
        WorldSnapshot dwaNaSzesc = Swiat(6, 2, DangerLevel.None);

        // ---- (a) parametry kryzysu z XML dochodza do planu - w OBIE strony ----
        foreach (NarratorProfile prof in cfg.profiles)
        {
            TensionModel m = Model(prof.Tension);
            TurnPlan bazowy = TurnPlanner.Plan(m, kp, new EventHistory(), trzyNaSzesc, 30f);

            CrisisParams wylaczony = kp.Clone();
            wylaczony.enabled = false;
            TurnPlan bezReguly = TurnPlanner.Plan(m, wylaczony, new EventHistory(), trzyNaSzesc, 30f);
            IntentDecision zKrzywej = IntentSelector.Select(bezReguly.Tension.Tension, prof.Tension);
            T.Ok("profil " + prof.Id + ": enabled=false wylacza regule (bazowo 3/6 JEST kryzysem)",
                 bazowy.Context.ExtremeCrisis
                 && !bezReguly.Crisis.Extreme && !bezReguly.Context.ExtremeCrisis
                 && bezReguly.Context.Intent == zKrzywej.Intent
                 && Math.Abs(bezReguly.Context.TargetIntensity - zKrzywej.TargetIntensity) < 1e-6f,
                 bezReguly.Crisis.Trace + " | " + bezReguly.Intent.Trace);

            CrisisParams wyzszyProg = kp.Clone();
            wyzszyProg.minDowned = 4;
            TurnPlan prog4 = TurnPlanner.Plan(m, wyzszyProg, new EventHistory(), trzyNaSzesc, 30f);
            T.Ok("profil " + prof.Id + ": minDowned=4 -> 3/6 NIE jest kryzysem", !prog4.Context.ExtremeCrisis,
                 prog4.Crisis.Trace);

            // Kierunek odwrotny: prog nizszy od domyslnego robi kryzys z 2/6. Bez tej asercji
            // zignorowanie calego bloku <crisis> (Evaluate z domyslnymi parametrami) dawaloby
            // ten sam wynik co wyzszy prog tylko przypadkiem.
            CrisisParams nizszyUlamek = kp.Clone();
            nizszyUlamek.downedFraction = 0.3f;
            TurnPlan ulamek03 = TurnPlanner.Plan(m, nizszyUlamek, new EventHistory(), dwaNaSzesc, 30f);
            TurnPlan ulamekDom = TurnPlanner.Plan(m, kp, new EventHistory(), dwaNaSzesc, 30f);
            T.Ok("profil " + prof.Id + ": downedFraction=0.3 -> 2/6 JEST kryzysem (domyslnie nie jest)",
                 ulamek03.Context.ExtremeCrisis && !ulamekDom.Context.ExtremeCrisis,
                 ulamek03.Crisis.Trace + " | domyslnie: " + ulamekDom.Crisis.Trace);
        }

        // ---- (b) historia, dzien gry i krzywa profilu dochodza do napiecia i intencji ----
        EventHistory hKatastrof = TestsDecision.Hist(
            new object[] { "PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major, 20f },
            new object[] { "PN_Akcja_Rojenie", Theme.Natural, Valence.Negative, EventScale.Major, 23.5f },
            new object[] { "PN_Akcja_Burza", Theme.Natural, Valence.Negative, EventScale.Moderate, 26f },
            new object[] { "PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major, 28.25f });
        hKatastrof.RecordPass(29f, 0, true);
        WorldSnapshot spokoj = Swiat(6, 0, DangerLevel.None);
        const float Dzien = 30f;

        int profiliRoznychOdDomyslnego = 0;
        foreach (NarratorProfile prof in cfg.profiles)
        {
            TensionModel m = Model(prof.Tension);
            TensionReading oczekiwane = m.Compute(hKatastrof, spokoj, Dzien);
            TensionReading bezHistorii = m.Compute(new EventHistory(), spokoj, Dzien);
            TensionReading dzienZero = m.Compute(hKatastrof, spokoj, 0f);

            T.Ok("STRAZNIK profil " + prof.Id + ": historia zmienia napiecie (inaczej zignorowanie historii przejdzie)",
                 Math.Abs(oczekiwane.Tension - bezHistorii.Tension) > 1e-3f,
                 F(oczekiwane.Tension) + " wobec " + F(bezHistorii.Tension));
            T.Ok("STRAZNIK profil " + prof.Id + ": dzien gry zmienia napiecie (zanik wpisow)",
                 Math.Abs(oczekiwane.Tension - dzienZero.Tension) > 1e-3f,
                 F(oczekiwane.Tension) + " wobec dnia 0: " + F(dzienZero.Tension));

            TurnPlan plan = TurnPlanner.Plan(m, kp, hKatastrof, spokoj, Dzien);
            IntentDecision zamiar = IntentSelector.Select(oczekiwane.Tension, prof.Tension);
            T.Ok("profil " + prof.Id + ": napiecie planu == Compute(historia, swiat, dzien)",
                 Math.Abs(plan.Tension.Tension - oczekiwane.Tension) < 1e-6f
                 && Math.Abs(plan.Context.Tension - oczekiwane.Tension) < 1e-6f,
                 F(plan.Tension.Tension) + " == " + F(oczekiwane.Tension));
            T.Ok("profil " + prof.Id + ": intencja i moc planu == Select(napiecie, KRZYWA PROFILU)",
                 !plan.Context.ExtremeCrisis
                 && plan.Context.Intent == zamiar.Intent
                 && Math.Abs(plan.Context.TargetIntensity - zamiar.TargetIntensity) < 1e-6f,
                 plan.Intent.Trace);

            IntentDecision zDomyslnej = IntentSelector.Select(oczekiwane.Tension, TensionParams.Default());
            if (zDomyslnej.Intent != zamiar.Intent || Math.Abs(zDomyslnej.TargetIntensity - zamiar.TargetIntensity) > 1e-3f)
            {
                profiliRoznychOdDomyslnego++;
            }

            // ---- (c) kontekst niesie TE historie, TEN dzien i TEN snapshot ----
            T.Ok("profil " + prof.Id + ": kontekst niesie historie, dzien i numer decyzji z wejscia",
                 ReferenceEquals(plan.Context.History, hKatastrof)
                 && ReferenceEquals(plan.Context.Snapshot, spokoj)
                 && Math.Abs(plan.Context.GameDay - Dzien) < 1e-6f
                 && plan.Context.DecisionIndex == hKatastrof.DecisionCount,
                 "decyzjaNr=" + plan.Context.DecisionIndex + " histDecyzji=" + hKatastrof.DecisionCount);
        }
        T.Ok("STRAZNIK: co najmniej jeden profil daje inna intencje/moc niz krzywa domyslna "
             + "(inaczej zignorowanie parametrow profilu przejdzie)",
             profiliRoznychOdDomyslnego >= 1, "profili: " + profiliRoznychOdDomyslnego);
        T.Ok("STRAZNIK: historia testowa ma decyzje (numer decyzji jest sprawdzalny)",
             hKatastrof.DecisionCount > 0, "decyzji: " + hKatastrof.DecisionCount);
    }

    // =====================================================================================
    //  TEST 10j - docelowa moc
    // =====================================================================================
    private static ScoredCandidate KandMocy(IntensityLevel moc)
    {
        ComposedEvent e = TestsDecision.Ev("PN_Akcja_Test", Theme.Natural, Valence.Negative, EventScale.Moderate);
        e.Intensity = moc;
        return new ScoredCandidate { Event = e, IsPass = false, SortKey = "M" + (int)moc };
    }

    private static void TestDocelowaMoc(XmlConfig cfg, List<Block> blocks)
    {
        T.Section("TEST 10j - DOCELOWA MOC: skladnik mocy, rozpietosc profili, ladunki z katalogu");

        var f = new Factor_IntentAlignment();
        string slad;
        ScoredCandidate wysoki = KandMocy(IntensityLevel.High);
        ScoredCandidate niski = KandMocy(IntensityLevel.Low);
        var pusta = new EventHistory();

        // (a) Dwaj kandydaci rozni WYLACZNIE moca. Ladunek jest wspolny, wiec cala roznica
        // pochodzi ze skladnika mocy - dotad kazdy test uzywal kandydatow o mocy Normal
        // i skladnik znosil sie w kazdym porownaniu (3 regresje przechodzily na zielono).
        double rozpietoscSkali = (int)IntensityLevel.VeryHigh - (int)IntensityLevel.VeryLow;
        double oczekiwanaRoznica = Factor_IntentAlignment.MagnitudeWeight * 2.0 / rozpietoscSkali;
        foreach (Intent intencja in new[] { Intent.Escalate, Intent.Hold, Intent.Breathe })
        {
            DecisionContext wGore = DecisionContext.Create(new WorldSnapshot(), pusta, 10f, intencja, 1f, 0.3f);
            DecisionContext wDol = DecisionContext.Create(new WorldSnapshot(), pusta, 10f, intencja, -1f, 0.3f);
            DecisionContext zero = DecisionContext.Create(new WorldSnapshot(), pusta, 10f, intencja, 0f, 0.3f);

            double gW = f.Evaluate(wysoki, wGore, out slad), gN = f.Evaluate(niski, wGore, out slad);
            double dW = f.Evaluate(wysoki, wDol, out slad), dN = f.Evaluate(niski, wDol, out slad);
            double zW = f.Evaluate(wysoki, zero, out slad), zN = f.Evaluate(niski, zero, out slad);

            T.Eq(intencja + ", moc +1: High bije Low dokladnie o MagnitudeWeight * 2 / 4",
                 gW - gN, oczekiwanaRoznica, 1e-5);
            T.Eq(intencja + ", moc -1: Low bije High o te sama roznice (preferencja sie ODWRACA)",
                 dN - dW, oczekiwanaRoznica, 1e-5);
            T.Eq(intencja + ", moc 0: High i Low rowne (symetria skladnika)", zW - zN, 0.0, 1e-6);
        }

        // Kryzys i moc: regula min(moc, 0) ma zabierac dodatni nacisk na moc. Profil, ktory
        // przed kryzysem chcial mocy dodatniej, po regule nie moze juz premiowac High.
        CrisisParams kp = cfg.crisis.Clone();
        kp.Sanitize();
        int profiliZDodatniaMoca = 0;
        foreach (NarratorProfile prof in cfg.profiles)
        {
            CrisisParams wylaczony = kp.Clone();
            wylaczony.enabled = false;
            TurnPlan bezReguly = TurnPlanner.Plan(Model(prof.Tension), wylaczony, new EventHistory(),
                                                  Swiat(6, 3, DangerLevel.None), 30f);
            TurnPlan zRegula = TurnPlanner.Plan(Model(prof.Tension), kp, new EventHistory(),
                                                Swiat(6, 3, DangerLevel.None), 30f);
            double przewagaHighBez = f.Evaluate(wysoki, bezReguly.Context, out slad) - f.Evaluate(niski, bezReguly.Context, out slad);
            double przewagaHighZ = f.Evaluate(wysoki, zRegula.Context, out slad) - f.Evaluate(niski, zRegula.Context, out slad);
            if (bezReguly.Context.TargetIntensity > 0f)
            {
                profiliZDodatniaMoca++;
                T.Ok("profil " + prof.Id + ": bez reguly moc > 0 premiuje High", przewagaHighBez > 0,
                     "moc=" + F(bezReguly.Context.TargetIntensity) + " przewaga=" + F(przewagaHighBez));
            }
            T.Ok("profil " + prof.Id + ": w kryzysie High NIE ma przewagi mocy nad Low (moc <= 0)",
                 przewagaHighZ <= 1e-6, "moc=" + F(zRegula.Context.TargetIntensity) + " przewaga=" + F(przewagaHighZ));
        }
        T.Ok("STRAZNIK: co najmniej jeden profil chcial przed kryzysem mocy dodatniej",
             profiliZDodatniaMoca >= 1, "profili: " + profiliZDodatniaMoca);

        // (b) intensitySpan KAZDEGO profilu z XML dochodzi do docelowej mocy.
        int spanowInnychNizJeden = 0;
        foreach (NarratorProfile prof in cfg.profiles)
        {
            float span = prof.Tension.intensitySpan;
            if (Math.Abs(span - 1f) > 1e-3f) spanowInnychNizJeden++;
            T.Eq("profil " + prof.Id + ": napiecie 0 -> moc +intensitySpan",
                 IntentSelector.Select(0f, prof.Tension).TargetIntensity, span, 1e-6);
            T.Eq("profil " + prof.Id + ": napiecie 1 -> moc -intensitySpan",
                 IntentSelector.Select(1f, prof.Tension).TargetIntensity, -span, 1e-6);
            T.Eq("profil " + prof.Id + ": napiecie 0.5 -> moc 0",
                 IntentSelector.Select(0.5f, prof.Tension).TargetIntensity, 0.0, 1e-6);
        }
        T.Ok("STRAZNIK: co najmniej jeden profil ma intensitySpan != 1 (inaczej pominiecie spanu przejdzie)",
             spanowInnychNizJeden >= 1, "profili: " + spanowInnychNizJeden);

        // (c) porzadek zadanych ladunkow: Escalate < Hold < Breathe.
        float lE = Factor_IntentAlignment.DesiredCharge(Intent.Escalate);
        float lH = Factor_IntentAlignment.DesiredCharge(Intent.Hold);
        float lB = Factor_IntentAlignment.DesiredCharge(Intent.Breathe);
        T.Ok("zadany ladunek: Escalate < Hold < Breathe (scisle)", lE < lH && lH < lB,
             F(lE) + " < " + F(lH) + " < " + F(lB));

        // (d) skrajne ladunki z WCZYTANEGO katalogu akcji, a nie z literalu Charge(...).
        List<Block> akcje = blocks.Where(b => b.Type == BlockType.Action).ToList();
        T.Ok("STRAZNIK: katalog ma klocki akcji", akcje.Count > 0, "akcji: " + akcje.Count);
        List<float> ladunki = akcje.Select(b => Factor_DramaticContrast.Charge(b.Valence, b.Scale)).ToList();
        float minL = ladunki.Min(), maxL = ladunki.Max();
        double sredni = ladunki.Average();
        var rozne = ladunki.Select(x => (double)Math.Round(x, 3)).Distinct().OrderBy(x => x).ToList();
        Console.WriteLine("      ladunki katalogu (" + rozne.Count + " roznych): "
                          + string.Join(", ", rozne.Select(x => x.ToString("0.00", CultureInfo.InvariantCulture)))
                          + " | srednia po " + akcje.Count + " akcjach: " + F(sredni));
        T.Eq("zadany ladunek Escalate == NAJNIZSZY ladunek wczytanego katalogu", lE, minL, 1e-6);
        T.Eq("zadany ladunek Breathe == NAJWYZSZY ladunek wczytanego katalogu", lB, maxL, 1e-6);
        T.Ok("zadany ladunek Hold ~ srednia ladunku katalogu (|roznica| <= 0.05; uzasadnienie w komentarzu)",
             Math.Abs(lH - sredni) <= 0.05, F(lH) + " wobec " + F(sredni));
    }

    // =====================================================================================
    //  TEST 10k - Clone/Sanitize profili i wag przez refleksje
    // =====================================================================================
    /// <summary>
    /// Ustawia KAZDE publiczne pole na wartosc rozna od domyslnej, klonuje i porownuje.
    /// Pole dodane w przyszlosci, a pominiete w Clone, wypadnie tu bez zmiany testu.
    /// </summary>
    private static List<string> BrakujaceWKlonie<TT>(Func<TT, TT> klon) where TT : new()
    {
        var zrodlo = new TT();
        FieldInfo[] pola = typeof(TT).GetFields(BindingFlags.Public | BindingFlags.Instance);
        int nr = 1;
        foreach (FieldInfo p in pola)
        {
            object v;
            if (p.FieldType == typeof(float)) v = 0.123f * nr + 0.5f;
            else if (p.FieldType == typeof(int)) v = 40 + nr;
            else if (p.FieldType == typeof(bool)) v = !(bool)p.GetValue(zrodlo);
            else throw new Exception("TEST 10k: nieobslugiwany typ pola " + typeof(TT).Name + "." + p.Name);
            p.SetValue(zrodlo, v);
            nr++;
        }
        TT kopia = klon(zrodlo);
        var brak = new List<string>();
        foreach (FieldInfo p in pola)
        {
            if (!Equals(p.GetValue(zrodlo), p.GetValue(kopia))) brak.Add(p.Name);
        }
        if (pola.Length == 0) brak.Add("(brak pol - straznik)");
        return brak;
    }

    private static void TestKlonyProfili(XmlConfig cfg)
    {
        T.Section("TEST 10k - KLONY: TensionParams, ScoringWeights, CrisisParams, profil awaryjny");

        List<string> bT = BrakujaceWKlonie<TensionParams>(x => x.Clone());
        T.Ok("TensionParams.Clone kopiuje KAZDE pole publiczne (refleksja)", bT.Count == 0,
             bT.Count == 0 ? "pol: " + typeof(TensionParams).GetFields(BindingFlags.Public | BindingFlags.Instance).Length
                           : "brak: " + string.Join(", ", bT));
        List<string> bW = BrakujaceWKlonie<ScoringWeights>(x => x.Clone());
        T.Ok("ScoringWeights.Clone kopiuje KAZDE pole publiczne (refleksja)", bW.Count == 0,
             bW.Count == 0 ? "ok" : "brak: " + string.Join(", ", bW));
        List<string> bC = BrakujaceWKlonie<CrisisParams>(x => x.Clone());
        T.Ok("CrisisParams.Clone kopiuje KAZDE pole publiczne (refleksja)", bC.Count == 0,
             bC.Count == 0 ? "ok" : "brak: " + string.Join(", ", bC));

        // Profile z XML: kopia jest rowna oryginalowi pole w pole, a Sanitize NIC nie poprawia.
        // Sanitize, ktory po cichu przycina poprawne wartosci profilu, zmienialby osobowosc
        // w grze (Resolve sanityzuje kopie) bez sladu w walidatorze, ktory liczy na surowych.
        FieldInfo[] polaT = typeof(TensionParams).GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (NarratorProfile prof in cfg.profiles)
        {
            TensionParams c = prof.Tension.Clone();
            string poprawki = c.Sanitize();
            var rozne = polaT.Where(p => !Equals(p.GetValue(c), p.GetValue(prof.Tension))).Select(p => p.Name).ToList();
            T.Ok("profil " + prof.Id + ": Sanitize() kopii nic nie zmienia (pusty opis, pola rowne)",
                 string.IsNullOrEmpty(poprawki) && rozne.Count == 0,
                 (poprawki ?? "") + (rozne.Count == 0 ? "" : " zmienione: " + string.Join(", ", rozne)));
        }

        // Profil awaryjny bierze wagi z podanego zestawu (blok <weights>), jako KOPIE.
        var zXml = cfg.weights.Clone();
        zXml.freshness += 0.25f;
        NarratorProfile awaryjny = NarratorProfile.Fallback(zXml);
        bool zgodne = Math.Abs(awaryjny.Weights.freshness - zXml.freshness) < 1e-6f
                      && Math.Abs(awaryjny.Weights.contextFit - zXml.contextFit) < 1e-6f;
        zXml.freshness += 1f;
        bool kopia = Math.Abs(awaryjny.Weights.freshness - (zXml.freshness - 1f)) < 1e-6f;
        T.Ok("profil awaryjny: wagi == podany blok <weights> i sa jego KOPIA",
             zgodne && kopia && awaryjny.Id == NarratorProfile.FallbackId, awaryjny.Weights.Describe());
        NarratorProfile bezWag = NarratorProfile.Fallback(null);
        var domyslne = new ScoringWeights();
        T.Ok("profil awaryjny bez bloku: inicjalizatory ScoringWeights i krzywa domyslna",
             bezWag.Weights.Describe() == domyslne.Describe()
             && bezWag.Tension.ToString() == TensionParams.Default().ToString(), bezWag.ToString());
    }

    // =====================================================================================
    //  TEST 12 - kodek pamieci narratora
    // =====================================================================================
    private static string StanWpisu(EventHistoryEntry e)
    {
        if (e == null) return "null";
        return e.DecisionIndex.ToString(CultureInfo.InvariantCulture)
               + "|" + BitConverter.SingleToInt32Bits(e.GameDay).ToString(CultureInfo.InvariantCulture)
               + "|" + e.GameTick.ToString(CultureInfo.InvariantCulture)
               + "|" + (e.ActionBlockId ?? "<null>") + "|" + (e.ActionPayload ?? "<null>")
               + "|" + e.Theme + "|" + e.Valence + "|" + e.Scale;
    }

    /// <summary>
    /// Pelny zrzut stanu historii Z POL - NIE przez Encode. Porownanie oparte o Encode jest
    /// slepe na strate po stronie zapisu: zle kodowana walencja daje po odczycie te sama linie,
    /// wiec odcisk przed == po, mimo ze pole zmienilo sie z Negative na Neutral.
    /// </summary>
    private static string StanHistorii(EventHistory h)
    {
        var czesci = new List<string>();
        czesci.Add("dc=" + h.DecisionCount + " seria=" + h.DeliberateSilenceStreak + " n=" + h.Count);
        foreach (EventHistoryEntry e in h.Entries) czesci.Add(StanWpisu(e));
        czesci.Add("newest=" + StanWpisu(h.Newest));
        czesci.Add("recent5=" + string.Join(",", h.Recent(5).Select(e => e.DecisionIndex.ToString(CultureInfo.InvariantCulture))));
        return string.Join("\n", czesci);
    }

    private static readonly Theme[] Tematy = (Theme[])Enum.GetValues(typeof(Theme));
    private static readonly Valence[] Walencje = (Valence[])Enum.GetValues(typeof(Valence));
    private static readonly EventScale[] Skale = (EventScale[])Enum.GetValues(typeof(EventScale));

    private static void TestKodekPamieci()
    {
        T.Section("TEST 12 - KODEK PAMIECI: round-trip pole po polu, linie wadliwe, autonaprawy");

        var fc = new Factor_DramaticContrast(ContrastTuning.Default());
        TensionModel model = Model(TensionParams.Default());
        WorldSnapshot swiat = Swiat(6, 0, DangerLevel.None);

        int historii = 0, rozjazdow = 0, odrzuconychWsumie = 0, rozjazdowNapiecia = 0;
        int zSeria = 0, zOgonemCiszy = 0, ponadBufor = 0, zUlamkiemDnia = 0, zInnymPayloadem = 0, zPustymPayloadem = 0;
        string pierwszyRozjazd = null;

        for (int ziarno = 0; ziarno < 400; ziarno++)
        {
            var rng = new Random(ziarno);
            var h = new EventHistory();
            int decyzji = rng.Next(0, 70);
            float dzien = (float)(rng.NextDouble() * 3.0);
            for (int d = 0; d < decyzji; d++)
            {
                dzien += (float)(rng.NextDouble() * 4.0);
                int tick = (int)(dzien * 60000f);
                double los = rng.NextDouble();
                if (los < 0.55)
                {
                    string id = "PN_Akcja_" + rng.Next(13).ToString(CultureInfo.InvariantCulture);
                    ComposedEvent e = TestsDecision.Ev(id, Tematy[rng.Next(Tematy.Length)],
                                                       Walencje[rng.Next(Walencje.Length)], Skale[rng.Next(Skale.Length)]);
                    int wariantPayloadu = rng.Next(3);
                    if (wariantPayloadu == 1) e.ActionPayload = "Incydent_" + rng.Next(9).ToString(CultureInfo.InvariantCulture);
                    if (wariantPayloadu == 2) e.ActionPayload = null;
                    h.RecordEvent(e, dzien, tick);
                }
                else
                {
                    h.RecordPass(dzien, tick, los < 0.8);
                }
            }

            historii++;
            if (h.DeliberateSilenceStreak > 0) zSeria++;
            if (h.Newest != null && h.DecisionCount - 1 > h.Newest.DecisionIndex) zOgonemCiszy++;
            if (h.DecisionCount > h.Capacity && h.Count == h.Capacity) ponadBufor++;
            if (h.Entries.Any(e => Math.Abs(e.GameDay - Math.Round(e.GameDay)) > 1e-3)) zUlamkiemDnia++;
            if (h.Entries.Any(e => e.ActionPayload != null && e.ActionPayload != e.ActionBlockId)) zInnymPayloadem++;
            if (h.Entries.Any(e => e.ActionPayload == null)) zPustymPayloadem++;

            string przed = StanHistorii(h);
            float napiecieDzien = dzien + 0.5f;
            TensionReading tPrzed = model.Compute(h, swiat, napiecieDzien);
            float rytmPrzed = fc.ComputeRhythm(h).Charge;

            var odtworzona = new EventHistory();
            int odrzucone = odtworzona.RestoreFromLines(h.DecisionCount, h.DeliberateSilenceStreak, h.ToPersistableLines());
            odrzuconychWsumie += odrzucone;
            string po = StanHistorii(odtworzona);
            if (przed != po)
            {
                rozjazdow++;
                if (pierwszyRozjazd == null) pierwszyRozjazd = "ziarno " + ziarno + ":\n" + przed + "\n--- po ---\n" + po;
            }

            TensionReading tPo = model.Compute(odtworzona, swiat, napiecieDzien);
            float rytmPo = fc.ComputeRhythm(odtworzona).Charge;
            if (BitConverter.SingleToInt32Bits(tPrzed.Tension) != BitConverter.SingleToInt32Bits(tPo.Tension)
                || BitConverter.SingleToInt32Bits(rytmPrzed) != BitConverter.SingleToInt32Bits(rytmPo))
            {
                rozjazdowNapiecia++;
            }
        }

        T.EqI("round-trip: stan POLE PO POLU identyczny (wpisy, kolejnosc, Newest, Recent, liczniki)", rozjazdow, 0);
        if (pierwszyRozjazd != null) Console.WriteLine("      pierwszy rozjazd: " + pierwszyRozjazd.Replace("\n", "\n        "));
        T.EqI("round-trip: zero odrzuconych linii przy danych poprawnych", odrzuconychWsumie, 0);
        T.EqI("round-trip: napiecie i rytm IDENTYCZNE bitowo przed i po", rozjazdowNapiecia, 0);
        T.Ok("STRAZNIK: historie z seria ciszy > 0", zSeria > 0, zSeria + "/" + historii);
        T.Ok("STRAZNIK: historie z ogonem ciszy po ostatnim zdarzeniu", zOgonemCiszy > 0, zOgonemCiszy + "/" + historii);
        T.Ok("STRAZNIK: historie przepelnione ponad bufor", ponadBufor > 0, ponadBufor + "/" + historii);
        T.Ok("STRAZNIK: wpisy z ulamkowym dniem gry", zUlamkiemDnia > 0, zUlamkiemDnia + "/" + historii);
        T.Ok("STRAZNIK: payload rozny od klocka akcji i payload pusty",
             zInnymPayloadem > 0 && zPustymPayloadem > 0, zInnymPayloadem + " / " + zPustymPayloadem);

        // ---- linie wadliwe: odrzucane POJEDYNCZO, reszta zostaje ----
        var wzor = new EventHistory();
        wzor.RecordEvent(TestsDecision.Ev("PN_Akcja_A", Theme.Raid, Valence.Negative, EventScale.Major), 1.25f, 75000);
        wzor.RecordEvent(TestsDecision.Ev("PN_Akcja_B", Theme.Economic, Valence.Positive, EventScale.Minor), 2.5f, 150000);
        List<string> dobre = wzor.ToPersistableLines();
        var wadliwe = new List<string>
        {
            "",
            "za|malo|pol",
            "5|NaN|0|PN_X|-|Raid|Negative|Major",
            "5|1.5|0|PN_X|-|17|Negative|Major",
            "5|1.5|0|PN_X|-|raid|Negative|Major",
            "x|1.5|0|PN_X|-|Raid|Negative|Major"
        };
        var mieszane = new List<string> { dobre[0] };
        mieszane.AddRange(wadliwe);
        mieszane.Add(dobre[1]);
        var zWadliwymi = new EventHistory();
        int odrz = zWadliwymi.RestoreFromLines(wzor.DecisionCount, 0, mieszane);
        T.EqI("linie wadliwe: odrzucone dokladnie te " + wadliwe.Count, odrz, wadliwe.Count);
        T.Ok("linie wadliwe: poprawne wpisy zachowane pole po polu",
             zWadliwymi.Count == 2 && StanWpisu(zWadliwymi.Entries[0]) == StanWpisu(wzor.Entries[0])
             && StanWpisu(zWadliwymi.Entries[1]) == StanWpisu(wzor.Entries[1]),
             StanHistorii(zWadliwymi).Replace("\n", " ; "));

        // ---- autonaprawy stanu sprzecznego (zapis uszkodzony albo edytowany recznie) ----
        string linia40 = "40|30.5|1830000|PN_Akcja_Nowa|-|Raid|Negative|Major";
        string linia2 = "2|3.25|195000|PN_Akcja_Stara|-|Economic|Positive|Minor";
        var odwrocone = new EventHistory();
        odwrocone.RestoreFromLines(5, 0, new[] { linia40, linia2 });
        T.Ok("kolejnosc odwrocona: wpisy posortowane, Newest = najwiekszy DecisionIndex, licznik ponad nim",
             odwrocone.Newest != null && odwrocone.Newest.DecisionIndex == 40
             && odwrocone.Entries[0].DecisionIndex == 2 && odwrocone.DecisionCount == 41,
             StanHistorii(odwrocone).Replace("\n", " ; "));

        var seriaPoZdarzeniu = new EventHistory();
        seriaPoZdarzeniu.RestoreFromLines(3, 3, new[] { linia2 });
        T.EqI("seria przy zdarzeniu bedacym OSTATNIA decyzja -> 0 (jak po RecordEvent)",
              seriaPoZdarzeniu.DeliberateSilenceStreak, 0);

        var seriaZaDluga = new EventHistory();
        seriaZaDluga.RestoreFromLines(10, 9, new[] { linia2 });
        T.EqI("seria dluzsza niz decyzje po zdarzeniu -> przycieta do 10 - 1 - 2 = 7",
              seriaZaDluga.DeliberateSilenceStreak, 7);

        var seriaBezWpisow = new EventHistory();
        seriaBezWpisow.RestoreFromLines(2, 5, new string[0]);
        T.EqI("bez wpisow seria nie dluzsza niz wszystkie decyzje (2)", seriaBezWpisow.DeliberateSilenceStreak, 2);

        var spojna = new EventHistory();
        spojna.RestoreFromLines(10, 3, new[] { linia2 });
        T.Ok("stan spojny zostaje NIETKNIETY (autonaprawa nie nadgorliwa)",
             spojna.DeliberateSilenceStreak == 3 && spojna.DecisionCount == 10,
             StanHistorii(spojna).Replace("\n", " ; "));
    }

    // =====================================================================================
    //  TEST 5m - cisza po petli: rundy zjedzone przez niedostepne werdykty
    // =====================================================================================
    private static ScoredCandidate KandP(float u, string payload)
    {
        ComposedEvent e = TestsDecision.Ev("BLOK_" + payload, Theme.Raid, Valence.Negative, EventScale.Major);
        e.ActionPayload = payload;
        e.Signature = "sig_" + payload;
        return new ScoredCandidate
        {
            Event = e, IsPass = false, SortKey = payload, RawUtility = u, Utility = u,
            Vetoed = false, Factors = new List<FactorScore>()
        };
    }

    private static ScoredCandidate PassK(float u)
    {
        return new ScoredCandidate
        {
            Event = null, IsPass = true, SortKey = UtilityScorer.PassSortKey, RawUtility = u, Utility = u,
            Vetoed = false, Factors = new List<FactorScore>()
        };
    }

    private static string ZakresP(ScoredCandidate k)
    {
        return k == null || k.Event == null ? null : k.Event.ActionPayload;
    }

    private static string KluczP(ScoredCandidate k)
    {
        return k == null || k.Event == null ? null
            : (k.Event.ActionPayload ?? "?") + "|" + ((int)k.Event.Intensity).ToString(CultureInfo.InvariantCulture);
    }

    private static void TestCiszaPoPetli(XmlConfig cfg)
    {
        T.Section("TEST 5m - CISZA PO PETLI: rundy zjedzone przez niedostepne werdykty -> VerdictUnavailable");

        var policy = new SelectionPolicy(cfg.Selection());
        const int MaxRund = 2;
        var runner = new TurnRunner(policy, cfg.pass, MaxRund);

        // Czolo (A) potwierdzone w fazie 0; o pozostale payloady pytala w tym ticku "inna mapa".
        CandidateAcceptor inneMapy = delegate(ScoredCandidate k, bool pytaj, out string pw)
        {
            pw = null;
            if (!pytaj || ZakresP(k) == "A") return AcceptorVerdict.Accepted;
            pw = "test: pytano w tym ticku o " + ZakresP(k);
            return AcceptorVerdict.Unanswerable;
        };
        // Kontrola: te same payloady, ale gra ODMAWIA - budzet pytan naprawde wiazacy.
        CandidateAcceptor odmowy = delegate(ScoredCandidate k, bool pytaj, out string pw)
        {
            pw = null;
            if (!pytaj || ZakresP(k) == "A") return AcceptorVerdict.Accepted;
            pw = "test: odmowa " + ZakresP(k);
            return AcceptorVerdict.RefusedByGame;
        };

        int turNiedostepnych = 0, zlaEtykieta = 0, swiadomych = 0, zPRunda = 0;
        int turBudzetu = 0, zlaEtykietaBudzetu = 0;
        for (int ziarno = 0; ziarno < 300; ziarno++)
        {
            var pula = new List<ScoredCandidate> { KandP(0.60f, "A"), KandP(0.59f, "B"), KandP(0.58f, "C"), KandP(0.57f, "D") };
            DecisionContext ctx = DecisionContext.Create(new WorldSnapshot(), new EventHistory(), 10f, Intent.Hold);
            TurnResult r = runner.Run(ctx, pula, PassK(0.05f), new SeededRandom(ziarno), 0f, inneMapy, KluczP, ZakresP);
            if (r.Rounds == MaxRund && !r.Accepted && r.Decision.IsPass
                && r.UnanswerableScopes > 0 && r.AcceptorCalls < MaxRund)
            {
                turNiedostepnych++;
                if (r.Decision.PassReason != PassReason.VerdictUnavailable) zlaEtykieta++;
                if (r.DeliberateSilence) swiadomych++;
                if (r.Decision.WinnerRoundProbability != null) zPRunda++;
            }

            var pula2 = new List<ScoredCandidate> { KandP(0.60f, "A"), KandP(0.59f, "B"), KandP(0.58f, "C"), KandP(0.57f, "D") };
            TurnResult r2 = runner.Run(ctx, pula2, PassK(0.05f), new SeededRandom(ziarno), 0f, odmowy, KluczP, ZakresP);
            if (!r2.Accepted && r2.Decision.IsPass && r2.AcceptorCalls >= MaxRund && r2.UnanswerableScopes == 0)
            {
                turBudzetu++;
                if (r2.Decision.PassReason != PassReason.RoundBudgetExhausted) zlaEtykietaBudzetu++;
            }
        }

        T.Ok("STRAZNIK: tury z rundami zjedzonymi przez niedostepne werdykty wystepuja",
             turNiedostepnych > 0, "tur: " + turNiedostepnych + "/300");
        T.EqI("takie tury maja powod VerdictUnavailable (nie RoundBudgetExhausted)", zlaEtykieta, 0);
        T.EqI("... sa cisza techniczna (nie podnosza serii)", swiadomych, 0);
        T.EqI("... i maja puste pRunda (cisza nie z losowania)", zPRunda, 0);
        T.Ok("STRAZNIK kontroli: tury z NAPRAWDE wyczerpanym budzetem pytan wystepuja",
             turBudzetu > 0, "tur: " + turBudzetu + "/300");
        T.EqI("kontrola: wyczerpany budzet pytan nadal daje RoundBudgetExhausted", zlaEtykietaBudzetu, 0);
    }
}
