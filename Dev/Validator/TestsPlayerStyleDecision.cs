using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.PlayerModel;
using ProceduralNarrator.Core.Tension;
using ProceduralNarrator.Core.Util;

/// <summary>
/// TEST 14 (S3) - styl gracza w DECYZJI: 14g (wartosc stylu kandydatow), 14h (premia stylu tylko w koncowym
/// wyborze: brama, prog, pasmo i Utility bez zmian), 14k (warunek i katalog lukow, naprawa SafeId),
/// 14m (linia P z pamiecia stylu), 14n (kolumny danych v9).
/// </summary>
static class TestsPlayerStyleDecision
{
    public static void Run(XmlConfig cfg, EventComposer composer)
    {
        PlayerStyleParams p = cfg.playerStyle == null ? PlayerStyleParams.Default() : cfg.playerStyle.Clone();
        p.Sanitize();
        Test14g(p);
        Test14h(cfg, p, composer);
        Test14k();
        Test14m();
        Test14n(cfg, p);
    }

    // Profil wzgledny fikstury: z = (0.8, 0.5, 0.5, 0.4), srednia 0.55, skala z XML.
    static StyleReading Odczyt(PlayerStyleParams p, bool reaktywnoscZnana = true)
    {
        return PlayerStyleModel.FromVector(new[] { 0.8f, 0.5f, 0.5f, 0.4f },
                                           new[] { true, true, true, reaktywnoscZnana }, p.capacityDays, p);
    }

    static StyleWeights Wagi(float w, float g, float e, float r)
    {
        return new StyleWeights { walka = w, gospodarka = g, ekspansja = e, reaktywnosc = r };
    }

    /// <summary>Zdarzenie z klockiem akcji o danych wagach i klockiem aktora (niezerowe wagi = pulapka).</summary>
    static ComposedEvent Zd(string akcja, Valence v, StyleWeights wagi, string sig, StyleWeights wagiAktora = null)
    {
        var e = new ComposedEvent { ActionBlockId = akcja, ActionPayload = akcja, Signature = sig, Valence = v, Theme = Theme.Raid };
        e.Blocks.Add(new Block { Id = "PN_Aktor_Test", Type = BlockType.Actor, StyleWeights = wagiAktora ?? new StyleWeights() });
        e.Blocks.Add(new Block { Id = akcja, Type = BlockType.Action, StyleWeights = wagi });
        return e;
    }

    /// <summary>Niezalezny wzor dopasowania: srednia wazona profilu wzglednego po ZNANYCH cechach.</summary>
    static double Dopasowanie(StyleReading r, StyleWeights w)
    {
        double sw = 0, swc = 0;
        for (int d = 0; d < 4; d++)
        {
            float wd = w.Get((StyleDimension)d);
            if (!r.Known[d] || wd <= 0) continue;
            sw += wd;
            swc += wd * r.C[d];
        }
        return sw > 0 ? swc / sw : 0;
    }

    // ------------------------------------------------------------------ 14g

    static void Test14g(PlayerStyleParams p)
    {
        T.Section("TEST 14g - wartosc stylu kandydatow (StyleFocus)");
        StyleReading r = Odczyt(p);
        T.Ok("STRAZNIK: profil wzgledny ma rozne znaki (Walka mocna, Reaktywnosc slaba)", r.C[0] > 0 && r.C[3] < 0,
             "c=" + string.Join("/", r.C.Select(x => T.F(x))));
        StyleWeights napad = Wagi(1, 0, 0, 0.5f), dar = Wagi(0, 1, 0, 0);
        StyleFocus eskal = StyleFocus.Build(r, 0f, Intent.Escalate, p);
        float d = StyleDirection.Compute(0f, Intent.Escalate, p);
        T.Ok("STRAZNIK: kierunek w eskalacji rozny od 0 i od 1", d != 0f && d != 1f, "d=" + T.F(d));

        string why;
        double aN = Dopasowanie(r, napad);
        T.Eq("zagrozenie w eskalacji: v = d * a (a liczone niezaleznie)", eskal.ValueFor(Zd("N", Valence.Negative, napad, "N1"), out why),
             Math.Max(-1, Math.Min(1, d * aN)), 1e-5);
        T.Eq("dar w eskalacji: walencja niezgodna -> 0", eskal.ValueFor(Zd("Z", Valence.Positive, dar, "Z1"), out why), 0.0, 0.0);
        StyleFocus oddech = StyleFocus.Build(r, 0f, Intent.Breathe, p);
        T.Eq("zagrozenie w oddechu (i kryzysie): walencja niezgodna -> 0", oddech.ValueFor(Zd("N", Valence.Negative, napad, "N1"), out why), 0.0, 0.0);
        T.Eq("dar w oddechu: v = d * a", oddech.ValueFor(Zd("Z", Valence.Positive, dar, "Z1"), out why),
             StyleDirection.Compute(0f, Intent.Breathe, p) * Dopasowanie(r, dar), 1e-5);
        StyleFocus utrzym = StyleFocus.Build(r, 1f, Intent.Hold, p);
        T.Ok("w utrzymaniu styl dziala na obie walencje",
             utrzym.ValueFor(Zd("N", Valence.Negative, napad, "N1"), out why) != 0f && utrzym.ValueFor(Zd("Z", Valence.Positive, dar, "Z1"), out why) != 0f, null);
        T.Ok("zdarzenie neutralne dziala w kazdej intencji",
             eskal.ValueFor(Zd("U", Valence.Neutral, Wagi(0, 0, 1, 0), "U1"), out why) != 0f
             && oddech.ValueFor(Zd("U", Valence.Neutral, Wagi(0, 0, 1, 0), "U1"), out why) != 0f, null);

        T.Eq("liczy sie WYLACZNIE klocek akcji (aktor z waga Walki nie zmienia wyniku)",
             eskal.ValueFor(Zd("Z", Valence.Negative, dar, "Z2", Wagi(1, 0, 0, 0)), out why),
             d * Dopasowanie(r, dar), 1e-5);
        StyleReading bezR = Odczyt(p, false);
        StyleFocus fBezR = StyleFocus.Build(bezR, 0f, Intent.Escalate, p);
        T.Eq("nieznana cecha pominieta w dopasowaniu (Reaktywnosc nieznana)", fBezR.ValueFor(Zd("N", Valence.Negative, napad, "N1"), out why),
             Math.Max(-1, Math.Min(1, d * Dopasowanie(bezR, napad))), 1e-5);
        T.Ok("STRAZNIK: nieznana cecha zmienia wynik (fikstura rozroznia)", Math.Abs(Dopasowanie(bezR, napad) - aN) > 0.05, null);

        // Apply: te same wartosci dla wariantow jednej akcji; PASS i zawetowani bez stylu.
        var lista = new List<ScoredCandidate>
        {
            new ScoredCandidate { Event = Zd("N", Valence.Negative, napad, "N1"), Utility = 0.7f },
            new ScoredCandidate { Event = Zd("N", Valence.Negative, napad, "N2"), Utility = 0.6f },
            new ScoredCandidate { Event = Zd("Z", Valence.Negative, dar, "Z1"), Utility = 0.6f, Vetoed = true },
            new ScoredCandidate { IsPass = true, Utility = 0.5f }
        };
        eskal.Apply(lista);
        T.Ok("Apply NIE zmienia Utility (styl tylko w koncowym wyborze)",
             lista[0].Utility == 0.7f && lista[1].Utility == 0.6f && lista[2].Utility == 0.6f && lista[3].Utility == 0.5f, null);
        T.Ok("warianty jednej akcji maja te sama wartosc stylu", lista[0].StyleApplied && lista[0].StyleValue == lista[1].StyleValue, null);
        T.Ok("zawetowany i PASS bez stylu", !lista[2].StyleApplied && !lista[3].StyleApplied && lista[2].StyleValue == 0f, null);
        T.EqI("NonZero liczy kandydatow z niezerowym stylem", eskal.NonZero, 2);

        StyleReading rozgrz = PlayerStyleModel.FromVector(new[] { 0.8f, 0.5f, 0.5f, 0.4f }, null, p.warmupDays - 1, p);
        StyleFocus fr = StyleFocus.Build(rozgrz, 0f, Intent.Escalate, p);
        fr.Apply(lista);
        T.Ok("styl w rozgrzewce: nikt nie dostaje wartosci, fokus niezastosowany",
             !fr.Applied && lista.All(k => !k.StyleApplied && k.StyleValue == 0f), null);
    }

    // ------------------------------------------------------------------ 14h

    static ScoredCandidate K(string akcja, string sig, float u, float styl)
    {
        return new ScoredCandidate
        {
            Event = new ComposedEvent { ActionBlockId = akcja, ActionPayload = akcja, Signature = sig, Theme = Theme.Raid, Valence = Valence.Negative },
            SortKey = sig, Utility = u, RawUtility = u, Factors = new List<FactorScore>(),
            StyleApplied = !float.IsNaN(styl), StyleValue = float.IsNaN(styl) ? 0f : styl
        };
    }

    static ScoredCandidate Cisza(float u)
    {
        return new ScoredCandidate { IsPass = true, SortKey = "PASS", Utility = u, RawUtility = u, Factors = new List<FactorScore>() };
    }

    static List<ScoredCandidate> Pula(bool zeStylem)
    {
        float brak = float.NaN;
        return new List<ScoredCandidate>
        {
            K("A", "A1", 0.70f, zeStylem ? -0.8f : brak), K("B", "B1", 0.66f, zeStylem ? 0.9f : brak),
            K("C", "C1", 0.60f, zeStylem ? 0f : brak), K("D", "D1", 0.54f, zeStylem ? 1f : brak),
            K("E", "E1", 0.52f, zeStylem ? -1f : brak), K("F", "F1", 0.30f, zeStylem ? 1f : brak)
        };
    }

    static void Test14h(XmlConfig cfg, PlayerStyleParams p, EventComposer composer)
    {
        T.Section("TEST 14h - premia stylu tylko w koncowym wyborze: brama, prog, pasmo i Utility bez zmian");
        var policy = new SelectionPolicy(cfg.Selection());
        float f = cfg.nearBestFraction;

        int zgodnych = 0, prob = 0;
        for (int ziarno = 0; ziarno < 200; ziarno++)
        {
            var db = policy.Select(Pula(false), Cisza(0.5f), new CountingRandom(ziarno), false, null, null);
            var dz = policy.Select(Pula(true), Cisza(0.5f), new CountingRandom(ziarno), false, null, null);
            prob++;
            if (db.BestUtility == dz.BestUtility && db.BandThreshold == dz.BandThreshold
                && db.GatePassProbability == dz.GatePassProbability && db.RandomDraws == dz.RandomDraws
                && db.IsPass == dz.IsPass && db.CountInSoftmax == dz.CountInSoftmax
                && db.CountBelowBand == dz.CountBelowBand && db.CountBelowCutoff == dz.CountBelowCutoff)
            {
                zgodnych++;
            }
        }
        T.EqI("200 ziaren: best, pasmo, pBrama, losowania, brama i liczniki IDENTYCZNE ze stylem i bez", zgodnych, prob);

        var p1 = Pula(true);
        var d1 = policy.Select(p1, Cisza(0.5f), new CountingRandom(1), false, null, null);
        Func<string, ScoredCandidate> po = s => p1.First(k => k.SortKey == s);
        T.Ok("kandydat ze stylem PONIZEJ PASMA bez premii i odrzucony", po("E1").StyleBonus == 0f && po("E1").Rejected == RejectionStage.NearBestBand,
             po("E1").Rejected.ToString());
        T.Ok("kandydat ze stylem PONIZEJ PROGU bez premii i odrzucony", po("F1").StyleBonus == 0f && po("F1").Rejected == RejectionStage.QualityCutoff,
             po("F1").Rejected.ToString());
        T.Ok("STRAZNIK: w pasmie jest kandydat ze stylem 0 (C1)", po("C1").StyleApplied && po("C1").StyleValue == 0f
             && po("C1").Rejected == RejectionStage.None, null);
        T.EqI("stylWPasmie = kandydaci w pasmie z NIEZEROWYM stylem (A, B, D - bez C)", d1.StyleNonZeroInBand, 3);
        T.Eq("suma p po puli i PASS = 1 takze ze stylem", p1.Sum(k => (double)k.SelectionProbability) + d1.PassCandidate.SelectionProbability, 1.0, 1e-4);

        float best = 0.8f;
        var pm = new List<ScoredCandidate> { K("A", "A1", best, float.NaN), K("M", "M1", f * best, 0.5f) };
        policy.Select(pm, Cisza(0.2f), new CountingRandom(3), false, null, null);
        var m = pm.First(k => k.SortKey == "M1");
        T.Eq("premia stylu = (1 - nearBestFraction) * best * v (f z XML = " + T.F(f) + ")", m.StyleBonus, (1f - f) * best * 0.5f, 1e-6);
        T.Ok("kandydat bez stylu nie dostaje premii", pm[0].StyleBonus == 0f, null);

        var ps = new List<ScoredCandidate> { K("A", "A1", best, float.NaN), K("M", "M1", f * best, 0.5f) };
        ps[1].ArcValue = 1f;
        policy.Select(ps, Cisza(0.2f), new CountingRandom(3), false, null, null);
        T.Eq("premia lukowa i DODATNIA premia stylu SUMUJA sie w SelectionScore", ps[1].SelectionScore,
             ps[1].Utility + (1f - f) * best * 1f + (1f - f) * best * 0.5f, 1e-5);

        // LUK MA PIERWSZENSTWO (decyzja autora po przegladzie S8): kandydat z faza luku nie dostaje UJEMNEJ premii stylu;
        // kandydat bez luku - dostaje.
        var pu = new List<ScoredCandidate> { K("A", "A1", best, float.NaN), K("M", "M1", f * best, -0.6f), K("N", "N1", f * best + 0.01f, -0.6f) };
        pu[1].ArcValue = 1f;
        var dpu = policy.Select(pu, Cisza(0.2f), new CountingRandom(3), false, null, null);
        T.Ok("STRAZNIK: oba kandydaci w pasmie z wartoscia stylu -0.6", pu[1].StyleApplied && pu[2].StyleApplied
             && pu[1].Rejected == RejectionStage.None && pu[2].Rejected == RejectionStage.None, null);
        T.Eq("kandydat z faza luku: ujemna premia stylu STLUMIONA (0)", pu[1].StyleBonus, 0.0, 0.0);
        T.Eq("...a premia luku zostaje", pu[1].ArcBonus, (1f - f) * best, 1e-6);
        T.Eq("kandydat bez luku: ujemna premia stylu = (1 - f) * best * v", pu[2].StyleBonus, (1f - f) * best * -0.6f, 1e-6);
        T.EqI("stylWPasmie liczy WARTOSCI stylu (tlumiona tez)", dpu.StyleNonZeroInBand, 2);

        // Etap B2 (wariant) bez zmian: jednakowa wartosc stylu dla wariantow jednej akcji nie zmienia ich proporcji.
        var w0 = new List<ScoredCandidate> { K("X", "X1", 0.70f, float.NaN), K("X", "X2", 0.66f, float.NaN), K("Y", "Y1", 0.68f, float.NaN) };
        var w1 = new List<ScoredCandidate> { K("X", "X1", 0.70f, 0.6f), K("X", "X2", 0.66f, 0.6f), K("Y", "Y1", 0.68f, -0.2f) };
        policy.Select(w0, Cisza(0.1f), new CountingRandom(5), false, null, null);
        policy.Select(w1, Cisza(0.1f), new CountingRandom(5), false, null, null);
        // Tolerancja: kwantyzacja rozkladu 1e-6 plus ulp float32 razy 1/T - iloraz dwoch p z tej samej grupy.
        T.Eq("proporcja wariantow jednej akcji (B2) identyczna ze stylem i bez",
             w1[0].SelectionProbability / w1[1].SelectionProbability, w0[0].SelectionProbability / w0[1].SelectionProbability, 1e-3);
        T.Ok("...a akcja X zyskuje wobec Y (B1)", w1[0].SelectionProbability + w1[1].SelectionProbability > w0[0].SelectionProbability + w0[1].SelectionProbability, null);

        int wygranych = 0, prob2 = 0;
        double pX = 0;
        for (int ziarno = 0; ziarno < 3000; ziarno++)
        {
            var pw = new List<ScoredCandidate> { K("X", "X1", 0.70f, -0.5f), K("Y", "Y1", 0.66f, 1f) };
            var dw = policy.Select(pw, Cisza(0.0f), new CountingRandom(ziarno), false, null, null);
            if (dw.IsPass) continue;
            prob2++;
            pX = pw[1].SelectionProbability / (1.0 - dw.GatePassProbability);
            if (dw.Winner.SortKey == "Y1") wygranych++;
        }
        T.Ok("STRAZNIK: proby z wyborem zdarzenia", prob2 > 2000, "prob " + prob2);
        T.Eq("czestosc wygranej akcji ze stylem = jej raportowane p (3000 ziaren)", (double)wygranych / prob2, pX, 0.03);

        // Premia jest wlasnoscia RUNDY: kandydat, ktory wypadl z pasma w kolejnym wywolaniu, traci premie.
        var pr = new List<ScoredCandidate> { K("A", "A1", 0.70f, float.NaN), K("B", "B1", 0.66f, 1f) };
        policy.Select(pr, Cisza(0.0f), new CountingRandom(7), false, null, null);
        T.Ok("STRAZNIK: w pierwszym wywolaniu B ma premie", pr[1].StyleBonus > 0f, null);
        pr[1].Utility = 0.36f;
        pr[1].RawUtility = 0.36f;
        policy.Select(pr, Cisza(0.0f), new CountingRandom(7), false, null, null);
        T.Eq("po wypadnieciu z pasma premia stylu wyzerowana", pr[1].StyleBonus, 0.0, 0.0);

        // Na PRAWDZIWYM katalogu: mocne strony w snapshocie nie zmieniaja ani puli, ani Utility.
        string wzor = null;
        int sprawdzonych = 0;
        foreach (string mocne in new[] { "", ";Walka;", ";Gospodarka;", ";Ekspansja;", ";Reaktywnosc;", ";Walka;Ekspansja;" })
        {
            string odcisk = Odcisk(cfg, composer, mocne);
            sprawdzonych++;
            if (wzor == null) wzor = odcisk;
            T.Ok("prawdziwy katalog: sygnatury, m, K i Utility identyczne przy mocnych stronach '" + mocne + "'",
                 odcisk == wzor, odcisk.Length > 160 ? odcisk.Substring(0, 160) : odcisk);
        }
        T.Ok("STRAZNIK: odcisk niepusty i porownano 6 wariantow", wzor != null && wzor.Length > 50 && sprawdzonych == 6, null);

        // Scorer naklada fokus stylu (jak fokus lukow): prawdziwy katalog, aktywny styl w kontekscie.
        List<ScoredCandidate> zeStylem = OcenZeStylem(cfg, composer, p);
        T.Ok("UtilityScorer.ScoreAll naklada fokus stylu (sa kandydaci z niezerowym stylem)",
             zeStylem.Any(k => k.StyleApplied && k.StyleValue != 0f), "kandydatow " + zeStylem.Count);
        T.Ok("...PASS i zawetowani bez stylu", zeStylem.Where(k => k.IsPass || k.Vetoed).All(k => !k.StyleApplied), null);

        // Planer: fokus stylu z intencji PO regule kryzysu.
        var model = new TensionModel(TensionParams.Default(), cfg.contrast);
        var kryzys = new WorldSnapshot { DaysPassed = 30, ColonistCount = 4, ColonistsOnMap = 4, AcuteDownedCount = 2 };
        StyleReading r = Odczyt(p);
        TurnPlan pk = TurnPlanner.Plan(model, cfg.crisis, new EventHistory(), kryzys, 30f, null, null, null, r, -1f, p);
        Intent surowa = IntentSelector.Select(pk.Tension.Tension, model.Parameters).Intent;
        T.Ok("STRAZNIK: intencja przed kryzysem rozna od oddechu", surowa != Intent.Breathe, surowa.ToString());
        T.Ok("kryzys: fokus stylu z intencja oddechu", pk.Context.StyleFocus != null && pk.Context.StyleFocus.Intent == Intent.Breathe
             && pk.Context.StyleFocus.Direction == StyleDirection.Compute(-1f, Intent.Breathe, p),
             pk.Context.StyleFocus == null ? "brak fokusu" : T.F(pk.Context.StyleFocus.Direction));
        TurnPlan bez = TurnPlanner.Plan(model, cfg.crisis, new EventHistory(), kryzys, 30f, null, null, null, null, 1f, p);
        T.Ok("brak odczytu stylu = fokus pusty (warstwa nieobecna)", bez.Context.StyleFocus == null, null);
    }

    static List<ScoredCandidate> OcenZeStylem(XmlConfig cfg, EventComposer composer, PlayerStyleParams p)
    {
        var s = new WorldSnapshot
        {
            DaysPassed = 40, ColonistCount = 6, ColonistsOnMap = 6, ColonyWealth = 40000, WealthRelative = 1.5f,
            MountainRoofCellsNearColony = 250, HasHostileFaction = true, Season = 2, IsNight = true,
            WildAnimalCount = 8, MaddenableAnimalCount = 6, ThreatPoints = 600f, DaysSinceLastEvent = 6f,
            KidnappedColonistCount = 2, HasPoweredCommsConsole = true
        };
        var gen = new CandidateGenerator(composer);
        var scorer = new UtilityScorer(TestsDecision.CzynnikiZdarzen(), cfg.weights,
                                       UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, cfg.vetoContextFitBelow);
        CandidateSet cs = gen.Generate(new EventRecipe(), s, new SeededRandom(1), cfg.candidateBudget);
        var ctx = DecisionContext.Create(s, new EventHistory(), 40f, Intent.Escalate, 0f, 0.3f, false);
        ctx.StyleFocus = StyleFocus.Build(Odczyt(p), 0f, Intent.Escalate, p);
        return scorer.ScoreAll(cs.Candidates, ctx);
    }

    static string Odcisk(XmlConfig cfg, EventComposer composer, string mocne)
    {
        var s = new WorldSnapshot
        {
            DaysPassed = 40, ColonistCount = 6, ColonistsOnMap = 6, ColonyWealth = 40000, WealthRelative = 1.5f,
            MountainRoofCellsNearColony = 250, HasHostileFaction = true, Season = 2, IsNight = true,
            WildAnimalCount = 8, MaddenableAnimalCount = 6, ThreatPoints = 600f, DaysSinceLastEvent = 6f,
            KidnappedColonistCount = 2, HasPoweredCommsConsole = true, StyleStrongSides = mocne
        };
        var gen = new CandidateGenerator(composer);
        var scorer = new UtilityScorer(TestsDecision.CzynnikiZdarzen(), cfg.weights,
                                       UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, cfg.vetoContextFitBelow);
        CandidateSet cs = gen.Generate(new EventRecipe(), s, new SeededRandom(1), cfg.candidateBudget);
        var ctx = DecisionContext.Create(s, new EventHistory(), 40f, Intent.Escalate, 0f, 0.3f, false);
        var oc = scorer.ScoreAll(cs.Candidates, ctx);
        return "m=" + cs.ActionCount + ";K=" + cs.PerActionQuota + ";" + string.Join(",",
            oc.Select(k => (k.Event == null ? "PASS" : k.Event.Signature) + ":" + k.Utility.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
    }

    // ------------------------------------------------------------------ 14k

    static ArcDefinition Luk(string defName, NarrativeCondition warunek)
    {
        var a = new ArcDefinition { defName = defName, label = "test" };
        a.startConditions = new List<NarrativeCondition> { warunek };
        a.phases = new List<ArcPhase>
        {
            new ArcPhase { id = "Zasiew", kind = ArcPhaseKind.Seed, expectations = new List<ArcExpectation> { new ArcExpectation { valences = new List<Valence> { Valence.Negative } } } },
            new ArcPhase { id = "Rozwiazanie", kind = ArcPhaseKind.Resolution, maxDays = 10, expectations = new List<ArcExpectation> { new ArcExpectation { valences = new List<Valence> { Valence.Positive } } } }
        };
        return a;
    }

    static void Test14k()
    {
        T.Section("TEST 14k - warunek Cond_StylMocnaStrona i katalog lukow (w tym naprawa SafeId z S6)");
        var s = new WorldSnapshot { StyleStrongSides = ";Walka;Ekspansja;" };
        T.Ok("mocna strona obecna -> spelniony", new Cond_StylMocnaStrona { dimension = "Ekspansja" }.IsMet(s), null);
        T.Ok("cecha niebedaca mocna strona -> niespelniony", !new Cond_StylMocnaStrona { dimension = "Gospodarka" }.IsMet(s), null);
        T.Ok("required=false odwraca", new Cond_StylMocnaStrona { dimension = "Gospodarka", required = false }.IsMet(s)
             && !new Cond_StylMocnaStrona { dimension = "Walka", required = false }.IsMet(s), null);
        T.Ok("pusty zbior (rozgrzewka) -> niespelniony", !new Cond_StylMocnaStrona { dimension = "Walka" }.IsMet(new WorldSnapshot()), null);
        T.Ok("nieznana cecha -> niespelniony", !new Cond_StylMocnaStrona { dimension = "walka" }.IsMet(s), null);

        List<string> pr;
        var ok = ArcCatalog.Build(new[] { Luk("PN_Luk_StylTest", new Cond_StylMocnaStrona { dimension = "Walka" }) }, out pr);
        T.EqI("STRAZNIK: poprawny luk ze stylem przyjety", ok.Arcs.Count, 1);
        var zly = ArcCatalog.Build(new[] { Luk("PN_Luk_StylTest", new Cond_StylMocnaStrona { dimension = "Walkaa" }) }, out pr);
        T.Ok("nieznana cecha w warunku -> luk odrzucony", zly.Arcs.Count == 0 && pr.Any(x => x.Contains("nieznana cecha")), string.Join(" | ", pr));
        var pusty = ArcCatalog.Build(new[] { Luk("PN_Luk_StylTest", new Cond_StylMocnaStrona()) }, out pr);
        T.Ok("brak cechy w warunku -> luk odrzucony", pusty.Arcs.Count == 0, string.Join(" | ", pr));
        var nazwa = ArcCatalog.Build(new[] { Luk("PN Luk!", new Cond_StylMocnaStrona { dimension = "Walka" }) }, out pr);
        T.Ok("luk Z WARUNKAMI STARTU i niebezpieczna nazwa -> odrzucony (regresja S6)", nazwa.Arcs.Count == 0 && pr.Any(x => x.Contains("defName")),
             string.Join(" | ", pr));
    }

    // ------------------------------------------------------------------ 14m

    static void Test14m()
    {
        T.Section("TEST 14m - linia P: pamiec mocnych stron stylu z chwili decyzji");
        var pe = new PendingExecution { Tick = 900000, GameDay = 15f, DecisionIndex = 7, IncidentDefName = "RaidEnemy" };
        pe.CaptureDecisionMemory(new WorldSnapshot { TurnsSinceThemes = ";Raid=1;", DaysSinceLastEvent = 1.5f, DaysPassed = 15, StyleStrongSides = ";Walka;Ekspansja;" });
        PendingExecution o;
        T.Ok("round-trip 19 pol z mocnymi stronami", PendingExecution.TryDecode(pe.Encode(), out o) && o.HasDecisionStyle
             && o.DecisionStyleStrongSides == ";Walka;Ekspansja;" && pe.Encode().Split('|').Length == PendingExecution.FieldCount, pe.Encode());
        var pusta = new PendingExecution { Tick = 1, DecisionIndex = 1, IncidentDefName = "X" };
        pusta.CaptureDecisionMemory(new WorldSnapshot { StyleStrongSides = "" });
        PendingExecution op;
        T.Ok("styl zapamietany bez mocnych stron != brak pamieci stylu", PendingExecution.TryDecode(pusta.Encode(), out op)
             && op.HasDecisionStyle && op.DecisionStyleStrongSides == "", pusta.Encode());
        string[] pola = pe.Encode().Split('|');
        PendingExecution o18, o15;
        T.Ok("linia 18 pol (S6) wczytuje sie bez pamieci stylu", PendingExecution.TryDecode(string.Join("|", pola.Take(PendingExecution.S6FieldCount)), out o18)
             && o18.HasDecisionMemory && !o18.HasDecisionStyle, null);
        T.Ok("linia 15 pol wczytuje sie bez pamieci decyzji i stylu", PendingExecution.TryDecode(string.Join("|", pola.Take(PendingExecution.LegacyFieldCount)), out o15)
             && !o15.HasDecisionMemory && !o15.HasDecisionStyle, null);
        PendingExecution oz;
        T.Ok("zniekszalcone pole stylu (bez srednikow) odrzuca cala linie",
             !PendingExecution.TryDecode(string.Join("|", pola.Take(PendingExecution.S6FieldCount)) + "|Walka", out oz), null);
        var spozniony = new WorldSnapshot { StyleStrongSides = ";Gospodarka;" };
        o.ApplyDecisionMemoryTo(spozniony);
        T.EqS("nakladka sciezki spoznionej przywraca mocne strony z decyzji", spozniony.StyleStrongSides, ";Walka;Ekspansja;");
        var spozniony18 = new WorldSnapshot { StyleStrongSides = ";Gospodarka;" };
        o18.ApplyDecisionMemoryTo(spozniony18);
        T.EqS("linia bez pamieci stylu nie rusza mocnych stron snapshotu", spozniony18.StyleStrongSides, ";Gospodarka;");
    }

    // ------------------------------------------------------------------ 14n

    static void Test14n(XmlConfig cfg, PlayerStyleParams p)
    {
        T.Section("TEST 14n - kolumny danych v9: preambula stylu i ogon decyzji");
        StyleReading akt = PlayerStyleModel.FromVector(new[] { 0.9f, 0.5f, 0.5f, 0.5f }, new[] { true, true, true, false }, 30, p);
        var kol = StyleFocus.Build(akt, 0f, Intent.Hold, p).Preamble();
        T.Ok("aktywny: dni, aktywny, znane cechy wypelnione, nieznana pusta",
             kol.Dni == "30" && kol.Aktywny == "true" && kol.Z[0] == "0.900" && kol.Z[3] == "", string.Join("|", kol.Z));
        T.Ok("aktywny: mocne strony i etykieta", kol.Mocne == "Walka" && kol.Etykieta == akt.Label && kol.Etykieta.Length > 0, kol.Mocne + " " + kol.Etykieta);
        StyleReading rowny = PlayerStyleModel.FromVector(new[] { 0.5f, 0.5f, 0.5f, 0.5f }, null, 30, p);
        T.EqS("aktywny bez mocnych stron: '-'", StyleFocus.Build(rowny, 0f, Intent.Hold, p).Preamble().Mocne, "-");
        StyleReading roz = PlayerStyleModel.FromVector(new[] { 0.9f, 0.5f, 0.5f, 0.5f }, null, p.warmupDays - 1, p);
        var kr = StyleFocus.Build(roz, -1f, Intent.Escalate, p).Preamble();
        T.Ok("rozgrzewka: cechy, mocne i etykieta puste, kierunek jest",
             kr.Aktywny == "false" && kr.Z.All(x => x == "") && kr.Mocne == "" && kr.Etykieta == "" && kr.Kierunek == "-1.000",
             kr.Kierunek);

        var policy = new SelectionPolicy(cfg.Selection());
        var pula = new List<ScoredCandidate> { K("A", "A1", 0.70f, 0.5f), K("B", "B1", 0.30f, 0f) };
        var d = policy.Select(pula, Cisza(0.0f), new CountingRandom(2), false, null, null);
        var pola = d.ToDataFragment().Split(new[] { "; " }, StringSplitOptions.None).ToDictionary(x => x.Split('=')[0], x => x.Substring(x.IndexOf('=') + 1));
        Func<string, double> num = s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        T.Ok("STRAZNIK: wygralo zdarzenie w jednej rundzie", !d.IsPass && d.RandomDraws == 3, d.RandomDraws.ToString());
        T.Ok("zwyciezca ze stylem: stylWartosc i stylWPasmie wypelnione", pola["stylWartosc"] == "0.500" && pola["stylWPasmie"] == "1", d.ToDataFragment());
        T.Eq("premiaStylu = (best - pasmo) * stylWartosc", num(pola["premiaStylu"]), (d.BestUtility - d.BandThreshold) * 0.5, 1e-3);

        var bezStylu = new List<ScoredCandidate> { K("A", "A1", 0.70f, float.NaN) };
        var db = policy.Select(bezStylu, Cisza(0.0f), new CountingRandom(2), false, null, null);
        var pb = db.ToDataFragment().Split(new[] { "; " }, StringSplitOptions.None).ToDictionary(x => x.Split('=')[0], x => x.Substring(x.IndexOf('=') + 1));
        T.Ok("styl nieobecny: trzy kolumny puste", pb["stylWartosc"] == "" && pb["premiaStylu"] == "" && pb["stylWPasmie"] == "", db.ToDataFragment());
        var cisza = new List<ScoredCandidate> { K("A", "A1", 0.40f, 0.5f) };
        var dc = policy.Select(cisza, Cisza(1.0f), new CountingRandom(2), false, null, null);
        var pc = dc.ToDataFragment().Split(new[] { "; " }, StringSplitOptions.None).ToDictionary(x => x.Split('=')[0], x => x.Substring(x.IndexOf('=') + 1));
        // STRAZNIK (przeglad S8): ziarno ma dac PASS - inaczej asercja nizej jest pusta.
        T.Ok("STRAZNIK: fikstura konczy sie cisza (PASS)", dc.IsPass, dc.IsPass ? "PASS" : "zdarzenie");
        T.Ok("PASS: wartosc i premia stylu puste (brak zwyciezcy-zdarzenia)", !dc.IsPass || (pc["stylWartosc"] == "" && pc["premiaStylu"] == ""),
             dc.IsPass ? "PASS" : "zdarzenie");
    }
}
