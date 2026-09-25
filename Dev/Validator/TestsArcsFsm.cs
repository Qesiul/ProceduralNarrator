using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Blackboard;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;

/// <summary>
/// TEST 11f-m - automat lukow (etap S2): przejscia, dojrzalosc, limity, straznicy, sloty, grupy,
/// odstep, splatanie, nastepnik, kodek pamieci, potwierdzanie wykonania.
/// </summary>
static class TestsArcsFsm
{
    public static void Run(XmlConfig cfg)
    {
        Automat();
        SlotyISplatanie(cfg);
        LukiStylu(cfg);
        Nastepnik();
        Kodek();
        PotwierdzenieWykonania();
    }

    // ---------------------------------------------------------------------------------
    //  Narzedzia
    // ---------------------------------------------------------------------------------

    static ArcObservation Obs(float dzien, int straty = 0, int porwani = 0, DangerLevel zagr = DangerLevel.None)
    {
        return new ArcObservation
        {
            Tick = (int)(dzien * 60000f), GameDay = dzien, ColonistLosses = straty, KidnappedCount = porwani, Danger = zagr
        };
    }

    static WorldSnapshot Swiat(int dni = 40, bool wrogowie = true, string fakty = null)
    {
        return new WorldSnapshot { DaysPassed = dni, ColonistCount = 6, ColonistsOnMap = 6, HasHostileFaction = wrogowie,
                                   Facts = fakty ?? string.Empty };
    }

    /// <summary>
    /// Fakty w postaci kanonicznej, budowane PRZEZ ksiege - a nie sklejane recznie ze stringow.
    /// Dzieki temu test jedzie dokladnie tym samym koderem co gra i zmiana formatu postaci
    /// kanonicznej nie ominie tych fixture'ow po cichu.
    /// </summary>
    static string Fakty(params string[] pary)
    {
        var ks = new FactLedger();
        foreach (string para in pary)
        {
            string[] s = para.Split('=');
            ks.Set(s[0], float.Parse(s[1], System.Globalization.CultureInfo.InvariantCulture), 0f, 0f);
        }
        return ks.Canonical(0f);
    }

    static ArcEventView Zd(Theme t, Valence v, EventScale s = EventScale.Major, IntensityLevel moc = IntensityLevel.Normal,
                           string frakcja = null, bool nosnik = false, params string[] tagi)
    {
        var e = new ArcEventView { Theme = t, Valence = v, Scale = s, Intensity = moc, FactionId = frakcja, CarriesFaction = nosnik,
                                   ActionBlockId = "X", Payload = "Y" };
        foreach (var g in tagi) e.Tags.Add(g);
        return e;
    }

    static ArcEventView Napad(IntensityLevel moc = IntensityLevel.Normal, string frakcja = null)
    {
        return Zd(Theme.Raid, Valence.Negative, EventScale.Major, moc, frakcja, true, "militarny");
    }

    static ArcEventView Dar()
    {
        return Zd(Theme.Economic, Valence.Positive, EventScale.Minor);
    }

    static ArcExpectation Ocz(Valence v, params Theme[] motywy)
    {
        return new ArcExpectation { themes = motywy.ToList(), valences = new List<Valence> { v } };
    }

    /// <summary>Luk syntetyczny: Zasiew napad -> Eskalacja napad (2/10) -> Kulminacja napad High (2/10) -> Rozwiazanie dar (-/5).</summary>
    static ArcDefinition LukT(string id = "PN_Luk_T", string grupa = "gt", int priorytet = 1, float minDni = 2f)
    {
        var kulm = new ArcPhase
        {
            id = "Kulminacja", kind = ArcPhaseKind.Climax, minDaysAfterPrevious = minDni, maxDays = 10f,
            expectations = new List<ArcExpectation> { new ArcExpectation { themes = new List<Theme> { Theme.Raid },
                valences = new List<Valence> { Valence.Negative }, minIntensity = IntensityLevel.High } },
            message = "kulminacja"
        };
        kulm.transitions.Add(new ArcTransition { guards = new List<ArcGuard> { new Guard_ColonistsLost { min = 1 } }, target = "Rozwiazanie", message = "straty" });
        return new ArcDefinition
        {
            defName = id, priority = priorytet, exclusionGroup = grupa, cooldownDays = 30f,
            startConditions = new List<NarrativeCondition> { new Cond_MinDaysPassed { min = 11 } },
            phases = new List<ArcPhase>
            {
                new ArcPhase { id = "Zasiew", kind = ArcPhaseKind.Seed, expectations = new List<ArcExpectation> { Ocz(Valence.Negative, Theme.Raid) }, message = "zasiew" },
                new ArcPhase { id = "Eskalacja", kind = ArcPhaseKind.Escalation, minDaysAfterPrevious = minDni, maxDays = 10f,
                               expectations = new List<ArcExpectation> { Ocz(Valence.Negative, Theme.Raid) }, message = "eskalacja" },
                kulm,
                new ArcPhase { id = "Rozwiazanie", kind = ArcPhaseKind.Resolution, maxDays = 5f,
                               expectations = new List<ArcExpectation> { Ocz(Valence.Positive) }, message = "rozwiazanie" }
            }
        };
    }

    static ArcDirector Dyrektor(params ArcDefinition[] luki)
    {
        return Dyrektor(2, luki);
    }

    static ArcDirector Dyrektor(int max, params ArcDefinition[] luki)
    {
        List<string> p;
        var kat = ArcCatalog.Build(luki, out p);
        if (p.Count > 0) throw new Exception("fixture niepoprawny: " + string.Join(" | ", p));
        return new ArcDirector(kat, new ArcParams { enabled = true, maxConcurrent = max });
    }

    /// <summary>Otwiera LukT napadem w dniu `dzien` i zwraca ksiege.</summary>
    static ArcLedger Otwarty(ArcDirector d, float dzien = 20f)
    {
        var l = new ArcLedger();
        d.OnExecuted(l, Napad(), ExecStatus.Executed, Swiat(), Obs(dzien));
        return l;
    }

    // ---------------------------------------------------------------------------------
    //  11f - automat
    // ---------------------------------------------------------------------------------
    static void Automat()
    {
        T.Section("TEST 11f - automat luku: otwarcie, dojrzalosc, przejscia, limity, straznicy");
        var d = Dyrektor(LukT());

        // Otwarcie tylko po wykonaniu zaliczanym (decyzja autora nr 6). Tabela z definicji statusow.
        foreach (ExecStatus st in Enum.GetValues(typeof(ExecStatus)))
        {
            var l = new ArcLedger();
            d.OnExecuted(l, Napad(), st, Swiat(), Obs(20f));
            bool oczek = st == ExecStatus.Executed || st == ExecStatus.Simulated || st == ExecStatus.LateExecuted;
            T.Ok("status " + st + ": luk " + (oczek ? "otwiera sie" : "NIE otwiera sie"), (l.Active.Count == 1) == oczek,
                 "aktywnych: " + l.Active.Count);
        }

        var lo = new ArcLedger();
        var rec = d.OnExecuted(lo, Napad(), ExecStatus.Executed, Swiat(), Obs(20f));
        var inst = lo.Active.FirstOrDefault();
        T.Ok("po otwarciu luk czeka na DRUGA faze (zasiew spelnil sie otwarciem)", inst != null && inst.PhaseId == "Eskalacja",
             inst == null ? "brak" : inst.PhaseId);
        T.Ok("rekord otwarcia: powod wykonanie, komunikat zasiewu", rec.Count == 1 && rec[0].Kind == ArcEventKind.Open
             && rec[0].Reason == ArcDirector.ReasonExecution && rec[0].Message == "zasiew", string.Join(" | ", rec));
        T.Eq("dzien otwarcia i wejscia w faze = dzien zdarzenia", inst == null ? -1 : inst.PhaseEnteredDay, 20f, 1e-6);

        var lw = new ArcLedger();
        d.OnExecuted(lw, Napad(), ExecStatus.Executed, Swiat(dni: 5), Obs(5f));
        T.EqI("warunki startu: dzien 5 (< 11) -> brak otwarcia", lw.Active.Count, 0);
        var ln = new ArcLedger();
        d.OnExecuted(ln, Napad(), ExecStatus.Executed, null, Obs(20f));
        T.EqI("brak stanu swiata -> brak otwarcia (warunkow nie da sie sprawdzic)", ln.Active.Count, 0);
        var lz = new ArcLedger();
        d.OnExecuted(lz, Dar(), ExecStatus.Executed, Swiat(), Obs(20f));
        T.EqI("zdarzenie niepasujace do zasiewu -> brak otwarcia", lz.Active.Count, 0);

        // Dojrzalosc: granica >= (2 dni po wejsciu w faze).
        var l1 = Otwarty(d, 20f);
        d.OnExecuted(l1, Napad(), ExecStatus.Executed, Swiat(), Obs(21.9f));
        T.EqS("przed dojrzaloscia (1.9 d < 2) faza stoi", l1.Active[0].PhaseId, "Eskalacja");
        var r2 = d.OnExecuted(l1, Napad(), ExecStatus.Executed, Swiat(), Obs(22f, straty: 3));
        T.EqS("dokladnie po 2 dniach (granica >=) faza sie przesuwa", l1.Active[0].PhaseId, "Kulminacja");
        T.Ok("rekord przejscia: Eskalacja->Kulminacja, powod wykonanie, komunikat fazy spelnionej",
             r2.Count == 1 && r2[0].Kind == ArcEventKind.Advance && r2[0].From == "Eskalacja" && r2[0].To == "Kulminacja"
             && r2[0].Message == "eskalacja", string.Join(" | ", r2));
        T.EqI("baza strat ustawiona przy wejsciu w faze", l1.Active[0].BaseColonistLosses, 3);

        // Dojrzalosc liczy sie od wejscia w BIEZACA faze (22), nie od otwarcia luku (20) - w pierwszej
        // fazie po otwarciu oba dni sa rowne, wiec tylko pozniejsza faza odroznia te dwa zegary.
        d.OnExecuted(l1, Napad(IntensityLevel.High), ExecStatus.Executed, Swiat(), Obs(23f, straty: 3));
        T.EqS("dojrzalosc od wejscia w biezaca faze: napad High dzien po wejsciu nie przesuwa", l1.Active[0].PhaseId, "Kulminacja");

        // Kulminacja wymaga mocy High.
        d.OnExecuted(l1, Napad(IntensityLevel.Normal), ExecStatus.Executed, Swiat(), Obs(25f, straty: 3));
        T.EqS("napad Normal nie spelnia kulminacji (moc >= High)", l1.Active[0].PhaseId, "Kulminacja");
        d.OnExecuted(l1, Napad(IntensityLevel.High), ExecStatus.Executed, Swiat(), Obs(25f, straty: 3));
        T.EqS("napad High spelnia kulminacje", l1.Active[0].PhaseId, "Rozwiazanie");

        // Rozwiazanie spelnione -> zamkniecie "rozwiazany".
        var r3 = d.OnExecuted(l1, Dar(), ExecStatus.Executed, Swiat(), Obs(26f));
        T.Ok("rozwiazanie spelnione -> luk zamkniety jako rozwiazany", l1.Active.Count == 0 && r3.Any(r => r.Kind == ArcEventKind.Close
             && r.Outcome == ArcDirector.OutcomeResolved), string.Join(" | ", r3));
        float dz;
        T.Ok("dzien zamkniecia zapisany (odstep przed ponownym otwarciem)", l1.LastCloseDay.TryGetValue("PN_Luk_T", out dz) && dz == 26f,
             "dzien: " + dz);

        // Slad po zamknietym watku (krok 6): LastCloseDay niesie tylko dzien, a model danych pracy
        // wymaga takze statusu i fazy koncowej. Asercja pilnuje WPIECIA w ArcDirector.Close - kodek
        // samego rekordu sprawdza TEST 13c, ale bez tego nikt nie zauwazylby braku wywolania.
        ArcClosure zam = l1.LastClosure("PN_Luk_T");
        T.Ok("zamkniecie przez dyrektora dopisuje slad do ksiegi (wynik, faza koncowa, dzien)",
             zam != null && zam.Outcome == ArcDirector.OutcomeResolved && zam.FinalPhaseId == "Rozwiazanie"
             && zam.Day == 26f && zam.Number == l1.NextNumber - 1,
             zam == null ? "brak rekordu" : zam.Encode());

        // Jeden krok na zdarzenie: przy zerowej dojrzalosci napad High pasuje do Eskalacji I Kulminacji.
        var d0 = Dyrektor(LukT(minDni: 0f));
        var l0 = Otwarty(d0, 20f);
        d0.OnExecuted(l0, Napad(IntensityLevel.High), ExecStatus.Executed, Swiat(), Obs(20f));
        T.EqS("jedno zdarzenie przesuwa luk najwyzej o JEDNA faze", l0.Active[0].PhaseId, "Kulminacja");
        // Zdarzenie, ktore otwiera luk, nie przesuwa go od razu (zasiew spelnia sie otwarciem).
        var l00 = new ArcLedger();
        d0.OnExecuted(l00, Napad(IntensityLevel.High), ExecStatus.Executed, Swiat(), Obs(20f));
        T.EqS("zdarzenie otwierajace nie przesuwa swiezo otwartego luku", l00.Active[0].PhaseId, "Eskalacja");

        // Limity czasu (scisle >): Eskalacja -> Rozwiazanie, Rozwiazanie -> wygaszony.
        var lt = Otwarty(d, 20f);
        var rt0 = d.Observe(lt, Obs(30f));
        T.Ok("dokladnie na limicie (10 d) nic sie nie dzieje", rt0.Count == 0 && lt.Active[0].PhaseId == "Eskalacja", string.Join(" | ", rt0));
        var rt1 = d.Observe(lt, Obs(30.01f));
        T.Ok("po limicie Eskalacja przechodzi do Rozwiazania (limitCzasu, bez komunikatu)", lt.Active[0].PhaseId == "Rozwiazanie"
             && rt1.Count == 1 && rt1[0].Reason == ArcDirector.ReasonTimeout && rt1[0].Message == null, string.Join(" | ", rt1));
        var rt2 = d.Observe(lt, Obs(35.02f));
        T.Ok("po limicie Rozwiazania luk zamyka sie jako wygaszony", lt.Active.Count == 0 && rt2.Count == 1
             && rt2[0].Outcome == ArcDirector.OutcomeFaded, string.Join(" | ", rt2));

        // Straznik strat - wzgledem bazy z wejscia w faze.
        var ls = Otwarty(d, 20f);
        d.OnExecuted(ls, Napad(), ExecStatus.Executed, Swiat(), Obs(22f, straty: 3));   // -> Kulminacja, baza 3
        var rs0 = d.Observe(ls, Obs(23f, straty: 3));
        T.Ok("straty sprzed wejscia w faze sie nie licza", rs0.Count == 0 && ls.Active[0].PhaseId == "Kulminacja", string.Join(" | ", rs0));
        var rs1 = d.Observe(ls, Obs(23f, straty: 4));
        T.Ok("strata kolonisty po wejsciu -> skok do Rozwiazania (straznik)", ls.Active[0].PhaseId == "Rozwiazanie" && rs1.Count == 1
             && rs1[0].Reason.StartsWith(ArcDirector.ReasonGuardPrefix) && rs1[0].Message == "straty", string.Join(" | ", rs1));

        // Straznik ma pierwszenstwo przed limitem czasu.
        var lp = Otwarty(d, 20f);
        d.OnExecuted(lp, Napad(), ExecStatus.Executed, Swiat(), Obs(22f));
        var rp = d.Observe(lp, Obs(40f, straty: 1));
        T.Ok("gdy zachodzi i straznik, i limit - wygrywa straznik", rp.Count == 1 && rp[0].Reason.StartsWith(ArcDirector.ReasonGuardPrefix),
             string.Join(" | ", rp));

        // Straznicy pojedynczo (tabela na sztucznych instancjach).
        var bazowa = new ArcInstance { BaseKidnapped = 2, BoundFactionId = "F", PeakDanger = DangerLevel.High };
        var obsF = Obs(1f);
        obsF.Factions["F"] = new FactionStatus { Exists = true, Hostile = false, Defeated = false };
        T.Ok("Guard_BoundFactionNotHostile: istnieje, nie wroga -> zachodzi", new Guard_BoundFactionNotHostile().Holds(obsF, bazowa), null);
        obsF.Factions["F"] = new FactionStatus { Exists = true, Hostile = true };
        T.Ok("Guard_BoundFactionNotHostile: wroga -> nie", !new Guard_BoundFactionNotHostile().Holds(obsF, bazowa), null);
        T.Ok("Guard_BoundFactionGone: wroga i istnieje -> nie", !new Guard_BoundFactionGone().Holds(obsF, bazowa), null);
        obsF.Factions["F"] = new FactionStatus { Exists = true, Hostile = true, Defeated = true };
        T.Ok("Guard_BoundFactionGone: pokonana -> zachodzi", new Guard_BoundFactionGone().Holds(obsF, bazowa), null);
        T.Ok("Guard_BoundFactionNotHostile: pokonana -> nie (to przypadek Gone)", !new Guard_BoundFactionNotHostile().Holds(obsF, bazowa), null);
        obsF.Factions.Clear();
        T.Ok("Guard_BoundFactionGone: frakcji nie ma -> zachodzi", new Guard_BoundFactionGone().Holds(obsF, bazowa), null);
        T.Ok("straznicy frakcji bez zwiazanej frakcji -> nigdy", !new Guard_BoundFactionGone().Holds(obsF, new ArcInstance())
             && !new Guard_BoundFactionNotHostile().Holds(obsF, new ArcInstance()), null);
        T.Ok("Guard_KidnappedDropped: 2 -> 1 zachodzi", new Guard_KidnappedDropped { min = 1 }.Holds(Obs(1f, porwani: 1), bazowa), null);
        T.Ok("Guard_KidnappedDropped: 2 -> 2 nie", !new Guard_KidnappedDropped { min = 1 }.Holds(Obs(1f, porwani: 2), bazowa), null);
        T.Ok("Guard_DangerPassed: szczyt High i teraz None -> zachodzi", new Guard_DangerPassed().Holds(Obs(1f), bazowa), null);
        T.Ok("Guard_DangerPassed: szczyt Low -> nie", !new Guard_DangerPassed().Holds(Obs(1f), new ArcInstance { PeakDanger = DangerLevel.Low }), null);
        T.Ok("Guard_DangerPassed: teraz Low -> nie", !new Guard_DangerPassed().Holds(Obs(1f, zagr: DangerLevel.Low), bazowa), null);

        // Szczyt zagrozenia sledzony przez Observe (baza Guard_DangerPassed).
        var lg = Otwarty(d, 20f);
        d.Observe(lg, Obs(21f, zagr: DangerLevel.High));
        d.Observe(lg, Obs(21.5f, zagr: DangerLevel.Low));
        T.Ok("Observe zapamietuje SZCZYT zagrozenia od wejscia w faze", lg.Active[0].PeakDanger == DangerLevel.High, lg.Active[0].PeakDanger.ToString());

        // Przejscia calego luku zamykaja z wynikiem z Defa (luk wiazacy frakcje).
        var wz = LukT("PN_Luk_W", "gw");
        wz.phases[0].bindsFaction = true;
        wz.transitions.Add(new ArcTransition { guards = new List<ArcGuard> { new Guard_BoundFactionNotHostile() }, close = "pojednanie", message = "pokoj" });
        var dw = Dyrektor(wz);
        var lwz = new ArcLedger();
        dw.OnExecuted(lwz, Napad(frakcja: "F"), ExecStatus.Executed, Swiat(), Obs(20f));
        var ow = Obs(21f);
        ow.Factions["F"] = new FactionStatus { Exists = true, Hostile = false };
        var rw = dw.Observe(lwz, ow);
        T.Ok("przejscie calego luku: frakcja niewroga -> zamkniecie 'pojednanie' z komunikatem", lwz.Active.Count == 0 && rw.Count == 1
             && rw[0].Outcome == "pojednanie" && rw[0].Message == "pokoj" && rw[0].FactionId == "F", string.Join(" | ", rw));

        // Wylacznik warstwy.
        var dOff = new ArcDirector(Dyrektor(LukT()).Catalog, new ArcParams { enabled = false, maxConcurrent = 2 });
        var lOff = new ArcLedger();
        dOff.OnExecuted(lOff, Napad(), ExecStatus.Executed, Swiat(), Obs(20f));
        T.EqI("warstwa wylaczona (<arcs><enabled>false) -> zadnych otwarc", lOff.Active.Count, 0);

        // Reconcile: nieznany luk, nieznana faza i instancja "w zasiewie" sa odrzucane z sladem.
        var lr = new ArcLedger();
        lr.Active.Add(new ArcInstance { ArcId = "PN_Luk_T", Number = 1, PhaseId = "Kulminacja" });
        lr.Active.Add(new ArcInstance { ArcId = "PN_Luk_Brak", Number = 2, PhaseId = "Eskalacja" });
        lr.Active.Add(new ArcInstance { ArcId = "PN_Luk_T2", Number = 3, PhaseId = "X" });
        lr.Active.Add(new ArcInstance { ArcId = "PN_Luk_T3", Number = 4, PhaseId = "Zasiew" });
        var dr = Dyrektor(LukT(), LukT("PN_Luk_T2", "g2"), LukT("PN_Luk_T3", "g3"));
        var rr = dr.Reconcile(lr, 100, 1f);
        T.Ok("Reconcile zostawia tylko poprawna instancje", lr.Active.Count == 1 && lr.Active[0].ArcId == "PN_Luk_T", string.Join(",", lr.Active));
        T.Ok("Reconcile: 3 odrzucenia z powodem brakDefa", rr.Count == 3 && rr.All(r => r.Kind == ArcEventKind.Drop && r.Reason == ArcDirector.ReasonMissingDef),
             string.Join(" | ", rr));
    }

    // ---------------------------------------------------------------------------------
    //  11g - sloty, grupy, odstep, splatanie (w tym na PRAWDZIWYM katalogu)
    // ---------------------------------------------------------------------------------
    static void SlotyISplatanie(XmlConfig cfg)
    {
        T.Section("TEST 11g - sloty, grupy wykluczajace, odstep, splatanie");

        var d3 = Dyrektor(2, LukT("PN_Luk_A", "ga", 3), LukT("PN_Luk_B", "gb", 2), LukT("PN_Luk_C", "gc", 1));
        var l3 = new ArcLedger();
        d3.OnExecuted(l3, Napad(), ExecStatus.Executed, Swiat(), Obs(20f));
        T.EqS("limit 2: z trzech pasujacych otwieraja sie dwa o najwyzszym priorytecie",
              string.Join(",", l3.Active.Select(a => a.ArcId)), "PN_Luk_A,PN_Luk_B");

        var dg = Dyrektor(2, LukT("PN_Luk_A", "wspolna", 2), LukT("PN_Luk_B", "wspolna", 1));
        var lg = new ArcLedger();
        dg.OnExecuted(lg, Napad(), ExecStatus.Executed, Swiat(), Obs(20f));
        T.EqS("grupa wykluczajaca: otwiera sie tylko luk o wyzszym priorytecie", string.Join(",", lg.Active.Select(a => a.ArcId)), "PN_Luk_A");

        var d1 = Dyrektor(LukT());
        var lc = new ArcLedger();
        lc.LastCloseDay["PN_Luk_T"] = 10f;
        d1.OnExecuted(lc, Napad(), ExecStatus.Executed, Swiat(), Obs(39.9f));
        T.EqI("odstep 30 d: w dniu 39.9 po zamknieciu w dniu 10 - brak otwarcia", lc.Active.Count, 0);
        d1.OnExecuted(lc, Napad(), ExecStatus.Executed, Swiat(), Obs(40f));
        T.EqI("odstep 30 d: w dniu 40 (granica >=) - otwarcie", lc.Active.Count, 1);

        int nr = lc.NextNumber;
        d1.OnExecuted(lc, Napad(), ExecStatus.Executed, Swiat(), Obs(40.5f));
        T.Ok("jedna instancja luku naraz: drugi zasiew nie otwiera kopii", lc.Active.Count == 1 && lc.NextNumber == nr,
             "aktywnych " + lc.Active.Count);

        // PRAWDZIWY katalog: Wendeta wymaga znanej frakcji; ta sama frakcja przesuwa, inna nie.
        // KROK 6: wymaga takze faktu walka.byla >= 1 - watek rodzi sie z SERII starc. Warunki widza
        // swiat z POCZATKU TURY, wiec liczy sie walka WCZESNIEJSZA niz napad otwierajacy watek.
        List<string> p;
        var kat = ArcCatalog.Build(cfg.arcDefs, out p);
        var dk = new ArcDirector(kat, cfg.arcs);
        var lk = new ArcLedger();
        string poStarciach = Fakty("walka.byla=1");
        var lbez = new ArcLedger();
        dk.OnExecuted(lbez, Napad(frakcja: "F9"), ExecStatus.Executed, Swiat(), Obs(19f));
        T.Ok("KROK 6: Wendeta NIE otwiera sie bez pamieci o starciach (sam napad nie wystarcza)",
             lbez.Find("PN_Luk_Wendeta") == null, "bez faktu walka.byla: " + lbez.Summary());
        // Granica progu: licznik rowny zero (fakt obecny, wartosc 0) - dalej brak wczesniejszej walki.
        var lzero = new ArcLedger();
        dk.OnExecuted(lzero, Napad(frakcja: "F9"), ExecStatus.Executed, Swiat(fakty: Fakty("walka.byla=0")), Obs(19f));
        T.Ok("KROK 6: walka.byla = 0 nie otwiera Wendety (prog >= 1)", lzero.Find("PN_Luk_Wendeta") == null,
             lzero.Summary());
        dk.OnExecuted(lk, Napad(), ExecStatus.Executed, Swiat(fakty: poStarciach), Obs(20f));
        T.Ok("Wendeta NIE otwiera sie bez znanej frakcji napadu", lk.Find("PN_Luk_Wendeta") == null, lk.Summary());
        dk.OnExecuted(lk, Napad(frakcja: "F9"), ExecStatus.Executed, Swiat(fakty: poStarciach), Obs(20.5f));
        var wen = lk.Find("PN_Luk_Wendeta");
        T.Ok("Wendeta otwiera sie napadem ze znana frakcja i ja wiaze", wen != null && wen.BoundFactionId == "F9",
             wen == null ? lk.Summary() : wen.ToString());
        dk.OnExecuted(lk, Napad(frakcja: "F8"), ExecStatus.Executed, Swiat(fakty: poStarciach), Obs(24f));
        // "?." - brak luku ma zapalic ASERCJE, nie wywrocic przebiegu przed TEST 13 (przeglad S6: mutacja B8
        // byla wykrywana tylko awaria, a asercje blackboardu, ktore tez by ja wykryly, nie doszly do skutku).
        T.EqS("napad INNEJ frakcji nie przesuwa Wendety", lk.Find("PN_Luk_Wendeta")?.PhaseId, "Eskalacja");
        dk.OnExecuted(lk, Napad(frakcja: "F9"), ExecStatus.Executed, Swiat(fakty: poStarciach), Obs(24f));
        T.EqS("napad TEJ SAMEJ frakcji (po 3 dniach dojrzalosci) przesuwa Wendete", lk.Find("PN_Luk_Wendeta")?.PhaseId, "Kulminacja");

        // SPLATANIE na prawdziwym katalogu (decyzja autora nr 4): napad w Kulminacji Scigonych
        // jednoczesnie przesuwa Scigonych i otwiera Wendete.
        var ls = new ArcLedger();
        // KROK 6: Scigani wymagaja wczesniejszych wiesci (wiesci.zrodlo), Wendeta - wczesniejszej walki.
        string poPrzybyszu = Fakty("wiesci.zrodlo=1", "walka.byla=1");
        var lbezWiesci = new ArcLedger();
        dk.OnExecuted(lbezWiesci, Zd(Theme.Social, Valence.Neutral, EventScale.Minor, IntensityLevel.Low, null, false,
                                     "spoleczny", "kapsula"), ExecStatus.Executed, Swiat(fakty: Fakty("walka.byla=1")), Obs(20f));
        T.Ok("KROK 6: Scigani NIE otwieraja sie bez wczesniejszych wiesci", lbezWiesci.Find("PN_Luk_Scigani") == null,
             lbezWiesci.Summary());
        var uchodzcy = Zd(Theme.Social, Valence.Neutral, EventScale.Minor, IntensityLevel.Low, null, false, "spoleczny", "kapsula");
        dk.OnExecuted(ls, uchodzcy, ExecStatus.Executed, Swiat(fakty: poPrzybyszu), Obs(20f));
        T.Ok("Scigani otwieraja sie rozbiciem kapsuly", ls.Find("PN_Luk_Scigani") != null && ls.Find("PN_Luk_Scigani").PhaseId == "Kulminacja",
             ls.Summary());

        // ZNAKI Z NIEBA (przeglad S6: strona czytelnika nie miala testu negatywnego). Zasiew: zdarzenie
        // POZYTYWNE z tagiem "niebo" (Zrzut, Meteoryt) - otwiera watek tylko po znaku (niebo.znak).
        var dar = Zd(Theme.Economic, Valence.Positive, EventScale.Minor, IntensityLevel.Normal, null, false, "niebo", "zasoby");
        var lbezZnaku = new ArcLedger();
        dk.OnExecuted(lbezZnaku, dar, ExecStatus.Executed, Swiat(), Obs(25f));
        T.Ok("KROK 6: Znaki z nieba NIE otwieraja sie bez wczesniejszego znaku (niebo.znak)",
             lbezZnaku.Find("PN_Luk_ZnakiZNieba") == null, lbezZnaku.Summary());
        var lzZnakiem = new ArcLedger();
        dk.OnExecuted(lzZnakiem, dar, ExecStatus.Executed, Swiat(fakty: Fakty("niebo.znak=1")), Obs(25f));
        T.Ok("Znaki z nieba otwieraja sie darem z nieba PO znaku", lzZnakiem.Find("PN_Luk_ZnakiZNieba") != null,
             lzZnakiem.Summary());
        var rs = dk.OnExecuted(ls, Napad(frakcja: "F5"), ExecStatus.Executed, Swiat(fakty: poPrzybyszu), Obs(22f));
        T.Ok("jeden napad: Scigani -> Rozwiazanie ORAZ otwarcie Wendety z frakcja F5",
             ls.Find("PN_Luk_Scigani") != null && ls.Find("PN_Luk_Scigani").PhaseId == "Rozwiazanie"
             && ls.Find("PN_Luk_Wendeta") != null && ls.Find("PN_Luk_Wendeta").BoundFactionId == "F5"
             && rs.Count(r => r.Kind == ArcEventKind.Advance) == 1 && rs.Count(r => r.Kind == ArcEventKind.Open) == 1,
             string.Join(" | ", rs));
        T.EqI("limit 2 z XML obowiazuje takze tu (dwa aktywne)", ls.Active.Count, 2);
    }

    // ---------------------------------------------------------------------------------
    //  14o - luki pod styl gracza na PRAWDZIWYM katalogu (krok 7)
    // ---------------------------------------------------------------------------------
    static WorldSnapshot SwiatStyl(string mocne, string fakty = null)
    {
        WorldSnapshot s = Swiat(fakty: fakty);
        s.StyleStrongSides = mocne;
        return s;
    }

    static void LukiStylu(XmlConfig cfg)
    {
        T.Section("TEST 14o - luki pod styl gracza na prawdziwym katalogu (otwarcie tylko przy mocnej stronie)");
        List<string> p;
        var dk = new ArcDirector(ArcCatalog.Build(cfg.arcDefs, out p), cfg.arcs);
        string walki = Fakty("walka.byla=1");

        var l1 = new ArcLedger();
        dk.OnExecuted(l1, Napad(frakcja: "F9"), ExecStatus.Executed, SwiatStyl(";Walka;", walki), Obs(20f));
        T.Ok("Walka mocna: napad otwiera Slawe twierdzy, nie Wendete (grupa frakcja, wyzszy priorytet)",
             l1.Find("PN_Luk_SlawaTwierdzy") != null && l1.Find("PN_Luk_Wendeta") == null, l1.Summary());
        var l2 = new ArcLedger();
        dk.OnExecuted(l2, Napad(frakcja: "F9"), ExecStatus.Executed, SwiatStyl("", walki), Obs(20f));
        T.Ok("bez mocnej Walki ten sam napad otwiera Wendete, nie Slawe",
             l2.Find("PN_Luk_Wendeta") != null && l2.Find("PN_Luk_SlawaTwierdzy") == null, l2.Summary());
        var l3 = new ArcLedger();
        dk.OnExecuted(l3, Napad(frakcja: "F9"), ExecStatus.Executed, SwiatStyl(";Gospodarka;", walki), Obs(20f));
        T.Ok("inna mocna strona (Gospodarka) nie otwiera Slawy", l3.Find("PN_Luk_SlawaTwierdzy") == null, l3.Summary());
        // Warunek stylu dziala TYLKO przy otwarciu: dalsze fazy ida bez mocnej strony.
        var szal = Zd(Theme.Natural, Valence.Negative, EventScale.Moderate, IntensityLevel.Normal, null, false, "militarny", "zwierzeta");
        dk.OnExecuted(l1, szal, ExecStatus.Executed, SwiatStyl("", walki), Obs(23.5f));
        T.EqS("otwarta Slawa przesuwa sie dalej bez mocnej strony (styl czytany tylko przy otwarciu)",
              l1.Find("PN_Luk_SlawaTwierdzy")?.PhaseId, "Kulminacja");

        var dar = Zd(Theme.Economic, Valence.Positive, EventScale.Minor, IntensityLevel.Normal, null, false, "zasoby");
        var ld = new ArcLedger();
        dk.OnExecuted(ld, dar, ExecStatus.Executed, SwiatStyl(";Gospodarka;"), Obs(20f));
        T.Ok("Gospodarka mocna: dar otwiera Dostatek", ld.Find("PN_Luk_Dostatek") != null, ld.Summary());
        var ld0 = new ArcLedger();
        dk.OnExecuted(ld0, dar, ExecStatus.Executed, SwiatStyl(""), Obs(20f));
        T.Ok("bez mocnej Gospodarki dar nie otwiera Dostatku", ld0.Find("PN_Luk_Dostatek") == null, ld0.Summary());

        var wedrowiec = Zd(Theme.Social, Valence.Positive, EventScale.Minor, IntensityLevel.Normal, null, false, "spoleczny");
        var lz = new ArcLedger();
        dk.OnExecuted(lz, wedrowiec, ExecStatus.Executed, SwiatStyl(";Ekspansja;"), Obs(20f));
        T.Ok("Ekspansja mocna: wedrowiec otwiera Ziemie obiecana", lz.Find("PN_Luk_ZiemiaObiecana") != null, lz.Summary());
        var lz0 = new ArcLedger();
        dk.OnExecuted(lz0, wedrowiec, ExecStatus.Executed, SwiatStyl(";Walka;"), Obs(20f));
        T.Ok("inna mocna strona: wedrowiec nie otwiera Ziemi obiecanej", lz0.Find("PN_Luk_ZiemiaObiecana") == null, lz0.Summary());

        var ln = new ArcLedger();
        dk.OnExecuted(ln, szal, ExecStatus.Executed, SwiatStyl(";Reaktywnosc;"), Obs(20f));
        T.Ok("Reaktywnosc mocna: szal zwierzat otwiera Niespokojne noce", ln.Find("PN_Luk_NiespokojneNoce") != null, ln.Summary());
        var ln0 = new ArcLedger();
        dk.OnExecuted(ln0, szal, ExecStatus.Executed, SwiatStyl(""), Obs(20f));
        T.Ok("bez mocnej Reaktywnosci szal nie otwiera Niespokojnych nocy", ln0.Find("PN_Luk_NiespokojneNoce") == null, ln0.Summary());

        // Przeglad S8: Amok (Natural Negative Minor) przy mocnej Reaktywnosci otwiera Gniew natury, NIE Noce.
        var amok = Zd(Theme.Natural, Valence.Negative, EventScale.Minor, IntensityLevel.Normal, null, false, "zwierzeta");
        var lg = new ArcLedger();
        dk.OnExecuted(lg, amok, ExecStatus.Executed, SwiatStyl(";Reaktywnosc;"), Obs(20f));
        T.Ok("Reaktywnosc mocna: Amok otwiera Gniew natury, nie Niespokojne noce",
             lg.Find("PN_Luk_GniewNatury") != null && lg.Find("PN_Luk_NiespokojneNoce") == null, lg.Summary());

        // Przeglad S8: dar "z nieba" przy mocnej Gospodarce i fakcie niebo.znak - otwiera sie TYLKO Dostatek (wspolna grupa).
        var zNieba = Zd(Theme.Economic, Valence.Positive, EventScale.Minor, IntensityLevel.Normal, null, false, "zasoby", "niebo");
        var lb = new ArcLedger();
        dk.OnExecuted(lb, zNieba, ExecStatus.Executed, SwiatStyl(";Gospodarka;", Fakty("niebo.znak=1")), Obs(25f));
        T.Ok("dar z nieba przy mocnej Gospodarce: tylko Dostatek, bez Znakow z nieba (grupa niebo)",
             lb.Find("PN_Luk_Dostatek") != null && lb.Find("PN_Luk_ZnakiZNieba") == null && lb.Active.Count == 1, lb.Summary());
        var lb0 = new ArcLedger();
        dk.OnExecuted(lb0, zNieba, ExecStatus.Executed, SwiatStyl("", Fakty("niebo.znak=1")), Obs(25f));
        T.Ok("STRAZNIK: bez mocnej Gospodarki ten sam dar otwiera Znaki z nieba", lb0.Find("PN_Luk_ZnakiZNieba") != null, lb0.Summary());
    }

    // ---------------------------------------------------------------------------------
    //  11h - nastepnik (splatanie przez zamkniecie)
    // ---------------------------------------------------------------------------------
    static void Nastepnik()
    {
        T.Section("TEST 11h - nastepnik: otwarcie zamknieciem poprzednika");
        var a = LukT("PN_Luk_A", "ga", 2);
        a.successor = "PN_Luk_B";
        var b = LukT("PN_Luk_B", "gb", 1);
        b.phases[0].expectations = new List<ArcExpectation> { Ocz(Valence.Neutral, Theme.Trade) };   // zasiew B nie pasuje do niczego tutaj
        var d = Dyrektor(a, b);

        var l = new ArcLedger();
        l.Active.Add(new ArcInstance { ArcId = "PN_Luk_A", Number = 1, PhaseId = "Rozwiazanie", PhaseEnteredDay = 20f });
        var r = d.OnExecuted(l, Dar(), ExecStatus.Executed, Swiat(), Obs(21f));
        var bi = l.Find("PN_Luk_B");
        T.Ok("rozwiazanie A otwiera nastepnika B na jego DRUGIEJ fazie", bi != null && bi.PhaseId == "Eskalacja", l.Summary());
        T.Ok("rekord otwarcia B: powod nastepca, bez komunikatu (nie bylo zdarzenia zasiewu)",
             r.Any(x => x.Kind == ArcEventKind.Open && x.ArcId == "PN_Luk_B" && x.Reason == ArcDirector.ReasonSuccessor && x.Message == null),
             string.Join(" | ", r));

        var l2 = new ArcLedger();
        l2.Active.Add(new ArcInstance { ArcId = "PN_Luk_A", Number = 1, PhaseId = "Rozwiazanie", PhaseEnteredDay = 20f });
        d.Observe(l2, Obs(26f));
        T.Ok("A wygaszony limitem czasu NIE otwiera nastepnika", l2.Active.Count == 0 && l2.Find("PN_Luk_B") == null, l2.Summary());

        var l3 = new ArcLedger();
        l3.Active.Add(new ArcInstance { ArcId = "PN_Luk_A", Number = 1, PhaseId = "Rozwiazanie", PhaseEnteredDay = 20f });
        d.OnExecuted(l3, Dar(), ExecStatus.Executed, Swiat(dni: 5), Obs(21f));
        T.Ok("nastepnik podlega warunkom startu (dzien 5 < 11)", l3.Find("PN_Luk_B") == null, l3.Summary());

        // Walidacja: nastepnik wiazacy frakcje w Zasiewie nie ma skad jej wziac.
        var bw = LukT("PN_Luk_B", "gb", 1);
        bw.phases[0].bindsFaction = true;
        List<string> p;
        var k = ArcCatalog.Build(new[] { a, bw }, out p);
        T.Ok("walidacja odrzuca nastepnika wiazacego frakcje w Zasiewie", k.ById("PN_Luk_A") == null && p.Any(x => x.Contains("nie ma skad jej wziac")),
             string.Join(" | ", p));
    }

    // ---------------------------------------------------------------------------------
    //  11l - kodek pamieci lukow
    // ---------------------------------------------------------------------------------
    static void Kodek()
    {
        T.Section("TEST 11l - kodek pamieci lukow (ArcInstance, PendingExecution, ArcLedger)");

        // Instancja z KAZDYM polem niedomyslnym i roznym - porownanie pole po polu refleksja,
        // a nie przez ponowne kodowanie (to byloby slepe na strate przy kodowaniu).
        var a = new ArcInstance
        {
            ArcId = "PN_Luk_Wendeta", Number = 7, PhaseId = "Kulminacja", OpenedDay = 12.25f, PhaseEnteredDay = 19.5f,
            PhaseEnteredTick = 1170000, BoundFactionId = "42", BaseColonistLosses = 3, BaseKidnapped = 2, PeakDanger = DangerLevel.High
        };
        var pola = typeof(ArcInstance).GetFields(BindingFlags.Public | BindingFlags.Instance);
        var domyslna = new ArcInstance();
        T.Ok("STRAZNIK: fixture ustawia KAZDE pole ArcInstance na wartosc niedomyslna",
             pola.All(f => !Equals(f.GetValue(a), f.GetValue(domyslna))),
             string.Join(",", pola.Where(f => Equals(f.GetValue(a), f.GetValue(domyslna))).Select(f => f.Name)));
        ArcInstance b;
        bool ok = ArcInstance.TryDecode(a.Encode(), out b);
        var rozne = ok ? pola.Where(f => !Equals(f.GetValue(a), f.GetValue(b))).Select(f => f.Name).ToList() : new List<string> { "dekodowanie" };
        T.Ok("ArcInstance: round-trip odtwarza wszystkie pola", ok && rozne.Count == 0, "pol " + pola.Length + ", roznych: " + string.Join(",", rozne));

        string wz = a.Encode();
        var zle = new List<(string opis, string linia)>
        {
            ("za malo pol", wz.Substring(0, wz.LastIndexOf('|'))),
            ("zly znacznik", "X" + wz.Substring(1)),
            ("liczba nieliczbowa", wz.Replace("|7|", "|x|")),
            ("dzien NaN", wz.Replace("|12.25|", "|NaN|")),
            ("enum liczbowy", wz.Substring(0, wz.LastIndexOf('|')) + "|2"),
            ("enum nieznany", wz.Substring(0, wz.LastIndexOf('|')) + "|Extreme"),
            ("pusty arcId", wz.Replace("|PN_Luk_Wendeta|", "|-|")),
        };
        foreach (var (opis, linia) in zle)
        {
            ArcInstance x;
            T.Ok("ArcInstance odrzuca uszkodzona linie: " + opis, !ArcInstance.TryDecode(linia, out x) && x == null, linia);
        }

        var p = new PendingExecution
        {
            Tick = 1234567, GameDay = 20.5f, DecisionIndex = 9, IncidentDefName = "RaidEnemy", LastFireBefore = -1,
            Event = new ArcEventView { ActionBlockId = "PN_Akcja_Napad", Payload = "RaidEnemy", Theme = Theme.Raid, Valence = Valence.Negative,
                                       Scale = EventScale.Major, Intensity = IntensityLevel.High, CarriesFaction = true, FactionId = "42" }
        };
        p.Event.Tags.Add("militarny");
        p.Event.Tags.Add("niebo");
        PendingExecution q;
        bool okp = PendingExecution.TryDecode(p.Encode(), out q);
        T.Ok("PendingExecution: round-trip (tick, dzien, decyzja, def, osie, moc, tagi, nosnik, frakcja, przed)",
             okp && q.Tick == p.Tick && q.GameDay == p.GameDay && q.DecisionIndex == 9 && q.IncidentDefName == "RaidEnemy"
             && q.LastFireBefore == -1 && q.Event.ActionBlockId == "PN_Akcja_Napad" && q.Event.Payload == "RaidEnemy"
             && q.Event.Theme == Theme.Raid && q.Event.Valence == Valence.Negative && q.Event.Scale == EventScale.Major
             && q.Event.Intensity == IntensityLevel.High && q.Event.CarriesFaction && q.Event.FactionId == "42"
             && q.Event.Tags.SetEquals(new[] { "militarny", "niebo" }), okp ? q.Encode() : "dekodowanie");

        var l = new ArcLedger { NextNumber = 9, ColonistLosses = 4 };
        l.Active.Add(a);
        l.Active.Add(new ArcInstance { ArcId = "PN_Luk_Scigani", Number = 8, PhaseId = "Rozwiazanie", PeakDanger = DangerLevel.Low });
        l.LastCloseDay["PN_Luk_GniewNatury"] = 33.5f;
        l.LastCloseDay["PN_Luk_ZnakiZNieba"] = 3f;
        l.Pending = p;
        var r = new ArcLedger();
        int odrz = r.RestoreFromLines(l.ToPersistableLines());
        T.Ok("ArcLedger: round-trip licznikow, instancji (w kolejnosci numerow), zamkniec i oczekujacego",
             odrz == 0 && r.NextNumber == 9 && r.ColonistLosses == 4 && r.Active.Count == 2 && r.Active[0].Number == 7 && r.Active[1].Number == 8
             && r.Active[0].BoundFactionId == "42" && r.LastCloseDay.Count == 2 && r.LastCloseDay["PN_Luk_GniewNatury"] == 33.5f
             && r.Pending != null && r.Pending.Tick == p.Tick, "odrzuconych " + odrz + ", " + r.Summary());

        var zlaKsiega = new List<string>(l.ToPersistableLines()) { "smieci", "N|0|1", a.Encode() };
        var r2 = new ArcLedger();
        int odrz2 = r2.RestoreFromLines(zlaKsiega);
        T.Ok("ArcLedger odrzuca smieci, zly licznik i DUPLIKAT instancji luku", odrz2 == 3 && r2.Active.Count == 2, "odrzuconych " + odrz2);

        var r3 = new ArcLedger();
        r3.RestoreFromLines(new[] { new ArcInstance { ArcId = "PN_Luk_A", Number = 5, PhaseId = "X" }.Encode(), "N|2|0" });
        T.Ok("numer nastepnej instancji podniesiony ponad numery otwartych (uszkodzony licznik)", r3.NextNumber == 6, "NextNumber " + r3.NextNumber);

        var klon = l.Clone();
        klon.Active[0].PhaseId = "Rozwiazanie";
        klon.LastCloseDay["PN_Luk_X"] = 1f;
        T.Ok("Clone jest gleboka kopia (zmiana kopii nie rusza oryginalu)", l.Active[0].PhaseId == "Kulminacja" && !l.LastCloseDay.ContainsKey("PN_Luk_X"), null);
        T.Ok("pusta ksiega: RestoreFromLines(null) zeruje stan", new ArcLedger().RestoreFromLines(null) == 0, null);
    }

    // ---------------------------------------------------------------------------------
    //  11m - potwierdzanie wykonania
    // ---------------------------------------------------------------------------------
    static void PotwierdzenieWykonania()
    {
        T.Section("TEST 11m - potwierdzanie wykonania (lastFireTicks przed/po)");
        const int tick = 5000;
        T.Ok("przed=-1, po=tick -> wykonane", ExecutionConfirmation.Classify(-1, tick, tick, false) == ExecStatus.Executed, null);
        T.Ok("przed=1000, po=tick -> wykonane", ExecutionConfirmation.Classify(1000, tick, tick, false) == ExecStatus.Executed, null);
        T.Ok("przed=1000, po=1000 -> niewykonane", ExecutionConfirmation.Classify(1000, 1000, tick, false) == ExecStatus.NotExecuted, null);
        T.Ok("przed=tick (ktos odpalil ten def w tym ticku) -> niejednoznaczne",
             ExecutionConfirmation.Classify(tick, tick, tick, false) == ExecStatus.Ambiguous, null);
        T.Ok("symulator -> symulacja (niezaleznie od tickow)", ExecutionConfirmation.Classify(-1, -1, tick, true) == ExecStatus.Simulated, null);

        var p = new PendingExecution { Tick = tick, LastFireBefore = 100 };
        T.Ok("pozne: teraz=tick decyzji -> pozno-wykonane", ExecutionConfirmation.ClassifyLate(tick, p) == ExecStatus.LateExecuted, null);
        T.Ok("pozne: teraz=100 -> pozno-niewykonane", ExecutionConfirmation.ClassifyLate(100, p) == ExecStatus.LateNotExecuted, null);
        T.Ok("pozne: teraz > tick decyzji (nadpisane) -> niejednoznaczne", ExecutionConfirmation.ClassifyLate(tick + 1000, p) == ExecStatus.Ambiguous, null);
        T.Ok("pozne: przed == tick decyzji -> niejednoznaczne",
             ExecutionConfirmation.ClassifyLate(tick, new PendingExecution { Tick = tick, LastFireBefore = tick }) == ExecStatus.Ambiguous, null);

        var zaliczane = Enum.GetValues(typeof(ExecStatus)).Cast<ExecStatus>().Where(ExecutionConfirmation.CountsAsExecuted)
                            .Select(s => s.ToString()).OrderBy(x => x, StringComparer.Ordinal);
        T.EqS("wykonanie zaliczane = Executed, LateExecuted, Simulated", string.Join(",", zaliczane), "Executed,LateExecuted,Simulated");
        var etykiety = Enum.GetValues(typeof(ExecStatus)).Cast<ExecStatus>().Select(ExecutionConfirmation.Label).ToList();
        T.Ok("etykiety statusow unikalne i bez ';'", etykiety.Distinct().Count() == etykiety.Count && etykiety.All(e => !e.Contains(";")),
             string.Join(",", etykiety));
    }
}
