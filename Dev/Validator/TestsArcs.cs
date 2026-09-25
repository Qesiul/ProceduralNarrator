using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

/// <summary>
/// TEST 11 - luki narracyjne (krok 5). Sekcje 11a-e: katalog i jego walidacja, osiagalnosc
/// kazdej alternatywy oczekiwanego typu, tagi, tablica dopasowan, zgodnosc fazy z intencja.
/// Kolejne sekcje (automat, scoring, kodek) dochodza w etapach S2-S3.
/// </summary>
static class TestsArcs
{
    public static void Run(List<Block> blocks, EventComposer composer, XmlConfig cfg)
    {
        Katalog(cfg);
        KatalogStylu(cfg);
        Walidacja();
        Osiagalnosc(blocks, composer, cfg);
        Tagi(blocks, cfg);
        Dopasowania();
        ZgodnoscZIntencja();
        TestsArcsFsm.Run(cfg);
        TestsArcsScoring.Run(cfg, composer);
    }

    // =====================================================================================
    //  11a - prawdziwy katalog z Defs/Arcs
    // =====================================================================================
    static void Katalog(XmlConfig cfg)
    {
        T.Section("TEST 11a - katalog lukow z XML (Defs/Arcs)");
        T.Ok("STRAZNIK: plik lukow wczytany i niepusty", cfg.arcDefs.Count > 0,
             "lukow w XML: " + cfg.arcDefs.Count + " (" + cfg.arcsPath + ")");

        List<string> problemy;
        ArcCatalog kat = ArcCatalog.Build(cfg.arcDefs, out problemy);
        T.EqI("prawdziwy katalog przechodzi walidacje bez bledow", problemy.Count, 0);
        foreach (var p in problemy) Console.WriteLine("      problem: " + p);
        T.EqI("zaden luk nie zostal odrzucony", kat.Count, cfg.arcDefs.Count);

        // Zestaw lukow to DECYZJE AUTORA: nr 9 kroku 5 (2026-09-22) i nr 15/18 kroku 7 (2026-09-24, cztery
        // luki pod styl gracza) - zmiana SPECYFIKACJI, wiec porownujemy z nowa lista, a nie luzujemy testu.
        var oczekiwane = new[]
        {
            "PN_Luk_Dostatek", "PN_Luk_GniewNatury", "PN_Luk_NiespokojneNoce", "PN_Luk_Scigani",
            "PN_Luk_SlawaTwierdzy", "PN_Luk_Wendeta", "PN_Luk_ZiemiaObiecana", "PN_Luk_ZnakiZNieba"
        };
        T.EqS("katalog = osiem lukow z decyzji autora (4 z kroku 5 + 4 pod styl z kroku 7)",
              string.Join(",", kat.Arcs.Select(a => a.defName).OrderBy(x => x, StringComparer.Ordinal)),
              string.Join(",", oczekiwane));

        // Kolejnosc kanoniczna WYPROWADZONA z pol (priorytet malejaco, potem defName ordinal),
        // a nie przepisana lista - test ma isc za danymi.
        var wyprowadzona = kat.Arcs.OrderByDescending(a => a.priority).ThenBy(a => a.defName, StringComparer.Ordinal)
                              .Select(a => a.defName).ToList();
        T.EqS("kolejnosc katalogu = priorytet malejaco, potem defName",
              string.Join(",", kat.Arcs.Select(a => a.defName)), string.Join(",", wyprowadzona));

        var scigani = kat.ById("PN_Luk_Scigani");
        T.Ok("Scigani to luk 3-fazowy bez eskalacji (decyzja autora)",
             scigani != null && scigani.phases.Count == 3 && scigani.phases.All(f => f.kind != ArcPhaseKind.Escalation),
             scigani == null ? "brak" : string.Join(",", scigani.phases.Select(f => f.kind)));

        T.EqS("frakcje wiaze wylacznie Wendeta (decyzja autora nr 7)",
              string.Join(",", kat.Arcs.Where(a => a.BindsFaction).Select(a => a.defName)), "PN_Luk_Wendeta");

        T.Ok("blok <arcs> obecny w Storyteller_Generative.xml", cfg.arcsBlockPresent, null);
        T.EqI("maxConcurrent z XML = 2 (decyzja autora nr 4)", cfg.arcs.maxConcurrent, 2);
        T.Ok("warstwa lukow wlaczona w XML", cfg.arcs.enabled, null);

        int nieAscii = kat.Arcs.SelectMany(a => a.phases.Select(f => f.message)
                                                .Concat(a.phases.SelectMany(f => f.transitions ?? new List<ArcTransition>()).Select(t => t.message))
                                                .Concat((a.transitions ?? new List<ArcTransition>()).Select(t => t.message)))
                              .Where(m => m != null).Count(m => m.Any(c => c > 127));
        T.EqI("teksty komunikatow sa czystym ASCII", nieAscii, 0);
    }

    // =====================================================================================
    //  11a (cd.) - walidacja katalogu: kazda regula odrzuca wlasciwy blad
    // =====================================================================================

    static ArcExpectation Ocz(Valence v, params Theme[] motywy)
    {
        return new ArcExpectation { themes = motywy.ToList(), valences = new List<Valence> { v } };
    }

    static ArcPhase Faza(string id, ArcPhaseKind kind, ArcExpectation e)
    {
        return new ArcPhase { id = id, kind = kind, expectations = new List<ArcExpectation> { e }, maxDays = 20f };
    }

    static ArcTransition Przejscie(string target, string close)
    {
        return new ArcTransition
        {
            guards = new List<ArcGuard> { new Guard_ColonistsLost { min = 1 } },
            target = target,
            close = close
        };
    }

    /// <summary>Poprawny luk wzorcowy - kazdy przypadek negatywny psuje w nim DOKLADNIE jedna rzecz.</summary>
    static ArcDefinition Baza(string id = "PN_Luk_Test")
    {
        var kulm = Faza("Kulminacja", ArcPhaseKind.Climax, Ocz(Valence.Negative, Theme.Raid));
        kulm.transitions.Add(Przejscie("Rozwiazanie", null));
        return new ArcDefinition
        {
            defName = id,
            priority = 1,
            exclusionGroup = "g",
            cooldownDays = 10f,
            phases = new List<ArcPhase>
            {
                Faza("Zasiew", ArcPhaseKind.Seed, Ocz(Valence.Negative, Theme.Raid)),
                kulm,
                Faza("Rozwiazanie", ArcPhaseKind.Resolution, Ocz(Valence.Positive))
            }
        };
    }

    static void Odrzucony(string opis, Action<ArcDefinition> psuj, string oczekiwanyFragment)
    {
        var a = Baza();
        psuj(a);
        List<string> problemy;
        var kat = ArcCatalog.Build(new[] { a }, out problemy);
        bool trafiony = problemy.Any(p => p.IndexOf(oczekiwanyFragment, StringComparison.Ordinal) >= 0);
        T.Ok("walidacja odrzuca: " + opis, kat.Count == 0 && trafiony,
             "odrzucony=" + (kat.Count == 0) + " problemy: " + string.Join(" | ", problemy));
    }

    static void Walidacja()
    {
        T.Section("TEST 11a - walidacja katalogu (przypadki negatywne)");

        // STRAZNIK: gdyby luk wzorcowy sam byl niepoprawny, kazdy przypadek negatywny przechodzilby
        // trywialnie (odrzucony z INNEGO powodu) - stad dodatkowo sprawdzany fragment komunikatu.
        List<string> p0;
        var k0 = ArcCatalog.Build(new[] { Baza() }, out p0);
        T.Ok("STRAZNIK: luk wzorcowy jest poprawny", k0.Count == 1 && p0.Count == 0, string.Join(" | ", p0));

        Odrzucony("pierwsza faza nie jest Seed", a => a.phases[0].kind = ArcPhaseKind.Escalation, "pierwsza faza musi byc Seed");
        Odrzucony("ostatnia faza nie jest Resolution", a => { a.phases.RemoveAt(2); a.phases[1].transitions.Clear(); },
                  "ostatnia faza musi byc Resolution");
        Odrzucony("rodzaje faz nie rosna scisle", a => a.phases.Insert(2, Faza("Eskalacja", ArcPhaseKind.Escalation, Ocz(Valence.Negative))),
                  "scisle rosnaco");
        Odrzucony("oczekiwanie bez walencji", a => a.phases[1].expectations[0].valences.Clear(), "walencje sa obowiazkowe");
        Odrzucony("sameFaction bez wczesniejszej fazy wiazacej", a => a.phases[1].expectations[0].sameFaction = true,
                  "sameFaction wymaga");
        Odrzucony("przejscie wstecz", a => a.phases[1].transitions[0].target = "Zasiew", "wstecz");
        // Osobno "w miejscu": granica warunku j <= i. Sam przypadek wsteczny nie odroznia <= od <.
        Odrzucony("przejscie w miejscu (do tej samej fazy)", a => a.phases[1].transitions[0].target = "Kulminacja", "w miejscu");
        Odrzucony("przejscie do nieistniejacej fazy", a => a.phases[1].transitions[0].target = "Nie_ma", "nieistniejacej fazy");
        Odrzucony("przejscie bez straznikow", a => a.phases[1].transitions[0].guards.Clear(), "bez straznikow");
        Odrzucony("przejscie z target i close naraz", a => a.phases[1].transitions[0].close = "koniec", "DOKLADNIE jedno");
        Odrzucony("{FRAKCJA} w komunikacie przed zwiazaniem", a => a.phases[1].message = "{FRAKCJA} atakuja.",
                  "nie jest jeszcze zwiazana");
        Odrzucony("{FRAKCJA} w przejsciu przed zwiazaniem", a => a.phases[1].transitions[0].message = "{FRAKCJA} odchodza.",
                  "przed zwiazaniem frakcji");
        Odrzucony("Zasiew z przejsciami", a => a.phases[0].transitions.Add(Przejscie(null, "koniec")), "Zasiew nie moze miec przejsc");
        Odrzucony("przejscie calego luku z target", a => a.transitions.Add(Przejscie("Rozwiazanie", null)), "moze tylko zamykac");
        Odrzucony("minDaysAfterPrevious >= maxDays", a => a.phases[1].minDaysAfterPrevious = 20f, "minDaysAfterPrevious");
        Odrzucony("niedozwolone znaki w id fazy", a => a.phases[1].id = "Kul:minacja", "niedozwolonymi znakami");
        Odrzucony("mniej niz dwie fazy", a => a.phases.RemoveRange(1, 2), "co najmniej 2 fazy");
        Odrzucony("faza bez oczekiwanego typu", a => a.phases[1].expectations.Clear(), "brak oczekiwanego typu");
        // Fragment KONKRETNY: ogolne "nastepnik" pasowaloby tez do komunikatu petli wiszacych
        // referencji, ktora odrzuca luk z innego powodu - i maskowaloby wylaczenie tej reguly.
        Odrzucony("nastepnik nie istnieje", a => a.successor = "PN_Luk_Brak", "nie istnieje albo wskazuje na siebie");

        // Kierunek pozytywny regul frakcji: zwiazanie w Zasiewie POZWALA na sameFaction i {FRAKCJA}
        // pozniej. Bez tego regula moglaby odrzucac wszystko i przypadki wyzej przechodzilyby dalej.
        var wiazacy = Baza("PN_Luk_Wiazacy");
        wiazacy.phases[0].bindsFaction = true;
        wiazacy.phases[0].message = "Zapamietano: {FRAKCJA}.";
        wiazacy.phases[1].expectations[0].sameFaction = true;
        wiazacy.phases[1].message = "{FRAKCJA} wracaja.";
        wiazacy.phases[1].transitions[0].message = "{FRAKCJA} odchodza.";
        List<string> pw;
        var kw = ArcCatalog.Build(new[] { wiazacy }, out pw);
        T.Ok("walidacja PRZEPUSZCZA sameFaction i {FRAKCJA} po zwiazaniu w Zasiewie", kw.Count == 1 && pw.Count == 0,
             string.Join(" | ", pw));

        // Reguly miedzy lukami.
        List<string> pd;
        var kd = ArcCatalog.Build(new[] { Baza("PN_Luk_X"), Baza("PN_Luk_X") }, out pd);
        T.Ok("walidacja odrzuca powtorzony defName (zostaje jeden)", kd.Count == 1 && pd.Any(x => x.Contains("powtorzony defName")),
             string.Join(" | ", pd));

        var ca = Baza("PN_Luk_A"); ca.successor = "PN_Luk_B"; ca.exclusionGroup = "ga";
        var cb = Baza("PN_Luk_B"); cb.successor = "PN_Luk_A"; cb.exclusionGroup = "gb";
        List<string> pc;
        var kc = ArcCatalog.Build(new[] { ca, cb }, out pc);
        T.Ok("walidacja odrzuca cykl nastepnikow", kc.Count == 0 && pc.Any(x => x.Contains("cykl")), string.Join(" | ", pc));

        var fa = Baza("PN_Luk_FA"); fa.phases[0].bindsFaction = true; fa.exclusionGroup = "g1";
        var fb = Baza("PN_Luk_FB"); fb.phases[0].bindsFaction = true; fb.exclusionGroup = "g2";
        List<string> pf;
        var kf = ArcCatalog.Build(new[] { fa, fb }, out pf);
        T.Ok("walidacja odrzuca luki wiazace frakcje w roznych grupach", kf.Count == 0 && pf.Any(x => x.Contains("JEDNEJ niepustej grupy")),
             string.Join(" | ", pf));
        fb.exclusionGroup = "g1";
        var kf2 = ArcCatalog.Build(new[] { fa, fb }, out pf);
        T.Ok("...a w jednej wspolnej grupie przepuszcza", kf2.Count == 2 && pf.Count == 0, string.Join(" | ", pf));

        var na = Baza("PN_Luk_NA"); na.successor = "PN_Luk_NB"; na.exclusionGroup = "ga";
        var nb = Baza("PN_Luk_NB"); nb.exclusionGroup = "gb"; nb.phases[0].kind = ArcPhaseKind.Escalation;
        List<string> pn;
        var kn = ArcCatalog.Build(new[] { na, nb }, out pn);
        T.Ok("luk, ktorego nastepnik odrzucono, tez jest odrzucany (brak wiszacej referencji)",
             kn.Count == 0 && pn.Any(x => x.Contains("jego nastepnik")), string.Join(" | ", pn));

        // ---- S6: warunki pamieci w warunkach startu lukow ----
        // (a) nastepnik pytajacy o watek swojego poprzednika: odrzucony nastepnik, poprzednik kaskadowo.
        var sa = Baza("PN_Luk_SA"); sa.successor = "PN_Luk_SB"; sa.exclusionGroup = "ga";
        var sb = Baza("PN_Luk_SB"); sb.exclusionGroup = "gb";
        sb.startConditions.Add(new Cond_WatekZamkniety { arc = "PN_Luk_SA" });
        List<string> ps;
        var kS = ArcCatalog.Build(new[] { sa, sb }, out ps);
        T.Ok("nastepnik pytajacy o watek poprzednika jest odrzucany (a poprzednik kaskadowo)",
             kS.Count == 0 && ps.Any(x => x.Contains("PN_Luk_SB") && x.Contains("swojego poprzednika")), string.Join(" | ", ps));
        var sb2 = Baza("PN_Luk_SB"); sb2.exclusionGroup = "gb";
        sb2.startConditions.Add(new Cond_WatekZamkniety { arc = "PN_Luk_SC" });
        var sc = Baza("PN_Luk_SC"); sc.exclusionGroup = "gc";
        var kS2 = ArcCatalog.Build(new[] { sa, sb2, sc }, out ps);
        T.Ok("KONTROLA: nastepnik pytajacy o INNY istniejacy luk przechodzi", kS2.Count == 3 && ps.Count == 0, string.Join(" | ", ps));

        // (b) warunek watku wskazujacy luk spoza katalogu (literowka) albo luk odrzucony.
        var wb = Baza("PN_Luk_WB");
        wb.startConditions.Add(new Cond_WatekOtwarty { arc = "PN_Luk_Brak" });
        var kB = ArcCatalog.Build(new[] { wb }, out ps);
        T.Ok("warunek watku z lukiem spoza katalogu odrzuca luk", kB.Count == 0 && ps.Any(x => x.Contains("PN_Luk_Brak")), string.Join(" | ", ps));
        var zly = Baza("PN_Luk_Zly"); zly.phases[0].kind = ArcPhaseKind.Escalation;
        var wb2 = Baza("PN_Luk_WB2"); wb2.exclusionGroup = "gw";
        wb2.startConditions.Add(new Cond_WatekOtwarty { arc = "PN_Luk_Zly", required = false });
        var kB2 = ArcCatalog.Build(new[] { zly, wb2 }, out ps);
        T.Ok("warunek watku wskazujacy luk ODRZUCONY tez odrzuca (punkt staly)",
             kB2.Count == 0 && ps.Any(x => x.Contains("PN_Luk_WB2") && x.Contains("PN_Luk_Zly")), string.Join(" | ", ps));

        // (c) wynik zamkniecia: stale dyrektora albo "close" przejscia wskazanego luku.
        var cel = Baza("PN_Luk_Cel"); cel.exclusionGroup = "gcel";
        cel.transitions.Add(Przejscie(null, "pojednanie"));
        foreach (var (wynik, ok) in new[] { ("pojednanie", true), (ArcDirector.OutcomeFaded, true), ("pojednane", false) })
        {
            var wc = Baza("PN_Luk_WC"); wc.exclusionGroup = "gwc";
            wc.startConditions.Add(new Cond_WatekZamkniety { arc = "PN_Luk_Cel", outcome = wynik });
            var kC = ArcCatalog.Build(new[] { cel, wc }, out ps);
            T.Ok("wynik zamkniecia '" + wynik + "' " + (ok ? "przyjety" : "odrzucony (literowka)"),
                 ok ? kC.Count == 2 && ps.Count == 0 : kC.Count == 1 && ps.Any(x => x.Contains("pojednane")), string.Join(" | ", ps));
        }

        // (d) klucz faktu w warunku startu - regula ksiegi faktow, nie sama niepustosc.
        Odrzucony("Cond_Fakt z pustym kluczem (z required=false bylby ZAWSZE spelniony)",
                  a => a.startConditions.Add(new Cond_Fakt { key = "", required = false }), "niepoprawnym kluczem faktu");
        Odrzucony("Cond_FaktLiczba z kluczem spoza alfabetu",
                  a => a.startConditions.Add(new Cond_FaktLiczba { key = "walka byla", min = 1f }), "niepoprawnym kluczem faktu");
        Odrzucony("Cond_FaktOd z kluczem '-'",
                  a => a.startConditions.Add(new Cond_FaktOd { key = "-", minDays = 1f }), "niepoprawnym kluczem faktu");
        var kd2 = Baza("PN_Luk_KD");
        kd2.startConditions.Add(new Cond_FaktLiczba { key = "walka.byla", min = 1f });
        var kD = ArcCatalog.Build(new[] { kd2 }, out ps);
        T.Ok("KONTROLA: poprawny klucz faktu przechodzi", kD.Count == 1 && ps.Count == 0, string.Join(" | ", ps));
    }

    // =====================================================================================
    //  11b - osiagalnosc: kazda alternatywa oczekiwanego typu ma kandydata w katalogu klockow
    // =====================================================================================

    static WorldSnapshot Scena(bool noc)
    {
        // Pelny swiat dnia 40: kazdy warunek twardy katalogu jest spelnialny naraz (ten sam
        // uklad co snapD40 w sekcji [5c], ktorej asercja wymaga osiagalnosci CALEGO katalogu).
        return new WorldSnapshot
        {
            DaysPassed = 40, ColonistCount = 6, ColonyWealth = 40000, WealthRelative = 1.5f,
            MountainRoofCellsNearColony = 250, HasHostileFaction = true, Season = 2, IsNight = noc,
            WildAnimalCount = 8, MaddenableAnimalCount = 6, ThreatPoints = 600f, DaysSinceLastEvent = 6f,
            KidnappedColonistCount = 2, HasPoweredCommsConsole = true
        };
    }

    static List<ComposedEvent> Warianty(EventComposer composer, WorldSnapshot s)
    {
        var wynik = new List<ComposedEvent>();
        foreach (Block akcja in composer.AvailableActions(s, new EventRecipe()))
        {
            VariantEnumerationStats st;
            wynik.AddRange(composer.EnumerateVariants(akcja, s, 100000, new SeededRandom(1), out st));
        }
        return wynik;
    }

    static void Osiagalnosc(List<Block> blocks, EventComposer composer, XmlConfig cfg)
    {
        T.Section("TEST 11b - osiagalnosc kazdej alternatywy oczekiwanego typu");
        List<string> problemy;
        ArcCatalog kat = ArcCatalog.Build(cfg.arcDefs, out problemy);

        var noc = Warianty(composer, Scena(true));
        var dzien = Warianty(composer, Scena(false));
        T.Ok("STRAZNIK: sa warianty w obu scenach", noc.Count > 0 && dzien.Count > 0, "noc " + noc.Count + ", dzien " + dzien.Count);

        const string F = "F1";
        Func<ComposedEvent, ArcEventView> widok = e =>
        {
            var v = ArcEventView.FromCandidate(e, null);
            if (v.CarriesFaction) v.FactionId = F;
            return v;
        };

        int alternatyw = 0;
        foreach (ArcDefinition a in kat.Arcs)
        {
            foreach (ArcPhase f in a.phases)
            {
                for (int i = 0; i < f.expectations.Count; i++)
                {
                    ArcExpectation e = f.expectations[i];
                    alternatyw++;
                    string bound = e.sameFaction ? F : null;
                    string why;
                    int trafien = noc.Concat(dzien).Count(k => e.Matches(widok(k), bound, out why));
                    T.Ok("osiagalna: " + a.defName + "/" + f.id + " alt" + i + " (" + e.Describe() + ")", trafien > 0,
                         "pasujacych wariantow: " + trafien);
                }
            }
        }
        T.Ok("STRAZNIK: sprawdzono alternatywy", alternatyw > 0, "alternatyw: " + alternatyw);

        // Fakty z przegladu planu (F7), na ktorych stoi decyzja autora R4-3 - asercje, zeby zmiana
        // katalogu klockow nie uniewaznila po cichu uzasadnienia progu kulminacji Gniewu natury.
        var szal = noc.Concat(dzien).Where(k => k.ActionBlockId == "PN_Akcja_Szal").ToList();
        T.Ok("STRAZNIK: sa warianty Szalu", szal.Count > 0, "wariantow: " + szal.Count);
        T.Ok("Szal NIGDY nie osiaga mocy High (max = " + (szal.Count == 0 ? "-" : szal.Max(k => k.Intensity).ToString()) + ")",
             szal.All(k => (int)k.Intensity < (int)IntensityLevel.High), null);
        var burzaNoc = noc.Where(k => k.ActionBlockId == "PN_Akcja_Burza").ToList();
        var burzaDzien = dzien.Where(k => k.ActionBlockId == "PN_Akcja_Burza").ToList();
        T.Ok("Burza osiaga moc High noca", burzaNoc.Any(k => (int)k.Intensity >= (int)IntensityLevel.High),
             "wariantow noca: " + burzaNoc.Count);
        T.Ok("Burza NIE osiaga mocy High za dnia", burzaDzien.Count > 0 && burzaDzien.All(k => (int)k.Intensity < (int)IntensityLevel.High),
             "wariantow za dnia: " + burzaDzien.Count);

        // Sens R4-3 (ze SPROSTOWANIEM z 2026-09-22): kulminacja Gniewu natury ma byc osiagalna
        // na KAZDEJ mapie, takze bez dachu gorskiego - ale tylko NOCA. Za dnia slot modyfikatora
        // zawsze zajmuje PN_Mod_Slabo (-1; pusty slot opcjonalny powstaje wylacznie wymuszony),
        // wiec Burza dochodzi najwyzej do Low, a Szal do VeryLow. Pytanie kwestionariusza mowilo
        // "Szal noca, Burza" - Burza za dnia byla bledem opisu. Obie strony sa asercjami, zeby
        // zmiana katalogu (np. nowy dzienny modyfikator) wymusila ponowne przemyslenie progu.
        var gniew = kat.ById("PN_Luk_GniewNatury");
        var kulm = gniew == null ? null : gniew.PhaseById("Kulminacja");
        string w2;
        Func<WorldSnapshot, int> trafienKulm = sn => kulm == null ? 0
            : Warianty(composer, sn).Count(k => kulm.expectations.Any(e => e.Matches(widok(k), null, out w2)));
        var nocBezGor = Scena(true);
        nocBezGor.MountainRoofCellsNearColony = 0;
        var dzienBezGor = Scena(false);
        dzienBezGor.MountainRoofCellsNearColony = 0;
        int trafNoc = trafienKulm(nocBezGor);
        int trafDzien = trafienKulm(dzienBezGor);
        T.Ok("kulminacja Gniewu natury osiagalna NOCA bez dachu gorskiego (R4-3: kazda mapa)", trafNoc > 0, "wariantow: " + trafNoc);
        T.EqI("kulminacja Gniewu natury NIEosiagalna za dnia bez dachu gorskiego (Mod_Slabo -1)", trafDzien, 0);
    }

    // =====================================================================================
    //  11c - tagi i flaga frakcji w katalogu klockow
    // =====================================================================================
    static void Tagi(List<Block> blocks, XmlConfig cfg)
    {
        T.Section("TEST 11c - tagi lukow i carriesFaction w katalogu klockow");
        var akcje = blocks.Where(b => b.Type == BlockType.Action).ToList();
        Func<string, string> zTagiem = tag => string.Join(",", akcje.Where(b => b.Tags.Contains(tag)).Select(b => b.Id)
                                                                     .OrderBy(x => x, StringComparer.Ordinal));
        T.EqS("tag 'niebo' = Meteoryt, Zrzut, Burza, Emanator (katalog Znakow z nieba)", zTagiem("niebo"),
              "PN_Akcja_Burza,PN_Akcja_Emanator,PN_Akcja_Meteoryt,PN_Akcja_Zrzut");
        T.EqS("tag 'kapsula' = Uchodzcy", zTagiem("kapsula"), "PN_Akcja_Uchodzcy");
        T.EqS("carriesFaction wylacznie na Napadzie (jedyny RaidEnemy)",
              string.Join(",", blocks.Where(b => b.CarriesFaction).Select(b => b.Id)), "PN_Akcja_Napad");
        T.EqI("tag 'niebo' tylko na klockach akcji", blocks.Count(b => b.Type != BlockType.Action && b.Tags.Contains("niebo")), 0);

        var tagiAkcji = new HashSet<string>(akcje.SelectMany(b => b.Tags));
        var wymagane = cfg.arcDefs.SelectMany(a => a.phases).SelectMany(f => f.expectations)
                          .Where(e => !string.IsNullOrEmpty(e.requiredTag)).Select(e => e.requiredTag).Distinct().ToList();
        T.Ok("STRAZNIK: luki wymagaja tagow", wymagane.Count > 0, string.Join(",", wymagane));
        T.EqS("kazdy tag wymagany przez luk jest na jakims klocku akcji",
              string.Join(",", wymagane.Where(t => !tagiAkcji.Contains(t))), "");
    }

    // =====================================================================================
    //  11d - tablica dopasowan ArcExpectation.Matches
    // =====================================================================================
    // =====================================================================================
    //  14l - luki pod styl gracza (krok 7): specyfikacja z decyzji autora nr 18
    // =====================================================================================
    static void KatalogStylu(XmlConfig cfg)
    {
        T.Section("TEST 14l - luki pod styl gracza: specyfikacja z decyzji autora (teksty, grupy, priorytety, warunki)");
        List<string> pr;
        ArcCatalog kat = ArcCatalog.Build(cfg.arcDefs, out pr);
        var spec = new[]
        {
            // Teksty po przegladzie S8 (decyzje autora 2026-09-24): bez superlatywow, bez stanu kolonii, bez "ludzie".
            new { Id = "PN_Luk_SlawaTwierdzy", Cecha = "Walka", Grupa = "frakcja", Sasiad = "PN_Luk_Wendeta",
                  Teksty = new[] { "O obroncach kolonii zaczyna sie mowic w okolicy.", "Ktos znowu wystawia obroncow kolonii na probe.",
                                   "Obroncow kolonii czeka ciezka proba.", "Ktos prosi o miejsce wsrod obroncow kolonii." } },
            new { Id = "PN_Luk_Dostatek", Cecha = "Gospodarka", Grupa = "niebo", Sasiad = "PN_Luk_ZnakiZNieba",
                  Teksty = new[] { "Do kolonii trafia dobro.", "Dobytek kolonii jest zagrozony.",
                                   "Tym razem kolonii zagrazaja napastnicy.", "Do kolonii znowu trafia dobro." } },
            new { Id = "PN_Luk_ZiemiaObiecana", Cecha = "Ekspansja", Grupa = "ludzie", Sasiad = "PN_Luk_Scigani",
                  Teksty = new[] { "Wiesc o goscinnej kolonii niesie sie po okolicy.", "Do kolonii trafiaja kolejni przybysze.",
                                   "Po przybyszach przychodza klopoty.", "Goscinnosc kolonii nie idzie na marne." } },
            new { Id = "PN_Luk_NiespokojneNoce", Cecha = "Reaktywnosc", Grupa = "natura", Sasiad = "PN_Luk_GniewNatury",
                  Teksty = new[] { "Kolonia uczy sie spac czujnie.", "Zagrozenie znowu przychodzi znienacka.",
                                   "Kolonia staje przed ciezka proba czujnosci.", "Po niespokojnych dniach przychodzi cos dobrego." } }
        };
        var cechy = new HashSet<string>();
        foreach (var s in spec)
        {
            ArcDefinition a = kat.ById(s.Id);
            T.Ok("luk " + s.Id + " w katalogu", a != null, null);
            if (a == null) continue;
            var st = (a.startConditions ?? new List<NarrativeCondition>()).OfType<Cond_StylMocnaStrona>().ToList();
            T.Ok(s.Id + ": dokladnie jeden warunek stylu, cecha " + s.Cecha + " jako mocna strona",
                 st.Count == 1 && st[0].dimension == s.Cecha && st[0].required, st.Count == 1 ? st[0].Describe() : "warunkow stylu: " + st.Count);
            cechy.Add(st.Count == 1 ? st[0].dimension : "?");
            T.Ok(s.Id + ": warunki startu z wroga frakcja i dniem 11",
                 a.startConditions.Any(c => c is Cond_HostileFaction) && a.startConditions.OfType<Cond_MinDaysPassed>().Any(c => c.min == 11), null);
            T.EqS(s.Id + ": grupa wykluczajaca", a.exclusionGroup, s.Grupa);
            T.Eq(s.Id + ": odstep 30 dni", a.cooldownDays, 30.0, 0.0);
            ArcDefinition n = kat.ById(s.Sasiad);
            T.Ok(s.Id + ": priorytet wyzszy niz " + s.Sasiad + " (spor o ostatnie miejsce wygrywa luk pod styl)",
                 n != null && a.priority > n.priority, n == null ? "brak sasiada" : a.priority + " vs " + n.priority);
            T.EqS(s.Id + ": teksty faz slowo w slowo (zatwierdzone)", string.Join(" / ", a.phases.Select(f => f.message)), string.Join(" / ", s.Teksty));
            foreach (ArcPhase f in a.phases.Where(f => f.kind == ArcPhaseKind.Escalation || f.kind == ArcPhaseKind.Climax))
            {
                T.Ok(s.Id + "/" + f.id + ": strata kolonisty -> Rozwiazanie z komunikatem",
                     f.transitions != null && f.transitions.Any(t => t.target == "Rozwiazanie" && t.message == "Kolonia liczy straty."
                                                                    && t.guards != null && t.guards.Any(g => g is Guard_ColonistsLost)), null);
            }
        }
        T.EqI("kazda cecha ma dokladnie jeden luk pod styl", cechy.Count, 4);

        // Opis luku do [PN-CONFIG] niesie warunek stylu - analiza (niezmiennik 39) sprawdza z niego, ze luk
        // otwiera sie tylko przy tej cesze w stylMocne decyzji. Oczekiwania wyprowadzone z warunkow, nie wpisane.
        foreach (ArcDefinition a in kat.Arcs)
        {
            var st = (a.startConditions ?? new List<NarrativeCondition>()).OfType<Cond_StylMocnaStrona>().ToList();
            string oczek = st.Count == 0 ? "-" : string.Join("/", st.Select(c => (c.required ? "" : "!") + c.dimension));
            T.Ok(a.defName + ": opis konczy sie warunkiem stylu '" + oczek + "'",
                 ArcCatalog.Describe(a).EndsWith("; warunekStylu=" + oczek, StringComparison.Ordinal), ArcCatalog.Describe(a));
        }
        ArcDefinition wzor = kat.ById("PN_Luk_Wendeta");
        var zakazStylu = new ArcDefinition
        {
            defName = "PN_Luk_TestStylu", priority = 1, cooldownDays = 1f, phases = wzor.phases,
            startConditions = new List<NarrativeCondition>
            {
                new Cond_StylMocnaStrona { dimension = "Gospodarka", required = false },
                new Cond_StylMocnaStrona { dimension = "Walka" }
            }
        };
        T.Ok("opis: required=false jako '!', kilka warunkow laczonych '/'",
             ArcCatalog.Describe(zakazStylu).EndsWith("; warunekStylu=!Gospodarka/Walka", StringComparison.Ordinal),
             ArcCatalog.Describe(zakazStylu));
        T.Ok("Slawa twierdzy i Wendeta w tej samej grupie (u wojownika Slawa zastepuje Wendete)",
             kat.ById("PN_Luk_Wendeta") != null && kat.ById("PN_Luk_Wendeta").exclusionGroup == "frakcja", null);
        T.Ok("Dostatek i Znaki z nieba w tej samej grupie (przeglad S8: wspolny dar otwieral oba)",
             kat.ById("PN_Luk_ZnakiZNieba") != null && kat.ById("PN_Luk_ZnakiZNieba").exclusionGroup == "niebo", null);
        ArcDefinition slawa = kat.ById("PN_Luk_SlawaTwierdzy");
        ArcPhase kulm = slawa == null ? null : slawa.phases.FirstOrDefault(f => f.kind == ArcPhaseKind.Climax);
        T.Ok("Kulminacja Slawy wymaga mocy >= High (\"ciezka proba\" prawdziwa, jak Kulminacja Wendety)",
             kulm != null && kulm.expectations.All(e => e != null && e.minIntensity >= IntensityLevel.High),
             kulm == null ? "brak" : string.Join(",", kulm.expectations.Select(e => e == null ? "null" : e.minIntensity.ToString())));

        // PRAWDA TEKSTU (przeglad S8): zadnego superlatywu ani slowa "ludzie" w komunikatach lukow pod styl.
        var zakazane = new[] { "najciez", "najwiek", "najgor", "ludzie" };
        var komunikaty = spec.SelectMany(s => kat.ById(s.Id) == null ? Enumerable.Empty<string>()
                                                 : kat.ById(s.Id).phases.Select(f => f.message ?? "")).ToList();
        T.Ok("STRAZNIK: sprawdzono komunikaty wszystkich faz lukow pod styl", komunikaty.Count == 16, "komunikatow " + komunikaty.Count);
        T.Ok("komunikaty lukow pod styl bez superlatywow i bez \"ludzie\"",
             komunikaty.All(m => zakazane.All(z => m.IndexOf(z, StringComparison.OrdinalIgnoreCase) < 0)),
             string.Join(" | ", komunikaty.Where(m => zakazane.Any(z => m.IndexOf(z, StringComparison.OrdinalIgnoreCase) >= 0))));

        // ROZLACZNE ZASIEWY w grupie "natura" (przeglad S8): tylko skala Moderate w zasiewie Nocy odgradza Amok (Minor)
        // - jedyny zasiew Gniewu natury - od Niespokojnych nocy o wyzszym priorytecie.
        ArcDefinition noceZ = kat.ById("PN_Luk_NiespokojneNoce");
        T.Ok("zasiew Niespokojnych nocy tylko w skali Moderate (Amok zostaje Gniewowi natury)",
             noceZ != null && noceZ.phases[0].expectations.All(e => e != null && e.scales != null && e.scales.Count == 1
                                                                  && e.scales[0] == EventScale.Moderate), null);
        ArcDefinition noce = kat.ById("PN_Luk_NiespokojneNoce");
        T.Ok("Niespokojne noce: fazy po Zasiewie krotkie (maxDays w (0, 8])",
             noce != null && noce.phases.Skip(1).All(f => f.maxDays > 0f && f.maxDays <= 8f),
             noce == null ? "brak" : string.Join(",", noce.phases.Select(f => T.F(f.maxDays))));

        // Regula ogolna: faza oczekujaca WYLACZNIE napadu albo spraw zbrojnych wymaga wrogiej frakcji w warunkach startu,
        // inaczej luk moze utknac w fazie, ktorej zadne zdarzenie nie spelni.
        int fazZbrojnych = 0;
        foreach (ArcDefinition a in kat.Arcs)
        {
            foreach (ArcPhase f in a.phases)
            {
                foreach (ArcExpectation x in f.expectations ?? new List<ArcExpectation>())
                {
                    if (x != null && x.themes != null && x.themes.Count > 0 && x.themes.All(t => t == Theme.Raid || t == Theme.Military))
                    {
                        fazZbrojnych++;
                        T.Ok(a.defName + "/" + f.id + ": faza wylacznie Raid/Military -> Cond_HostileFaction w warunkach startu",
                             a.startConditions != null && a.startConditions.Any(c => c is Cond_HostileFaction), null);
                    }
                }
            }
        }
        T.Ok("STRAZNIK: katalog ma fazy wylacznie zbrojne", fazZbrojnych >= 4, "faz: " + fazZbrojnych);
    }

    static ArcEventView Zd(Theme t, Valence v, EventScale s, IntensityLevel moc, params string[] tagi)
    {
        var e = new ArcEventView { Theme = t, Valence = v, Scale = s, Intensity = moc, ActionBlockId = "X", Payload = "Y" };
        foreach (var g in tagi) e.Tags.Add(g);
        return e;
    }

    static void Dopasowania()
    {
        T.Section("TEST 11d - tablica dopasowan oczekiwanego typu");
        string why;
        var bazaZd = Zd(Theme.Raid, Valence.Negative, EventScale.Major, IntensityLevel.Normal, "militarny");
        Func<ArcExpectation> ocz = () => new ArcExpectation
        {
            themes = new List<Theme> { Theme.Raid },
            valences = new List<Valence> { Valence.Negative },
            scales = new List<EventScale> { EventScale.Major },
            requiredTag = "militarny",
            minIntensity = IntensityLevel.Normal
        };

        T.Ok("STRAZNIK: pelne dopasowanie wzorca", ocz().Matches(bazaZd, null, out why), why);

        // Kazda para rozni sie JEDNYM polem - wynik moze zalezec tylko od niego.
        T.Ok("inny motyw -> brak", !ocz().Matches(Zd(Theme.Military, Valence.Negative, EventScale.Major, IntensityLevel.Normal, "militarny"), null, out why), why);
        T.Ok("inna walencja -> brak", !ocz().Matches(Zd(Theme.Raid, Valence.Neutral, EventScale.Major, IntensityLevel.Normal, "militarny"), null, out why), why);
        T.Ok("inna skala -> brak", !ocz().Matches(Zd(Theme.Raid, Valence.Negative, EventScale.Moderate, IntensityLevel.Normal, "militarny"), null, out why), why);
        T.Ok("brak wymaganego tagu -> brak", !ocz().Matches(Zd(Theme.Raid, Valence.Negative, EventScale.Major, IntensityLevel.Normal, "inny"), null, out why), why);
        T.Ok("moc ponizej minimum -> brak", !ocz().Matches(Zd(Theme.Raid, Valence.Negative, EventScale.Major, IntensityLevel.Low, "militarny"), null, out why), why);
        T.Ok("moc powyzej minimum -> dopasowanie", ocz().Matches(Zd(Theme.Raid, Valence.Negative, EventScale.Major, IntensityLevel.VeryHigh, "militarny"), null, out why), why);

        var dowolne = new ArcExpectation { valences = new List<Valence> { Valence.Positive } };
        T.Ok("puste motywy i skale = dowolne", dowolne.Matches(Zd(Theme.Economic, Valence.Positive, EventScale.Minor, IntensityLevel.VeryLow), null, out why), why);
        T.Ok("...ale walencja dalej obowiazuje", !dowolne.Matches(Zd(Theme.Economic, Valence.Neutral, EventScale.Minor, IntensityLevel.VeryLow), null, out why), why);

        var tf = ocz(); tf.sameFaction = true;
        var zFrakcja = Zd(Theme.Raid, Valence.Negative, EventScale.Major, IntensityLevel.Normal, "militarny");
        zFrakcja.CarriesFaction = true; zFrakcja.FactionId = "7";
        T.Ok("sameFaction: ta sama frakcja -> dopasowanie", tf.Matches(zFrakcja, "7", out why), why);
        T.Ok("sameFaction: inna frakcja -> brak", !tf.Matches(zFrakcja, "8", out why), why);
        T.Ok("sameFaction: luk bez zwiazanej frakcji -> brak", !tf.Matches(zFrakcja, null, out why), why);
        var bezNosnika = Zd(Theme.Raid, Valence.Negative, EventScale.Major, IntensityLevel.Normal, "militarny");
        bezNosnika.FactionId = "7";
        T.Ok("sameFaction: akcja nie niesie frakcji -> brak", !tf.Matches(bezNosnika, "7", out why), why);
        var bezFrakcji = Zd(Theme.Raid, Valence.Negative, EventScale.Major, IntensityLevel.Normal, "militarny");
        bezFrakcji.CarriesFaction = true;
        T.Ok("sameFaction: zdarzenie bez znanej frakcji -> brak", !tf.Matches(bezFrakcji, "7", out why), why);
        T.Ok("bez sameFaction frakcja nie ma znaczenia", ocz().Matches(bezNosnika, "8", out why), why);
    }

    // =====================================================================================
    //  11e - zgodnosc walencji fazy z intencja (decyzja autora nr 2)
    // =====================================================================================
    static void ZgodnoscZIntencja()
    {
        T.Section("TEST 11e - krzywa decyduje KIEDY: zgodnosc walencji z intencja");
        // Tabela jest SPECYFIKACJA (decyzja autora nr 2), nie kopia kodu.
        var spec = new (Valence v, Intent i, bool ok)[]
        {
            (Valence.Negative, Intent.Escalate, true), (Valence.Negative, Intent.Hold, true), (Valence.Negative, Intent.Breathe, false),
            (Valence.Neutral, Intent.Escalate, true), (Valence.Neutral, Intent.Hold, true), (Valence.Neutral, Intent.Breathe, true),
            (Valence.Positive, Intent.Escalate, false), (Valence.Positive, Intent.Hold, true), (Valence.Positive, Intent.Breathe, true),
            // Pass jest zarezerwowany i nigdy nie zwracany; gdyby sie pojawil - jak Breathe.
            (Valence.Negative, Intent.Pass, false), (Valence.Neutral, Intent.Pass, true), (Valence.Positive, Intent.Pass, true),
        };
        foreach (var (v, i, ok) in spec)
        {
            T.Ok(v + " przy " + i + " -> " + (ok ? "steruje" : "czeka"), ArcIntentRules.Compatible(v, i) == ok, null);
        }
        T.EqI("STRAZNIK: tabela pokrywa wszystkie pary walencja x intencja",
              spec.Length, Enum.GetValues(typeof(Valence)).Length * Enum.GetValues(typeof(Intent)).Length);
    }
}
