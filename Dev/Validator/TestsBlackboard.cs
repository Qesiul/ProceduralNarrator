using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Blackboard;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

/// <summary>
/// TEST 13a-i - blackboard kroku 6: ksiega faktow, slad po zamknietym watku, fasada zapytan
/// i postac kanoniczna, ktora widza warunki przez WorldSnapshot.
/// </summary>
static class TestsBlackboard
{
    public static void Run()
    {
        KodekFaktow();
        WygasanieISortowanie();
        ZamkniecieWatku();
        ZapytaniaFasady();
        PostacKanoniczna();
        WarunkiPamieci();
        SlotKonsekwencji();
        KolejkaFaktow();
        PrzegladS6();
    }

    // ---------------------------------------------------------------------------------
    //  13i - przeglad adwersarialny kroku 6 (S6, 2026-09-23): luki w testach i poprawki rdzenia
    // ---------------------------------------------------------------------------------
    static void PrzegladS6()
    {
        T.Section("TEST 13i - przeglad S6: werdykty kolejki, kodeki na prawdziwych dniach, granica, zamkniecia, pamiec decyzji");

        var slad = new FactWrite { key = "walka.byla", value = 1f, accumulate = true, lifespanDays = 20f };

        // --- (1) ConfirmPending dla WSZYSTKICH werdyktow. Tabela jest JAWNA (nie CountsAsExecuted):
        // "liczy sie jak wykonanie" = wykonane, symulacja, pozno-wykonane. W symulatorze status to
        // zawsze Simulated - regula "tylko Executed" skasowalaby tam wszystkie fakty.
        var tabela = new Dictionary<ExecStatus, bool>
        {
            { ExecStatus.Executed, true }, { ExecStatus.NotExecuted, false }, { ExecStatus.Ambiguous, false },
            { ExecStatus.Simulated, true }, { ExecStatus.LateExecuted, true }, { ExecStatus.LateNotExecuted, false },
        };
        T.EqI("STRAZNIK: tabela werdyktow obejmuje KAZDA wartosc ExecStatus",
              tabela.Count, Enum.GetValues(typeof(ExecStatus)).Length);
        bool nadpisano;
        foreach (var kv in tabela)
        {
            var ks = new FactLedger();
            ks.Queue(Oczekujace(60000, 1f, 5, slad), out nadpisano);
            FactEvent wypad = ks.ConfirmPending(5, 60000, kv.Key);
            bool potwierdzone = wypad == null && ks.Pending != null && ks.Pending.Confirmed;
            bool odrzucone = wypad != null && wypad.Kind == FactEventKind.Dropped && ks.Pending == null
                             && wypad.Reason == ExecutionConfirmation.Label(kv.Key);
            T.Ok("ConfirmPending(" + kv.Key + ") " + (kv.Value ? "potwierdza kolejke" : "odrzuca kolejke z powodem"),
                 kv.Value ? potwierdzone : odrzucone, wypad == null ? "brak wpisu" : wypad.Reason);
        }

        // --- (2) ResolvePending: oba przypadki "niejednoznaczne" sciezki spoznionej odrzucaja fakty.
        var niejedn = new List<(string opis, int przed, int teraz)>
        {
            ("incydent odpalil juz PRZED decyzja w tym samym ticku", 60000, 60000),
            ("incydent odpalil ponownie PO decyzji (slad nadpisany)", 1000, 60500),
        };
        T.Ok("STRAZNIK: przypadki niejednoznaczne istnieja", niejedn.Count == 2, "");
        foreach (var (opis, przed, teraz) in niejedn)
        {
            var ks = new FactLedger();
            var q = Oczekujace(60000, 1f, 5, slad);
            q.LastFireBefore = przed;
            ks.Queue(q, out nadpisano);
            var wynik = ks.ResolvePending(61000, nazwa => teraz);
            bool ok = wynik.Count == 1 && wynik[0].Kind == FactEventKind.Dropped && ks.Count == 0 && ks.Pending == null
                      && wynik[0].Reason == ExecutionConfirmation.Label(ExecStatus.Ambiguous);
            T.Ok("ResolvePending, niejednoznaczne: " + opis + " -> odrzucenie", ok,
                 wynik.Count > 0 ? wynik[0].Kind + "/" + wynik[0].Reason : "brak");
        }

        // --- (3) FactEvent.Value to STAN po Add, nie przyrost - z tego pola analiza odtwarza licznik.
        var licz = new FactWrite { key = "licznik.x", value = 2.5f, accumulate = true, lifespanDays = 20f };
        var kl = new FactLedger();
        kl.Queue(Oczekujace(60000, 1f, 5, licz), out nadpisano);
        kl.ConfirmPending(5, 60000, ExecStatus.Executed);
        kl.ResolvePending(61000, null);
        kl.Queue(Oczekujace(62000, 62000 / 60000f, 6, licz), out nadpisano);
        kl.ConfirmPending(6, 62000, ExecStatus.Executed);
        var drugie = kl.ResolvePending(63000, null);
        float stan = kl.ValueOf("licznik.x", 62000 / 60000f, float.NaN);
        T.Ok("STRAZNIK: przyrost rozny od stanu po drugim Add (inaczej test nie odroznia pojec)",
             Math.Abs(stan - licz.value) > 1e-3f, "stan " + stan);
        T.Ok("FactEvent.Value po drugim Add == stan w ksiedze (nie przyrost)",
             drugie.Count == 1 && Math.Abs(drugie[0].Value - stan) < 1e-5f,
             drugie.Count == 1 ? drugie[0].Value + " vs " + stan : "zdarzen " + drugie.Count);

        // --- (4) Kodek kolejki: druga deklaracja z wartosciami NIEDOMYSLNYMI i ROZNYMI od pierwszej.
        // Dawny fixture mial obie deklaracje z value=1 (domyslne), accumulate=true, zycie=20 -
        // dekoder gubiacy te pola przechodzil (pulapka z CLAUDE.md 2.9).
        var inna = new FactWrite { key = "plotka.nowa", value = 2.5f, accumulate = false, lifespanDays = 7.25f };
        var polaW = typeof(FactWrite).GetFields(BindingFlags.Public | BindingFlags.Instance);
        var domW = new FactWrite();
        // Warunek wykrywalnosci: kazde pole ma w obu deklaracjach ROZNE wartosci, a w co najmniej
        // jednej - NIEDOMYSLNA. Wtedy dekoder, ktory pole gubi (domyslna) albo wymusza (stala),
        // psuje co najmniej jedna deklaracje. (Pole bool nie moze byc rozne jednoczesnie od
        // domyslnego i od pierwszej deklaracji - dlatego nie "kazde pole niedomyslne w drugiej".)
        T.Ok("STRAZNIK: kazde pole FactWrite rozni sie miedzy deklaracjami i w jednej jest niedomyslne",
             polaW.Length > 0 && polaW.All(p => !Equals(p.GetValue(inna), p.GetValue(slad))
                                                && (!Equals(p.GetValue(inna), p.GetValue(domW)) || !Equals(p.GetValue(slad), p.GetValue(domW)))),
             string.Join(",", polaW.Where(p => Equals(p.GetValue(inna), p.GetValue(slad))).Select(p => p.Name)));
        var qk = Oczekujace(123456, 123456 / 60000f, 9, slad, inna);
        PendingFacts qk2;
        bool okQ = PendingFacts.TryDecode(qk.Encode(), out qk2);
        var rozneW = new List<string>();
        if (!okQ || qk2.Writes.Count != qk.Writes.Count)
        {
            rozneW.Add("dekodowanie/liczba deklaracji");
        }
        else
        {
            for (int i = 0; i < qk.Writes.Count; i++)
            {
                foreach (FieldInfo p in polaW)
                {
                    if (!Equals(p.GetValue(qk.Writes[i]), p.GetValue(qk2.Writes[i])))
                    {
                        rozneW.Add("deklaracja " + i + "." + p.Name);
                    }
                }
            }
        }
        T.Ok("PendingFacts: round-trip KAZDEGO pola KAZDEJ deklaracji (refleksja)", rozneW.Count == 0, string.Join(",", rozneW));

        // --- (5) Dni z PRAWDZIWYCH tickow (tick/60000f) przechodza kodeki bitowo - dawne fixture'y
        // mialy dni "okragle" (12.5, 20.5), ktore przechodzil nawet stratny format "0.#".
        var tickiDni = new[] { 740123, 662000, 1502000, 5999999, 60001 };
        int rozneDni = 0;
        foreach (int t in tickiDni)
        {
            float dzien = t / 60000f;
            Fact fx;
            if (!Fact.TryDecode(new Fact { Key = "k", Value = 1f, SetDay = dzien, LifespanDays = 20f }.Encode(), out fx)
                || fx.SetDay != dzien)
            {
                rozneDni++;
            }
            PendingFacts qx;
            var qq = Oczekujace(t, dzien, 3, slad);
            if (!PendingFacts.TryDecode(qq.Encode(), out qx) || qx.Day != dzien)
            {
                rozneDni++;
            }
            PendingExecution px;
            var pp = new PendingExecution { Tick = t, GameDay = dzien, DecisionIndex = 3, IncidentDefName = "RaidEnemy" };
            pp.CaptureDecisionMemory(new WorldSnapshot { DaysSinceLastEvent = dzien / 3f, TurnsSinceThemes = ";Raid=2;", DaysPassed = 11 });
            if (!PendingExecution.TryDecode(pp.Encode(), out px) || px.GameDay != dzien
                || px.DecisionDaysSinceLastEvent != dzien / 3f)
            {
                rozneDni++;
            }
        }
        T.Ok("STRAZNIK: dni z tickow NIE sa okragle (inaczej stratny format by przeszedl)",
             tickiDni.Any(t => Math.Abs((t / 60000f) * 10f - Math.Round((t / 60000f) * 10f)) > 1e-3), "");
        T.EqI("dni z prawdziwych tickow przechodza kodeki F, Q i P BITOWO", rozneDni, 0);

        // --- (6) FactCountIn (zrodlo kolumny faktow) == liczba obowiazujacych.
        var kc = new FactLedger();
        kc.Set("a.x", 1f, 0f, 0f);
        kc.Set("b.y", 0f, 1f, 30f);
        kc.Set("c.z", 3f, 1f, 5f);
        float d6 = 20f;
        T.Ok("STRAZNIK: ksiega ma >= 2 obowiazujace i >= 1 wygasly fakt",
             kc.Active(d6).Count >= 2 && kc.Count > kc.Active(d6).Count, "aktywnych " + kc.Active(d6).Count);
        T.EqI("FactCountIn(Canonical) == Active.Count", NarratorBlackboard.FactCountIn(kc.Canonical(d6)), kc.Active(d6).Count);
        T.EqI("FactCountIn pustego napisu == 0", NarratorBlackboard.FactCountIn(string.Empty), 0);

        // --- (7) lastFireOf dostaje NAZWE incydentu: odpowiedz dla innej nazwy nie moze potwierdzic.
        var kn = new FactLedger();
        kn.Queue(Oczekujace(60000, 1f, 5, slad), out nadpisano);
        var wynikN = kn.ResolvePending(61000, nazwa => nazwa == "RaidEnemy" ? 60000 : -1);
        T.Ok("spoznione: lastFireOf pytany o NAZWE z kolejki (RaidEnemy) -> wykonane",
             wynikN.Count == 1 && wynikN[0].Kind == FactEventKind.Set, wynikN.Count > 0 ? wynikN[0].Reason : "");
        var kn2 = new FactLedger();
        var qInny = Oczekujace(60000, 1f, 5, slad);
        qInny.IncidentDefName = "ManhunterPack";
        kn2.Queue(qInny, out nadpisano);
        var wynikN2 = kn2.ResolvePending(61000, nazwa => nazwa == "RaidEnemy" ? 60000 : -1);
        T.Ok("spoznione: odpalenie INNEGO incydentu nie potwierdza kolejki",
             wynikN2.Count == 1 && wynikN2[0].Kind == FactEventKind.Dropped, wynikN2.Count > 0 ? wynikN2[0].Reason : "");

        // --- (8) Granica wygasania na siatce tickow: w ticku ustawienie + zycie*60000 fakt jest ZAWSZE
        // wygasly, interwal wczesniej - zawsze aktywny. Arytmetyka ta sama co w grze (tick/60000f).
        int przypadkow = 0, bledow = 0, szumu = 0;
        foreach (float zycie in new[] { 15f, 20f, 25f, 30f, 40f })
        {
            for (int t0 = 5 * 60000; t0 <= 100 * 60000; t0 += 1000)
            {
                int tGr = t0 + (int)(zycie * 60000f);
                var f = new Fact { Key = "g", Value = 1f, SetDay = t0 / 60000f, LifespanDays = zycie };
                przypadkow++;
                if (f.IsActive(tGr / 60000f) || !f.IsActive((tGr - 1000) / 60000f))
                {
                    bledow++;
                }
                if (tGr / 60000f - t0 / 60000f < zycie)
                {
                    szumu++;   // bez tolerancji fakt bylby tu jeszcze "aktywny"
                }
            }
        }
        T.Ok("STRAZNIK: siatka zawiera przypadki, w ktorych float32 bez tolerancji daje 'jeszcze aktywny'",
             przypadkow > 0 && szumu >= 1, "przypadkow " + przypadkow + ", szumu " + szumu);
        T.EqI("granica wygasania na siatce tickow: tick granicy wygasly, interwal wczesniej aktywny", bledow, 0);

        // (8b) Ta sama tolerancja w Cond_FaktOd: w ticku, w ktorym wiek faktu osiaga minDays, warunek jest
        // spelniony; interwal wczesniej - nie. Wiek idzie przez postac kanoniczna (G9), jak w grze.
        int przypOd = 0, bledowOd = 0, szumuOd = 0;
        foreach (float minDni in new[] { 1f, 7f, 15f })
        {
            for (int t0 = 5 * 60000; t0 <= 60 * 60000; t0 += 1000)
            {
                var kf = new FactLedger();
                kf.Set("od", 1f, t0 / 60000f, 0f);
                int tPr = t0 + (int)(minDni * 60000f);
                var warunek = new Cond_FaktOd { key = "od", minDays = minDni };
                bool wProgu = warunek.IsMet(new WorldSnapshot { Facts = new NarratorBlackboard(null, null, kf, tPr / 60000f).FactsCanonical() });
                bool przed = warunek.IsMet(new WorldSnapshot { Facts = new NarratorBlackboard(null, null, kf, (tPr - 1000) / 60000f).FactsCanonical() });
                przypOd++;
                if (!wProgu || przed)
                {
                    bledowOd++;
                }
                if (tPr / 60000f - t0 / 60000f < minDni)
                {
                    szumuOd++;
                }
            }
        }
        T.Ok("STRAZNIK: siatka Cond_FaktOd zawiera przypadki, w ktorych wiek float32 jest ponizej progu",
             przypOd > 0 && szumuOd >= 1, "przypadkow " + przypOd + ", szumu " + szumuOd);
        T.EqI("Cond_FaktOd na siatce tickow: spelniony w ticku progu, niespelniony interwal wczesniej", bledowOd, 0);

        // --- (9) Klucz musi zaczynac sie litera albo cyfra ("-" to token pustosci kodeka).
        foreach (string k in new[] { "-", "-a", ".a", "_a" })
        {
            T.Ok("IsValidKey odrzuca klucz bez alfanumerycznego pierwszego znaku: \"" + k + "\"", !FactLedger.IsValidKey(k), k);
        }
        T.Ok("IsValidKey przyjmuje '-' i '.' wewnatrz klucza", FactLedger.IsValidKey("a-b.c_d"), "");
        var kminus = new FactLedger();
        T.Ok("Set odrzuca klucz \"-\" (inaczej fakt ginie po zapisie)", !kminus.Set("-", 1f, 0f, 0f) && kminus.Count == 0, "");

        // --- (10) Zamkniecia: rzadki luk zachowuje swoje ostatnie zamkniecie; wczytanie zostawia NAJNOWSZE.
        var kz = new ArcLedger();
        kz.RecordClosure(new ArcInstance { ArcId = "PN_Luk_Rzadki", Number = 1, PhaseId = "Rozwiazanie" }, ArcDirector.OutcomeResolved, 1f);
        int innych = ArcLedger.MaxClosed + 4;
        for (int i = 0; i < innych; i++)
        {
            kz.RecordClosure(new ArcInstance { ArcId = i % 2 == 0 ? "PN_Luk_A" : "PN_Luk_B", Number = i + 2, PhaseId = "Rozwiazanie" },
                             ArcDirector.OutcomeFaded, 2f + i);
        }
        T.Ok("STRAZNIK: po rzadkim luku zamknieto wiecej niz MaxClosed innych (stara regula by go wypchnela)",
             innych > ArcLedger.MaxClosed, "innych " + innych);
        T.Ok("rzadki luk zachowuje swoje ostatnie zamkniecie, bufor ma MaxClosed wpisow",
             kz.Closed.Count == ArcLedger.MaxClosed && kz.LastClosure("PN_Luk_Rzadki") != null
             && kz.LastClosure("PN_Luk_A") != null && kz.LastClosure("PN_Luk_A").Number == innych,
             "wpisow " + kz.Closed.Count);
        var linie = new List<string>();
        int wZapisie = ArcLedger.MaxClosed + 4;
        for (int i = 1; i <= wZapisie; i++)
        {
            linie.Add(new ArcClosure { ArcId = "PN_Luk_" + i, Number = i, Outcome = ArcDirector.OutcomeFaded, FinalPhaseId = "X", Day = i }.Encode());
        }
        var kw = new ArcLedger();
        int odrzZ = kw.RestoreFromLines(linie);
        T.Ok("wczytanie ponad MaxClosed linii Z zostawia NAJNOWSZE (jak gra) i nie liczy nadmiaru jako odrzuconego",
             odrzZ == 0 && kw.Closed.Count == ArcLedger.MaxClosed && kw.Closed[kw.Closed.Count - 1].Number == wZapisie
             && kw.Closed[0].Number == wZapisie - ArcLedger.MaxClosed + 1,
             "odrzuconych " + odrzZ + ", pierwszy " + (kw.Closed.Count > 0 ? kw.Closed[0].Number : -1));

        // --- (12) Pamiec decyzji w PendingExecution: kodek + nakladka, TAKZE przy pelnym buforze.
        var pe = new PendingExecution { Tick = 740000, GameDay = 740000 / 60000f, DecisionIndex = 30, IncidentDefName = "RaidEnemy", LastFireBefore = 5 };
        pe.CaptureDecisionMemory(new WorldSnapshot { TurnsSinceThemes = ";Raid=3;Social=-1;", DaysSinceLastEvent = 2.75f, DaysPassed = 12 });
        PendingExecution pe2;
        bool okP = PendingExecution.TryDecode(pe.Encode(), out pe2);
        T.Ok("PendingExecution: round-trip pamieci decyzji (flaga, wiek tematow, dni od zdarzenia, dzien gry)",
             okP && pe2.HasDecisionMemory && pe2.DecisionTurnsSinceThemes == pe.DecisionTurnsSinceThemes
             && pe2.DecisionDaysSinceLastEvent == pe.DecisionDaysSinceLastEvent && pe2.DecisionDaysPassed == pe.DecisionDaysPassed
             && pe2.Tick == pe.Tick && pe2.DecisionIndex == pe.DecisionIndex, okP ? pe.Encode() : "dekodowanie");
        var bezPamieci = new PendingExecution { Tick = 1000, GameDay = 1000 / 60000f, DecisionIndex = 1, IncidentDefName = "RaidEnemy" };
        PendingExecution bp;
        T.Ok("PendingExecution bez pamieci: round-trip z HasDecisionMemory == false",
             PendingExecution.TryDecode(bezPamieci.Encode(), out bp) && !bp.HasDecisionMemory, bezPamieci.Encode());
        string[] polaLinii = pe.Encode().Split('|');
        string stara = string.Join("|", polaLinii.Take(PendingExecution.LegacyFieldCount));
        PendingExecution ps;
        T.Ok("linia P sprzed S6 (" + PendingExecution.LegacyFieldCount + " pol) nadal sie wczytuje, bez pamieci decyzji",
             PendingExecution.TryDecode(stara, out ps) && !ps.HasDecisionMemory && ps.Tick == pe.Tick, stara);
        PendingExecution pz;
        T.Ok("polowiczna pamiec decyzji (dni NaN) jest odrzucana w calosci",
             !PendingExecution.TryDecode(pe.Encode().Replace("|2.75|", "|NaN|"), out pz), pe.Encode());

        // Nakladka przy PELNYM buforze: najstarszy wpis (jedyny Supernatural) wypada przy RecordEvent
        // decyzji, wiec snapshot zbudowany od nowa na sciezce spoznionej widzialby inny swiat.
        var h = new EventHistory();
        h.RecordEvent(TestsDecision.Ev("PN_Akcja_Emanator", Theme.Supernatural, Valence.Negative, EventScale.Major), 0.5f, 30000);
        for (int i = 1; i < EventHistory.DefaultCapacity; i++)
        {
            h.RecordEvent(TestsDecision.Ev(i % 2 == 0 ? "PN_Akcja_Napad" : "PN_Akcja_Zrzut", i % 2 == 0 ? Theme.Raid : Theme.Economic,
                                           Valence.Negative, EventScale.Minor), 1f + i, 60000 * (1 + i));
        }
        float dzienDecyzji = 40f;
        var decyzji = new WorldSnapshot
        {
            TurnsSinceThemes = new NarratorBlackboard(h, null, null, dzienDecyzji).TurnsSinceThemesCanonical(),
            DaysSinceLastEvent = dzienDecyzji - h.Newest.GameDay,
            DaysPassed = 40
        };
        var czeka = new PendingExecution { Tick = 2400000, GameDay = dzienDecyzji, DecisionIndex = h.DecisionCount, IncidentDefName = "RaidEnemy" };
        czeka.CaptureDecisionMemory(decyzji);
        h.RecordEvent(TestsDecision.Ev("PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major), dzienDecyzji, 2400000);
        float dzienSpoz = dzienDecyzji + 1f / 60f;
        var spozniony = new WorldSnapshot
        {
            TurnsSinceThemes = new NarratorBlackboard(h, null, null, dzienSpoz).TurnsSinceThemesCanonical(),
            DaysSinceLastEvent = dzienSpoz - h.Newest.GameDay,
            DaysPassed = 40
        };
        T.Ok("STRAZNIK: bufor byl pelny, a snapshot zbudowany od nowa ROZNI sie od snapshotu decyzji",
             h.Count == EventHistory.DefaultCapacity && spozniony.TurnsSinceThemes != decyzji.TurnsSinceThemes
             && spozniony.DaysSinceLastEvent != decyzji.DaysSinceLastEvent,
             decyzji.TurnsSinceThemes + " vs " + spozniony.TurnsSinceThemes);
        bool nalozono = czeka.ApplyDecisionMemoryTo(spozniony);
        T.Ok("po nakladce pola historii sa IDENTYCZNE jak w snapshocie decyzji",
             nalozono && spozniony.TurnsSinceThemes == decyzji.TurnsSinceThemes
             && spozniony.DaysSinceLastEvent == decyzji.DaysSinceLastEvent && spozniony.DaysPassed == decyzji.DaysPassed,
             spozniony.TurnsSinceThemes);
        // "ps != null": odrzucona stara linia ma zapalic ASERCJE powyzej, a nie wywrocic przebiegu tutaj.
        T.Ok("nakladka bez pamieci (stara linia) nie zmienia snapshotu",
             ps != null && !ps.ApplyDecisionMemoryTo(new WorldSnapshot()), ps == null ? "stara linia niezdekodowana" : "");

        // --- (13) Cond_TurBezTematu na PUSTEJ historii: poza horyzontem = spelnione (domyslnie).
        // SPROSTOWANIE wpisu S2 ("pusty snapshot nie spelnia") - ten warunek jest wyjatkiem z deklaracji.
        var pusty = new WorldSnapshot();
        T.Ok("Cond_TurBezTematu na pustym snapshocie: domyslnie SPELNIONY (temat poza horyzontem)",
             new Cond_TurBezTematu { theme = Theme.Raid }.IsMet(pusty), "");
        T.Ok("Cond_TurBezTematu na pustym snapshocie z beyondHorizonCounts=false: niespelniony",
             !new Cond_TurBezTematu { theme = Theme.Raid, beyondHorizonCounts = false }.IsMet(pusty), "");

        // --- (14) Uzyteczny czas zycia: 0 albo mniej (wieczny) albo co najmniej dzien.
        var zycia = new List<(float zycie, bool ok)> { (0f, true), (-1f, true), (1f, true), (20f, true), (0.5f, false), (0.01f, false), (float.NaN, false) };
        foreach (var (zycie, ok) in zycia)
        {
            T.Ok("IsUsableLifespan(" + zycie + ") == " + ok, FactLedger.IsUsableLifespan(zycie) == ok, "");
        }
    }

    // ---------------------------------------------------------------------------------
    //  13h - kolejka faktow: zakolejkowanie, potwierdzenie, rozstrzygniecie, kodek
    // ---------------------------------------------------------------------------------
    static PendingFacts Oczekujace(int tick, float dzien, int decyzja, params FactWrite[] zapisy)
    {
        return new PendingFacts
        {
            Tick = tick, Day = dzien, DecisionIndex = decyzja, IncidentDefName = "RaidEnemy",
            LastFireBefore = 1000, Writes = zapisy.ToList()
        };
    }

    static void KolejkaFaktow()
    {
        T.Section("TEST 13h - kolejka faktow: fakt trafia do pamieci DOPIERO po rozstrzygnieciu, z dniem decyzji");

        var slad = new FactWrite { key = "walka.byla", value = 1f, accumulate = true, lifespanDays = 20f };
        var trofea = new FactWrite { key = "lupy.zebrane", value = 1f, accumulate = true, lifespanDays = 20f };

        // --- sciezka normalna: zakolejkuj -> potwierdz (ten sam tick) -> NIC w ksiedze -> rozstrzygnij
        var ks = new FactLedger();
        bool nadpisano;
        var odrz = ks.Queue(Oczekujace(60000, 1f, 5, slad, trofea), out nadpisano);
        T.Ok("zakolejkowanie poprawnych deklaracji: nic odrzuconego, kolejka pusta wczesniej",
             odrz.Count == 0 && !nadpisano && ks.Pending != null && ks.Pending.Writes.Count == 2, "");
        T.EqI("po zakolejkowaniu ksiega jest NADAL pusta (fakt nie jest widoczny w turze decyzji)", ks.Count, 0);

        T.Ok("potwierdzenie wykonania tylko OZNACZA kolejke",
             ks.ConfirmPending(5, 60000, ExecStatus.Executed) == null && ks.Pending != null && ks.Pending.Confirmed
             && ks.Count == 0, "Count " + ks.Count);

        // W tym samym ticku rozstrzygniecie nic nie robi - fakt ma byc widoczny od NASTEPNEGO wywolania.
        T.EqI("rozstrzygniecie w TYM SAMYM ticku nie stosuje faktow", ks.ResolvePending(60000, null).Count, 0);
        var zast = ks.ResolvePending(61000, null);
        T.Ok("nastepne wywolanie stosuje oba fakty i czysci kolejke",
             zast.Count == 2 && zast.All(e => e.Kind == FactEventKind.Set) && ks.Pending == null
             && ks.Has("walka.byla", 1f) && ks.Has("lupy.zebrane", 1f), string.Join(",", zast.Select(e => e.Key)));
        // Wiek liczony od dnia DECYZJI (1.0), nie od dnia zastosowania (61000 tickow ~ 1.017).
        T.Eq("fakt ostemplowany dniem DECYZJI, nie dniem zastosowania",
             ks.AgeDays("walka.byla", 11f), 10f, 1e-5f);
        T.Ok("wpis logu niesie zrodlo: tick i numer decyzji zdarzenia",
             zast.All(e => e.SourceTick == 60000 && e.SourceDecision == 5), "");

        // --- zdarzenie niewykonane na sciezce normalnej: kolejka wypada OD RAZU, z wpisem
        var kn = new FactLedger();
        kn.Queue(Oczekujace(60000, 1f, 5, slad), out nadpisano);
        FactEvent wypad = kn.ConfirmPending(5, 60000, ExecStatus.NotExecuted);
        T.Ok("niewykonane: kolejka wypada, wpis 'odrzucenie' z powodem",
             wypad != null && wypad.Kind == FactEventKind.Dropped && kn.Pending == null
             && wypad.Reason == ExecutionConfirmation.Label(ExecStatus.NotExecuted), wypad == null ? "brak" : wypad.Reason);
        T.EqI("niewykonane: ksiega pusta takze po nastepnym wywolaniu", kn.ResolvePending(61000, null).Count + kn.Count, 0);

        // --- potwierdzenie CUDZEJ decyzji nie rusza kolejki
        var kc = new FactLedger();
        kc.Queue(Oczekujace(60000, 1f, 5, slad), out nadpisano);
        T.Ok("werdykt innej decyzji (inny numer) nie potwierdza kolejki",
             kc.ConfirmPending(6, 60000, ExecStatus.Executed) == null && !kc.Pending.Confirmed, "");
        T.Ok("werdykt innego ticku nie potwierdza kolejki",
             kc.ConfirmPending(5, 59000, ExecStatus.Executed) == null && !kc.Pending.Confirmed, "");

        // --- sciezka spozniona: iterator nie wrocil, rozstrzyga lastFireTicks (ta sama klasyfikacja co luki)
        var kl = new FactLedger();
        kl.Queue(Oczekujace(60000, 1f, 5, slad), out nadpisano);
        var spoz = kl.ResolvePending(61000, nazwa => 60000);
        T.Ok("spoznione: incydent odpalil w ticku decyzji -> fakty zastosowane",
             spoz.Count == 1 && spoz[0].Kind == FactEventKind.Set && kl.Has("walka.byla", 2f)
             && spoz[0].Reason == ExecutionConfirmation.Label(ExecStatus.LateExecuted), spoz.Count > 0 ? spoz[0].Reason : "");

        var kl2 = new FactLedger();
        kl2.Queue(Oczekujace(60000, 1f, 5, slad), out nadpisano);
        var spoz2 = kl2.ResolvePending(61000, nazwa => 1000);
        T.Ok("spoznione: brak sladu odpalenia -> fakty odrzucone",
             spoz2.Count == 1 && spoz2[0].Kind == FactEventKind.Dropped && kl2.Count == 0 && kl2.Pending == null, "");

        // Klasyfikacja spozniona jest JEDNA dla lukow i faktow - te same liczby, ten sam werdykt.
        var wzorLuku = new PendingExecution { Tick = 60000, LastFireBefore = 1000 };
        int zgodnych = 0, przypadkow = 0;
        foreach (int teraz in new[] { -1, 1000, 59999, 60000, 60001 })
        {
            przypadkow++;
            if (ExecutionConfirmation.ClassifyLate(teraz, wzorLuku) == ExecutionConfirmation.ClassifyLate(teraz, 1000, 60000))
            {
                zgodnych++;
            }
        }
        T.EqI("ClassifyLate dla lukow i dla faktow daje ten sam werdykt w kazdym przypadku", zgodnych, przypadkow);

        // --- deklaracja o zlym kluczu nie trafia do kolejki, poprawne zostaja
        var kz = new FactLedger();
        var odrzucone = kz.Queue(Oczekujace(60000, 1f, 5, slad, new FactWrite { key = "zly|klucz", value = 1f }), out nadpisano);
        T.Ok("zly klucz odrzucony przy kolejkowaniu, poprawny zostaje",
             odrzucone.Count == 1 && odrzucone[0].Kind == FactEventKind.Rejected && kz.Pending != null
             && kz.Pending.Writes.Count == 1 && kz.Pending.Writes[0].key == "walka.byla", "");
        var samZly = new FactLedger();
        samZly.Queue(Oczekujace(60000, 1f, 5, new FactWrite { key = "zly;klucz" }), out nadpisano);
        T.Ok("zdarzenie bez zadnej poprawnej deklaracji nie zostawia kolejki", samZly.Pending == null, "");

        // --- nadpisanie niepustej kolejki jest zglaszane
        var kd = new FactLedger();
        kd.Queue(Oczekujace(60000, 1f, 5, slad), out nadpisano);
        kd.Queue(Oczekujace(61000, 1.1f, 6, trofea), out nadpisano);
        T.Ok("drugie zakolejkowanie przy niepustej kolejce zglasza nadpisanie", nadpisano, "");

        // --- kodek kolejki: round-trip pole po polu (refleksja), razem z deklaracjami
        var q = Oczekujace(123456, 20.5f, 9, slad, trofea);
        q.Confirmed = true;
        var pola = typeof(PendingFacts).GetFields(BindingFlags.Public | BindingFlags.Instance);
        var domyslna = new PendingFacts();
        T.Ok("STRAZNIK: fixture ustawia KAZDE pole PendingFacts na wartosc niedomyslna",
             pola.All(p => p.FieldType == typeof(List<FactWrite>) || !Equals(p.GetValue(q), p.GetValue(domyslna))),
             string.Join(",", pola.Where(p => p.FieldType != typeof(List<FactWrite>)
                                           && Equals(p.GetValue(q), p.GetValue(domyslna))).Select(p => p.Name)));
        PendingFacts q2;
        bool ok = PendingFacts.TryDecode(q.Encode(), out q2);
        var rozne = ok ? pola.Where(p => p.FieldType != typeof(List<FactWrite>) && !Equals(p.GetValue(q), p.GetValue(q2)))
                             .Select(p => p.Name).ToList()
                       : new List<string> { "dekodowanie" };
        T.Ok("PendingFacts: round-trip pol skalarnych", ok && rozne.Count == 0, string.Join(",", rozne));
        T.Ok("PendingFacts: round-trip deklaracji (klucz, wartosc, tryb, czas zycia, kolejnosc)",
             ok && q2.Writes.Count == 2
             && q2.Writes[0].key == "walka.byla" && q2.Writes[0].accumulate && q2.Writes[0].lifespanDays == 20f
             && q2.Writes[1].key == "lupy.zebrane" && q2.Writes[1].value == 1f, ok ? q.Encode() : "");

        string wz = q.Encode();
        var zle = new List<(string opis, string linia)>
        {
            ("za malo pol", wz.Substring(0, wz.LastIndexOf('|'))),
            ("zly znacznik", "X" + wz.Substring(1)),
            ("potwierdzenie nie 0/1", wz.Replace("|1|walka", "|2|walka")),
            ("deklaracja z trzema polami", wz.Replace("walka.byla,1,1,20", "walka.byla,1,1")),
            ("deklaracja ze zlym kluczem", wz.Replace("walka.byla", "walka byla")),
        };
        T.Ok("STRAZNIK: tablica uszkodzen kolejki nie jest pusta", zle.Count > 0, "");
        foreach (var (opis, linia) in zle)
        {
            PendingFacts x;
            T.Ok("PendingFacts odrzuca uszkodzona linie: " + opis, !PendingFacts.TryDecode(linia, out x) && x == null, linia);
        }

        // Kolejka przezywa zapis gry i klon checkpointu - razem z faktami, jako jedna linia Q.
        var kk = new FactLedger();
        kk.Set("wiesci.zrodlo", 1f, 3f, 25f);
        kk.Queue(Oczekujace(60000, 1f, 5, slad), out nadpisano);
        var kopia = kk.Clone();
        T.Ok("Clone przenosi fakty i kolejke", kopia.Count == 1 && kopia.Pending != null
             && kopia.Pending.Encode() == kk.Pending.Encode(), "");
        var dwieQ = new List<string>(kk.ToPersistableLines()) { kk.Pending.Encode() };
        var zDwiema = new FactLedger();
        T.EqI("druga linia kolejki w zapisie jest odrzucana (najwyzej jedna kolejka)",
              zDwiema.RestoreFromLines(dwieQ), 1);

        // Zapis bez kolejki (stary) wczytuje sie bez odrzucen.
        var bezQ = new FactLedger();
        bezQ.Set("a", 1f, 0f, 0f);
        T.EqI("ksiega bez kolejki: round-trip bez odrzucen", new FactLedger().RestoreFromLines(bezQ.ToPersistableLines()), 0);
    }

    // ---------------------------------------------------------------------------------
    //  13g - slot konsekwencji na katalogu SYNTETYCZNYM
    // ---------------------------------------------------------------------------------
    static void SlotKonsekwencji()
    {
        T.Section("TEST 13g - slot konsekwencji: enumeracja, sygnatura, deklaracja faktow");

        // Katalog syntetyczny, a nie prawdziwy: mechanizm slotu ma byc sprawdzony NIEZALEZNIE od
        // tego, czy tresc juz powstala. Inaczej test mechanizmu czekalby na katalog, a mutacja
        // wyjmujaca slot z enumeracji przechodzilaby na zielono az do dosypki XML.
        var akcja = new Block { Id = "A_Akcja", Type = BlockType.Action, Payload = "X",
                                Theme = Theme.Raid, Valence = Valence.Negative, Scale = EventScale.Major };
        var aktor = new Block { Id = "A_Aktor", Type = BlockType.Actor };
        var cel = new Block { Id = "A_Cel", Type = BlockType.Target };
        var k1 = new Block { Id = "A_Kons1", Type = BlockType.Consequence, Intensity = IntensityLevel.Normal };
        var k2 = new Block { Id = "A_Kons2", Type = BlockType.Consequence, Intensity = IntensityLevel.Normal };
        k1.FactsOnExecute.Add(new FactWrite { key = "slad.pierwszy", value = 1f });
        k2.FactsOnExecute.Add(new FactWrite { key = "slad.drugi", value = 2f, accumulate = true, lifespanDays = 10f });

        var bezKons = new EventComposer(new[] { akcja, aktor, cel }, new CompatibilityGraph());
        var zKons = new EventComposer(new[] { akcja, aktor, cel, k1, k2 }, new CompatibilityGraph());

        VariantEnumerationStats st1, st2;
        var wBez = bezKons.EnumerateVariants(akcja, null, 100, new SeededRandom(1), out st1);
        var wZ = zKons.EnumerateVariants(akcja, null, 100, new SeededRandom(1), out st2);

        // Asercje sa pisane ODPORNIE na pusty wynik: mutacja wyjmujaca slot z enumeracji ma
        // zapalic ASERCJE z czytelnym komunikatem, a nie wywrocic przebieg wyjatkiem z LINQ.
        // Wykrycie awaria jest w tym projekcie forma slabsza - patrz L34/L39 z kroku 5.
        string sigBez = wBez.Count > 0 ? wBez[0].Signature : "brak wariantow";
        string sigZ = wZ.Count > 0 ? wZ[0].Signature : "brak wariantow";
        T.Ok("STRAZNIK: katalog syntetyczny w ogole produkuje warianty", wBez.Count > 0, "bez konsekwencji " + wBez.Count);
        T.EqI("dwa klocki konsekwencji MNOZA liczbe wariantow przez dwa", wZ.Count, wBez.Count * 2);
        T.Ok("slot pusty, gdy katalog nie ma konsekwencji (pusty tylko WYMUSZONY)",
             wBez.Count > 0 && wBez.All(e => e.Signature.Split('|').Last() == "-"), sigBez);
        T.Ok("slot wypelniony, gdy konsekwencje istnieja",
             wZ.Count > 0 && wZ.All(e => e.Signature.Split('|').Last() != "-"), sigZ);
        T.Ok("obie konsekwencje pojawiaja sie w sygnaturach",
             wZ.Any(e => e.Signature.EndsWith("|A_Kons1")) && wZ.Any(e => e.Signature.EndsWith("|A_Kons2")),
             string.Join(" ", wZ.Select(e => e.Signature)));
        T.Ok("sygnatura ma szesc segmentow",
             wZ.Count > 0 && wZ.All(e => e.Signature.Split('|').Length == 6), sigZ);

        // Deklaracja faktow jedzie z klockiem do gotowego kandydata - inaczej warstwa integracji
        // nie mialaby czego zapisac po potwierdzonym wykonaniu.
        var zK1 = wZ.FirstOrDefault(e => e.Signature.EndsWith("|A_Kons1"));
        var deklaracje = zK1 == null ? new List<FactWrite>() : zK1.Blocks.SelectMany(b => b.FactsOnExecute).ToList();
        T.EqI("kandydat niesie deklaracje faktow swojego klocka konsekwencji", deklaracje.Count, 1);
        T.EqS("i jest to wlasciwy klucz", deklaracje.Count > 0 ? deklaracje[0].key : "brak", "slad.pierwszy");

        // Zastosowanie deklaracji do ksiegi - Set kontra Add.
        var ks = new FactLedger();
        new FactWrite { key = "licz", value = 2f, accumulate = true }.ApplyTo(ks, 1f);
        new FactWrite { key = "licz", value = 3f, accumulate = true }.ApplyTo(ks, 2f);
        new FactWrite { key = "flaga", value = 5f }.ApplyTo(ks, 2f);
        new FactWrite { key = "flaga", value = 7f }.ApplyTo(ks, 3f);
        T.Eq("accumulate=true dolicza", ks.ValueOf("licz", 2f, -1f), 5f, 1e-6f);
        T.Eq("accumulate=false nadpisuje", ks.ValueOf("flaga", 3f, -1f), 7f, 1e-6f);
        T.Ok("ApplyTo odrzuca niepoprawny klucz i nie rusza ksiegi",
             !new FactWrite { key = "zly|klucz" }.ApplyTo(ks, 3f) && ks.Count == 2, "Count " + ks.Count);
        T.Ok("ApplyTo bez ksiegi nie rzuca", !new FactWrite { key = "ok" }.ApplyTo(null, 1f), "");
    }

    // ---------------------------------------------------------------------------------
    //  13f - warunki czytajace pamiec przez snapshot
    // ---------------------------------------------------------------------------------
    static void WarunkiPamieci()
    {
        T.Section("TEST 13f - warunki pamieci (Cond_Fakt, Cond_FaktOd, Cond_FaktLiczba, Cond_Watek*, Cond_TurBezTematu)");

        var fakty = new FactLedger();
        fakty.Set("napad.odparty", 2f, 4f, 0f);
        fakty.Set("zero", 0f, 4f, 0f);
        var arcs = new ArcLedger();
        arcs.Active.Add(new ArcInstance { ArcId = "PN_Luk_Wendeta", Number = 1, PhaseId = "Eskalacja" });
        arcs.RecordClosure(new ArcInstance { ArcId = "PN_Luk_Scigani", Number = 1, PhaseId = "Kulminacja" },
                           ArcDirector.OutcomeFaded, 8f);
        var h = new EventHistory();
        h.RecordEvent(TestsDecision.Ev("PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major), 1f, 0);
        h.RecordEvent(TestsDecision.Ev("PN_Akcja_Zrzut", Theme.Economic, Valence.Positive, EventScale.Minor), 2f, 0);

        var bb = new NarratorBlackboard(h, arcs, fakty, 10f);
        var s = new WorldSnapshot
        {
            Facts = bb.FactsCanonical(),
            Threads = bb.ThreadsCanonical(),
            TurnsSinceThemes = bb.TurnsSinceThemesCanonical()
        };

        T.Ok("Cond_Fakt: obecny fakt spelnia, nieobecny nie",
             new Cond_Fakt { key = "napad.odparty" }.IsMet(s) && !new Cond_Fakt { key = "nie.ma" }.IsMet(s), s.Facts);
        T.Ok("Cond_Fakt: fakt o wartosci 0 tez jest obecny",
             new Cond_Fakt { key = "zero" }.IsMet(s), s.Facts);
        T.Ok("Cond_Fakt z required=false dziala jako zaprzeczenie",
             new Cond_Fakt { key = "nie.ma", required = false }.IsMet(s)
             && !new Cond_Fakt { key = "zero", required = false }.IsMet(s), s.Facts);

        // Wiek faktu: ustawiony w dniu 4, blackboard w dniu 10 -> 6 dni.
        float wiek = bb.GameDay - 4f;
        T.Ok("Cond_FaktOd: spelniony dla progu ponizej wieku, niespelniony powyzej",
             new Cond_FaktOd { key = "napad.odparty", minDays = wiek - 0.5f }.IsMet(s)
             && !new Cond_FaktOd { key = "napad.odparty", minDays = wiek + 0.5f }.IsMet(s), "wiek " + wiek);
        T.Ok("Cond_FaktOd: BRAK faktu nie spelnia (nigdy != dawno)",
             !new Cond_FaktOd { key = "nie.ma", minDays = 0f }.IsMet(s), s.Facts);

        T.Ok("Cond_FaktLiczba: przedzial domkniety obustronnie",
             new Cond_FaktLiczba { key = "napad.odparty", min = 2f, max = 2f }.IsMet(s)
             && !new Cond_FaktLiczba { key = "napad.odparty", min = 3f }.IsMet(s)
             && !new Cond_FaktLiczba { key = "napad.odparty", max = 1f }.IsMet(s), s.Facts);
        T.Ok("Cond_FaktLiczba: brak faktu nie spelnia nawet przy przedziale obejmujacym zero",
             !new Cond_FaktLiczba { key = "nie.ma", min = -1f, max = 1f }.IsMet(s), s.Facts);

        T.Ok("Cond_WatekOtwarty: otwarty spelnia, zamkniety nie",
             new Cond_WatekOtwarty { arc = "PN_Luk_Wendeta" }.IsMet(s)
             && !new Cond_WatekOtwarty { arc = "PN_Luk_Scigani" }.IsMet(s), s.Threads);
        T.Ok("Cond_WatekOtwarty z required=false",
             new Cond_WatekOtwarty { arc = "PN_Luk_Scigani", required = false }.IsMet(s), s.Threads);

        T.Ok("Cond_WatekZamkniety: dowolny wynik i wynik konkretny",
             new Cond_WatekZamkniety { arc = "PN_Luk_Scigani" }.IsMet(s)
             && new Cond_WatekZamkniety { arc = "PN_Luk_Scigani", outcome = ArcDirector.OutcomeFaded }.IsMet(s)
             && !new Cond_WatekZamkniety { arc = "PN_Luk_Scigani", outcome = ArcDirector.OutcomeResolved }.IsMet(s), s.Threads);
        T.Ok("Cond_WatekZamkniety: watek tylko otwarty nie spelnia",
             !new Cond_WatekZamkniety { arc = "PN_Luk_Wendeta" }.IsMet(s), s.Threads);

        // Historia: dwie emisje, najnowsza Economic. Wyprowadzenie z AgeInDecisions, nie z liczby.
        int turRaid = bb.TurnsSinceTheme(Theme.Raid);
        T.Ok("Cond_TurBezTematu: prog rowny liczbie tur spelnia, wiekszy nie",
             new Cond_TurBezTematu { theme = Theme.Raid, min = turRaid }.IsMet(s)
             && !new Cond_TurBezTematu { theme = Theme.Raid, min = turRaid + 1 }.IsMet(s), "tur " + turRaid);
        T.Ok("Cond_TurBezTematu: poza horyzontem liczy sie albo nie, ZALEZNIE OD DEKLARACJI",
             new Cond_TurBezTematu { theme = Theme.Supernatural, min = 99 }.IsMet(s)
             && !new Cond_TurBezTematu { theme = Theme.Supernatural, min = 1, beyondHorizonCounts = false }.IsMet(s),
             s.TurnsSinceThemes);

        // Pusty snapshot (mapa bez pamieci) nie moze przepuszczac warunkow pamieciowych "na tak".
        // WYJATEK z deklaracji: Cond_TurBezTematu z domyslnym beyondHorizonCounts=true (TEST 13i).
        var pusty = new WorldSnapshot();
        T.Ok("na pustym snapshocie warunki pamieci sa NIESPELNIONE (poza jawnymi zaprzeczeniami)",
             !new Cond_Fakt { key = "x" }.IsMet(pusty)
             && !new Cond_FaktOd { key = "x", minDays = 0f }.IsMet(pusty)
             && !new Cond_FaktLiczba { key = "x" }.IsMet(pusty)
             && !new Cond_WatekOtwarty { arc = "x" }.IsMet(pusty)
             && !new Cond_WatekZamkniety { arc = "x" }.IsMet(pusty), "");

        // Slad idzie do logu decyzji (Block.FirstUnmetCondition) - opis musi niesc klucz.
        T.Ok("Describe niesie klucz albo luk (inaczej slad w logu nie wskaze przyczyny)",
             new Cond_Fakt { key = "napad.odparty" }.Describe().Contains("napad.odparty")
             && new Cond_WatekOtwarty { arc = "PN_Luk_Wendeta" }.Describe().Contains("PN_Luk_Wendeta")
             && new Cond_TurBezTematu { theme = Theme.Raid }.Describe().Contains("Raid"), "");
    }

    // ---------------------------------------------------------------------------------
    //  13a - kodek faktow
    // ---------------------------------------------------------------------------------
    static void KodekFaktow()
    {
        T.Section("TEST 13a - kodek faktow (Fact, FactLedger): round-trip, uszkodzenia, alfabet klucza");

        var f = new Fact { Key = "ruiny.otwarte", Value = 3f, SetDay = 12.5f, LifespanDays = 40f };
        var pola = typeof(Fact).GetFields(BindingFlags.Public | BindingFlags.Instance);
        var domyslny = new Fact();
        T.Ok("STRAZNIK: fixture ustawia KAZDE pole Fact na wartosc niedomyslna",
             pola.Length > 0 && pola.All(p => !Equals(p.GetValue(f), p.GetValue(domyslny))),
             "pol " + pola.Length + ", domyslne: "
             + string.Join(",", pola.Where(p => Equals(p.GetValue(f), p.GetValue(domyslny))).Select(p => p.Name)));

        Fact g;
        bool ok = Fact.TryDecode(f.Encode(), out g);
        var rozne = ok ? pola.Where(p => !Equals(p.GetValue(f), p.GetValue(g))).Select(p => p.Name).ToList()
                       : new List<string> { "dekodowanie" };
        T.Ok("Fact: round-trip odtwarza wszystkie pola (porownanie refleksja, nie przez Encode)",
             ok && rozne.Count == 0, "pol " + pola.Length + ", roznych: " + string.Join(",", rozne));

        string wz = f.Encode();
        var zle = new List<(string opis, string linia)>
        {
            ("za malo pol", wz.Substring(0, wz.LastIndexOf('|'))),
            ("za duzo pol", wz + "|1"),
            ("zly znacznik", "X" + wz.Substring(1)),
            ("wartosc nieliczbowa", wz.Replace("|3|", "|x|")),
            ("dzien NaN", wz.Replace("|12.5|", "|NaN|")),
            ("czas zycia nieskonczony", wz.Replace("|40", "|Infinity")),
            ("pusty klucz", wz.Replace("|ruiny.otwarte|", "|-|")),
            ("klucz spoza alfabetu", wz.Replace("|ruiny.otwarte|", "|ruiny otwarte|")),
        };
        T.Ok("STRAZNIK: tablica uszkodzen faktu nie jest pusta", zle.Count > 0, "przypadkow " + zle.Count);
        foreach (var (opis, linia) in zle)
        {
            Fact x;
            T.Ok("Fact odrzuca uszkodzona linie: " + opis, !Fact.TryDecode(linia, out x) && x == null, linia);
        }

        // Alfabet klucza jest waski, bo klucz jedzie przez DWA wyjscia: kodek pamieci (separator '|')
        // i postac kanoniczna w snapshocie (';', '=', '@'). Kazdy z tych znakow rozbilby jedno z nich.
        var zleKlucze = new[] { null, "", "a|b", "a;b", "a=b", "a@b", "a b", "zolw" + (char)0x105, new string('x', 65) };
        T.Ok("STRAZNIK: lista zlych kluczy nie jest pusta", zleKlucze.Length > 0, "kluczy " + zleKlucze.Length);
        foreach (string k in zleKlucze)
        {
            T.Ok("IsValidKey odrzuca klucz: " + (k == null ? "null" : "\"" + k + "\""),
                 !FactLedger.IsValidKey(k), k);
        }
        foreach (string k in new[] { "a", "ruiny.otwarte", "napad_odparty", "licznik-3", new string('x', 64) })
        {
            T.Ok("IsValidKey przyjmuje klucz: \"" + k + "\"", FactLedger.IsValidKey(k), k);
        }

        var ks = new FactLedger();
        T.Ok("Set odrzuca klucz z separatorem pamieci (a nie naprawia go po cichu przez Escape)",
             !ks.Set("a|b", 1f, 0f, 0f) && ks.Count == 0, "Count " + ks.Count);
        T.Ok("Set odrzuca wartosc NaN", !ks.Set("ok", float.NaN, 0f, 0f) && ks.Count == 0, "Count " + ks.Count);

        // Duplikat klucza w liniach: druga linia jest odrzucana, pierwsza zostaje - inaczej
        // uszkodzony zapis mogby po cichu podmienic wartosc faktu.
        var dubel = new FactLedger();
        int odrz = dubel.RestoreFromLines(new[]
        {
            new Fact { Key = "x", Value = 1f, SetDay = 0f, LifespanDays = 0f }.Encode(),
            new Fact { Key = "x", Value = 9f, SetDay = 0f, LifespanDays = 0f }.Encode(),
        });
        T.Ok("RestoreFromLines odrzuca duplikat klucza i zachowuje pierwszy",
             odrz == 1 && dubel.Count == 1 && Math.Abs(dubel.ValueOf("x", 0f, -1f) - 1f) < 1e-6f,
             "odrzuconych " + odrz + ", wartosc " + dubel.ValueOf("x", 0f, -1f));
        T.EqI("RestoreFromLines(null) nie odrzuca niczego", new FactLedger().RestoreFromLines(null), 0);
    }

    // ---------------------------------------------------------------------------------
    //  13b - wygasanie leniwe i kolejnosc
    // ---------------------------------------------------------------------------------
    static void WygasanieISortowanie()
    {
        T.Section("TEST 13b - fakty: wygasanie LENIWE, kolejnosc ordynalna, licznik po wygasnieciu");

        var ks = new FactLedger();
        ks.Set("krotki", 1f, 5f, 10f);
        ks.Set("wieczny", 2f, 5f, 0f);

        // Granica jest WYLACZAJACA: IsActive to (teraz - ustawiono) < zycie, wiec dzien 5+10 juz nie.
        T.Ok("fakt obowiazuje tuz przed uplywem czasu zycia", ks.Has("krotki", 14.99f), "dzien 14.99");
        T.Ok("fakt NIE obowiazuje w dniu uplywu (granica wylaczajaca)", !ks.Has("krotki", 15f), "dzien 15");
        T.Ok("czas zycia <= 0 znaczy bez wygasania", ks.Has("wieczny", 9999f), "dzien 9999");

        // LENIWOSC: odczyt nie moze ruszac ksiegi. Wariant czyszczacy uzaleznilby jej zawartosc od
        // liczby wywolan compa, czyli od liczby map, i rozjechalby odcisk pamieci miedzy ramionami.
        int przed = ks.Count;
        ks.Active(9999f);
        ks.Canonical(9999f);
        ks.Has("krotki", 9999f);
        T.EqI("odczyt po wygasnieciu NIE usuwa wpisu z ksiegi (wygasanie leniwe)", ks.Count, przed);
        T.EqI("wygasly fakt nadal idzie do zapisu gry", ks.ToPersistableLines().Count, przed);
        T.EqI("ale nie widac go wsrod obowiazujacych", ks.Active(9999f).Count, 1);

        T.Eq("wiek faktu to dni od ustawienia", ks.AgeDays("wieczny", 12.5f), 7.5f, 1e-5f);
        T.Eq("brak faktu daje -1, a NIE wiek kolonii", ks.AgeDays("nieistnieje", 12.5f), -1f, 1e-6f);
        T.Eq("wygasly fakt tez daje -1", ks.AgeDays("krotki", 100f), -1f, 1e-6f);

        // Licznik: Add na wygaslym fakcie startuje od zera, inaczej "ile razy w ostatnich N dniach"
        // po cichu sumowaloby sie z okresem, ktory juz nie obowiazuje.
        var lic = new FactLedger();
        lic.Add("licznik", 2f, 0f, 5f);
        lic.Add("licznik", 3f, 1f, 5f);
        T.Eq("Add sumuje w obrebie obowiazujacego faktu", lic.ValueOf("licznik", 1f, -1f), 5f, 1e-6f);
        lic.Add("licznik", 4f, 50f, 5f);
        T.Eq("Add po wygasnieciu startuje od zera", lic.ValueOf("licznik", 50f, -1f), 4f, 1e-6f);

        // Kolejnosc: swieza gra wstawia chronologicznie, wczytana leksykograficznie. Bez sortowania
        // "to samo ziarno i stan daje te same decyzje" przestaloby byc prawda po cyklu zapis-wczytanie.
        var chrono = new FactLedger();
        foreach (string k in new[] { "zeta", "alfa", "mika" })
        {
            chrono.Set(k, 1f, 0f, 0f);
        }
        var klucze = chrono.Active(0f).Select(x => x.Key).ToList();
        T.Ok("Active zwraca klucze posortowane ordynalnie",
             klucze.SequenceEqual(klucze.OrderBy(x => x, StringComparer.Ordinal)), string.Join(",", klucze));
        T.Ok("kolejnosc NIE jest kolejnoscia wstawiania (inaczej test nie sprawdza sortowania)",
             !klucze.SequenceEqual(new[] { "zeta", "alfa", "mika" }), string.Join(",", klucze));
        T.EqS("postac kanoniczna po round-tripie jest identyczna", chrono.Clone().Canonical(0f), chrono.Canonical(0f));

        // Round-trip NIE wystarcza jako dowod posortowania zapisu: obie strony porownania przeszly
        // by ten sam kodek, wiec brak sortowania byl by w nich identyczny i niewidoczny. Dlatego
        // kolejnosc linii sprawdzamy wprost, wyluskujac klucze z zakodowanych linii.
        // Odporne na linie o zlym ksztalcie: mutacja kodeka ma zapalic ASERCJE, nie wywrocic przebiegu.
        var kluczeZapisu = chrono.ToPersistableLines()
                                 .Select(l => { var p = (l ?? string.Empty).Split('|'); return p.Length > 1 ? p[1] : "<zla linia>"; })
                                 .ToList();
        T.Ok("linie zapisu ida w kolejnosci ordynalnej klucza",
             kluczeZapisu.SequenceEqual(kluczeZapisu.OrderBy(x => x, StringComparer.Ordinal)),
             string.Join(",", kluczeZapisu));
        T.Ok("STRAZNIK: kolejnosc zapisu NIE jest kolejnoscia wstawiania",
             !kluczeZapisu.SequenceEqual(new[] { "zeta", "alfa", "mika" }), string.Join(",", kluczeZapisu));
        T.EqS("linie zapisu po round-tripie sa identyczne",
              string.Join("#", chrono.Clone().ToPersistableLines()), string.Join("#", chrono.ToPersistableLines()));
    }

    // ---------------------------------------------------------------------------------
    //  13c - slad po zamknietym watku
    // ---------------------------------------------------------------------------------
    static void ZamkniecieWatku()
    {
        T.Section("TEST 13c - slad po zamknietym watku (ArcClosure w ArcLedger)");

        var z = new ArcClosure
        {
            ArcId = "PN_Luk_Wendeta", Number = 3, Outcome = ArcDirector.OutcomeResolved,
            FinalPhaseId = "Rozwiazanie", Day = 35.5f
        };
        var pola = typeof(ArcClosure).GetFields(BindingFlags.Public | BindingFlags.Instance);
        var domyslny = new ArcClosure();
        T.Ok("STRAZNIK: fixture ustawia KAZDE pole ArcClosure na wartosc niedomyslna",
             pola.Length > 0 && pola.All(p => !Equals(p.GetValue(z), p.GetValue(domyslny))),
             "pol " + pola.Length);
        ArcClosure z2;
        bool ok = ArcClosure.TryDecode(z.Encode(), out z2);
        var rozne = ok ? pola.Where(p => !Equals(p.GetValue(z), p.GetValue(z2))).Select(p => p.Name).ToList()
                       : new List<string> { "dekodowanie" };
        T.Ok("ArcClosure: round-trip odtwarza wszystkie pola", ok && rozne.Count == 0,
             "roznych: " + string.Join(",", rozne));

        string wz = z.Encode();
        var zle = new List<(string opis, string linia)>
        {
            ("za malo pol", wz.Substring(0, wz.LastIndexOf('|'))),
            ("zly znacznik", "Q" + wz.Substring(1)),
            ("numer zerowy", wz.Replace("|3|", "|0|")),
            ("dzien NaN", wz.Replace("|35.5", "|NaN")),
            ("pusty arcId", wz.Replace("|PN_Luk_Wendeta|", "|-|")),
        };
        T.Ok("STRAZNIK: tablica uszkodzen zamkniecia nie jest pusta", zle.Count > 0, "przypadkow " + zle.Count);
        foreach (var (opis, linia) in zle)
        {
            ArcClosure x;
            T.Ok("ArcClosure odrzuca uszkodzona linie: " + opis, !ArcClosure.TryDecode(linia, out x) && x == null, linia);
        }

        // Ewikcja od najstarszego (kazdy luk rozny, wiec regula S6 "najpierw luki z nowszym wpisem"
        // sprowadza sie tu do najstarszego): bufor nie moze rosnac przez cala rozgrywke.
        var ks = new ArcLedger();
        int ile = ArcLedger.MaxClosed + 3;
        for (int i = 1; i <= ile; i++)
        {
            ks.RecordClosure(new ArcInstance { ArcId = "PN_Luk_" + i, Number = i, PhaseId = "Rozwiazanie" },
                             ArcDirector.OutcomeFaded, i);
        }
        T.EqI("bufor zamkniec przyciety do MaxClosed", ks.Closed.Count, ArcLedger.MaxClosed);
        T.EqI("ewikcja idzie od NAJSTARSZEGO (zostaje ostatnie MaxClosed)",
              ks.Closed[0].Number, ile - ArcLedger.MaxClosed + 1);
        T.EqI("najnowsze zamkniecie zostaje", ks.Closed[ks.Closed.Count - 1].Number, ile);

        // Round-trip calej ksiegi razem z zamknieciami.
        var kopia = ks.Clone();
        T.Ok("ArcLedger.Clone przenosi zamkniecia co do pola",
             kopia.Closed.Count == ks.Closed.Count
             && kopia.Closed.Select(c => c.Encode()).SequenceEqual(ks.Closed.Select(c => c.Encode())),
             "kopia " + kopia.Closed.Count);
        T.Ok("LastClosure zwraca OSTATNIE zamkniecie danego luku",
             ks.LastClosure("PN_Luk_" + ile) != null && ks.LastClosure("PN_Luk_" + ile).Number == ile
             && ks.LastClosure("PN_Luk_nieistnieje") == null, "");

        // Zapis sprzed tej zmiany (bez linii Z) musi wczytac sie BEZ ani jednej odrzuconej linii -
        // to jest cala zaleta dopisania NOWEGO ZNACZNIKA zamiast nowego pola w liniach istniejacych.
        var stary = new ArcLedger();
        stary.NextNumber = 4;
        stary.ColonistLosses = 2;
        stary.LastCloseDay["PN_Luk_Wendeta"] = 12.5f;
        stary.Active.Add(new ArcInstance { ArcId = "PN_Luk_Scigani", Number = 3, PhaseId = "Kulminacja" });
        var wczytany = new ArcLedger();
        int odrz = wczytany.RestoreFromLines(stary.ToPersistableLines());
        T.Ok("zapis bez linii zamkniec wczytuje sie bez odrzucen",
             odrz == 0 && wczytany.Closed.Count == 0 && wczytany.Active.Count == 1 && wczytany.NextNumber == 4,
             "odrzuconych " + odrz);

        var zepsuty = new ArcLedger();
        int odrz2 = zepsuty.RestoreFromLines(new[] { "Z|PN_Luk_X|nieliczba|rozwiazany|Rozwiazanie|1" });
        T.EqI("uszkodzona linia zamkniecia jest odrzucana pojedynczo", odrz2, 1);
    }

    // ---------------------------------------------------------------------------------
    //  13d - zapytania fasady
    // ---------------------------------------------------------------------------------
    static void ZapytaniaFasady()
    {
        T.Section("TEST 13d - blackboard: zapytania o watki, fakty i tury bez tematu");

        var h = new EventHistory();
        h.RecordEvent(TestsDecision.Ev("PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major), 1f, 0);
        h.RecordEvent(TestsDecision.Ev("PN_Akcja_Zrzut", Theme.Economic, Valence.Positive, EventScale.Minor), 2f, 0);
        h.RecordEvent(TestsDecision.Ev("PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major), 3f, 0);

        // Wyprowadzenie, nie przepisana liczba: AgeInDecisions = max(1, decisionCount - indeksWpisu).
        // Po trzech emisjach decisionCount = 3; najnowszy Raid ma indeks 2, Economic indeks 1.
        var arcs = new ArcLedger();
        var fakty = new FactLedger();
        var bb = new NarratorBlackboard(h, arcs, fakty, 10f);

        T.EqI("tury od zdarzenia o temacie Raid", bb.TurnsSinceTheme(Theme.Raid), h.DecisionCount - 2);
        T.EqI("tury od zdarzenia o temacie Economic", bb.TurnsSinceTheme(Theme.Economic), h.DecisionCount - 1);
        T.EqI("temat nieobecny w buforze daje BeyondHorizon, a nie duza liczbe",
              bb.TurnsSinceTheme(Theme.Supernatural), NarratorBlackboard.BeyondHorizon);
        T.Ok("STRAZNIK: dwa tematy daja ROZNE wyniki (inaczej test nie rozroznia niczego)",
             bb.TurnsSinceTheme(Theme.Raid) != bb.TurnsSinceTheme(Theme.Economic),
             bb.TurnsSinceTheme(Theme.Raid) + " vs " + bb.TurnsSinceTheme(Theme.Economic));

        T.Ok("brak watku: nie otwarty, bez fazy, bez wyniku",
             !bb.IsThreadOpen("PN_Luk_Wendeta") && bb.PhaseOfThread("PN_Luk_Wendeta") == null
             && bb.ThreadOutcome("PN_Luk_Wendeta") == null, "");

        arcs.Active.Add(new ArcInstance { ArcId = "PN_Luk_Wendeta", Number = 1, PhaseId = "Eskalacja" });
        T.Ok("watek otwarty: status i faza",
             bb.IsThreadOpen("PN_Luk_Wendeta") && bb.PhaseOfThread("PN_Luk_Wendeta") == "Eskalacja", "");

        arcs.RecordClosure(arcs.Active[0], ArcDirector.OutcomeResolved, 20f);
        arcs.Active.Clear();
        T.Ok("watek zamkniety: wynik pamietany, faza juz nie",
             !bb.IsThreadOpen("PN_Luk_Wendeta") && bb.PhaseOfThread("PN_Luk_Wendeta") == null
             && bb.ThreadOutcome("PN_Luk_Wendeta") == ArcDirector.OutcomeResolved, "");

        fakty.Set("napad.odparty", 2f, 4f, 0f);
        T.Ok("fakt: obecnosc, wartosc i wiek liczone na dzien blackboardu",
             bb.HasFact("napad.odparty") && Math.Abs(bb.FactValue("napad.odparty", -1f) - 2f) < 1e-6f
             && Math.Abs(bb.FactAgeDays("napad.odparty") - 6f) < 1e-5f,
             "wiek " + bb.FactAgeDays("napad.odparty"));
        T.Ok("brak faktu: wartosc zastepcza i wiek -1",
             !bb.HasFact("nie.ma") && Math.Abs(bb.FactValue("nie.ma", -7f) + 7f) < 1e-6f
             && Math.Abs(bb.FactAgeDays("nie.ma") + 1f) < 1e-6f, "");

        // Kazdy magazyn moze byc null (mapa bez pamieci, luki wylaczone, stary zapis).
        var pusty = new NarratorBlackboard(null, null, null, 1f);
        T.Ok("blackboard bez magazynow odpowiada pusto, a nie wyjatkiem",
             !pusty.IsThreadOpen("x") && pusty.PhaseOfThread("x") == null && pusty.ThreadOutcome("x") == null
             && !pusty.HasFact("x") && pusty.TurnsSinceTheme(Theme.Raid) == NarratorBlackboard.BeyondHorizon
             && pusty.ThreadsCanonical() == string.Empty && pusty.FactsCanonical() == string.Empty, "");
    }

    // ---------------------------------------------------------------------------------
    //  13e - postac kanoniczna i jej odczyt
    // ---------------------------------------------------------------------------------
    static void PostacKanoniczna()
    {
        T.Section("TEST 13e - postac kanoniczna dla WorldSnapshot i jej odczyt przez warunki");

        var arcs = new ArcLedger();
        arcs.Active.Add(new ArcInstance { ArcId = "PN_Luk_Wendeta", Number = 2, PhaseId = "Kulminacja" });
        arcs.RecordClosure(new ArcInstance { ArcId = "PN_Luk_Wendeta", Number = 1, PhaseId = "Rozwiazanie" },
                           ArcDirector.OutcomeResolved, 10f);
        arcs.RecordClosure(new ArcInstance { ArcId = "PN_Luk_Scigani", Number = 1, PhaseId = "Kulminacja" },
                           ArcDirector.OutcomeFaded, 12f);

        var bb = new NarratorBlackboard(null, arcs, null, 20f);
        string w = bb.ThreadsCanonical();

        // Ten sam luk bywa jednoczesnie otwarty po raz drugi i ma slad po pierwszym zamknieciu -
        // dlatego status idzie PIERWSZY w elemencie, a nie na koncu.
        T.Ok("ten sam luk otwarty i zamkniety daje DWA rozlaczne wpisy",
             NarratorBlackboard.ThreadHasStatus(w, NarratorBlackboard.StatusOpen, "PN_Luk_Wendeta")
             && NarratorBlackboard.ThreadHasStatus(w, NarratorBlackboard.StatusClosed, "PN_Luk_Wendeta"), w);
        T.EqS("ogon wpisu otwartego to faza",
              NarratorBlackboard.ThreadTail(w, NarratorBlackboard.StatusOpen, "PN_Luk_Wendeta"), "Kulminacja");
        T.EqS("ogon wpisu zamknietego to wynik",
              NarratorBlackboard.ThreadTail(w, NarratorBlackboard.StatusClosed, "PN_Luk_Wendeta"),
              ArcDirector.OutcomeResolved);
        T.EqS("drugi luk czytany niezaleznie",
              NarratorBlackboard.ThreadTail(w, NarratorBlackboard.StatusClosed, "PN_Luk_Scigani"),
              ArcDirector.OutcomeFaded);
        T.Ok("luk nieobecny nie jest znajdowany",
             !NarratorBlackboard.ThreadHasStatus(w, NarratorBlackboard.StatusOpen, "PN_Luk_Nieistnieje"), w);

        // Tylko OSTATNIE zamkniecie luku trafia do postaci kanonicznej.
        arcs.RecordClosure(new ArcInstance { ArcId = "PN_Luk_Scigani", Number = 2, PhaseId = "Rozwiazanie" },
                           ArcDirector.OutcomeResolved, 15f);
        T.EqS("po ponownym zamknieciu widac wynik NOWSZY",
              NarratorBlackboard.ThreadTail(bb.ThreadsCanonical(), NarratorBlackboard.StatusClosed, "PN_Luk_Scigani"),
              ArcDirector.OutcomeResolved);

        // Pulapka prefiksu: "ruiny" nie moze trafic w "ruinyOtwarte".
        var fakty = new FactLedger();
        fakty.Set("ruiny", 1f, 3f, 0f);
        fakty.Set("ruinyOtwarte", 7f, 4f, 0f);
        var bf = new NarratorBlackboard(null, null, fakty, 20f);
        string c = bf.FactsCanonical();
        T.Eq("klucz krotszy nie trafia w klucz dluzszy (pulapka prefiksu)",
             NarratorBlackboard.FactValueFrom(c, "ruiny"), 1f, 1e-6f);
        T.Eq("klucz dluzszy czytany poprawnie", NarratorBlackboard.FactValueFrom(c, "ruinyOtwarte"), 7f, 1e-6f);
        // W postaci kanonicznej jest WIEK, nie dzien ustawienia: snapshot jest zamrozony na ture,
        // wiec warunek nie zna dzisiejszego dnia gry i nie mialby jak policzyc wieku sam.
        T.Eq("wiek faktu odczytany z postaci kanonicznej (dzien blackboardu minus dzien ustawienia)",
             NarratorBlackboard.FactAgeFrom(c, "ruinyOtwarte"), bf.GameDay - 4f, 1e-5f);
        T.Ok("brak faktu daje NaN, a nie zero (zero jest poprawna wartoscia faktu)",
             float.IsNaN(NarratorBlackboard.FactValueFrom(c, "nie.ma"))
             && float.IsNaN(NarratorBlackboard.FactAgeFrom(c, "nie.ma")), c);

        // Obecnosci faktu nie wolno sprawdzac porownaniem wartosci z zerem.
        var zerowy = new FactLedger();
        zerowy.Set("zero", 0f, 1f, 0f);
        string cz = new NarratorBlackboard(null, null, zerowy, 2f).FactsCanonical();
        T.Ok("fakt o wartosci 0 jest OBECNY", NarratorBlackboard.FactPresentIn(cz, "zero"), cz);
        T.Ok("fakt nieistniejacy nie jest obecny", !NarratorBlackboard.FactPresentIn(cz, "inny"), cz);

        // Tury bez tematu: postac kanoniczna musi dac sie odczytac dla KAZDEGO tematu osi.
        var h = new EventHistory();
        h.RecordEvent(TestsDecision.Ev("PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major), 1f, 0);
        var bt = new NarratorBlackboard(h, null, null, 5f);
        string t = bt.TurnsSinceThemesCanonical();
        var tematy = Enum.GetValues(typeof(Theme)).Cast<Theme>().ToList();
        T.Ok("STRAZNIK: os tematow nie jest pusta", tematy.Count > 0, "tematow " + tematy.Count);
        int zgodnych = tematy.Count(x => NarratorBlackboard.TurnsSinceThemeFrom(t, x) == bt.TurnsSinceTheme(x));
        T.EqI("odczyt z postaci kanonicznej zgadza sie z zapytaniem dla KAZDEGO tematu", zgodnych, tematy.Count);
        // Wyprowadzenie: wiek bierze sie z AgeInDecisions najnowszego pasujacego wpisu, a nie
        // z przepisanej liczby - inaczej zmiana definicji wieku nie zapalilaby tego testu.
        T.EqI("temat obecny w historii ma wiek rowny AgeInDecisions jego wpisu",
              NarratorBlackboard.TurnsSinceThemeFrom(t, Theme.Raid), h.AgeInDecisions(h.Newest));
        T.EqI("temat nieobecny ma BeyondHorizon",
              NarratorBlackboard.TurnsSinceThemeFrom(t, Theme.Social), NarratorBlackboard.BeyondHorizon);
    }
}
