using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

/// <summary>
/// TEST 16 - warianty tekstu zalezne od kontekstu (krok 8, decyzja autora K8-6) i tekst listu gracza.
///
/// Wyprowadzenia (a nie kopie kodu):
///   - wariant z warunkiem wygrywa, bo mowi WIECEJ prawdy o tej sytuacji; bazowy jest prawdziwy zawsze;
///   - bez wariantow tekst listu MUSI byc identyczny z Description (tekstem bazowym) - krok 8 nie ma
///     prawa zmienic zdania dla zdarzen, ktorych autor tresci nie ruszal;
///   - wybor jest funkcja stanu (ziarno, klocek, warunki): ten sam stan daje ten sam tekst.
/// </summary>
static class TestsTextVariants
{
    /// <summary>Payloady incydentow z IncidentDef.pointsScaleable=true w wanilii 1.5 (Data/Core, sprawdzone 2026-09-25).</summary>
    static readonly string[] SkalujaceSie = { "Infestation", "ManhunterPack", "PsychicEmanatorShipPartCrash", "RaidEnemy" };

    static TextVariant V(string id, string text) { return new TextVariant { id = id, text = text }; }

    /// <summary>Metoda "public new bool IsMet" PRZESLANIA bazowa zamiast ja nadpisac - wywolanie przez
    /// NarrativeCondition trafia w bazowe "return true", wiec to NIE jest warunek twardy (przeglad S10, K5).</summary>
    sealed class Cond_Przesloniety : NarrativeCondition
    {
        public new bool IsMet(WorldSnapshot s) { return false; }
    }

    /// <summary>Podklasa warunku twardego bez wlasnego IsMet - dziedziczy nadpisanie, wiec jest twarda.</summary>
    sealed class Cond_NocPochodna : Cond_Night { }

    /// <summary>FNV-1a 32-bit napisany NIEZALEZNIE od TextComposer (wyrocznia dla 16j).</summary>
    static uint Fnv(string s)
    {
        uint h = 0x811C9DC5;
        foreach (char c in s) { h ^= c; h *= 0x01000193; }
        return h;
    }

    static Block Klon(Block b)
    {
        var m = typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Block)m.Invoke(b, null);
    }

    /// <summary>Warunek pamieci POZYTYWNY - stwierdza, ze cos juz bylo (fakt jest, watek otwarty, zamkniecie bylo).</summary>
    static bool PamiecPozytywna(NarrativeCondition c)
    {
        if (c is Cond_Fakt f) return f.required;
        if (c is Cond_FaktOd) return true;
        if (c is Cond_FaktLiczba l) return l.min > 0f;
        if (c is Cond_WatekOtwarty w) return w.required;
        if (c is Cond_WatekZamkniety) return true;
        return false;
    }

    public static void Run(List<Block> klocki, EventComposer composer)
    {
        T.Section("TEST 16 - warianty tekstu i tekst listu gracza (krok 8)");

        // ---- 16a. scalesWithPoints: tylko akcje, zbior = pointsScaleable z wanilii
        var zlyTyp = klocki.Where(b => b.ScalesWithPoints && b.Type != BlockType.Action).Select(b => b.Id).ToList();
        T.EqI("16a scalesWithPoints wylacznie na klockach akcji", zlyTyp.Count, 0);
        var skaluja = klocki.Where(b => b.Type == BlockType.Action && b.ScalesWithPoints).Select(b => b.Payload)
                            .OrderBy(p => p, StringComparer.Ordinal).ToList();
        T.EqS("16a akcje skalujace sie punktami = pointsScaleable wanilii", string.Join(",", skaluja), string.Join(",", SkalujaceSie));

        // ---- 16b. prawdziwy katalog: walidacja bez odrzucen (ta sama funkcja co w grze)
        var problemy = TextComposer.Validate(klocki);
        T.EqI("16b katalog wariantow bez odrzucen", problemy.Count, 0);
        foreach (var p in problemy.Take(5)) Console.WriteLine("      " + p);

        TestWalidacji();
        TestWyboru();
        TestFlag();
        TestHasha();
        TestZdarzen(klocki, composer);
        TestTresci(klocki);
    }

    // ---- 16g. Tresc zatwierdzona przez autora 2026-09-25 (decyzja K8-6; runda 3 po przegladzie S10: bez
    // Cisza:noc/z1 i Napad:wendeta, nowe teksty Napad:noc, Uchodzcy, Dzikus, Zrzut, Burza, Mod_Noc, SladWalki,
    // NowyCzlowiek, Plotki; cele z waga wedlug MOCY KONCOWEJ; Slabo:skala od 300 punktow). Kanoniczny opis
    // warunkow: typ{pola rozne od swiezej instancji} po "+", potem flagi wariantu. Zmiana tekstu, warunku albo
    // flagi w XML bez aktualizacji tej tabeli (czyli bez nowej zgody autora) zapala test.
    static readonly (string blok, string id, string warunki, string tekst)[] Zatwierdzone =
    {
        ("PN_Trig_Bogactwo", "bardzo", "Cond_WealthRelative{min=4}", "Kolonia uchodzi w okolicy za wyjatkowo zamozna."),
        ("PN_Trig_Cisza", "dluga", "Cond_CalmPeriod{minDays=5}", "Po wielu dniach spokoju cos w koncu zaklocilo rutyne."),
        ("PN_Aktor_Piraci", "frakcja", "frakcja", "Wroga grupa zbrojna z frakcji {FRAKCJA}"),
        ("PN_Aktor_Piraci", "z1", "", "Wrogi oddzial"),
        ("PN_Aktor_Dzikie", "z1", "", "Okoliczna zwierzyna"),
        ("PN_Aktor_Obcy", "z1", "", "Ktos obcy"),
        ("PN_Akcja_Napad", "noc", "Cond_Night{}", "nadciaga noca na kolonie."),
        ("PN_Akcja_Napad", "z1", "", "rusza do ataku na kolonie."),
        ("PN_Akcja_Szal", "noc", "Cond_Night{}", "wpada w szal i w ciemnosciach rusza na osade."),
        ("PN_Akcja_Rojenie", "z1", "", "wygryza sie z glebi skaly."),
        ("PN_Akcja_Wedrowiec", "nieliczna", "Cond_Colonists{max=3}", "prosi o przyjecie do nielicznej kolonii."),
        ("PN_Akcja_Wedrowiec", "z1", "", "prosi o miejsce w kolonii."),
        ("PN_Akcja_Uchodzcy", "z1", "", "rozbija sie w kapsule ratunkowej."),
        ("PN_Akcja_Dzikus", "z1", "", "wychodzi z dziczy i wloczy sie po okolicy."),
        ("PN_Akcja_Zrzut", "z1", "", "zostawia po sobie rozbite kapsuly z zaopatrzeniem."),
        ("PN_Akcja_Burza", "noc", "Cond_Night{}", "rozpetuje w okolicy nocna burze z piorunami."),
        ("PN_Akcja_Burza", "z1", "", "sciaga w okolice burze z piorunami."),
        ("PN_Akcja_Amok", "z1", "", "traci rozum i atakuje kazdego, kogo napotka."),
        ("PN_Cel_Osada", "waga", "min=High", "Dla kolonii to sprawa pierwszej wagi."),
        ("PN_Cel_Osada", "waga2", "min=High", "Kolonia nie moze tego zlekcewazyc."),
        ("PN_Cel_Obrzeza", "waga", "max=Low", "Dla kolonii to na razie sprawa drugorzedna."),
        ("PN_Cel_Obrzeza", "waga2", "max=Low", "Na razie nie jest to sprawa pierwszej wagi."),
        ("PN_Mod_Noc", "z1", "", "Jest juz ciemno."),
        ("PN_Mod_Slabo", "skala", "Cond_MinThreatPoints{min=300}+max=Low+skala", "Rozmach jest mniejszy, niz moglby byc."),
        ("PN_Kons_SladWalki", "kolejna", "Cond_Fakt{key=walka.byla}", "Kolejnego takiego dnia kolonia predko nie zapomni."),
        ("PN_Kons_NowyCzlowiek", "znow", "Cond_Fakt{key=ludzie.przybyli}", "Znow ktos moze dolaczyc do kolonii."),
        ("PN_Kons_Plotki", "kolejne", "Cond_Fakt{key=wiesci.zrodlo}", "Do kolonii docieraja kolejne wiesci z zewnatrz."),
        ("PN_Kons_Wrak", "znow", "Cond_Fakt{key=wrak.lezy}", "Znow na ziemi zostaje to, co spadlo."),
        ("PN_Kons_ZnakNaNiebie", "znow", "Cond_Fakt{key=niebo.znak}", "Niebo znow daje kolonii znak."),
    };

    /// <summary>Teksty BAZOWE przepisane w rundzie 3 (przeglad S10) - zatwierdzone slowo w slowo; puste = brak tekstu.</summary>
    static readonly (string blok, string tekst)[] BazoweS10 =
    {
        ("PN_Akcja_Zrzut", "zrzuca w okolicy kapsuly z zaopatrzeniem."),
        ("PN_Akcja_Uchodzcy", "spada z nieba w kapsule ratunkowej."),
        ("PN_Akcja_Dzikus", "wychodzi z dziczy i kreci sie po okolicy."),
        ("PN_Akcja_Meteoryt", "zrzuca w okolicy meteoryt."),
        ("PN_Akcja_Burza", "rozpetuje w okolicy burze z piorunami."),
        ("PN_Akcja_Emanator", "roztrzaskuje sie o grunt, a z wraku zaczyna saczyc sie psychiczny szum."),
        ("PN_Cel_Osada", ""),
        ("PN_Cel_Obrzeza", ""),
        ("PN_Mod_Noc", "Nad osada zapadla juz noc."),
        ("PN_Mod_Slabo", ""),
        ("PN_Kons_NowyCzlowiek", "Ktos nowy moze dolaczyc do kolonii."),
        ("PN_Kons_Plotki", "Do kolonii docieraja wiesci z zewnatrz."),
    };

    /// <summary>Kanoniczny opis warunkow i flag wariantu (pola rozne od swiezej instancji typu).</summary>
    static string Opis(TextVariant v)
    {
        var czesci = new List<string>();
        foreach (var c in v.conditions ?? new List<NarrativeCondition>())
        {
            var t = c.GetType();
            var swiezy = System.Activator.CreateInstance(t);
            var pola = t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                        .Where(f => !Equals(f.GetValue(c), f.GetValue(swiezy)))
                        .OrderBy(f => f.Name, StringComparer.Ordinal)
                        .Select(f => f.Name + "=" + System.Convert.ToString(f.GetValue(c), System.Globalization.CultureInfo.InvariantCulture));
            czesci.Add(t.Name + "{" + string.Join(",", pola) + "}");
        }
        if (v.minIntensity != IntensityLevel.VeryLow) czesci.Add("min=" + v.minIntensity);
        if (v.maxIntensity != IntensityLevel.VeryHigh) czesci.Add("max=" + v.maxIntensity);
        if (v.requiresPointsScaling) czesci.Add("skala");
        if (v.requiresFaction) czesci.Add("frakcja");
        return string.Join("+", czesci);
    }

    static void TestTresci(List<Block> klocki)
    {
        var wXml = klocki.Where(b => b.TextVariants != null)
                         .SelectMany(b => b.TextVariants.Select(v => (blok: b.Id, id: v.id, warunki: Opis(v), tekst: v.text)))
                         .OrderBy(x => x.blok, StringComparer.Ordinal).ThenBy(x => x.id, StringComparer.Ordinal).ToList();
        var spec = Zatwierdzone.OrderBy(x => x.blok, StringComparer.Ordinal).ThenBy(x => x.id, StringComparer.Ordinal).ToList();
        T.EqI("16g liczba wariantow w XML == zatwierdzonych (29 po rundzie 3)", wXml.Count, spec.Count);
        var brak = spec.Except(wXml).ToList();
        var nadmiar = wXml.Except(spec).ToList();
        T.Ok("16g kazdy zatwierdzony wariant jest w XML slowo w slowo", brak.Count == 0,
             string.Join(" | ", brak.Select(x => x.blok + ":" + x.id + " [" + x.warunki + "] " + x.tekst)));
        T.Ok("16g XML nie ma wariantow spoza zatwierdzonych", nadmiar.Count == 0,
             string.Join(" | ", nadmiar.Select(x => x.blok + ":" + x.id + " [" + x.warunki + "] " + x.tekst)));
        Block slabo = klocki.FirstOrDefault(b => b.Id == "PN_Mod_Slabo");
        T.Ok("16g PN_Mod_Slabo: pusty tekst bazowy (decyzja autora 2026-09-25)",
             slabo != null && string.IsNullOrEmpty(slabo.TextFragment), slabo == null ? "brak klocka" : "\"" + slabo.TextFragment + "\"");
        int bazowych = 0;
        foreach (var (blok, tekst) in BazoweS10)
        {
            Block b = klocki.FirstOrDefault(x => x.Id == blok);
            T.EqS("16g tekst bazowy po rundzie 3: " + blok, b == null ? "(brak klocka)" : (b.TextFragment ?? ""), tekst);
            bazowych++;
        }
        T.EqI("16g sprawdzono wszystkie przepisane teksty bazowe", bazowych, BazoweS10.Length);

        // ---- 16h. Straznicy leksykalni zasady "fakt w tekscie = warunek twardy" - dla KAZDEGO tekstu katalogu
        // (bazowego i wariantu). Slowo o nocy/ciemnosci wymaga Cond_Night{wantNight} (wariantu albo klocka), slowo
        // o porze dnia - Cond_Night{!wantNight}; slowo o powtorzeniu wymaga POZYTYWNEGO warunku pamieci (fakt jest,
        // watek otwarty) w samym wariancie - klocki nie czytaja pamieci. Przeglad S10: dawny slownik przepuszczal
        // "ciemno", "mrok", "wciaz", "jeszcze raz", a warunek pamieci mogl byc negatywny (required=false).
        // "ciemk" osobno: "po ciemku" nie zaczyna sie od "ciemn" - te dziure pokazala mutacja X55.
        var opcje = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        var nocne = new System.Text.RegularExpressions.Regex(@"\b(noc|nocn|nocy|nocami|ciemn|ciemk|zmrok|mrok|zmierzch|wieczor|ksiezyc)", opcje);
        var dzienne = new System.Text.RegularExpressions.Regex(@"\b(poludni|swit|rano|ranek|slonc|za dnia|w dzien)", opcje);
        var powtorne = new System.Text.RegularExpressions.Regex(
            @"\b(znow|znowu|kolejn|ponown|powtorn|jeszcze raz|nie pierwszy|drugi raz|wraca|na nowo|poprzedni|ten sam|wciaz|nadal)", opcje);
        int sprawdzonych = 0, zNoca = 0, zPowtorzeniem = 0;
        foreach (Block b in klocki)
        {
            bool nocKlocka = b.Conditions.Any(c => c is Cond_Night && ((Cond_Night)c).wantNight);
            bool dzienKlocka = b.Conditions.Any(c => c is Cond_Night && !((Cond_Night)c).wantNight);
            if (!string.IsNullOrEmpty(b.TextFragment))
            {
                sprawdzonych++;
                if (nocne.IsMatch(b.TextFragment))
                {
                    zNoca++;
                    T.Ok("16h noc w tekscie bazowym " + b.Id + " -> Cond_Night klocka", nocKlocka, b.TextFragment);
                }
                if (dzienne.IsMatch(b.TextFragment))
                    T.Ok("16h pora dnia w tekscie bazowym " + b.Id + " -> Cond_Night{!wantNight} klocka", dzienKlocka, b.TextFragment);
                T.Ok("16h tekst bazowy " + b.Id + " nie stwierdza powtorzenia (klocek nie czyta pamieci)",
                     !powtorne.IsMatch(b.TextFragment), b.TextFragment);
            }
            foreach (TextVariant v in b.TextVariants ?? new List<TextVariant>())
            {
                sprawdzonych++;
                var warunki = v.conditions ?? new List<NarrativeCondition>();
                bool nocWariantu = warunki.Any(c => c is Cond_Night && ((Cond_Night)c).wantNight);
                bool dzienWariantu = warunki.Any(c => c is Cond_Night && !((Cond_Night)c).wantNight);
                if (nocne.IsMatch(v.text))
                {
                    zNoca++;
                    T.Ok("16h noc w wariancie " + b.Id + ":" + v.id + " -> Cond_Night", nocWariantu || nocKlocka, v.text);
                }
                if (dzienne.IsMatch(v.text))
                    T.Ok("16h pora dnia w wariancie " + b.Id + ":" + v.id + " -> Cond_Night{!wantNight}", dzienWariantu || dzienKlocka, v.text);
                if (powtorne.IsMatch(v.text))
                {
                    zPowtorzeniem++;
                    T.Ok("16h powtorzenie w wariancie " + b.Id + ":" + v.id + " -> POZYTYWNY warunek pamieci",
                         warunki.Any(PamiecPozytywna), v.text);
                }
            }
        }
        T.Ok("16h straznicy przeszli po tekstach katalogu", sprawdzonych >= Zatwierdzone.Length + 20, "tekstow: " + sprawdzonych);
        // Straznik pustego slownika: katalog MA teksty nocne i powtorzenia - regex, ktory nic nie lapie, zapala test.
        T.Ok("16h slownik nocny cos lapie", zNoca >= 4, "tekstow z noca: " + zNoca);
        T.Ok("16h slownik powtorzen cos lapie", zPowtorzeniem >= 5, "tekstow z powtorzeniem: " + zPowtorzeniem);
        T.Ok("16h wyrocznia pamieci: Cond_Fakt{required=false} NIE jest pozytywny",
             !PamiecPozytywna(new Cond_Fakt { key = "a", required = false }) && PamiecPozytywna(new Cond_Fakt { key = "a" }), null);
    }

    static void TestWalidacji()
    {
        // ---- 16c. kazda regula walidacji na osobnym przypadku; oczekiwany fragment powodu
        var przypadki = new List<(string opis, TextVariant v, string powod)>
        {
            ("brak id", V(null, "tekst"), "brak id"),
            ("id zarezerwowane baza", V("baza", "tekst"), "zarezerwowane"),
            ("id zarezerwowane -", V("-", "tekst"), "zarezerwowane"),
            ("id z przecinkiem", V("a,b", "tekst"), "spoza"),
            ("id z dwukropkiem", V("a:b", "tekst"), "spoza"),
            ("id ze srednikiem (pole danych)", V("a;b", "tekst"), "spoza"),
            ("id ze znakiem rownosci (pole danych)", V("a=b", "tekst"), "spoza"),
            ("id ze spacja", V("a b", "tekst"), "spoza"),
            ("id z nowa linia", V("a\nb", "tekst"), "spoza"),
            ("id z tabulatorem", V("a\tb", "tekst"), "spoza"),
            ("id spoza ASCII", V("noc\u0105", "tekst"), "spoza"),
            ("warunek przesloniety przez new", new TextVariant { id = "t8", text = "x", conditions = new List<NarrativeCondition> { new Cond_Przesloniety() } }, "nie jest twardy"),
            ("pusta tresc", V("t1", "  "), "pusta tresc"),
            ("znacznik bez requiresFaction", V("t2", "Z {FRAKCJA}"), "bez requiresFaction"),
            ("requiresFaction bez znacznika", new TextVariant { id = "t3", text = "Bez znacznika", requiresFaction = true }, "bez znacznika"),
            ("nieznany nawias", V("t4", "Tekst {KTO}"), "nawias"),
            ("min > max", new TextVariant { id = "t5", text = "x", minIntensity = IntensityLevel.High, maxIntensity = IntensityLevel.Low }, "minIntensity"),
            ("warunek miekki", new TextVariant { id = "t6", text = "noc", conditions = new List<NarrativeCondition> { new Pref_Night() } }, "nie jest twardy"),
            ("pusty warunek", new TextVariant { id = "t7", text = "x", conditions = new List<NarrativeCondition> { null } }, "pusty warunek"),
        };
        int sprawdzonych = 0;
        foreach (var (opis, v, powod) in przypadki)
        {
            string p = TextComposer.Problem(v, new HashSet<string>());
            T.Ok("16c odrzucony: " + opis, p != null && p.Contains(powod), "powod: " + (p ?? "(przyjety)"));
            sprawdzonych++;
        }
        T.EqI("16c sprawdzono wszystkie reguly", sprawdzonych, przypadki.Count);

        // Przypadki POPRAWNE - walidacja nie moze byc tak ostra, ze odrzuca wszystko.
        T.Ok("16c przyjety: zwykly zamiennik", TextComposer.Problem(V("ok1", "Zwykly tekst."), new HashSet<string>()) == null, null);
        T.Ok("16c przyjety: frakcja ze znacznikiem",
             TextComposer.Problem(new TextVariant { id = "ok2", text = "Ludzie z {FRAKCJA}.", requiresFaction = true }, new HashSet<string>()) == null, null);
        T.Ok("16c przyjety: warunek twardy",
             TextComposer.Problem(new TextVariant { id = "ok3", text = "Noc.", conditions = new List<NarrativeCondition> { new Cond_Night() } }, new HashSet<string>()) == null, null);
        T.Ok("16c twardy = nadpisuje IsMet (Cond_Night tak, Pref_Night nie)",
             TextComposer.JestTwardy(new Cond_Night()) && !TextComposer.JestTwardy(new Pref_Night()), null);
        T.Ok("16c twardy: 'new IsMet' (przesloniecie) NIE jest nadpisaniem", !TextComposer.JestTwardy(new Cond_Przesloniety()), null);
        T.Ok("16c twardy: podklasa warunku twardego dziedziczy nadpisanie", TextComposer.JestTwardy(new Cond_NocPochodna()), null);
        T.Ok("16c przyjety: id ze wszystkimi dozwolonymi znakami", TextComposer.Problem(V("aZ09_.-", "Tekst."), new HashSet<string>()) == null, null);

        // Validate na klocku: odrzuca zle, zostawia dobre, przy duplikacie zostaje WCZESNIEJSZY, null usuwa.
        var k = new Block { Id = "PN_Test", TextFragment = "Baza." };
        k.TextVariants = new List<TextVariant> { V("a", "Pierwszy."), null, V("a", "Drugi o tym samym id."), V("b", "{ZLE}"), V("c", "Trzeci.") };
        var probl = TextComposer.Validate(new List<Block> { k });
        T.EqS("16c Validate: zostaja a (pierwszy) i c", string.Join(",", k.TextVariants.Select(x => x.id + "=" + x.text)), "a=Pierwszy.,c=Trzeci.");
        T.EqI("16c Validate: trzy odrzucenia (null, duplikat, nawias)", probl.Count, 3);
    }

    static void TestWyboru()
    {
        // ---- 16d. zasada wyboru na fiksturze
        var k = new Block { Id = "PN_Fikstura", Type = BlockType.Actor, TextFragment = "Baza." };
        k.TextVariants = new List<TextVariant>
        {
            V("g1", "Zamiennik pierwszy."),
            V("g2", "Zamiennik drugi."),
            new TextVariant { id = "noc", text = "Noca.", conditions = new List<NarrativeCondition> { new Cond_Night() } },
            new TextVariant { id = "slabo", text = "Mniejszy rozmach.", maxIntensity = IntensityLevel.Low, requiresPointsScaling = true },
            new TextVariant { id = "frakcja", text = "Ludzie z {FRAKCJA}.", requiresFaction = true },
        };
        var noc = new WorldSnapshot { IsNight = true };
        var dzien = new WorldSnapshot { IsNight = false };
        string id;

        // Noc, moc Normal, bez frakcji: pula = {noc} dla KAZDEGO ziarna.
        var idNoc = Enumerable.Range(0, 200).Select(z => { TextComposer.Pick(k, noc, IntensityLevel.Normal, true, null, z, out id); return id; }).Distinct().ToList();
        T.EqS("16d noc -> zawsze wariant nocny", string.Join(",", idNoc), "noc");

        // Dzien, Normal, bez frakcji: pula = baza + zamienniki; wszystkie trzy wystepuja, nic dopasowanego.
        var idDzien = Enumerable.Range(0, 200).Select(z => { TextComposer.Pick(k, dzien, IntensityLevel.Normal, true, null, z, out id); return id; })
                                .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        T.EqS("16d dzien bez dopasowania -> baza i zamienniki (pokrycie 200 ziaren)", string.Join(",", idDzien), "baza,g1,g2");

        // Dzien, moc Low, akcja SKALUJE sie: {slabo}; akcja NIE skaluje sie: pula ogolna.
        string t = TextComposer.Pick(k, dzien, IntensityLevel.Low, true, null, 7, out id);
        T.EqS("16d niska moc + akcja skalujaca -> wariant slabo", id, "slabo");
        var idNieskal = Enumerable.Range(0, 200).Select(z => { TextComposer.Pick(k, dzien, IntensityLevel.Low, false, null, z, out id); return id; }).Distinct().ToList();
        T.Ok("16d niska moc, akcja NIE skaluje sie -> nigdy slabo", !idNieskal.Contains("slabo"), string.Join(",", idNieskal));
        TextComposer.Pick(k, dzien, IntensityLevel.Normal, true, null, 7, out id);
        T.Ok("16d moc Normal -> nie slabo (maxIntensity Low)", id != "slabo", id);

        // Frakcja znana: wariant frakcyjny, znacznik zastapiony nazwa, zero nawiasow w tekscie.
        t = TextComposer.Pick(k, dzien, IntensityLevel.Normal, false, "Ostrza Nocy", 3, out id);
        T.EqS("16d frakcja znana -> wariant frakcyjny z nazwa", t, "Ludzie z Ostrza Nocy.");
        T.EqS("16d frakcja znana -> id frakcja", id, "frakcja");
        // Noc i frakcja: dwa dopasowane, oba osiagalne.
        var idOba = Enumerable.Range(0, 200).Select(z => { TextComposer.Pick(k, noc, IntensityLevel.Normal, false, "X", z, out id); return id; })
                              .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        T.EqS("16d noc + frakcja -> oba dopasowane osiagalne", string.Join(",", idOba), "frakcja,noc");
        // Pusta nazwa frakcji = frakcja nieznana.
        TextComposer.Pick(k, dzien, IntensityLevel.Normal, false, "", 3, out id);
        T.Ok("16d pusta nazwa frakcji -> bez wariantu frakcyjnego", id != "frakcja", id);

        // Bez snapshotu warunek swiata NIE zachodzi (niczego niesprawdzonego nie stwierdzamy).
        var idBez = Enumerable.Range(0, 100).Select(z => { TextComposer.Pick(k, null, IntensityLevel.Normal, false, null, z, out id); return id; }).Distinct().ToList();
        T.Ok("16d brak snapshotu -> nigdy wariant nocny", !idBez.Contains("noc"), string.Join(",", idBez));

        // Determinizm: ten sam stan, ten sam wynik.
        string a1 = TextComposer.Pick(k, dzien, IntensityLevel.Normal, false, null, 12345, out id);
        string a2 = TextComposer.Pick(k, dzien, IntensityLevel.Normal, false, null, 12345, out id);
        T.EqS("16d determinizm wyboru", a1, a2);

        // Klocek bez bazy: tylko zamienniki; bez bazy i bez wariantow: brak tekstu, id "-".
        var bezBazy = new Block { Id = "PN_BezBazy", TextVariants = new List<TextVariant> { V("z1", "Jedyny.") } };
        T.EqS("16d bez bazy -> jedyny zamiennik", TextComposer.Pick(bezBazy, dzien, IntensityLevel.Normal, false, null, 1, out id), "Jedyny.");
        var pusty = new Block { Id = "PN_Pusty" };
        T.Ok("16d bez tekstu i wariantow -> null i id '-'",
             TextComposer.Pick(pusty, dzien, IntensityLevel.Normal, false, null, 1, out id) == null && id == TextComposer.NoneId, id);
    }

    static void TestFlag()
    {
        // ---- 16i. (przeglad S10, K4) Kazda flaga wariantu OSOBNO: sama czyni wariant dopasowanym (IsSpecific)
        // i sama potrafi go wykluczyc (Matches), przy pozostalych flagach spelnionych.
        var jednoflagowe = new List<(string opis, TextVariant v)>
        {
            ("warunek", new TextVariant { id = "f1", text = "x", conditions = new List<NarrativeCondition> { new Cond_Night() } }),
            ("minIntensity", new TextVariant { id = "f2", text = "x", minIntensity = IntensityLevel.Low }),
            ("maxIntensity", new TextVariant { id = "f3", text = "x", maxIntensity = IntensityLevel.High }),
            ("requiresPointsScaling", new TextVariant { id = "f4", text = "x", requiresPointsScaling = true }),
            ("requiresFaction", new TextVariant { id = "f5", text = "{FRAKCJA}", requiresFaction = true }),
        };
        int n = 0;
        foreach (var (opis, v) in jednoflagowe)
        {
            T.Ok("16i IsSpecific: sama flaga " + opis + " -> wariant dopasowany", v.IsSpecific, null);
            n++;
        }
        T.EqI("16i sprawdzono wszystkie flagi", n, 5);
        T.Ok("16i IsSpecific: bez flag -> zamiennik", !V("f0", "x").IsSpecific, null);
        T.Ok("16i IsSpecific: pusta lista warunkow to nie warunek", !new TextVariant { id = "f6", text = "x", conditions = new List<NarrativeCondition>() }.IsSpecific, null);

        var noc = new WorldSnapshot { IsNight = true };
        var dzien = new WorldSnapshot { IsNight = false };
        var fWar = jednoflagowe[0].v; var fMin = jednoflagowe[1].v; var fMax = jednoflagowe[2].v;
        var fSkal = jednoflagowe[3].v; var fFrak = jednoflagowe[4].v;
        T.Ok("16i Matches warunek: noc tak, dzien nie, brak snapshotu nie",
             fWar.Matches(noc, IntensityLevel.Normal, true, "F") && !fWar.Matches(dzien, IntensityLevel.Normal, true, "F")
             && !fWar.Matches(null, IntensityLevel.Normal, true, "F"), null);
        T.Ok("16i Matches minIntensity=Low: VeryLow nie, Low i VeryHigh tak",
             !fMin.Matches(dzien, IntensityLevel.VeryLow, true, "F") && fMin.Matches(dzien, IntensityLevel.Low, true, "F")
             && fMin.Matches(dzien, IntensityLevel.VeryHigh, true, "F"), null);
        T.Ok("16i Matches maxIntensity=High: VeryHigh nie, High i VeryLow tak",
             !fMax.Matches(dzien, IntensityLevel.VeryHigh, true, "F") && fMax.Matches(dzien, IntensityLevel.High, true, "F")
             && fMax.Matches(dzien, IntensityLevel.VeryLow, true, "F"), null);
        T.Ok("16i Matches requiresPointsScaling: akcja nieskalujaca nie, skalujaca tak",
             !fSkal.Matches(dzien, IntensityLevel.Normal, false, "F") && fSkal.Matches(dzien, IntensityLevel.Normal, true, "F"), null);
        T.Ok("16i Matches requiresFaction: null i pusta nazwa nie, znana tak",
             !fFrak.Matches(dzien, IntensityLevel.Normal, true, null) && !fFrak.Matches(dzien, IntensityLevel.Normal, true, "")
             && fFrak.Matches(dzien, IntensityLevel.Normal, true, "F"), null);
    }

    static void TestHasha()
    {
        // ---- 16j. (przeglad S10, K7) Wybor wariantu NIE zalezy od procesu: hash identyfikatora klocka to FNV-1a
        // (wektory z opublikowanej specyfikacji FNV, obciete do liczby dodatniej), a nie string.GetHashCode, ktory
        // w .NET Core jest losowany per proces - wtedy rozgrywka i ramiona symulatora dostawalyby inne teksty.
        T.EqI("16j FNV-1a(\"\") = 0x811C9DC5 & 0x7FFFFFFF", TextComposer.StableHash(""), (int)(0x811C9DC5u & 0x7FFFFFFFu));
        T.EqI("16j FNV-1a(\"a\") = 0xE40C292C & 0x7FFFFFFF", TextComposer.StableHash("a"), (int)(0xE40C292Cu & 0x7FFFFFFFu));
        T.EqI("16j FNV-1a(\"foobar\") = 0xBF9CF968 & 0x7FFFFFFF", TextComposer.StableHash("foobar"), (int)(0xBF9CF968u & 0x7FFFFFFFu));
        T.EqI("16j wyrocznia FNV testu zgodna z wektorem", (int)(Fnv("foobar") & 0x7FFFFFFFu), (int)(0xBF9CF968u & 0x7FFFFFFFu));

        // Replika indeksu z komentarza TextComposer.Indeks: Avalanche(ziarno ^ (FNV(id) * 16777619)), potem Next(n).
        // Dla KAZDEGO ziarna wybor = replika - w szczegolnosci zalezy od id klocka.
        var k1 = new Block { Id = "PN_Akcja_Burza", TextVariants = new List<TextVariant> { V("a", "A."), V("b", "B."), V("c", "C.") } };
        var k2 = new Block { Id = "PN_Akcja_Napad", TextVariants = new List<TextVariant> { V("a", "A."), V("b", "B."), V("c", "C.") } };
        var dzien = new WorldSnapshot();
        string[] ids = { "a", "b", "c" };
        int zgodnych = 0, roznych = 0;
        for (int z = 0; z < 300; z++)
        {
            string id1, id2;
            TextComposer.Pick(k1, dzien, IntensityLevel.Normal, false, null, z, out id1);
            TextComposer.Pick(k2, dzien, IntensityLevel.Normal, false, null, z, out id2);
            int h1 = (int)(Fnv(k1.Id) & 0x7FFFFFFFu);
            int oczek;
            unchecked { oczek = new SeededRandom(SeededRandom.Avalanche(z ^ (h1 * 16777619))).Next(3); }
            if (id1 == ids[oczek]) zgodnych++;
            if (id1 != id2) roznych++;
        }
        T.EqI("16j wybor = replika formuly (300 ziaren, niezalezny od procesu)", zgodnych, 300);
        T.Ok("16j dwa klocki o tych samych wariantach wybieraja niezaleznie (id klocka w hashu)", roznych > 100, "roznych: " + roznych);
    }

    static void TestZdarzen(List<Block> klocki, EventComposer composer)
    {
        // ---- 16e. zdarzenia z prawdziwego katalogu
        var s = new WorldSnapshot { DaysPassed = 40, ColonistCount = 6, ColonyWealth = 40000, WealthRelative = 1.5f, MountainRoofCellsNearColony = 250,
                                    HasHostileFaction = true, Season = 2, IsNight = true, WildAnimalCount = 8, MaddenableAnimalCount = 6,
                                    DaysSinceLastEvent = 6f, KidnappedColonistCount = 2, HasPoweredCommsConsole = true, ThreatPoints = 800f,
                                    ColonistsOnMap = 6 };
        var zdarzenia = new List<ComposedEvent>();
        foreach (Block a in composer.AvailableActions(s, new EventRecipe()))
        {
            VariantEnumerationStats st;
            zdarzenia.AddRange(composer.EnumerateVariants(a, s, 100000, new SeededRandom(1), out st));
        }
        T.Ok("16e zdarzen do sprawdzenia > 0", zdarzenia.Count > 0, "zdarzen: " + zdarzenia.Count);

        // Przeglad S10 (K1): dawny filtr "zdarzenia bez wariantow" byl po kroku 8 pusty (prawie kazde zdarzenie ma
        // klocek z wariantem), wiec rownosc 0 == 0 niczego nie sprawdzala. Teraz: to samo zdarzenie na KLONACH
        // klockow bez wariantow musi dac dokladnie Description - dla KAZDEGO zdarzenia.
        int zgodnych = 0, sladPoprawny = 0, zmienionych = 0;
        foreach (var e in zdarzenia)
        {
            string slad;
            string tekst = TextComposer.Compose(e, s, "Frakcja Testowa", 42, out slad);
            int zTekstem = e.Blocks.Count(b => !string.IsNullOrEmpty(b.TextFragment) || (b.TextVariants != null && b.TextVariants.Count > 0));
            if (slad.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length == zTekstem) sladPoprawny++;
            var klony = e.Blocks.Select(b => { Block k = Klon(b); k.TextVariants = null; return k; }).ToList();
            var eBez = new ComposedEvent { Blocks = klony, Intensity = e.Intensity, Description = e.Description };
            string slBez;
            if (TextComposer.Compose(eBez, s, "Frakcja Testowa", 42, out slBez) == e.Description) zgodnych++;
            if (tekst != e.Description) zmienionych++;
            T.Ok("16e tekst listu bez nawiasow klamrowych: " + e.Signature, tekst.IndexOf('{') < 0 && tekst.IndexOf('}') < 0, tekst);
        }
        T.EqI("16e klocki bez wariantow -> tekst listu == Description (kazde zdarzenie)", zgodnych, zdarzenia.Count);
        T.EqI("16e slad ma wpis dla kazdego klocka z tekstem", sladPoprawny, zdarzenia.Count);
        T.Ok("16e straznik: warianty naprawde zmieniaja tekst zdarzen", zmienionych > 0,
             "zmienionych: " + zmienionych + " z " + zdarzenia.Count);
        Console.WriteLine("    zdarzen: " + zdarzenia.Count + ", z tekstem innym niz bazowy: " + zmienionych);

        // ---- 16f. Compose na fiksturze: flaga "akcja skaluje sie" pochodzi z KLOCKA AKCJI zdarzenia,
        // a nie z klocka niosacego wariant; kolejnosc i slad zgodne z kolejnoscia klockow.
        var modyfikator = new Block { Id = "PN_F_Mod", Type = BlockType.Modifier, TextFragment = "Zwykly rozmach." };
        modyfikator.TextVariants = new List<TextVariant>
        {
            new TextVariant { id = "maly", text = "Mniejszy rozmach.", maxIntensity = IntensityLevel.Low, requiresPointsScaling = true }
        };
        var aktor = new Block { Id = "PN_F_Aktor", Type = BlockType.Actor, TextFragment = "Ktos" };
        var akcjaSkal = new Block { Id = "PN_F_Akcja", Type = BlockType.Action, TextFragment = "uderza.", ScalesWithPoints = true };
        var akcjaNie = new Block { Id = "PN_F_Akcja", Type = BlockType.Action, TextFragment = "uderza.", ScalesWithPoints = false };
        var pusty = new Block { Id = "PN_F_Cel", Type = BlockType.Target };
        var eSkal = new ComposedEvent { Blocks = new List<Block> { aktor, akcjaSkal, pusty, modyfikator }, Intensity = IntensityLevel.VeryLow, Description = "Ktos uderza. Zwykly rozmach." };
        var eNie = new ComposedEvent { Blocks = new List<Block> { aktor, akcjaNie, pusty, modyfikator }, Intensity = IntensityLevel.VeryLow, Description = "Ktos uderza. Zwykly rozmach." };
        string sl1, sl2;
        T.EqS("16f akcja skalujaca + niska moc -> wariant modyfikatora", TextComposer.Compose(eSkal, s, null, 5, out sl1), "Ktos uderza. Mniejszy rozmach.");
        T.EqS("16f slad w kolejnosci klockow, klocek bez tekstu pominiety", sl1, "PN_F_Aktor:baza,PN_F_Akcja:baza,PN_F_Mod:maly");
        T.EqS("16f akcja NIE skalujaca -> tekst bazowy modyfikatora", TextComposer.Compose(eNie, s, null, 5, out sl2), "Ktos uderza. Zwykly rozmach.");
        T.EqS("16f slad przy tekscie bazowym", sl2, "PN_F_Aktor:baza,PN_F_Akcja:baza,PN_F_Mod:baza");
    }
}
