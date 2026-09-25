using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Tension;
using ProceduralNarrator.Core.Util;

/// <summary>
/// TEST 11i-p - luk w warstwie decyzyjnej (etap S3): fokus i statusy faz, wartosc lukowa
/// kandydatow, premia w KONCOWYM wyborze (decyzja autora R4-1), niezaleznosc bramy, tozsamosc
/// z v6 bez luku, przeciazenie TurnPlanner, kolumny danych.
/// </summary>
static class TestsArcsScoring
{
    public static void Run(XmlConfig cfg, EventComposer composer)
    {
        Fokus(cfg);
        Wartosci(cfg, composer);
        Wybor(cfg);
        Planner(cfg);
        Kolumny(cfg);
    }

    static ArcCatalog Katalog(XmlConfig cfg)
    {
        List<string> p;
        return ArcCatalog.Build(cfg.arcDefs, out p);
    }

    static ArcInstance Inst(string arc, int nr, string faza, float wejscie, string frakcja = null)
    {
        return new ArcInstance { ArcId = arc, Number = nr, PhaseId = faza, OpenedDay = wejscie, PhaseEnteredDay = wejscie, BoundFactionId = frakcja };
    }

    // ---------------------------------------------------------------------------------
    //  Fokus: statusy faz
    // ---------------------------------------------------------------------------------
    static void Fokus(XmlConfig cfg)
    {
        T.Section("TEST 11i - fokus lukow: statusy faz (aktywna / czeka / niedojrzala)");
        var d = new ArcDirector(Katalog(cfg), cfg.arcs);
        var l = new ArcLedger();
        l.Active.Add(Inst("PN_Luk_Wendeta", 1, "Eskalacja", 20f, "F"));
        l.Active.Add(Inst("PN_Luk_GniewNatury", 2, "Rozwiazanie", 20f));

        Func<ArcFocus, string, string> status = (f, arc) => f.Entries.First(e => e.Arc.ArcId == arc).Status;
        var fe = d.BuildFocus(l, Intent.Escalate, 30f, null);
        T.EqS("Escalate: faza negatywna (Wendeta/Eskalacja) steruje", status(fe, "PN_Luk_Wendeta"), ArcFocus.StatusActive);
        T.EqS("Escalate: faza pozytywna (Gniew/Rozwiazanie) czeka", status(fe, "PN_Luk_GniewNatury"), ArcFocus.StatusWaiting);
        var fb = d.BuildFocus(l, Intent.Breathe, 30f, null);
        T.EqS("Breathe: faza negatywna czeka", status(fb, "PN_Luk_Wendeta"), ArcFocus.StatusWaiting);
        T.EqS("Breathe: faza pozytywna steruje", status(fb, "PN_Luk_GniewNatury"), ArcFocus.StatusActive);
        var fh = d.BuildFocus(l, Intent.Hold, 30f, null);
        T.Ok("Hold: obie fazy steruja", fh.Entries.All(e => e.Status == ArcFocus.StatusActive), fh.DataPhases());
        var fn = d.BuildFocus(l, Intent.Escalate, 21f, null);
        T.EqS("przed dojrzaloscia (1 d < 3 d Wendety) faza nie steruje", status(fn, "PN_Luk_Wendeta"), ArcFocus.StatusUnripe);
        T.EqS("kolumna lukFazy: luk:faza:status w kolejnosci instancji", fe.DataPhases(),
              "PN_Luk_Wendeta:Eskalacja:aktywna,PN_Luk_GniewNatury:Rozwiazanie:czeka");
        T.EqS("kolumna frakcjaLuku = frakcja zwiazana", fe.BoundFaction(), "F");
        T.Ok("budowa fokusu NIE rusza zegarow faz (czekanie nie zatrzymuje limitu - decyzja nr 2)",
             l.Active.All(a => a.PhaseEnteredDay == 20f), string.Join(",", l.Active.Select(a => a.PhaseEnteredDay)));
        var dOff = new ArcDirector(Katalog(cfg), new ArcParams { enabled = false, maxConcurrent = 2 });
        T.Ok("warstwa wylaczona -> fokus bez wpisow", dOff.BuildFocus(l, Intent.Hold, 30f, null).Entries.Count == 0, null);
    }

    // ---------------------------------------------------------------------------------
    //  Wartosc lukowa kandydatow (ScoreAll + ArcFocus.Apply)
    // ---------------------------------------------------------------------------------
    sealed class Uzytecznosc : IFactionUsability
    {
        public bool Wynik;
        public bool UsableAt(string factionId, IntensityLevel intensity) { return Wynik; }
    }

    static List<ScoredCandidate> Ocen(XmlConfig cfg, EventComposer composer, ArcFocus focus, Intent intencja)
    {
        return Ocen(cfg, composer, focus, intencja, cfg.vetoContextFitBelow);
    }

    static List<ScoredCandidate> Ocen(XmlConfig cfg, EventComposer composer, ArcFocus focus, Intent intencja, float weto)
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
                                       UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass, weto);
        CandidateSet cs = gen.Generate(new EventRecipe(), s, new SeededRandom(1), cfg.candidateBudget);
        var ctx = DecisionContext.Create(s, new EventHistory(), 40f, intencja, 0f, 0.3f, false);
        ctx.ArcFocus = focus;
        return scorer.ScoreAll(cs.Candidates, ctx);
    }

    static void Wartosci(XmlConfig cfg, EventComposer composer)
    {
        T.Section("TEST 11o - wartosc lukowa kandydatow: binarna, max po lukach, wstrzymanie, weto, frakcja");
        var d = new ArcDirector(Katalog(cfg), cfg.arcs);
        var l = new ArcLedger();
        l.Active.Add(Inst("PN_Luk_Wendeta", 1, "Eskalacja", 20f, "F"));

        var f = d.BuildFocus(l, Intent.Escalate, 30f, new Uzytecznosc { Wynik = true });
        var oc = Ocen(cfg, composer, f, Intent.Escalate);
        var napady = oc.Where(k => !k.Vetoed && k.Event.ActionBlockId == "PN_Akcja_Napad").ToList();
        var inne = oc.Where(k => !k.Vetoed && k.Event.ActionBlockId != "PN_Akcja_Napad").ToList();
        T.Ok("STRAZNIK: sa napady i inne zdarzenia w puli", napady.Count > 0 && inne.Count > 0, "napadow " + napady.Count + ", innych " + inne.Count);
        T.Ok("napady (ta sama frakcja F, uzyteczna) maja wartosc lukowa 1", napady.All(k => k.ArcValue == 1f), string.Join(",", napady.Select(k => k.ArcValue)));
        T.Ok("inne zdarzenia maja 0 (pomiar, nie brak pomiaru)", inne.All(k => k.ArcValue == 0f), null);
        // Weto podniesione do 0.6, zeby w puli NA PEWNO byli zawetowani - takze wsrod napadow.
        // Przy wecie z XML (0.15) pula moze nie miec ani jednego i asercja przechodzilaby trywialnie.
        var ocw = Ocen(cfg, composer, d.BuildFocus(l, Intent.Escalate, 30f, new Uzytecznosc { Wynik = true }), Intent.Escalate, 0.6f);
        T.Ok("STRAZNIK: przy wecie 0.6 sa zawetowane napady i niezawetowani kandydaci",
             ocw.Any(k => k.Vetoed && k.Event.ActionBlockId == "PN_Akcja_Napad") && ocw.Any(k => !k.Vetoed),
             "zawetowanych napadow " + ocw.Count(k => k.Vetoed && k.Event.ActionBlockId == "PN_Akcja_Napad"));
        T.Ok("zawetowani maja -1 (takze pasujace napady)", ocw.Where(k => k.Vetoed).All(k => k.ArcValue == -1f), null);
        T.Ok("fokus zastosowany, dopasowanych = liczba niezawetowanych napadow", f.Applied && f.Matched == napady.Count, "dopasowanych " + f.Matched);
        T.Ok("napadom zostanie ustawiona frakcja F", napady.All(k => f.FactionToBind(k.Event) == "F"), null);
        T.Ok("innym zdarzeniom - zadna", inne.All(k => f.FactionToBind(k.Event) == null), null);

        var fz = d.BuildFocus(l, Intent.Escalate, 30f, new Uzytecznosc { Wynik = false });
        var ocz = Ocen(cfg, composer, fz, Intent.Escalate);
        T.Ok("frakcja CHWILOWO nieuzyteczna: nic nie pasuje -> luk sie wstrzymuje (wszyscy -1)",
             !fz.Applied && ocz.All(k => k.ArcValue == -1f), "dopasowanych " + fz.Matched);
        T.Ok("...i zadnej frakcji do ustawienia", ocz.All(k => fz.FactionToBind(k.Event) == null), null);

        l.Active.Add(Inst("PN_Luk_Scigani", 2, "Kulminacja", 20f));
        var f2 = d.BuildFocus(l, Intent.Escalate, 30f, new Uzytecznosc { Wynik = true });
        var oc2 = Ocen(cfg, composer, f2, Intent.Escalate);
        var napady2 = oc2.Where(k => !k.Vetoed && k.Event.ActionBlockId == "PN_Akcja_Napad").ToList();
        T.Ok("dwa luki czekaja na napad: wartosc = MAKSIMUM (1), nie suma", napady2.Count > 0 && napady2.All(k => k.ArcValue == 1f),
             string.Join(",", napady2.Select(k => k.ArcValue)));
        T.Ok("...a frakcja F nadal wiazana (dopasowanie sameFaction Wendety)", napady2.All(k => f2.FactionToBind(k.Event) == "F"), null);

        var fb = d.BuildFocus(l, Intent.Breathe, 30f, null);
        var ocb = Ocen(cfg, composer, fb, Intent.Breathe);
        T.Ok("Breathe: fazy negatywne czekaja -> luk wstrzymany (wszyscy -1)", !fb.Applied && ocb.All(k => k.ArcValue == -1f), fb.DataPhases());

        // Zgodnosc walencji KANDYDATA z intencja: faza Rozwiazania Wendety (dowolne Positive) przy Hold.
        var lr = new ArcLedger();
        lr.Active.Add(Inst("PN_Luk_Wendeta", 1, "Rozwiazanie", 20f, "F"));
        var fr = d.BuildFocus(lr, Intent.Hold, 30f, null);
        var ocr = Ocen(cfg, composer, fr, Intent.Hold);
        T.Ok("Rozwiazanie przy Hold: pozytywne dostaja 1, negatywne 0",
             ocr.Where(k => !k.Vetoed).All(k => k.ArcValue == (k.Event.Valence == Valence.Positive ? 1f : 0f)), null);
        T.Ok("STRAZNIK: sa kandydaci pozytywni i negatywni", ocr.Any(k => !k.Vetoed && k.Event.Valence == Valence.Positive)
             && ocr.Any(k => !k.Vetoed && k.Event.Valence == Valence.Negative), null);

        // Faza NIEDOJRZALA nie steruje - sprawdzane na WARTOSCIACH, nie tylko na etykiecie statusu.
        // Osobna ksiega z SAMA Wendeta: w `l` siedza juz Scigani, ktorzy w dniu 21 sa dojrzali
        // (1 d) i slusznie steruja - test mowil by wtedy o dwoch lukach naraz.
        var lnd = new ArcLedger();
        lnd.Active.Add(Inst("PN_Luk_Wendeta", 1, "Eskalacja", 20f, "F"));
        var fnd = d.BuildFocus(lnd, Intent.Escalate, 21f, new Uzytecznosc { Wynik = true });
        var ocnd = Ocen(cfg, composer, fnd, Intent.Escalate);
        T.Ok("faza niedojrzala (dzien 21, Wendeta 3 d): brak wartosci lukowych (wszyscy -1)",
             !fnd.Applied && ocnd.All(k => k.ArcValue == -1f), fnd.DataPhases());

        // Zgodnosc walencji KANDYDATA z intencja: syntetyczna faza oczekujaca Negative LUB Positive
        // steruje przy Escalate (Negative zgodna), ale premie dostaja tylko kandydaci negatywni.
        // W prawdziwym katalogu kazda faza ma jedna walencje, wiec bez tego fixture'a regula
        // "kandydat musi byc zgodny z intencja" nie mialaby zadnego przypadku, ktory ja odroznia.
        var mieszana = new ArcDefinition
        {
            defName = "PN_Luk_Mieszany", exclusionGroup = "gm", cooldownDays = 1f,
            phases = new List<ArcPhase>
            {
                new ArcPhase { id = "Zasiew", kind = ArcPhaseKind.Seed, expectations = new List<ArcExpectation> { new ArcExpectation { valences = new List<Valence> { Valence.Negative } } } },
                new ArcPhase { id = "Eskalacja", kind = ArcPhaseKind.Escalation, maxDays = 20f,
                               expectations = new List<ArcExpectation> { new ArcExpectation { valences = new List<Valence> { Valence.Negative, Valence.Positive } } } },
                new ArcPhase { id = "Rozwiazanie", kind = ArcPhaseKind.Resolution, maxDays = 20f,
                               expectations = new List<ArcExpectation> { new ArcExpectation { valences = new List<Valence> { Valence.Positive } } } }
            }
        };
        List<string> pm;
        var dm = new ArcDirector(ArcCatalog.Build(new[] { mieszana }, out pm), cfg.arcs);
        var lm = new ArcLedger();
        lm.Active.Add(Inst("PN_Luk_Mieszany", 1, "Eskalacja", 20f));
        var fm = dm.BuildFocus(lm, Intent.Escalate, 30f, null);
        var ocm = Ocen(cfg, composer, fm, Intent.Escalate);
        T.Ok("STRAZNIK: faza mieszana steruje przy Escalate", fm.Entries.Count == 1 && fm.Entries[0].Active, fm.DataPhases());
        T.Ok("faza mieszana przy Escalate: negatywni 1, pozytywni i neutralni 0 (walencja KANDYDATA)",
             ocm.Where(k => !k.Vetoed).All(k => k.ArcValue == (k.Event.Valence == Valence.Negative ? 1f : 0f))
             && ocm.Any(k => !k.Vetoed && k.Event.Valence == Valence.Positive), null);

        var bezFokusu = Ocen(cfg, composer, null, Intent.Escalate);
        T.Ok("bez fokusu (brak lukow) wszyscy maja -1, SelectionScore == Utility",
             bezFokusu.All(k => k.ArcValue == -1f && k.SelectionScore == k.Utility), null);
    }

    // ---------------------------------------------------------------------------------
    //  Premia w koncowym wyborze - niezaleznosc bramy (decyzja R4-1)
    // ---------------------------------------------------------------------------------
    static ScoredCandidate K(string akcja, string sig, float u, float arc)
    {
        return new ScoredCandidate
        {
            Event = new ComposedEvent { ActionBlockId = akcja, ActionPayload = akcja, Signature = sig, Theme = Theme.Raid, Valence = Valence.Negative },
            SortKey = sig, Utility = u, RawUtility = u, ArcValue = arc, Factors = new List<FactorScore>()
        };
    }

    static ScoredCandidate Cisza(float u)
    {
        return new ScoredCandidate { IsPass = true, SortKey = "PASS", Utility = u, RawUtility = u, Factors = new List<FactorScore>() };
    }

    static List<ScoredCandidate> Pula(bool zLukiem)
    {
        float a = zLukiem ? 1f : -1f, z = zLukiem ? 0f : -1f;
        return new List<ScoredCandidate>
        {
            K("A", "A1", 0.70f, z), K("B", "B1", 0.66f, z), K("C", "C1", 0.60f, a),
            K("D", "D1", 0.54f, z), K("E", "E1", 0.52f, a), K("F", "F1", 0.30f, a)
        };
    }

    static void Wybor(XmlConfig cfg)
    {
        T.Section("TEST 11j - premia lukowa tylko w koncowym wyborze: brama, prog i pasmo bez zmian");
        var policy = new SelectionPolicy(cfg.Selection());
        float f = cfg.nearBestFraction;

        int zgodnychBram = 0, prob = 0;
        for (int ziarno = 0; ziarno < 200; ziarno++)
        {
            var bez = Pula(false);
            var z = Pula(true);
            var db = policy.Select(bez, Cisza(0.5f), new CountingRandom(ziarno), false, null, null);
            var dz = policy.Select(z, Cisza(0.5f), new CountingRandom(ziarno), false, null, null);
            prob++;
            if (db.BestUtility == dz.BestUtility && db.BandThreshold == dz.BandThreshold
                && db.GatePassProbability == dz.GatePassProbability && db.RandomDraws == dz.RandomDraws
                && db.IsPass == dz.IsPass && db.CountInSoftmax == dz.CountInSoftmax
                && db.CountBelowBand == dz.CountBelowBand && db.CountBelowCutoff == dz.CountBelowCutoff)
            {
                zgodnychBram++;
            }
        }
        T.EqI("200 ziaren: best, pasmo, pBrama, losowania, decyzja bramy i liczniki IDENTYCZNE z lukiem i bez", zgodnychBram, prob);

        var p0 = Pula(false);
        var p1 = Pula(true);
        var d0 = policy.Select(p0, Cisza(0.5f), new CountingRandom(1), false, null, null);
        var d1 = policy.Select(p1, Cisza(0.5f), new CountingRandom(1), false, null, null);
        Func<List<ScoredCandidate>, string, ScoredCandidate> po = (pl, s) => pl.First(k => k.SortKey == s);
        T.Ok("kandydat lukowy PONIZEJ PASMA zostaje odrzucony (luk nie wyciaga spoza pasma)",
             po(p1, "E1").Rejected == RejectionStage.NearBestBand && po(p1, "E1").SelectionProbability == 0f, po(p1, "E1").Rejected.ToString());
        T.Ok("kandydat lukowy PONIZEJ PROGU zostaje odrzucony", po(p1, "F1").Rejected == RejectionStage.QualityCutoff, po(p1, "F1").Rejected.ToString());
        T.Ok("kandydat lukowy W PASMIE ma wieksza szanse niz bez luku", po(p1, "C1").SelectionProbability > po(p0, "C1").SelectionProbability,
             T.F4(po(p0, "C1").SelectionProbability) + " -> " + T.F4(po(p1, "C1").SelectionProbability));
        T.Ok("...a niepasujacy lider mniejsza", po(p1, "A1").SelectionProbability < po(p0, "A1").SelectionProbability, null);
        double suma = p1.Sum(k => (double)k.SelectionProbability) + d1.PassCandidate.SelectionProbability;
        T.Eq("suma p po puli i PASS = 1 takze z lukiem", suma, 1.0, 1e-4);

        // Premia wyprowadzona z pasma: (1 - f) * best z XML; pasujacy z DNA pasma remisuje z najlepszym.
        float best = 0.8f;
        var pm = new List<ScoredCandidate> { K("A", "A1", best, 0f), K("M", "M1", f * best, 1f) };
        var dm = policy.Select(pm, Cisza(0.2f), new CountingRandom(3), false, null, null);
        var m = pm.First(k => k.SortKey == "M1");
        T.Eq("premia = (1 - nearBestFraction) * best (f z XML = " + T.F(f) + ")", m.ArcBonus, (1f - f) * best, 1e-6);
        T.Eq("pasujacy z dna pasma: SelectionScore == best (remis z liderem)", m.SelectionScore, best, 1e-6);
        T.Ok("niepasujacy nie dostaje premii", pm.First(k => k.SortKey == "A1").ArcBonus == 0f, null);

        // Dwa warianty JEDNEJ akcji: premia przechyla takze wybor wariantu (etap B2) i p laczne.
        var pw0 = new List<ScoredCandidate> { K("X", "X1", 0.70f, -1f), K("X", "X2", 0.66f, -1f) };
        var pw1 = new List<ScoredCandidate> { K("X", "X1", 0.70f, 0f), K("X", "X2", 0.66f, 1f) };
        policy.Select(pw0, Cisza(0.1f), new CountingRandom(5), false, null, null);
        policy.Select(pw1, Cisza(0.1f), new CountingRandom(5), false, null, null);
        T.Ok("bez luku lepszy wariant X1 ma wieksze p", pw0[0].SelectionProbability > pw0[1].SelectionProbability, null);
        T.Ok("z lukiem wariant lukowy X2 ma wieksze p (etap B2 i p laczne)", pw1[1].SelectionProbability > pw1[0].SelectionProbability,
             T.F4(pw1[0].SelectionProbability) + " vs " + T.F4(pw1[1].SelectionProbability));

        // LOSOWANIE zgodne z RAPORTOWANYM p: w etapie B2 wariant jest ciagniety z tego samego
        // rozkladu (SelectionScore), z ktorego liczy sie kolumna p. Sama asercja na p nie wystarcza -
        // raport i losowanie to dwa osobne miejsca kodu i mogly by sie rozjechac.
        int wygranychX2 = 0, prob2 = 0;
        double pX2 = 0;
        for (int ziarno = 0; ziarno < 3000; ziarno++)
        {
            var pw = new List<ScoredCandidate> { K("X", "X1", 0.70f, 0f), K("X", "X2", 0.66f, 1f) };
            var dw = policy.Select(pw, Cisza(0.0f), new CountingRandom(ziarno), false, null, null);
            if (dw.IsPass) continue;
            prob2++;
            pX2 = pw[1].SelectionProbability / (1.0 - dw.GatePassProbability);
            if (dw.Winner.SortKey == "X2") wygranychX2++;
        }
        T.Ok("STRAZNIK: proby z wyborem zdarzenia", prob2 > 2000, "prob " + prob2);
        T.Eq("czestosc wygranej wariantu lukowego = jego raportowane p (3000 ziaren)", (double)wygranychX2 / prob2, pX2, 0.03);

        // Dwie akcje: ocena AKCJI = maksimum SelectionScore (etap B1).
        var pa = new List<ScoredCandidate> { K("A", "A1", 0.70f, 0f), K("B", "B1", 0.66f, 1f) };
        policy.Select(pa, Cisza(0.1f), new CountingRandom(5), false, null, null);
        T.Ok("akcja lukowa B wyprzedza lepsza bazowo A w etapie B1", pa[1].SelectionProbability > pa[0].SelectionProbability, null);

        // Ocena akcji = maksimum po WSZYSTKICH wariantach, nie po pierwszym: X ma lidera bazowego
        // 0.70 bez luku i wariant lukowy 0.66 (+premia 0.18 = 0.84), Y ma 0.72 bez luku. Poprawnie
        // X > Y; gdyby grupa brala tylko pierwszy wariant (0.70), wygrywalby Y.
        var pg = new List<ScoredCandidate> { K("Y", "Y1", 0.72f, 0f), K("X", "X1", 0.70f, 0f), K("X", "X2", 0.66f, 1f) };
        policy.Select(pg, Cisza(0.0f), new CountingRandom(5), false, null, null);
        T.Ok("ocena akcji = maksimum SelectionScore po wariantach (lukowy X2 podnosi akcje X ponad Y)",
             pg[1].SelectionProbability + pg[2].SelectionProbability > pg[0].SelectionProbability,
             "X " + T.F4(pg[1].SelectionProbability + pg[2].SelectionProbability) + " vs Y " + T.F4(pg[0].SelectionProbability));

        // Tozsamosc z v6: wartosci -1 i 0 (luk wstrzymany / nic nie pasuje) daja te same decyzje.
        var r = new SeededRandom(77);
        int roznic = 0, porownan = 0;
        for (int i = 0; i < 200; i++)
        {
            var a = new List<ScoredCandidate>();
            var b = new List<ScoredCandidate>();
            int n = 2 + r.Next(8);
            for (int j = 0; j < n; j++)
            {
                float u = 0.3f + r.Next(600) / 1000f;
                string akcja = "A" + r.Next(4);
                a.Add(K(akcja, akcja + "_" + j, u, -1f));
                b.Add(K(akcja, akcja + "_" + j, u, 0f));
            }
            var da = policy.Select(a, Cisza(0.5f), new CountingRandom(i), false, null, null);
            var db2 = policy.Select(b, Cisza(0.5f), new CountingRandom(i), false, null, null);
            porownan++;
            if (da.Winner.SortKey != db2.Winner.SortKey || da.Winner.SelectionProbability != db2.Winner.SelectionProbability
                || da.RandomDraws != db2.RandomDraws)
            {
                roznic++;
            }
        }
        T.EqI("200 losowych pul: wartosc -1 (brak luku) i 0 (nic nie pasuje) daja IDENTYCZNE decyzje", roznic, 0);
        T.Ok("STRAZNIK: porownano pule", porownan == 200, null);
    }

    // ---------------------------------------------------------------------------------
    //  TurnPlanner: przeciazenie z lukami
    // ---------------------------------------------------------------------------------
    static void Planner(XmlConfig cfg)
    {
        T.Section("TEST 11k - TurnPlanner: fokus lukow w DecisionContext");
        var d = new ArcDirector(Katalog(cfg), cfg.arcs);
        var l = new ArcLedger();
        l.Active.Add(Inst("PN_Luk_Wendeta", 1, "Eskalacja", 20f, "F"));
        var model = new TensionModel(TensionParams.Default(), cfg.contrast);
        var spokoj = new WorldSnapshot { DaysPassed = 30, ColonistCount = 4, ColonistsOnMap = 4 };
        TurnPlan plan = TurnPlanner.Plan(model, cfg.crisis, new EventHistory(), spokoj, 30f, d, l, null);
        T.Ok("przeciazenie z lukami ustawia fokus w kontekscie", plan.Context.ArcFocus != null && plan.Context.ArcFocus.Entries.Count == 1, null);
        T.Ok("fokus zna intencje PO regule kryzysu", plan.Context.ArcFocus != null && plan.Context.ArcFocus.Intent == plan.Intent.Intent,
             plan.Intent.Intent.ToString());

        var kryzys = new WorldSnapshot { DaysPassed = 30, ColonistCount = 4, ColonistsOnMap = 4, AcuteDownedCount = 2 };
        TurnPlan pk = TurnPlanner.Plan(model, cfg.crisis, new EventHistory(), kryzys, 30f, d, l, null);
        // Odporne na null: brak fokusu ma dac czerwona asercje, a nie wyjatek przerywajacy walidator.
        T.Ok("kryzys -> Breathe -> faza negatywna Wendety CZEKA (bez osobnej reguly)",
             pk.Crisis.Extreme && pk.Intent.Intent == Intent.Breathe && pk.Context.ArcFocus != null
             && pk.Context.ArcFocus.Entries.Count == 1 && pk.Context.ArcFocus.Entries[0].Status == ArcFocus.StatusWaiting,
             pk.Context.ArcFocus == null ? "brak fokusu" : pk.Context.ArcFocus.DataPhases());

        TurnPlan bez = TurnPlanner.Plan(model, cfg.crisis, new EventHistory(), spokoj, 30f);
        T.Ok("stare przeciazenie (bez lukow) zostawia fokus pusty", bez.Context.ArcFocus == null, null);
    }

    // ---------------------------------------------------------------------------------
    //  Kolumny decyzji
    // ---------------------------------------------------------------------------------
    static void Kolumny(XmlConfig cfg)
    {
        T.Section("TEST 11p - kolumny lukowe we fragmencie danych decyzji");
        var policy = new SelectionPolicy(cfg.Selection());
        var pula = new List<ScoredCandidate> { K("A", "A1", 0.70f, 1f), K("B", "B1", 0.30f, 0f) };
        pula[0].Event.Intensity = IntensityLevel.High;
        var d = policy.Select(pula, Cisza(0.0f), new CountingRandom(2), false, null, null);
        var klucze = d.ToDataFragment().Split(new[] { "; " }, StringSplitOptions.None).Select(x => x.Split('=')[0]).ToList();
        // Od kroku 7 (format v9) fragment konczy sie trzema kolumnami stylu gracza (zmiana SPECYFIKACJI,
        // nie poluzowanie testu: v8 konczyl sie konsekwencja).
        T.EqS("ostatnie osiem kolumn fragmentu", string.Join(",", klucze.Skip(klucze.Count - 8)),
              "intensywnosc,arcAlignment,premiaLuku,lukWPasmie,konsekwencja,stylWartosc,premiaStylu,stylWPasmie");
        var pola = d.ToDataFragment().Split(new[] { "; " }, StringSplitOptions.None).ToDictionary(x => x.Split('=')[0], x => x.Substring(x.IndexOf('=') + 1));
        Func<string, double> num = s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        T.Ok("zwyciezca lukowy: intensywnosc=1 (High), arcAlignment=1, lukWPasmie=1",
             !d.IsPass && num(pola["intensywnosc"]) == 1.0 && num(pola["arcAlignment"]) == 1.0 && pola["lukWPasmie"] == "1", d.ToDataFragment());
        T.Eq("premiaLuku = (best - pasmo) * arcAlignment", double.Parse(pola["premiaLuku"], System.Globalization.CultureInfo.InvariantCulture),
             (d.BestUtility - d.BandThreshold) * 1.0, 1e-3);

        var bez = new List<ScoredCandidate> { K("A", "A1", 0.70f, -1f) };
        var db = policy.Select(bez, Cisza(0.0f), new CountingRandom(2), false, null, null);
        var pb = db.ToDataFragment().Split(new[] { "; " }, StringSplitOptions.None).ToDictionary(x => x.Split('=')[0], x => x.Substring(x.IndexOf('=') + 1));
        T.Ok("bez luku: arcAlignment, premiaLuku i lukWPasmie PUSTE (brak pomiaru, nie zero)",
             pb["arcAlignment"] == "" && pb["premiaLuku"] == "" && pb["lukWPasmie"] == "", db.ToDataFragment());

        // KROK 6: kolumna konsekwencji. Zdarzenie bez klocka konsekwencji -> "-" (slot pusty
        // WYMUSZONY), zdarzenie z klockiem -> jego id, PASS -> pusto (brak pomiaru).
        T.EqS("konsekwencja: zdarzenie bez klocka konsekwencji daje '-'", pola["konsekwencja"], "-");
        var zKons = new List<ScoredCandidate> { K("A", "A1", 0.70f, -1f) };
        zKons[0].Event.Blocks.Add(new Block { Id = "PN_Kons_Test", Type = BlockType.Consequence });
        var dk = policy.Select(zKons, Cisza(0.0f), new CountingRandom(2), false, null, null);
        var pk = dk.ToDataFragment().Split(new[] { "; " }, StringSplitOptions.None).ToDictionary(x => x.Split('=')[0], x => x.Substring(x.IndexOf('=') + 1));
        T.EqS("konsekwencja: zdarzenie z klockiem konsekwencji daje jego id", dk.IsPass ? "PASS" : pk["konsekwencja"], "PN_Kons_Test");
        var dp = policy.Select(new List<ScoredCandidate>(), Cisza(0.9f), new CountingRandom(2), false, null, null);
        var pp = dp.ToDataFragment().Split(new[] { "; " }, StringSplitOptions.None).ToDictionary(x => x.Split('=')[0], x => x.Substring(x.IndexOf('=') + 1));
        T.Ok("konsekwencja: PASS daje pusto (brak pomiaru, nie '-')", dp.IsPass && pp["konsekwencja"] == "", dp.ToDataFragment());
    }
}
