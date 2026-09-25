using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Decision;

/// <summary>
/// TEST 18 - izolacja naszych pytan od cache'u CanFireNow (krok 8, dlug 8, decyzja autora K8-3).
///
/// Atrapa workera odtwarza semantyke z dekompilacji 1.5.4063: jeden wpis na incydent, kluczem jest sam
/// tick; w tym samym ticku kazde pytanie dostaje werdykt policzony dla PIERWSZYCH parametrow. Scenariusz
/// "wanilia przed nami, my, wanilia po nas" pokazuje oba kierunki zatrucia - bez strazy i ze straza.
/// </summary>
static class TestsCacheGuard
{
    /// <summary>Worker z cache'em jak IncidentWorker.CanFireNow; werdykt = parametry &gt;= prog.</summary>
    sealed class Atrapa
    {
        public int Tick = 0;
        public bool Wynik;
        public int Liczen;
        public readonly int Prog;
        public Atrapa(int prog) { Prog = prog; }

        public bool CanFireNow(int parametry, int now)
        {
            if (Tick == now) return Wynik;
            Tick = now;
            Liczen++;
            Wynik = parametry >= Prog;
            return Wynik;
        }
    }

    /// <summary>Nasze pytanie w obecnosci strazy - tak jak ZapytajIzolowanie w compie.</summary>
    static bool Zapytaj(VerdictCacheGuard straz, Atrapa w, string klucz, int parametry, int now, out bool kolizja)
    {
        var ustaw = straz.BeforeQuery(klucz, now, new VerdictCacheState(w.Tick, w.Wynik), out kolizja);
        w.Tick = ustaw.Tick;
        w.Wynik = ustaw.Result;
        return w.CanFireNow(parametry, now);
    }

    static void Przywroc(VerdictCacheGuard straz, Dictionary<string, Atrapa> pracownicy, int now)
    {
        foreach (var kv in straz.EndTurn(now))
        {
            pracownicy[kv.Key].Tick = kv.Value.Tick;
            pracownicy[kv.Key].Wynik = kv.Value.Result;
        }
    }

    public static void Run()
    {
        T.Section("TEST 18 - izolacja cache'u CanFireNow (dlug 8)");
        const int T0 = 5000;

        // 18a. BEZ STRAZY (stan sprzed kroku 8): wanilia pyta z parametrami 10 (werdykt false przy progu 50),
        // my z 80 (powinno byc true), wanilia po nas z 10 (powinno byc false).
        var w = new Atrapa(50);
        bool waniliaPrzed = w.CanFireNow(10, T0);
        bool my = w.CanFireNow(80, T0);
        bool waniliaPo = w.CanFireNow(10, T0);
        T.Ok("18a bez strazy: nasze pytanie dostaje cudzy werdykt (zatrucie w strone narratora)", my == false, "my=" + my);
        T.Ok("18a bez strazy: wanilia przed nami - poprawnie", waniliaPrzed == false, null);

        // Drugi kierunek: my pierwsi z 80 (true), wanilia po nas z 10 dostaje nasze true.
        var w2 = new Atrapa(50);
        w2.CanFireNow(80, T0);
        T.Ok("18a bez strazy: wanilia po nas dostaje nasz werdykt (zatrucie w strone wanilii)", w2.CanFireNow(10, T0) == true, null);

        // 18b. ZE STRAZA: ten sam scenariusz, oba kierunki poprawne.
        var straz = new VerdictCacheGuard();
        var p = new Atrapa(50);
        var pracownicy = new Dictionary<string, Atrapa> { { "RaidEnemy", p } };
        bool wPrzed = p.CanFireNow(10, T0);
        bool kolizja;
        bool nasz = Zapytaj(straz, p, "RaidEnemy", 80, T0, out kolizja);
        T.Ok("18b ze straza: nasz werdykt liczony dla NASZYCH parametrow", nasz == true, "nasz=" + nasz);
        T.Ok("18b ze straza: kolizja wykryta (wanilia pytala w tym ticku)", kolizja, null);
        // Nasz TryFire (miedzy yield a wznowieniem) - ten sam tick, widzi NASZ werdykt.
        T.Ok("18b ze straza: nasz TryFire spojny z decyzja", p.CanFireNow(80, T0) == true, null);
        Przywroc(straz, pracownicy, T0);
        bool wPo = p.CanFireNow(10, T0);
        T.Ok("18b ze straza: wanilia po nas widzi SWOJ werdykt", wPo == wPrzed && wPo == false, "po=" + wPo);
        T.EqI("18b ze straza: nic nie czeka na przywrocenie", straz.PendingCount, 0);

        // 18c. Brak cudzego werdyktu w tym ticku: bez kolizji, a po naszej turze cache wraca do starego
        // ticku - kolejny pytajacy w T0 liczy od nowa dla swoich parametrow (nie dostaje naszego true).
        var q = new Atrapa(50);
        q.CanFireNow(10, T0 - 1000);           // werdykt z poprzedniego interwalu
        var pr2 = new Dictionary<string, Atrapa> { { "Infestation", q } };
        bool k2;
        Zapytaj(straz, q, "Infestation", 80, T0, out k2);
        T.Ok("18c werdykt z innego ticku to nie kolizja", !k2, null);
        Przywroc(straz, pr2, T0);
        T.EqI("18c po przywroceniu cache wskazuje stary tick", q.Tick, T0 - 1000);
        int liczenPrzed = q.Liczen;
        T.Ok("18c kolejny pytajacy liczy od nowa dla swoich parametrow", q.CanFireNow(10, T0) == false && q.Liczen == liczenPrzed + 1, null);

        // 18d. Drugie pytanie o ten sam incydent w turze: bez kolizji (w cache'u NASZ werdykt), oryginal
        // z pierwszego pytania zachowany.
        var r = new Atrapa(50);
        r.CanFireNow(10, T0);
        var pr3 = new Dictionary<string, Atrapa> { { "ManhunterPack", r } };
        bool k3a, k3b;
        Zapytaj(straz, r, "ManhunterPack", 80, T0, out k3a);
        Zapytaj(straz, r, "ManhunterPack", 90, T0, out k3b);
        T.Ok("18d pierwsze pytanie: kolizja; drugie: nie", k3a && !k3b, "k1=" + k3a + " k2=" + k3b);
        T.EqI("18d jeden incydent czeka na przywrocenie", straz.PendingCount, 1);
        Przywroc(straz, pr3, T0);
        T.Ok("18d przywrocony oryginal sprzed PIERWSZEGO pytania (werdykt wanilii false)", r.Tick == T0 && r.Wynik == false, "tick=" + r.Tick + " wynik=" + r.Wynik);

        // 18e. Kolejnosc przywracania = kolejnosc pytan; uniewaznienie nie trafia w prawdziwy tick.
        var s1 = new Atrapa(0); var s2 = new Atrapa(0);
        bool x;
        Zapytaj(straz, s2, "B", 1, T0, out x);
        Zapytaj(straz, s1, "A", 1, T0, out x);
        var lista = straz.EndTurn(T0);
        T.EqS("18e kolejnosc przywracania = kolejnosc pytan", string.Join(",", lista.Select(kv => kv.Key)), "B,A");
        T.Ok("18e tick uniewaznienia nie jest tickiem gry", VerdictCacheGuard.InvalidTick < 0, null);

        // 18f. (przeglad S10) DWIE KOLEJNE TURY o ten sam incydent. Tura 2 musi zapamietac SWOJ oryginal (werdykt
        // wanilii z T1), a nie oddac resztek tury 1; kolizja w turze 2 wykryta od nowa.
        const int T1 = T0 + 1000;
        var u = new Atrapa(50);
        var pr4 = new Dictionary<string, Atrapa> { { "RaidEnemy", u } };
        bool k4a, k4b;
        u.CanFireNow(10, T0);
        Zapytaj(straz, u, "RaidEnemy", 80, T0, out k4a);
        Przywroc(straz, pr4, T0);
        T.Ok("18f tura 1: przywrocony werdykt wanilii z T0", u.Tick == T0 && u.Wynik == false, "tick=" + u.Tick);
        u.CanFireNow(10, T1);                  // wanilia w T1 (werdykt false dla jej parametrow)
        bool nasz2 = Zapytaj(straz, u, "RaidEnemy", 80, T1, out k4b);
        T.Ok("18f tura 2: kolizja wykryta od nowa, nasz werdykt dla naszych parametrow", k4b && nasz2, "k=" + k4b + " nasz=" + nasz2);
        T.EqI("18f tura 2: jeden incydent czeka", straz.PendingCount, 1);
        Przywroc(straz, pr4, T1);
        T.Ok("18f tura 2: przywrocony werdykt wanilii z T1 (nie z T0)", u.Tick == T1 && u.Wynik == false, "tick=" + u.Tick + " wynik=" + u.Wynik);

        // 18g. (przeglad S10) TURA PRZERWANA (wyjatek miedzy pytaniem a EndTurn). Przywrocenie w innym ticku nie
        // moze nadpisac werdyktu, ktory ktos policzyl w biezacym ticku; pytanie w nowym ticku porzuca resztki.
        var v = new Atrapa(50);
        var pr5 = new Dictionary<string, Atrapa> { { "Infestation", v } };
        bool k5;
        Zapytaj(straz, v, "Infestation", 80, T0, out k5);   // tura w T0 przerwana - bez EndTurn
        v.CanFireNow(10, T1);                                // wanilia w T1 liczy swoj werdykt
        var spoznione = straz.EndTurn(T1);
        T.EqI("18g EndTurn w innym ticku: nic do przywrocenia", spoznione.Count, 0);
        T.Ok("18g werdykt wanilii z T1 nietkniety", v.Tick == T1 && v.Wynik == false, "tick=" + v.Tick);
        T.EqI("18g resztki porzucone", straz.PendingCount, 0);
        var w3 = new Atrapa(50);
        var pr6 = new Dictionary<string, Atrapa> { { "Infestation", w3 }, { "Flashstorm", new Atrapa(0) } };
        Zapytaj(straz, w3, "Infestation", 80, T0, out k5);  // tura w T0 przerwana
        Zapytaj(straz, pr6["Flashstorm"], "Flashstorm", 1, T1, out k5);  // nowa tura w T1
        T.EqI("18g pytanie w nowym ticku porzuca resztki tury przerwanej", straz.PendingCount, 1);
        var l6 = straz.EndTurn(T1);
        T.EqS("18g przywracany tylko incydent biezacej tury", string.Join(",", l6.Select(kv => kv.Key)), "Flashstorm");
        straz.BeforeQuery("X", T0, new VerdictCacheState(T0, true), out k5);
        straz.Discard();
        T.EqI("18g Discard czysci straze", straz.PendingCount, 0);
        T.EqI("18g po Discard EndTurn w tym samym ticku nic nie zwraca", straz.EndTurn(T0).Count, 0);
    }
}
