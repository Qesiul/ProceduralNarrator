using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.PlayerModel;
using ProceduralNarrator.Core.Tension;

/// <summary>
/// TEST 14 - STYL GRACZA (krok 7). Sekcje S1: 14a (parametry z XML wobec decyzji autora), 14b (kolejka FIFO
/// i rozgrzewka), 14c (tryby agregacji i progi znajomosci), 14d (wspolna skala), 14e (profil wzgledny,
/// mocne strony, etykiety), 14f (kierunek d), 14j (kodek N/A/D). Wartosci oczekiwane sa WYPROWADZONE
/// z decyzji autora albo z wzoru liczonego w tescie niezaleznie od kodu, nie przepisane z wyniku.
/// </summary>
static class TestsPlayerStyle
{
    public static void Run(XmlConfig cfg)
    {
        PlayerStyleParams p = Test14a(cfg);
        Test14b(p);
        Test14c(p);
        Test14d(p);
        Test14e(p);
        Test14f(cfg, p);
        Test14jNAD();
        Test14i(p);
        Test14jBEOW();
    }

    // ------------------------------------------------------------------ 14a

    static PlayerStyleParams Test14a(XmlConfig cfg)
    {
        T.Section("TEST 14a - blok <playerStyle> z XML wobec decyzji autora (krok 7)");
        T.Ok("blok <playerStyle> obecny w Storyteller_Generative.xml", cfg.playerStyle != null, null);
        if (cfg.playerStyle == null)
        {
            return PlayerStyleParams.Default();
        }
        PlayerStyleParams xml = cfg.playerStyle;
        string przed = xml.Describe();
        PlayerStyleParams p = xml.Clone();
        string popr = p.Sanitize();
        T.Ok("XML nie wymaga zadnej poprawki Sanitize", string.IsNullOrEmpty(popr), popr);
        T.EqS("Sanitize nie zmienil zadnej wartosci efektywnej", p.Describe(), przed);

        T.Ok("warstwa stylu wlaczona", xml.enabled, null);
        T.EqI("rozgrzewka = 15 dni (decyzja nr 9)", xml.warmupDays, 15);
        T.EqI("pojemnosc kolejki = 60 dni, rok gry (decyzja nr 10)", xml.capacityDays, 60);
        T.Eq("waga orientacji = 0.3 (decyzja nr 13)", xml.orientationWeight, 0.3, 1e-6);
        T.Eq("waga rytmu = 0.7 (decyzja nr 13)", xml.rhythmWeight, 0.7, 1e-6);
        T.Eq("mocna strona = co najmniej 0.1 ponad wlasna srednia (prog * skala)",
             xml.strongSideThreshold * xml.relativeScale, 0.1, 1e-6);

        // Pomiary: kazdy dokladnie raz, wagi z decyzji nr 19 (glowny 1, pomocniczy 0.5).
        var wagiSpec = new Dictionary<string, float>
        {
            { "Obrona", 1f }, { "Inicjatywa", 0.5f }, { "Praca", 1f }, { "Produkcja", 0.5f },
            { "Przyjecia", 1f }, { "Werbunek", 0.5f }, { "Poborowi", 1f }, { "Szybkosc", 0.5f }
        };
        T.EqI("STRAZNIK: tabela spec obejmuje wszystkie pomiary", wagiSpec.Count, StyleSignals.Count);
        foreach (var kv in wagiSpec)
        {
            var wpisy = xml.signals.Where(s => s != null && s.signal == kv.Key).ToList();
            T.EqI("pomiar " + kv.Key + " zadeklarowany dokladnie raz", wpisy.Count, 1);
            if (wpisy.Count == 1)
            {
                T.Eq("waga pomiaru " + kv.Key + " (decyzja nr 19)", wpisy[0].weight, kv.Value, 1e-6);
                T.Ok("rozpietosc pomiaru " + kv.Key + " dodatnia", wpisy[0].spread > 0f, "spread=" + T.F(wpisy[0].spread));
            }
        }
        T.EqI("Poborowi wymaga 2 epizodow (decyzja nr 9)", xml.Signal(StyleSignal.Poborowi).minEvents, 2);
        T.EqI("Szybkosc wymaga 2 epizodow (decyzja nr 9)", xml.Signal(StyleSignal.Szybkosc).minEvents, 2);
        // Przeglad S8 (decyzje autora 2026-09-24): jedno zdarzenie nie rozstrzyga Ekspansji.
        T.EqI("Przyjecia wymagaja 3 rozstrzygnietych ofert (decyzja S8)", xml.Signal(StyleSignal.Przyjecia).minEvents, 3);
        T.EqI("Werbunek wymaga 2 schwytanych (decyzja S8)", xml.Signal(StyleSignal.Werbunek).minEvents, 2);
        T.EqI("Inicjatywa znana dopiero z atakiem w oknie (decyzja S8, minNumerator)", xml.Signal(StyleSignal.Inicjatywa).minNumerator, 1);
        T.EqI("pozostale pomiary bez progu licznika",
              Enumerable.Range(0, StyleSignals.Count).Count(i => (StyleSignal)i != StyleSignal.Inicjatywa
                                                              && xml.Signal((StyleSignal)i).minNumerator != 0), 0);
        T.EqI("limit dlugosci epizodu zagrozenia = 1 doba (decyzja S8)", xml.maxEpisodeTicks, 60000);
        // Inicjatywa (decyzja nr 19): pierwszy atak w pelnym oknie daje 0.75; bez atakow pomiar NIEZNANY (S8).
        StyleSignalParams ini = xml.Signal(StyleSignal.Inicjatywa);
        T.Eq("Inicjatywa: norma 0 (z 0.5 przy zerze atakow - dlatego bez atakow pomiar nieznany)", Oczekiwane(0.0, ini), 0.5, 1e-6);
        T.Eq("Inicjatywa: jeden atak w pelnym oknie (1/60 na dzien) = z 0.75",
             Oczekiwane(1.0 / xml.capacityDays, ini), 0.75, 1e-4);
        for (int d = 0; d < StyleDimensions.Count; d++)
        {
            float suma = 0f;
            for (int i = 0; i < StyleSignals.Count; i++)
            {
                if ((int)StyleSignals.Dimension((StyleSignal)i) == d) suma += xml.Signal((StyleSignal)i).weight;
            }
            T.Ok("cecha " + StyleDimensions.Name((StyleDimension)d) + " ma dodatnia sume wag", suma > 0f, "suma=" + T.F(suma));
        }

        // Listy zrodel (decyzje techniczne planu, pomiary bez Harmony).
        ZbiorRowny("kategorie obrony", xml.defenseCategories, new[] { "Security" });
        ZbiorRowny("kategorie produkcji", xml.productionCategories, new[] { "Production", "Power" });
        ZbiorRowny("rekordy pomocnicze (bez gaszenia, opieki, napraw, pilnowania)", xml.supportRecords,
                   new[] { "TimeHauling", "TimeCleaning" });
        // WandererJoinAbasia (Royalty) - decyzja S8, w XML z MayRequire (gra bez DLC pomija wpis).
        ZbiorRowny("korzenie ofert dolaczenia", xml.offerQuestRoots, new[] { "WandererJoins", "RefugeePodCrash", "WandererJoinAbasia" });
        ZbiorRowny("rekordy produktywne", xml.productiveRecords, new[]
        {
            "TimeConstructing", "TimeSowingAndHarvesting", "TimeMining", "TimeResearching",
            "TimeHandlingAnimals", "TimeHunting"
        });

        // Tabela pomiarow (plan, "Pomiary"): cecha i tryb agregacji kazdego pomiaru - przeglad S8, wczesniej czesc
        // wpisow nie miala zadnej asercji.
        var tabela = new Dictionary<StyleSignal, KeyValuePair<StyleDimension, StyleAggregation>>
        {
            { StyleSignal.Obrona, new KeyValuePair<StyleDimension, StyleAggregation>(StyleDimension.Walka, StyleAggregation.DailyRatioMean) },
            { StyleSignal.Inicjatywa, new KeyValuePair<StyleDimension, StyleAggregation>(StyleDimension.Walka, StyleAggregation.RatioOfSums) },
            { StyleSignal.Praca, new KeyValuePair<StyleDimension, StyleAggregation>(StyleDimension.Gospodarka, StyleAggregation.DailyRatioMean) },
            { StyleSignal.Produkcja, new KeyValuePair<StyleDimension, StyleAggregation>(StyleDimension.Gospodarka, StyleAggregation.DailyRatioMean) },
            { StyleSignal.Przyjecia, new KeyValuePair<StyleDimension, StyleAggregation>(StyleDimension.Ekspansja, StyleAggregation.RatioOfSums) },
            { StyleSignal.Werbunek, new KeyValuePair<StyleDimension, StyleAggregation>(StyleDimension.Ekspansja, StyleAggregation.RatioOfSums) },
            { StyleSignal.Poborowi, new KeyValuePair<StyleDimension, StyleAggregation>(StyleDimension.Reaktywnosc, StyleAggregation.RatioOfSums) },
            { StyleSignal.Szybkosc, new KeyValuePair<StyleDimension, StyleAggregation>(StyleDimension.Reaktywnosc, StyleAggregation.RatioOfSums) }
        };
        T.EqI("STRAZNIK: tabela cech i trybow obejmuje wszystkie pomiary", tabela.Count, StyleSignals.Count);
        foreach (var kv in tabela)
        {
            T.Ok("pomiar " + kv.Key + ": cecha " + kv.Value.Key + ", tryb " + kv.Value.Value,
                 StyleSignals.Dimension(kv.Key) == kv.Value.Key && StyleSignals.Mode(kv.Key) == kv.Value.Value,
                 StyleSignals.Dimension(kv.Key) + "/" + StyleSignals.Mode(kv.Key));
        }
        T.Ok("skala promili tylko dla Poborowych i Szybkosci",
             Enumerable.Range(0, StyleSignals.Count).All(i => StyleSignals.Scale((StyleSignal)i)
                 == ((StyleSignal)i == StyleSignal.Poborowi || (StyleSignal)i == StyleSignal.Szybkosc ? 1000 : 1)), null);

        TestPrototypy(xml);
        TestSanitize();
        return p;
    }

    /// <summary>Etykiety z decyzji autora nr 22 (nazwy dwucechowe zatwierdzone z planem).</summary>
    static void TestPrototypy(PlayerStyleParams xml)
    {
        var protos = xml.prototypes;
        T.EqI("13 prototypow (decyzja nr 22)", protos.Count, 13);
        T.Ok("etykiety bezpieczne i unikalne",
             protos.All(pr => PlayerStyleParams.SafeLabel(pr.label)) && protos.Select(pr => pr.label).Distinct().Count() == protos.Count,
             string.Join(",", protos.Select(pr => pr.label)));
        T.Ok("wspolrzedne w [0,1]", protos.All(pr => Enumerable.Range(0, 4).All(d =>
             pr.Get((StyleDimension)d) >= 0f && pr.Get((StyleDimension)d) <= 1f)), null);
        T.EqS("pierwszy prototyp = Wszechstronny (remis rozstrzyga kolejnosc)", protos[0].label, "Wszechstronny");
        T.Ok("Wszechstronny = srodek skali we wszystkich cechach",
             Enumerable.Range(0, 4).All(d => protos[0].Get((StyleDimension)d) == 0.5f), protos[0].Describe());

        // Klasyfikacja po ksztalcie: ile cech ponad srodek, a reszta dokladnie w srodku.
        var spec = new Dictionary<string, string>
        {
            { "W", "Wojownik" }, { "G", "Gospodarz" }, { "E", "Osadnik" }, { "R", "Czujny" },
            { "WG", "Kasztelan" }, { "WE", "Zdobywca" }, { "WR", "Dowodca" },
            { "GE", "Zalozyciel" }, { "GR", "Zaradny" }, { "ER", "Opiekun" }
        };
        string litery = "WGER";
        int ostatniJedno = -1, pierwszyDwu = int.MaxValue;
        var widziane = new HashSet<string>();
        for (int i = 0; i < protos.Count; i++)
        {
            StylePrototype pr = protos[i];
            var ponad = Enumerable.Range(0, 4).Where(d => pr.Get((StyleDimension)d) > 0.5f).ToList();
            bool resztaSrodek = Enumerable.Range(0, 4).Where(d => !ponad.Contains(d)).All(d => pr.Get((StyleDimension)d) == 0.5f);
            if ((ponad.Count == 1 || ponad.Count == 2) && resztaSrodek)
            {
                string klucz = string.Concat(ponad.Select(d => litery[d]));
                string oczek;
                spec.TryGetValue(klucz, out oczek);
                T.EqS("prototyp " + klucz + " ma zatwierdzona nazwe", pr.label, oczek ?? "(brak w spec)");
                widziane.Add(klucz);
                if (ponad.Count == 1) ostatniJedno = Math.Max(ostatniJedno, i);
                else pierwszyDwu = Math.Min(pierwszyDwu, i);
            }
        }
        T.EqI("kazdy prototyp jedno- i dwucechowy obecny (4 + 6)", widziane.Count, spec.Count);
        T.Ok("jednocechowe przed dwucechowymi (przy nieznanej cesze Wojownik wygrywa z Dowodca)",
             ostatniJedno < pierwszyDwu, "ostatni jednocechowy " + ostatniJedno + ", pierwszy dwucechowy " + pierwszyDwu);
        T.Ok("jednolity 'wszystko ponad norme' (Zaangazowany) obecny",
             protos.Any(pr => pr.label == "Zaangazowany" && Enumerable.Range(0, 4).All(d => pr.Get((StyleDimension)d) > 0.5f
                 && pr.Get((StyleDimension)d) == pr.walka)), null);
        T.Ok("jednolity 'wszystko ponizej normy' (Bierny) obecny",
             protos.Any(pr => pr.label == "Bierny" && Enumerable.Range(0, 4).All(d => pr.Get((StyleDimension)d) < 0.5f
                 && pr.Get((StyleDimension)d) == pr.walka)), null);
    }

    /// <summary>Sanitize nie jest pusty: poprawia rozpietosc 0, duplikat, brak pomiaru, zla etykiete.</summary>
    static void TestSanitize()
    {
        PlayerStyleParams p = PlayerStyleParams.Default();
        p.signals[0].spread = 0f;
        p.signals.RemoveAll(s => s.signal == "Werbunek");
        p.signals.Add(new StyleSignalParams { signal = "Obrona", weight = 9f });
        p.signals.Add(new StyleSignalParams { signal = "Bzdura" });
        p.prototypes.Add(new StylePrototype { label = "zla etykieta" });
        p.warmupDays = 99;
        string popr = p.Sanitize();
        T.Ok("Sanitize zglasza poprawki zepsutego bloku", !string.IsNullOrEmpty(popr), popr);
        T.Ok("rozpietosc 0 zastapiona dodatnia", p.Signal(StyleSignal.Obrona).spread > 0f, null);
        T.EqI("duplikat Obrony pominiety", p.signals.Count(s => s.signal == "Obrona"), 1);
        T.Ok("brakujacy Werbunek uzupelniony", p.signals.Any(s => s.signal == "Werbunek"), null);
        T.Ok("nieznany pomiar pominiety", !p.signals.Any(s => s.signal == "Bzdura"), null);
        T.Ok("prototyp ze zla etykieta pominiety", !p.prototypes.Any(pr => pr.label == "zla etykieta"), null);
        T.EqI("rozgrzewka dluzsza niz kolejka scieta do pojemnosci", p.warmupDays, p.capacityDays);
    }

    // ------------------------------------------------------------------ 14b

    static void Test14b(PlayerStyleParams p)
    {
        T.Section("TEST 14b - kolejka FIFO dni i rozgrzewka");
        var led = new PlayerStyleLedger();
        int start = 37, ile = p.capacityDays + 1;
        for (int i = 0; i < ile; i++)
        {
            var d = new StyleDaySample(start + i);
            d.Set(StyleSignal.Obrona, start + i, 1000);
            led.PushDay(d, p.capacityDays);
        }
        T.EqI("STRAZNIK: wepchnieto pojemnosc + 1 dni", ile, p.capacityDays + 1);
        T.EqI("kolejka trzyma dokladnie pojemnosc dni", led.Days.Count, p.capacityDays);
        T.EqI("wypchniety NAJSTARSZY dzien (pierwszy w kolejce = drugi wepchniety)", led.Days[0].Day, start + 1);
        T.EqI("najnowszy dzien na koncu", led.Days[led.Days.Count - 1].Day, start + ile - 1);

        var krotka = new PlayerStyleLedger();
        for (int i = 0; i < p.warmupDays - 1; i++) krotka.PushDay(new StyleDaySample(100 + i), p.capacityDays);
        krotka.Partial = new StyleDaySample(100 + p.warmupDays - 1);
        T.Ok("rozgrzewka - 1 dzien (plus dzien w toku) = styl NIEAKTYWNY",
             !PlayerStyleModel.Evaluate(krotka, p).Active, "dni=" + krotka.Days.Count);
        krotka.PushDay(new StyleDaySample(100 + p.warmupDays - 1), p.capacityDays);
        T.Ok("dokladnie rozgrzewka dni = styl AKTYWNY", PlayerStyleModel.Evaluate(krotka, p).Active, "dni=" + krotka.Days.Count);
    }

    // ------------------------------------------------------------------ 14c

    static void Test14c(PlayerStyleParams p)
    {
        T.Section("TEST 14c - tryby agregacji i progi znajomosci");
        var led = new PlayerStyleLedger();
        var a = new StyleDaySample(10);
        a.Set(StyleSignal.Przyjecia, 1, 1);
        a.Set(StyleSignal.Obrona, 10, 100);
        var b = new StyleDaySample(11);
        b.Set(StyleSignal.Przyjecia, 0, 3);
        b.Set(StyleSignal.Obrona, 0, 0);
        var c = new StyleDaySample(12);
        c.Set(StyleSignal.Obrona, 60, 200);
        led.PushDay(a, 60); led.PushDay(b, 60); led.PushDay(c, 60);
        StyleReading r = PlayerStyleModel.Evaluate(led, p);
        // Zdarzenia: iloraz sum 1/(1+3) = 0.25; srednia z ilorazow dalaby (1 + 0)/2 = 0.5.
        T.Eq("Przyjecia = iloraz sum z okna (0.25), nie srednia z dni (0.5)", r.SignalX[(int)StyleSignal.Przyjecia], 0.25, 1e-6);
        // Stan: srednia z dni z mianownikiem, (0.1 + 0.3)/2 = 0.2; dzien 0/0 liczony jako 0 dalby 0.1333,
        // iloraz sum 70/300 = 0.2333.
        T.Eq("Obrona = srednia dzienna z pominieciem dnia 0/0 (0.2)", r.SignalX[(int)StyleSignal.Obrona], 0.2, 1e-6);

        T.Ok("Werbunek bez schwytanych = nieznany", !r.SignalKnown[(int)StyleSignal.Werbunek], null);
        // Prog z XML (decyzja S8: 2 schwytanych) - o jednego mniej nieznany, rowno z progiem znany.
        int minJencow = p.Signal(StyleSignal.Werbunek).minEvents;
        var w = new StyleDaySample(13);
        w.Set(StyleSignal.Werbunek, 1, minJencow - 1);
        led.PushDay(w, 60);
        T.Ok("Werbunek z " + (minJencow - 1) + " schwytanymi (prog " + minJencow + ") = nieznany",
             minJencow - 1 == 0 || !PlayerStyleModel.Evaluate(led, p).SignalKnown[(int)StyleSignal.Werbunek], null);
        var w2 = new StyleDaySample(14);
        w2.Set(StyleSignal.Werbunek, 0, 1);
        led.PushDay(w2, 60);
        T.Ok("Werbunek z " + minJencow + " schwytanymi = znany", PlayerStyleModel.Evaluate(led, p).SignalKnown[(int)StyleSignal.Werbunek], null);

        var epi = new PlayerStyleLedger();
        var e1 = new StyleDaySample(20);
        e1.Set(StyleSignal.Poborowi, 600, 1);
        epi.PushDay(e1, 60);
        T.Ok("Poborowi z 1 epizodem = nieznany (min 2)", !PlayerStyleModel.Evaluate(epi, p).SignalKnown[(int)StyleSignal.Poborowi], null);
        var e2 = new StyleDaySample(21);
        e2.Set(StyleSignal.Poborowi, 800, 1);
        epi.PushDay(e2, 60);
        StyleReading re = PlayerStyleModel.Evaluate(epi, p);
        T.Ok("Poborowi z 2 epizodami = znany", re.SignalKnown[(int)StyleSignal.Poborowi], null);
        T.Eq("Poborowi = (600 + 800) promili / 2 epizody = 0.7", re.SignalX[(int)StyleSignal.Poborowi], 0.7, 1e-6);

        // Znajomosc CECH po rozgrzewce: Ekspansja wymaga oferty albo jenca, Reaktywnosc 2 epizodow.
        var pelna = new PlayerStyleLedger();
        for (int i = 0; i < p.warmupDays; i++)
        {
            var d = new StyleDaySample(200 + i);
            d.Set(StyleSignal.Obrona, 5, 100);
            d.Set(StyleSignal.Inicjatywa, 0, 1);
            d.Set(StyleSignal.Praca, 60, 100);
            pelna.PushDay(d, p.capacityDays);
        }
        StyleReading rp = PlayerStyleModel.Evaluate(pelna, p);
        T.Ok("STRAZNIK: styl aktywny po rozgrzewce", rp.Active, null);
        T.Ok("Walka i Gospodarka znane", rp.Known[0] && rp.Known[1], null);
        T.Ok("Ekspansja bez ofert i jencow = nieznana", !rp.Known[2], null);
        T.Ok("Reaktywnosc bez epizodow = nieznana", !rp.Known[3], null);
        int minOfert = p.Signal(StyleSignal.Przyjecia).minEvents;
        T.Ok("STRAZNIK: prog ofert wiekszy niz 1 (decyzja S8)", minOfert > 1, "minEvents=" + minOfert);
        for (int i = 0; i < minOfert - 1; i++) pelna.Days[3 + i].Set(StyleSignal.Przyjecia, 1, 1);
        T.Ok("Ekspansja z " + (minOfert - 1) + " ofertami (o jedna za malo) = nieznana", !PlayerStyleModel.Evaluate(pelna, p).Known[2], null);
        pelna.Days[3 + minOfert - 1].Set(StyleSignal.Przyjecia, 1, 1);
        T.Ok("Ekspansja z " + minOfert + " ofertami = znana", PlayerStyleModel.Evaluate(pelna, p).Known[2], null);

        // INICJATYWA TYLKO W GORE (decyzja S8): bez atakow w oknie pomiar nieznany, Walka = sama Obrona.
        StyleReading bezAtakow = PlayerStyleModel.Evaluate(pelna, p);
        T.Ok("bez atakow w oknie Inicjatywa NIEZNANA", !bezAtakow.SignalKnown[(int)StyleSignal.Inicjatywa], null);
        T.Eq("...a Walka = z samej Obrony (pelny zakres 0..1)", bezAtakow.Z[0], Oczekiwane(0.05, p.Signal(StyleSignal.Obrona)), 1e-5);
        pelna.Days[5].Set(StyleSignal.Inicjatywa, 1, 1);
        StyleReading zAtakiem = PlayerStyleModel.Evaluate(pelna, p);
        double zI = Oczekiwane(1.0 / p.warmupDays, p.Signal(StyleSignal.Inicjatywa));
        double wO = p.Signal(StyleSignal.Obrona).weight, wI = p.Signal(StyleSignal.Inicjatywa).weight;
        T.Ok("jeden atak w oknie: Inicjatywa znana", zAtakiem.SignalKnown[(int)StyleSignal.Inicjatywa], null);
        T.Ok("...i moze Walke tylko PODNIESC (z Inicjatywy > 0.5)", zI > 0.5 && zAtakiem.Z[0] > bezAtakow.Z[0], "zI=" + T.F(zI));
        T.Eq("...Walka = srednia wazona Obrony i Inicjatywy", zAtakiem.Z[0],
             (wO * Oczekiwane(0.05, p.Signal(StyleSignal.Obrona)) + wI * zI) / (wO + wI), 1e-5);

        // SZYBKOSC w promilach (przeglad S8: skala 1000 nie miala asercji): 800 i 400 promili z dwoch epizodow = 0.6.
        var sz = new PlayerStyleLedger();
        var s1 = new StyleDaySample(10);
        s1.Set(StyleSignal.Szybkosc, 800, 1);
        s1.Set(StyleSignal.Poborowi, 300, 1);
        var s2 = new StyleDaySample(11);
        s2.Set(StyleSignal.Szybkosc, 400, 1);
        s2.Set(StyleSignal.Poborowi, 500, 1);
        sz.PushDay(s1, p.capacityDays);
        sz.PushDay(s2, p.capacityDays);
        StyleReading rs = PlayerStyleModel.Evaluate(sz, p);
        T.Eq("Szybkosc = (800 + 400) promili / 2 epizody = 0.6", rs.SignalX[(int)StyleSignal.Szybkosc], 0.6, 1e-6);

        // REAKTYWNOSC jako CECHA (przeglad S8: dotad tylko pomiar): znana od 2 epizodow, srednia wazona 1 i 0.5.
        var rk = new PlayerStyleLedger();
        for (int i = 0; i < p.warmupDays; i++)
        {
            var d = new StyleDaySample(400 + i);
            d.Set(StyleSignal.Obrona, 5, 100);
            d.Set(StyleSignal.Praca, 60, 100);
            rk.PushDay(d, p.capacityDays);
        }
        rk.Days[2].Set(StyleSignal.Poborowi, 600, 1);
        rk.Days[2].Set(StyleSignal.Szybkosc, 900, 1);
        T.Ok("Reaktywnosc z JEDNYM epizodem = nieznana", !PlayerStyleModel.Evaluate(rk, p).Known[3], null);
        rk.Days[7].Set(StyleSignal.Poborowi, 200, 1);
        rk.Days[7].Set(StyleSignal.Szybkosc, 300, 1);
        StyleReading rr = PlayerStyleModel.Evaluate(rk, p);
        double zP = Oczekiwane(0.4, p.Signal(StyleSignal.Poborowi)), zS = Oczekiwane(0.6, p.Signal(StyleSignal.Szybkosc));
        double wP = p.Signal(StyleSignal.Poborowi).weight, wS = p.Signal(StyleSignal.Szybkosc).weight;
        T.Ok("Reaktywnosc z 2 epizodami = znana", rr.Known[3], null);
        T.Ok("STRAZNIK: pomiary Reaktywnosci roznia sie wartoscia z", Math.Abs(zP - zS) > 0.05, "zP=" + T.F(zP) + " zS=" + T.F(zS));
        T.Eq("Reaktywnosc = (wP*z(Poborowi) + wS*z(Szybkosc)) / (wP + wS)", rr.Z[3], (wP * zP + wS * zS) / (wP + wS), 1e-5);
    }

    // ------------------------------------------------------------------ 14d

    static void Test14d(PlayerStyleParams p)
    {
        T.Section("TEST 14d - wspolna skala z norma i rozpietoscia");
        var sp = new StyleSignalParams { signal = "Obrona", norm = 0.3f, spread = 0.2f };
        T.Eq("x = norma -> 0.5", PlayerStyleModel.MapToScale(0.3f, sp), 0.5, 1e-6);
        T.Eq("x = norma + rozpietosc -> 1", PlayerStyleModel.MapToScale(0.5f, sp), 1.0, 1e-6);
        T.Eq("x = norma - pol rozpietosci -> 0.25", PlayerStyleModel.MapToScale(0.2f, sp), 0.25, 1e-6);
        T.Eq("ponizej zakresu klamrowane do 0", PlayerStyleModel.MapToScale(-1f, sp), 0.0, 1e-9);
        T.Eq("powyzej zakresu klamrowane do 1", PlayerStyleModel.MapToScale(5f, sp), 1.0, 1e-9);

        var normy = Enumerable.Range(0, StyleSignals.Count).Select(i => p.Signal((StyleSignal)i).norm).Distinct().Count();
        T.Ok("STRAZNIK: pomiary maja rozne normy (fikstura rozroznia pomiary)", normy >= 4, "roznych norm: " + normy);

        // Srednia wazona cechy liczona NIEZALEZNIE od kodu: Walka = (1*z(Obrona) + 0.5*z(Inicjatywa)) / 1.5.
        var led = new PlayerStyleLedger();
        for (int i = 0; i < p.warmupDays; i++)
        {
            var d = new StyleDaySample(300 + i);
            d.Set(StyleSignal.Obrona, 7, 100);            // x = 0.07
            d.Set(StyleSignal.Inicjatywa, i == 0 ? 1 : 0, 1); // x = 1/15
            d.Set(StyleSignal.Praca, 50, 100);             // x = 0.5, Produkcja nieznana (0/0)
            led.PushDay(d, p.capacityDays);
        }
        StyleReading r = PlayerStyleModel.Evaluate(led, p);
        double zO = Oczekiwane(0.07, p.Signal(StyleSignal.Obrona));
        double zI = Oczekiwane(1.0 / p.warmupDays, p.Signal(StyleSignal.Inicjatywa));
        double wO = p.Signal(StyleSignal.Obrona).weight, wI = p.Signal(StyleSignal.Inicjatywa).weight;
        T.Ok("STRAZNIK: oba pomiary Walki roznia sie wartoscia z", Math.Abs(zO - zI) > 0.05, "zO=" + T.F(zO) + " zI=" + T.F(zI));
        T.Eq("Walka = srednia wazona pomiarow (wagi 1 i 0.5)", r.Z[0], (wO * zO + wI * zI) / (wO + wI), 1e-5);
        T.Eq("Gospodarka = tylko znany pomiar Praca (Produkcja 0/0 pominieta)", r.Z[1],
             Oczekiwane(0.5, p.Signal(StyleSignal.Praca)), 1e-5);
    }

    /// <summary>Niezalezny wzor wspolnej skali (nie wola kodu rdzenia).</summary>
    static double Oczekiwane(double x, StyleSignalParams sp)
    {
        double z = 0.5 + 0.5 * (x - sp.norm) / sp.spread;
        return z < 0 ? 0 : (z > 1 ? 1 : z);
    }

    // ------------------------------------------------------------------ 14e

    static void Test14e(PlayerStyleParams p)
    {
        T.Section("TEST 14e - profil wzgledny, mocne strony, etykiety");
        PlayerStyleParams bin = p.Clone();
        bin.relativeScale = 0.25f;
        bin.strongSideThreshold = 0.5f;
        var wszystkie = new[] { true, true, true, true };
        // Dwojkowo dokladne: srednia 0.5, c_W = 0.125/0.25 = 0.5 dokladnie = prog -> mocna (>=).
        StyleReading r = PlayerStyleModel.FromVector(new[] { 0.625f, 0.375f, 0.5f, 0.5f }, wszystkie, bin.capacityDays, bin);
        T.Eq("c Walki = (0.625 - 0.5)/0.25 = 0.5", r.C[0], 0.5, 0.0);
        T.Ok("c rowne progowi = mocna strona (>=)", r.Strong[0], null);
        T.Ok("c ujemne = nie mocna", !r.Strong[1] && r.C[1] < 0f, "c_G=" + T.F(r.C[1]));

        StyleReading bezR = PlayerStyleModel.FromVector(new[] { 0.75f, 0.5f, 0.5f, 0.9f }, new[] { true, true, true, false }, p.capacityDays, p);
        double sr = (0.75 + 0.5 + 0.5) / 3.0;
        T.Eq("srednia tylko po ZNANYCH cechach", bezR.C[0], Math.Min(1.0, (0.75 - sr) / p.relativeScale), 1e-5);
        T.Ok("nieznana cecha ma c = 0 i nie jest mocna", bezR.C[3] == 0f && !bezR.Strong[3], null);

        StyleReading jedna = PlayerStyleModel.FromVector(new[] { 0.9f, 0.5f, 0.5f, 0.5f }, new[] { true, false, false, false }, p.capacityDays, p);
        T.Ok("jedna znana cecha: c = 0, brak mocnych stron", jedna.C[0] == 0f && !jedna.Strong[0], null);

        StyleReading skraj = PlayerStyleModel.FromVector(new[] { 1f, 0f, 0f, 0f }, wszystkie, p.capacityDays, p);
        T.Eq("c klamrowane do 1 ((1 - 0.25)/0.25 = 3 -> 1)", skraj.C[0], 1.0, 0.0);
        StyleReading dno = PlayerStyleModel.FromVector(new[] { 1f, 1f, 1f, 0f }, wszystkie, p.capacityDays, p);
        T.Eq("c klamrowane do -1 ((0 - 0.75)/0.25 = -3 -> -1)", dno.C[3], -1.0, 0.0);

        Etykieta(p, new[] { 0.8f, 0.8f, 0.8f, 0.8f }, wszystkie, "Zaangazowany", "rowno mocna kolonia");
        Etykieta(p, new[] { 0.8f, 0.8f, 0.5f, 0.5f }, wszystkie, "Kasztelan", "Walka + Gospodarka");
        Etykieta(p, new[] { 0.5f, 0.5f, 0.8f, 0.8f }, wszystkie, "Opiekun", "Ekspansja + Reaktywnosc");
        Etykieta(p, new[] { 0.2f, 0.2f, 0.2f, 0.2f }, wszystkie, "Bierny", "rowno slaba kolonia");
        Etykieta(p, new[] { 0.8f, 0.5f, 0.5f, 0.9f }, new[] { true, true, true, false }, "Wojownik",
                 "Reaktywnosc nieznana: remis Wojownik/Dowodca -> wczesniejszy");
        Etykieta(p, new[] { 0.5f, 0.5f, 0.5f, 0.9f }, new[] { true, true, true, false }, "Wszechstronny",
                 "Reaktywnosc nieznana: remis Wszechstronny/Czujny -> wczesniejszy");

        PlayerStyleParams remis = p.Clone();
        remis.prototypes = new List<StylePrototype>
        {
            // Wartosci dwojkowo dokladne (0.375 i 0.625): 0.4f i 0.6f NIE sa rowno odlegle od 0.5f
            // we float, wiec remis bylby pozorny (pierwsza wersja tej fikstury).
            new StylePrototype { label = "A", walka = 0.375f, gospodarka = 0.5f, ekspansja = 0.5f, reaktywnosc = 0.5f },
            new StylePrototype { label = "B", walka = 0.625f, gospodarka = 0.5f, ekspansja = 0.5f, reaktywnosc = 0.5f }
        };
        var srodek = new[] { 0.5f, 0.5f, 0.5f, 0.5f };
        T.EqS("dokladny remis -> prototyp wczesniejszy (A)", PlayerStyleModel.FromVector(srodek, wszystkie, p.capacityDays, remis).Label, "A");
        remis.prototypes.Reverse();
        T.EqS("ta sama fikstura odwrocona -> znow wczesniejszy (B)", PlayerStyleModel.FromVector(srodek, wszystkie, p.capacityDays, remis).Label, "B");

        StyleReading rozgrz = PlayerStyleModel.FromVector(new[] { 0.8f, 0.5f, 0.5f, 0.5f }, wszystkie, p.warmupDays - 1, p);
        T.Ok("w rozgrzewce: brak etykiety, cech znanych i mocnych stron",
             rozgrz.Label.Length == 0 && rozgrz.KnownCount == 0 && !rozgrz.Strong.Any(x => x), null);
        T.EqS("postac kanoniczna mocnych stron", PlayerStyleModel.FromVector(new[] { 0.9f, 0.5f, 0.9f, 0.3f }, wszystkie, p.capacityDays, p).StrongCanonical(),
              ";Walka;Ekspansja;");
    }

    static void Etykieta(PlayerStyleParams p, float[] z, bool[] known, string oczek, string opis)
    {
        T.EqS("etykieta: " + opis, PlayerStyleModel.FromVector(z, known, p.capacityDays, p).Label, oczek);
    }

    // ------------------------------------------------------------------ 14f

    static void Test14f(XmlConfig cfg, PlayerStyleParams p)
    {
        T.Section("TEST 14f - kierunek d = 0.3*orientacja + 0.7*rytm (decyzja nr 13)");
        var orient = new Dictionary<string, float> { { "Powsciagliwy", 1f }, { "Zrownowazony", 0f }, { "Napastliwy", -1f } };
        // Tabela autora (oddech / utrzymanie / eskalacja), zatwierdzona w kwestionariuszu.
        var tabela = new Dictionary<string, float[]>
        {
            { "Powsciagliwy", new[] { 1.0f, 0.3f, -0.4f } },
            { "Zrownowazony", new[] { 0.7f, 0.0f, -0.7f } },
            { "Napastliwy", new[] { 0.4f, -0.3f, -1.0f } }
        };
        int sprawdzonych = 0;
        foreach (var kv in orient)
        {
            NarratorProfile prof = cfg.profiles.FirstOrDefault(x => x.Id.Contains(kv.Key));
            T.Ok("profil " + kv.Key + " obecny", prof != null, null);
            if (prof == null) continue;
            T.Eq("orientacja " + kv.Key + " z XML", prof.StyleOrientation, kv.Value, 0.0);
            float[] oczek = tabela[kv.Key];
            T.Eq(kv.Key + " w oddechu", StyleDirection.Compute(prof.StyleOrientation, Intent.Breathe, p), oczek[0], 1e-5);
            T.Eq(kv.Key + " w utrzymaniu", StyleDirection.Compute(prof.StyleOrientation, Intent.Hold, p), oczek[1], 1e-5);
            T.Eq(kv.Key + " w eskalacji", StyleDirection.Compute(prof.StyleOrientation, Intent.Escalate, p), oczek[2], 1e-5);
            sprawdzonych++;
        }
        T.EqI("STRAZNIK: sprawdzono trzy profile", sprawdzonych, 3);
        T.Eq("Intent.Pass traktowany jak oddech",
             StyleDirection.Compute(-1f, Intent.Pass, p), StyleDirection.Compute(-1f, Intent.Breathe, p), 0.0);
        T.Eq("profil awaryjny ma orientacje Zrownowazonego (0)", NarratorProfile.Fallback().StyleOrientation, orient["Zrownowazony"], 0.0);
        T.Eq("orientacja spoza zakresu: d sciete do 1", StyleDirection.Compute(5f, Intent.Breathe, p), 1.0, 0.0);
    }

    // ------------------------------------------------------------------ 14j (N/A/D)

    static void Test14jNAD()
    {
        T.Section("TEST 14j - kodek ksiegi stylu: linie N, D, A");
        var led = new PlayerStyleLedger
        {
            CurrentDay = 123, Initialized = true, LastRaidTick = 7654321, MaxOfferId = 4321
        };
        var domyslna = new PlayerStyleLedger();
        T.Ok("STRAZNIK: kazde pole naglowka rozne od domyslnego",
             led.CurrentDay != domyslna.CurrentDay && led.Initialized != domyslna.Initialized
             && led.LastRaidTick != domyslna.LastRaidTick && led.MaxOfferId != domyslna.MaxOfferId, null);
        led.PushDay(Dzien(37), 60);
        led.PushDay(Dzien(38), 60);
        led.Partial = Dzien(39);
        List<string> linie = led.ToPersistableLines();
        var odt = new PlayerStyleLedger();
        int odrz = odt.RestoreFromLines(linie);
        T.EqI("zapis -> odczyt bez odrzucen", odrz, 0);
        T.Ok("naglowek odtworzony pole po polu",
             odt.CurrentDay == 123 && odt.Initialized && odt.LastRaidTick == 7654321 && odt.MaxOfferId == 4321, null);
        T.EqI("dwa dni zamkniete", odt.Days.Count, 2);
        T.Ok("dni odtworzone co do kazdego licznika i mianownika",
             odt.Days.Count == 2 && odt.Days[0].SameAs(led.Days[0]) && odt.Days[1].SameAs(led.Days[1]), null);
        T.Ok("dzien w toku odtworzony", odt.Partial != null && odt.Partial.SameAs(led.Partial), null);

        var zle = new List<string>(linie)
        {
            linie[0],                                    // drugie N
            linie[1],                                    // dzien 37 ponownie (nie wiekszy od 38)
            linie[linie.Count - 1],                      // drugie A
            "X|1",                                       // nieznany znacznik
            "D|40|1|2",                                  // zla liczba pol
            "D|41|x" + string.Concat(Enumerable.Repeat("|1", 2 * StyleSignals.Count - 1)), // zla liczba, poprawna dlugosc
            "N|2|1|0|0|0"                                // zla wersja
        };
        T.EqI("STRAZNIK: linia ze zla liczba ma poprawna liczbe pol", zle[linie.Count + 5].Split('|').Length,
              2 + 2 * StyleSignals.Count);
        var odt2 = new PlayerStyleLedger();
        int odrz2 = odt2.RestoreFromLines(zle);
        T.EqI("odrzucone: drugie N, powtorzony dzien, drugie A, nieznany znacznik, zle pola, zla liczba, zla wersja",
              odrz2, 7);
        T.EqI("poprawne linie zostaly", odt2.Days.Count, 2);

        var pusta = new PlayerStyleLedger();
        T.EqI("pusta lista = 0 odrzuconych", pusta.RestoreFromLines(new List<string>()), 0);
        T.Ok("pusta lista = ksiega w stanie poczatkowym",
             pusta.CurrentDay == -1 && !pusta.Initialized && pusta.Days.Count == 0 && pusta.Partial == null, null);
    }

    // ------------------------------------------------------------------ 14i (S2)

    const long Doba = 60000;

    static PawnRecordSample Pionek(int id, bool ok, long prod, long pom, long zwerb, long schw)
    {
        return new PawnRecordSample { Id = id, Eligible = ok, Productive = prod, Support = pom, Recruited = zwerb, Captured = schw };
    }

    static void Test14i(PlayerStyleParams p)
    {
        T.Section("TEST 14i - obserwacja: bazy rekordow, epizody zagrozenia, oferty, dzicy ludzie, ataki");
        // Identyfikatory celowo nieciagle i rozne miedzy rodzajami (pionki, mapy, zadania, dzicy),
        // a liczby poborowych rozne od zdolnych - zamiana dwoch pojec jest wtedy widoczna.
        var led = new PlayerStyleLedger();
        var init = new DayObservation { LastPlayerRaidTick = 5000 };
        init.Pawns.Add(Pionek(1001, true, 500, 100, 2, 3));
        init.Pawns.Add(Pionek(2002, false, 900, 900, 9, 9));
        init.Offers.Add(new OfferQuestSample { Id = 10, State = OfferState.Success });
        init.Offers.Add(new OfferQuestSample { Id = 12, State = OfferState.Pending });
        init.WildMen.Add(new WildManSample { Id = 7001, Status = WildManStatus.Wild });
        led.Initialize(37 * Doba + 123, init, p);
        T.Ok("Initialize: zainicjowana, dzien w toku 37", led.Initialized && led.CurrentDay == 37 && led.Partial != null && led.Partial.Day == 37, null);
        T.Ok("Initialize: baza tylko dla kolonisty (nie dla nie-kolonisty)", led.Baselines.ContainsKey(1001) && !led.Baselines.ContainsKey(2002), null);
        T.EqI("Initialize: znak wodny = najwyzsze id zadania-oferty", led.MaxOfferId, 12);
        T.Ok("Initialize: oferta w toku sledzona, zakonczona nie", led.PendingOffers.SetEquals(new[] { 12 }), null);
        T.Ok("Initialize: dziki czlowiek sledzony od dnia 37", led.WildMen.ContainsKey(7001) && led.WildMen[7001] == 37, null);

        // ---- ataki gracza: liczy sie ZMIANA ticku ostatniego ataku
        led.ObserveRaidTick(5000);
        led.ObserveRaidTick(6000);
        led.ObserveRaidTick(6000);
        T.EqI("atak liczony raz na zmiane ticku (5000 -> 6000)", (int)led.Partial.Num[(int)StyleSignal.Inicjatywa], 1);

        // ---- epizod zagrozenia na mapie 3 z histereza
        long t0 = 37 * Doba + 1000;
        int ciche = p.quietSamplesToClose;
        long t = t0;
        Probka(led, p, t, 3, true, 0, 4);                // start epizodu, nikt pod bronia
        Probka(led, p, t += 250, 3, true, 2, 4);         // pierwszy pobor po 250 tickach
        Probka(led, p, t += 250, 3, false, 0, 4);        // migotanie: cicha probka...
        Probka(led, p, t += 250, 3, true, 3, 6);         // ...i znowu zagrozenie (licznik ciszy od zera)
        for (int i = 0; i < ciche - 1; i++) Probka(led, p, t += 250, 3, false, 0, 6);
        T.Ok("epizod otwarty po " + (ciche - 1) + " cichych probkach (histereza)", led.Episodes.ContainsKey(3), null);
        T.EqI("przed zamknieciem nic nie trafilo do Poborowych", (int)led.Partial.Den[(int)StyleSignal.Poborowi], 0);
        Probka(led, p, t += 250, 3, false, 0, 6);
        T.Ok("epizod zamkniety po " + ciche + " cichych probkach", !led.Episodes.ContainsKey(3), null);
        // Oczekiwane liczone z definicji: pobor = (0+2+3)/(4+4+6), szybkosc = 1 - 250/limit.
        long pobor = (long)Math.Round(1000.0 * 5 / 14, MidpointRounding.AwayFromZero);
        long szybk = (long)Math.Round(1000.0 * (1.0 - 250.0 / p.reactionCapTicks), MidpointRounding.AwayFromZero);
        T.EqI("Poborowi: jeden epizod", (int)led.Partial.Den[(int)StyleSignal.Poborowi], 1);
        T.EqI("Poborowi = 1000 * suma poborowych / suma zdolnych", (int)led.Partial.Num[(int)StyleSignal.Poborowi], (int)pobor);
        T.EqI("Szybkosc = 1000 * (1 - opoznienie pierwszego poboru / limit)", (int)led.Partial.Num[(int)StyleSignal.Szybkosc], (int)szybk);

        // Za krotki epizod (1 probka z zagrozeniem) - odrzucony; mapa znikajaca - epizod przepada.
        Probka(led, p, t += 250, 4, true, 1, 3);
        for (int i = 0; i < ciche; i++) Probka(led, p, t += 250, 4, false, 0, 3);
        T.EqI("epizod krotszy niz minimum probek nie liczony", (int)led.Partial.Den[(int)StyleSignal.Poborowi], 1);
        Probka(led, p, t += 250, 5, true, 1, 3);
        Probka(led, p, t += 250, 5, true, 1, 3);
        led.OnThreatSample(t += 250, new List<MapThreatSample> { new MapThreatSample { MapId = 3, Threat = false } }, p);
        T.Ok("epizod mapy, ktorej nie ma w probce, przepada bez liczenia",
             !led.Episodes.ContainsKey(5) && led.Partial.Den[(int)StyleSignal.Poborowi] == 1, null);

        // ---- zamkniecie doby 37 obserwacja z poczatku doby 38
        var o38 = new DayObservation { DefenseValue = 300, ProductionValue = 1200, BuildingValue = 6000, LastPlayerRaidTick = 6000 };
        o38.Pawns.Add(Pionek(1001, true, 800, 250, 3, 5));   // delty: 300, 150, 1, 2
        o38.Pawns.Add(Pionek(2002, true, 950, 999, 9, 9));   // kolonista od dzis: tylko baza
        o38.Pawns.Add(Pionek(3003, true, 100, 0, 0, 0));     // nowy: tylko baza
        o38.Offers.Add(new OfferQuestSample { Id = 10, State = OfferState.Success }); // stara, ponizej znaku wodnego
        o38.Offers.Add(new OfferQuestSample { Id = 12, State = OfferState.Success }); // oczekujaca -> przyjeta
        o38.Offers.Add(new OfferQuestSample { Id = 12, State = OfferState.Success }); // duplikat w probce
        o38.Offers.Add(new OfferQuestSample { Id = 13, State = OfferState.Fail });    // nowa i od razu odrzucona
        o38.Offers.Add(new OfferQuestSample { Id = 14, State = OfferState.Pending }); // nowa w toku
        o38.Offers.Add(new OfferQuestSample { Id = 15, State = OfferState.Void });    // niewazna
        o38.WildMen.Add(new WildManSample { Id = 7001, Status = WildManStatus.Joined });
        o38.WildMen.Add(new WildManSample { Id = 7002, Status = WildManStatus.Wild });
        StyleDaySample d37 = led.CloseDay(38 * Doba + 5, o38, p);
        T.Ok("CloseDay zwraca zamkniety dzien 37", d37 != null && d37.Day == 37, null);
        T.Ok("dzien 37 w kolejce, dzien w toku 38", led.Days.Count == 1 && led.Days[0].Day == 37 && led.CurrentDay == 38 && led.Partial.Day == 38, null);
        Para("Obrona = obrona / budynki", d37, StyleSignal.Obrona, 300, 6000);
        Para("Produkcja = produkcja / budynki", d37, StyleSignal.Produkcja, 1200, 6000);
        Para("Praca = delta produktywna / (produktywna + pomocnicza), tylko kolonista z baza", d37, StyleSignal.Praca, 300, 450);
        Para("Werbunek = delta zwerbowanych / delta schwytanych", d37, StyleSignal.Werbunek, 1, 2);
        Para("Przyjecia: oferta oczekujaca przyjeta + nowa odrzucona + dzikus dolaczyl", d37, StyleSignal.Przyjecia, 2, 3);
        Para("Inicjatywa: jeden atak w dobie, mianownik jeden dzien", d37, StyleSignal.Inicjatywa, 1, 1);
        Para("Poborowi z epizodu mapy 3", d37, StyleSignal.Poborowi, pobor, 1);
        T.Ok("bazy odswiezone dla kolonistow (1001 z nowa wartoscia, 2002 i 3003 dopisane)",
             led.Baselines.Count == 3 && led.Baselines[1001].Productive == 800 && led.Baselines.ContainsKey(2002), null);
        T.Ok("oczekujace: tylko nowa oferta w toku", led.PendingOffers.SetEquals(new[] { 14 }), null);
        T.EqI("znak wodny przesuniety do najwyzszego id", led.MaxOfferId, 15);
        T.Ok("dzikus 7001 rozstrzygniety, 7002 sledzony od dnia 37",
             !led.WildMen.ContainsKey(7001) && led.WildMen.ContainsKey(7002) && led.WildMen[7002] == 37, null);

        // ---- doba 38: kolonista przestal byc kolonista, ujemna delta, oferta znika, stara przyjeta wraca
        var o39 = new DayObservation { BuildingValue = 6000 };
        o39.Pawns.Add(Pionek(1001, false, 900, 900, 9, 9));  // np. zniewolony: bez delty, baza usunieta
        o39.Pawns.Add(Pionek(2002, true, 1000, 999, 9, 9));  // delta produktywna 50
        o39.Pawns.Add(Pionek(3003, true, 50, 0, 0, 0));      // spadek rekordu: delta sciety do 0
        o39.Offers.Add(new OfferQuestSample { Id = 12, State = OfferState.Success }); // juz policzona
        o39.WildMen.Add(new WildManSample { Id = 7002, Status = WildManStatus.Wild });
        StyleDaySample d38 = led.CloseDay(39 * Doba, o39, p);
        Para("Praca: bez nie-kolonisty i z ujemna delta scieta", d38, StyleSignal.Praca, 50, 50);
        Para("Przyjecia: oferta policzona wczesniej nie wraca", d38, StyleSignal.Przyjecia, 0, 0);
        T.Ok("baza nie-kolonisty usunieta", !led.Baselines.ContainsKey(1001) && led.Baselines.Count == 2, null);
        T.Ok("oferta, ktorej nie ma w menedzerze zadan, wypada bez liczenia", led.PendingOffers.Count == 0, null);

        // ---- dziki czlowiek: limit dni od REJESTRACJI (zamkniecie doby 37). Przeglad S8: odrzucenie przy zamknieciu
        // doby, w ktorej od rejestracji minelo "limit" dob - czyli doby 37 + limit (dawniej o jedna wczesniej).
        int limitDzien = 37 + p.wildManTimeoutDays;       // doba, ktorej zamkniecie odrzuca ofert
        StyleDaySample ost = null;
        for (int dzien = 39; dzien <= limitDzien; dzien++)
        {
            var o = new DayObservation { BuildingValue = 6000 };
            o.WildMen.Add(new WildManSample { Id = 7002, Status = WildManStatus.Wild });
            if (dzien == limitDzien)
            {
                T.Ok("dzikus jeszcze sledzony przed dniem limitu", led.WildMen.ContainsKey(7002), "dzien " + dzien);
            }
            ost = led.CloseDay((dzien + 1) * Doba, o, p);
        }
        T.Ok("dzikus nieoswojony przez limit dni = oferta odrzucona",
             !led.WildMen.ContainsKey(7002) && ost != null && ost.Den[(int)StyleSignal.Przyjecia] == 1 && ost.Num[(int)StyleSignal.Przyjecia] == 0,
             "dzien zamkniety " + (ost == null ? -1 : ost.Day));
        var zn = new DayObservation { BuildingValue = 6000 };
        zn.WildMen.Add(new WildManSample { Id = 7003, Status = WildManStatus.Wild });
        led.CloseDay((limitDzien + 2) * Doba, zn, p);
        StyleDaySample gone = led.CloseDay((limitDzien + 3) * Doba, new DayObservation { BuildingValue = 6000 }, p);
        Para("dzikus znikniety z gry = oferta odrzucona", gone, StyleSignal.Przyjecia, 0, 1);

        T.Ok("CloseDay na niezainicjowanej ksiedze inicjuje ja i nic nie wpycha",
             new PlayerStyleLedger().CloseDay(10 * Doba, init, p) == null, null);

        TestPrzegladuS8(p);
    }

    /// <summary>
    /// Przeglad adwersarialny S8 kroku 7: regula znaku wodnego niezalezna od kolejnosci, limit dzikusa 1, jeniec bez
    /// limitu, epizod bez zdolnych do walki, atak zauwazony dopiero przy zamknieciu doby, limit dlugosci epizodu.
    /// </summary>
    static void TestPrzegladuS8(PlayerStyleParams p)
    {
        T.Section("TEST 14i (S8) - poprawki z przegladu: kolejnosc ofert, limit dzikusa, jeniec, epizod bez zdolnych, limit epizodu");
        // Oferty w ODWROTNEJ kolejnosci id (15 przed 14), obie nowe wzgledem znaku wodnego 12.
        var lo = new PlayerStyleLedger();
        var io = new DayObservation();
        io.Offers.Add(new OfferQuestSample { Id = 12, State = OfferState.Success });
        lo.Initialize(20 * Doba, io, p);
        var oo = new DayObservation { BuildingValue = 1000 };
        oo.Offers.Add(new OfferQuestSample { Id = 15, State = OfferState.Pending });
        oo.Offers.Add(new OfferQuestSample { Id = 14, State = OfferState.Success });
        StyleDaySample d20 = lo.CloseDay(21 * Doba, oo, p);
        Para("oferta 14 po ofercie 15 (obie nowe) - przyjecie policzone", d20, StyleSignal.Przyjecia, 1, 1);
        T.Ok("...15 w toku, znak wodny 15", lo.PendingOffers.SetEquals(new[] { 15 }) && lo.MaxOfferId == 15, null);

        // Limit dzikusa 1: rejestracja przy zamknieciu doby D, odrzucenie dopiero przy zamknieciu D+1.
        PlayerStyleParams p1 = p.Clone();
        p1.wildManTimeoutDays = 1;
        var lw = new PlayerStyleLedger();
        lw.Initialize(30 * Doba, new DayObservation(), p1);
        var ow = new DayObservation { BuildingValue = 1000 };
        ow.WildMen.Add(new WildManSample { Id = 8001, Status = WildManStatus.Wild });
        StyleDaySample r30 = lw.CloseDay(31 * Doba, ow, p1);
        T.Ok("limit 1: dzikus zarejestrowany i NIE odrzucony w tej samej dobie", lw.WildMen.ContainsKey(8001)
             && r30.Den[(int)StyleSignal.Przyjecia] == 0, null);
        StyleDaySample r31 = lw.CloseDay(32 * Doba, ow, p1);
        Para("limit 1: odrzucony przy zamknieciu nastepnej doby", r31, StyleSignal.Przyjecia, 0, 1);

        // Jeniec (dziki wziety do niewoli) nie ma limitu - oferta otwarta bez konca.
        var lj = new PlayerStyleLedger();
        lj.Initialize(40 * Doba, new DayObservation(), p);
        long tj = 41 * Doba;
        int rozstrz = 0;
        for (int dz = 0; dz < p.wildManTimeoutDays + 3; dz++)
        {
            var oj = new DayObservation { BuildingValue = 1000 };
            oj.WildMen.Add(new WildManSample { Id = 8002, Status = WildManStatus.Prisoner });
            StyleDaySample dd = lj.CloseDay(tj + dz * Doba, oj, p);
            rozstrz += (int)dd.Den[(int)StyleSignal.Przyjecia];
        }
        T.Ok("jeniec-dzikus sledzony dalej po " + (p.wildManTimeoutDays + 3) + " dobach, bez rozstrzygniecia",
             lj.WildMen.ContainsKey(8002) && rozstrz == 0, "rozstrzygniec " + rozstrz);

        // Epizod bez nikogo zdolnego do walki (np. wszyscy powaleni) - nic nie mowi o reakcji.
        var le = new PlayerStyleLedger();
        le.Initialize(50 * Doba, new DayObservation(), p);
        long te = 50 * Doba + 1000;
        Probka(le, p, te, 6, true, 0, 0);
        Probka(le, p, te += 250, 6, true, 0, 0);
        for (int i = 0; i < p.quietSamplesToClose; i++) Probka(le, p, te += 250, 6, false, 0, 0);
        T.Ok("STRAZNIK: epizod zamkniety", !le.Episodes.ContainsKey(6), null);
        T.EqI("epizod bez zdolnych do walki nie liczony", (int)le.Partial.Den[(int)StyleSignal.Poborowi], 0);

        // Atak zauwazony dopiero przy zamknieciu doby (miedzy ostatnia probka a polnoca) liczy sie w zamykanej dobie.
        var la = new PlayerStyleLedger();
        la.Initialize(60 * Doba, new DayObservation { LastPlayerRaidTick = 1000 }, p);
        StyleDaySample d60 = la.CloseDay(61 * Doba, new DayObservation { BuildingValue = 1000, LastPlayerRaidTick = 60 * Doba + 59000 }, p);
        Para("atak widziany dopiero przy zamknieciu doby: Inicjatywa 1/1", d60, StyleSignal.Inicjatywa, 1, 1);

        // LIMIT DLUGOSCI EPIZODU (decyzja S8): ciagle zagrozenie dluzsze niz limit = JEDEN epizod, policzony przy limicie;
        // dalsze zagrozenie nic nie dopisuje, nowy epizod dopiero po ciszy.
        var ll = new PlayerStyleLedger();
        ll.Initialize(70 * Doba, new DayObservation(), p);
        long tl = 70 * Doba + 1000, start = tl;
        int probek = 0;
        while (tl - start < p.maxEpisodeTicks)
        {
            Probka(ll, p, tl, 9, true, 1, 4);
            probek++;
            tl += p.threatSampleTicks;
        }
        T.EqI("STRAZNIK: przed limitem epizod otwarty i niepoliczony", (int)ll.Partial.Den[(int)StyleSignal.Poborowi], 0);
        Probka(ll, p, tl, 9, true, 1, 4);
        probek++;
        T.EqI("przy limicie epizod POLICZONY raz", (int)ll.Partial.Den[(int)StyleSignal.Poborowi], 1);
        T.EqI("...Poborowi z probek do limitu = 1000 * 1/4", (int)ll.Partial.Num[(int)StyleSignal.Poborowi], 250);
        T.Ok("...epizod zostaje jako blokada (Capped)", ll.Episodes.ContainsKey(9) && ll.Episodes[9].Capped, null);
        for (int i = 0; i < 10; i++) Probka(ll, p, tl += p.threatSampleTicks, 9, true, 4, 4);
        T.EqI("zagrozenie trwa dalej: nic nie dopisane", (int)ll.Partial.Den[(int)StyleSignal.Poborowi], 1);
        for (int i = 0; i < p.quietSamplesToClose; i++) Probka(ll, p, tl += p.threatSampleTicks, 9, false, 0, 4);
        T.Ok("po ciszy blokada zdjeta bez drugiego liczenia", !ll.Episodes.ContainsKey(9)
             && ll.Partial.Den[(int)StyleSignal.Poborowi] == 1, null);
        Probka(ll, p, tl += p.threatSampleTicks, 9, true, 2, 4);
        T.Ok("nowe zagrozenie po ciszy = nowy epizod", ll.Episodes.ContainsKey(9) && !ll.Episodes[9].Capped
             && ll.Episodes[9].ThreatSamples == 1, null);
    }

    static void Probka(PlayerStyleLedger led, PlayerStyleParams p, long tick, int mapa, bool zagrozenie, int pobor, int zdolni)
    {
        led.OnThreatSample(tick, new List<MapThreatSample>
        {
            new MapThreatSample { MapId = mapa, Threat = zagrozenie, Drafted = pobor, Eligible = zdolni }
        }, p);
    }

    static void Para(string nazwa, StyleDaySample d, StyleSignal s, long num, long den)
    {
        long gn = d == null ? -1 : d.Num[(int)s], gd = d == null ? -1 : d.Den[(int)s];
        T.Ok(nazwa, gn == num && gd == den, "oczekiwano " + num + "/" + den + ", otrzymano " + gn + "/" + gd);
    }

    // ------------------------------------------------------------------ 14j (B/E/O/W)

    static void Test14jBEOW()
    {
        T.Section("TEST 14j - kodek ksiegi stylu: linie B, E, O, W");
        var led = new PlayerStyleLedger { CurrentDay = 50, Initialized = true, LastRaidTick = 11, MaxOfferId = 77 };
        led.Baselines[1234] = new PawnBaseline { Productive = 11, Support = 22, Recruited = 33, Captured = 44 };
        led.Baselines[5678] = new PawnBaseline { Productive = 55, Support = 66, Recruited = 77, Captured = 88 };
        led.Episodes[9] = new ThreatEpisode
        {
            MapId = 9, StartTick = 3000100, ThreatSamples = 3, DraftedSum = 5, EligibleSum = 13, FirstDraftTick = 3000350, QuietSamples = 2,
            Capped = true
        };
        led.PendingOffers.Add(71);
        led.PendingOffers.Add(73);
        led.WildMen[4321] = 44;
        var odt = new PlayerStyleLedger();
        T.EqI("zapis -> odczyt bez odrzucen", odt.RestoreFromLines(led.ToPersistableLines()), 0);
        T.Ok("bazy odtworzone pole po polu", odt.Baselines.Count == 2
             && odt.Baselines[1234].Productive == 11 && odt.Baselines[1234].Support == 22
             && odt.Baselines[1234].Recruited == 33 && odt.Baselines[1234].Captured == 44
             && odt.Baselines[5678].Captured == 88, null);
        ThreatEpisode e;
        T.Ok("epizod w toku odtworzony pole po polu", odt.Episodes.TryGetValue(9, out e) && e.StartTick == 3000100
             && e.ThreatSamples == 3 && e.DraftedSum == 5 && e.EligibleSum == 13 && e.FirstDraftTick == 3000350 && e.QuietSamples == 2
             && e.Capped, null);
        T.Ok("oferty oczekujace odtworzone", odt.PendingOffers.SetEquals(new[] { 71, 73 }), null);
        T.Ok("sledzeni dzicy odtworzeni", odt.WildMen.Count == 1 && odt.WildMen[4321] == 44, null);
        var zle = new List<string> { "B|1|1|1|1|1", "B|1|2|2|2|2", "E|3|1|1|1|1|1|1|0", "E|3|1|1|1|1|1|1|0", "O|5", "O|5",
                                     "W|6|1", "W|6|2", "B|1|1", "E|4|1", "O", "W|7",
                                     "E|8|1|1|1|1|1|1", "E|8|1|1|1|1|1|1|2" };
        var odt2 = new PlayerStyleLedger();
        T.EqI("odrzucone: 4 duplikaty kluczy, 4 linie ze zla liczba pol, stara linia E (8 pol) i zla flaga limitu",
              odt2.RestoreFromLines(zle), 10);
        T.Ok("pierwsze wystapienia zostaly", odt2.Baselines.Count == 1 && odt2.Baselines[1].Productive == 1
             && odt2.Episodes.Count == 1 && odt2.PendingOffers.Count == 1 && odt2.WildMen[6] == 1, null);

        // Ksiega zainicjowana BEZ linii A (odrzuconej albo brakujacej): dzien w toku zaczyna sie od zera (przeglad S8),
        // zamiast gubic probki do granicy doby i ponownie inicjowac.
        var bezA = new PlayerStyleLedger();
        bezA.RestoreFromLines(new List<string> { "N|1|52|1|-1|-1", "A|52|1" });
        T.Ok("zainicjowana ksiega bez poprawnej linii A ma dzien w toku = biezacy dzien", bezA.Initialized && bezA.Partial != null
             && bezA.Partial.Day == 52, bezA.Partial == null ? "Partial null" : "dzien " + bezA.Partial.Day);
        var nieA = new PlayerStyleLedger();
        nieA.RestoreFromLines(new List<string> { "N|1|52|0|-1|-1" });
        T.Ok("niezainicjowana ksiega dalej bez dnia w toku", !nieA.Initialized && nieA.Partial == null, null);
    }

    static StyleDaySample Dzien(int day)
    {
        var d = new StyleDaySample(day);
        for (int i = 0; i < StyleSignals.Count; i++)
        {
            // Wartosci rozne dla kazdego pola i dnia - zamiana dwoch pol albo przesuniecie jest widoczne.
            d.Num[i] = day * 100 + i * 7 + 1;
            d.Den[i] = day * 10 + i * 3 + 2;
        }
        return d;
    }

    static void ZbiorRowny(string nazwa, List<string> got, string[] spec)
    {
        bool ok = got != null && got.Count == spec.Length && new HashSet<string>(got).SetEquals(spec);
        T.Ok(nazwa + " zgodne ze spec", ok, got == null ? "null" : string.Join("/", got));
    }
}
