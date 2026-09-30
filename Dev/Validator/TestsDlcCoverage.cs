using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

/// <summary>
/// TEST 20 - zestawy DLC i pokrycie toru (krok 9, etap K0; decyzja autora E-8).
///
/// 20a: przycinanie MayRequire / MayRequireAnyOf na syntetycznym XML - kazda regula osobno.
/// 20b: katalog klockow i lukow w DWOCH zestawach (wszystkie DLC, brak DLC): graf, budzet, warianty tekstu,
///      katalog lukow, niezmiennik "klocek DLC nie rozmnaza wariantow akcji Core".
/// 20c: WYROCZNIA POKRYCIA. Tor = incydenty, ktore moga wylosowac trzy podmieniane compy Cassandry
///      (OnOffCycle ThreatBig, OnOffCycle ThreatSmall, CategoryMTB Misc na mapie domowej):
///      category w {ThreatBig, ThreatSmall, Misc}, Map_PlayerHome w targetTags i BaseChanceThisGame &gt; 0
///      (waga 0 jest pomijana przez TryRandomElementByWeight). Liczona z DANYCH GRY (D:\Games\RimWorld\Data,
///      z dziedziczeniem ParentName), a nie z recznej listy. Brakujace akcje porownujemy z tabela
///      wyprowadzona NIEZALEZNIE w rozpoznaniu planu kroku 9 - dwie drogi musza dac ten sam wynik.
///      Tabela kurczy sie w K1/K2; na koncu ma byc pusta (pelne pokrycie).
/// </summary>
static class TestsDlcCoverage
{
    const string DaneGry = @"D:\Games\RimWorld\Data";

    static readonly (string folder, string pakiet)[] FolderyGry =
    {
        ("Core", Loader.Core), ("Royalty", "ludeon.rimworld.royalty"), ("Ideology", "ludeon.rimworld.ideology"),
        ("Biotech", "ludeon.rimworld.biotech"), ("Anomaly", "ludeon.rimworld.anomaly"),
    };

    // ---- Tabela oczekiwanych BRAKOW (rozpoznanie planu kroku 9, 2026-09-25): incydenty toru bez akcji ----
    // Krok 9, K1 (2026-09-26): tor gry podstawowej pokryty w calosci - 15 akcji w Blocks_CoreTrack.xml.
    static readonly string[] BrakCore = { };
    // Krok 9, K2 (2026-09-28): pokryte - 3 akcje Royalty i 31 Anomaly (Blocks_Royalty.xml, Blocks_Anomaly.xml).
    static readonly string[] BrakRoyalty = { };
    static readonly string[] BrakAnomaly = { };

    static readonly string[] KategorieToru = { "ThreatBig", "ThreatSmall", "Misc" };

    public static void Run(string[] plikiKlockow, XmlConfig cfg)
    {
        T.Section("TEST 20 - zestawy DLC i pokrycie toru (krok 9, K0)");
        TestPrzycinania();
        var zestawy = new (string nazwa, HashSet<string> aktywne)[] { ("wszystkie DLC", Loader.WszystkieDlc), ("brak DLC", Loader.BezDlc) };
        var wariantyAkcji = new Dictionary<string, Dictionary<string, int>>();
        foreach (var (nazwa, aktywne) in zestawy)
        {
            wariantyAkcji[nazwa] = TestKatalogu(nazwa, aktywne, plikiKlockow, cfg);
            TestPokrycia(nazwa, aktywne, plikiKlockow);
        }

        // Niezmiennik DLC: klocek slotu dolozony przez DLC nie moze po cichu rozmnozyc wariantow akcji Core.
        var wszystkie = wariantyAkcji["wszystkie DLC"];
        var brak = wariantyAkcji["brak DLC"];
        var rozne = brak.Where(kv => !wszystkie.ContainsKey(kv.Key) || wszystkie[kv.Key] != kv.Value)
                        .Select(kv => kv.Key + " " + kv.Value + "/" + (wszystkie.ContainsKey(kv.Key) ? wszystkie[kv.Key].ToString() : "brak")).ToList();
        T.Ok("20b STRAZNIK: katalog bez DLC ma akcje", brak.Count > 0, "akcji: " + brak.Count);
        T.Ok("20b akcje Core maja te same warianty z DLC i bez DLC", rozne.Count == 0, string.Join(", ", rozne));
    }

    // ------------------------------------------------------------------------------------------ 20a
    static int Przytnij(string xml, HashSet<string> aktywne, bool scisle, out XDocument doc)
    {
        doc = XDocument.Parse(xml);
        return Loader.Prune(doc, aktywne, "test", scisle);
    }

    static void TestPrzycinania()
    {
        var anomaly = Loader.Zestaw("ludeon.rimworld.anomaly");
        const string xml =
            "<Defs>" +
            "<X><defName>Core</defName><lista><li>a</li><li MayRequire=\"Ludeon.RimWorld.Royalty\">r</li><li MayRequireAnyOf=\"Ludeon.RimWorld.Royalty,Ludeon.RimWorld.Anomaly\">ra</li></lista></X>" +
            "<X MayRequire=\"Ludeon.RimWorld.Anomaly\"><defName>Ano</defName><lista><li MayRequire=\"Ludeon.RimWorld.Royalty\">wewn</li></lista></X>" +
            "<X MayRequire=\"Ludeon.RimWorld.Anomaly, Ludeon.RimWorld.Royalty\"><defName>Oba</defName></X>" +
            "</Defs>";
        XDocument d;

        int n = Przytnij(xml, Loader.WszystkieDlc, true, out d);
        T.EqI("20a wszystkie DLC: nic nie usuniete", n, 0);
        T.EqI("20a wszystkie DLC: 3 Defy", d.Root.Elements("X").Count(), 3);

        n = Przytnij(xml, Loader.BezDlc, true, out d);
        T.EqS("20a brak DLC: zostaje tylko Def bez MayRequire",
              string.Join(",", d.Root.Elements("X").Select(x => (string)x.Element("defName"))), "Core");
        T.EqS("20a brak DLC: z listy znika <li> Royalty i <li> AnyOf", string.Join(",", d.Root.Element("X").Element("lista").Elements("li").Select(x => x.Value)), "a");
        T.EqI("20a liczone tylko wezly najwyzszego poziomu (li wewnatrz usunietego Defa nie liczy sie drugi raz)", n, 4);

        n = Przytnij(xml, anomaly, true, out d);
        T.EqS("20a tylko Anomaly: Def Anomaly zostaje, Def wymagajacy dwoch DLC znika",
              string.Join(",", d.Root.Elements("X").Select(x => (string)x.Element("defName"))), "Core,Ano");
        T.EqS("20a tylko Anomaly: AnyOf spelnione jednym DLC",
              string.Join(",", d.Root.Element("X").Element("lista").Elements("li").Select(x => x.Value)), "a,ra");
        T.EqI("20a tylko Anomaly: <li> Royalty wewnatrz Defa Anomaly znika", d.Root.Elements("X").ElementAt(1).Element("lista").Elements("li").Count(), 0);

        // Surowosc wobec bledow, ktore gra polknelaby po cichu.
        bool rzucil = false;
        try { Przytnij("<Defs><X><payload MayRequire=\"Ludeon.RimWorld.Anomaly\">p</payload></X></Defs>", Loader.BezDlc, true, out d); }
        catch (Exception) { rzucil = true; }
        T.Ok("20a scisle: MayRequire na polu (nie Def, nie <li>) to wyjatek", rzucil, null);
        rzucil = false;
        try { Przytnij("<Defs><X MayRequire=\"Ludeon.RimWorld.Anomally\"><defName>L</defName></X></Defs>", Loader.WszystkieDlc, true, out d); }
        catch (Exception) { rzucil = true; }
        T.Ok("20a scisle: literowka w packageId to wyjatek (gra: Faulty MayRequire)", rzucil, null);
        n = Przytnij("<Defs><X MayRequire=\"Obcy.Mod\"><defName>L</defName></X></Defs>", Loader.WszystkieDlc, false, out d);
        T.EqI("20a lagodnie (dane gry): nieznany pakiet = nieaktywny, Def usuniety", n, 1);
    }

    // ------------------------------------------------------------------------------------------ 20b
    static Dictionary<string, int> TestKatalogu(string nazwa, HashSet<string> aktywne, string[] plikiKlockow, XmlConfig cfg)
    {
        var (blocks, graph, _) = Loader.Load(aktywne, plikiKlockow);
        int przycietychKlockow = Loader.Przycietych;
        T.EqI("20b [" + nazwa + "] listy onlyWith bez problemow", Loader.ProblemyOnlyWith.Count, 0);
        var composer = new EventComposer(blocks, graph);
        var gen = new CandidateGenerator(composer);
        CandidateSet pelny = gen.Generate(new EventRecipe(), null, new SeededRandom(7), 1000000);
        int akcji = blocks.Count(b => b.Type == BlockType.Action);
        Console.WriteLine("    [" + nazwa + "] klockow " + blocks.Count + ", akcji " + akcji + ", krawedzi " + graph.ForbiddenEdgeCount
                          + ", wariantow " + pelny.TotalVariants + ", przycietych wezlow " + przycietychKlockow);
        T.Ok("20b [" + nazwa + "] katalog niepusty", akcji > 0 && pelny.Candidates.Count > 0, "akcji " + akcji);

        int zlamania = 0;
        foreach (var c in pelny.Candidates)
        {
            var bl = c.Blocks;
            for (int i = 0; i < bl.Count; i++)
                for (int j = i + 1; j < bl.Count; j++)
                    if (!graph.Allows(bl[i].Id, bl[j].Id)) zlamania++;
        }
        T.EqI("20b [" + nazwa + "] zadna kombinacja nie lamie grafu", zlamania, 0);

        int maxNaAkcje = pelny.PerAction.Max(p => p.VariantsSeen);
        int k = cfg.candidateBudget / akcji;
        T.Ok("20b [" + nazwa + "] budzet z XML: K >= maksimum wariantow na akcje (pelna enumeracja)", k >= maxNaAkcje,
             "budzet " + cfg.candidateBudget + " / akcji " + akcji + " = K " + k + ", max " + maxNaAkcje);

        var problemyTekstu = TextComposer.Validate(blocks);
        T.EqI("20b [" + nazwa + "] warianty tekstu bez odrzucen", problemyTekstu.Count, 0);

        var luki = Loader.LoadArcs(cfg.arcsPath, aktywne);
        List<string> problemyLukow;
        ArcCatalog.Build(luki, out problemyLukow);
        T.EqI("20b [" + nazwa + "] katalog lukow bez odrzucen", problemyLukow.Count, 0);
        T.Ok("20b [" + nazwa + "] STRAZNIK: katalog lukow niepusty", luki.Count > 0, "lukow " + luki.Count);

        return pelny.PerAction.ToDictionary(p => p.ActionId, p => p.VariantsSeen);
    }

    // ------------------------------------------------------------------------------------------ 20c
    /// <summary>Incydent z danych gry po rozwiazaniu dziedziczenia (tylko pola potrzebne regule toru).</summary>
    sealed class Incydent
    {
        public string DefName, Kategoria;
        public List<string> Tagi = new List<string>();
        public float BaseChance, BaseChanceWithRoyalty = -1f;
    }

    /// <summary>
    /// Czyta IncidentDef z aktywnych folderow Data/*/Defs z dziedziczeniem ParentName (lustro XmlInheritance
    /// w zakresie potrzebnym tu: pola proste nadpisuje dziecko, listy &lt;li&gt; sa dopisywane, chyba ze
    /// Inherit="False"; obiekty zagniezdzone scalane rekurencyjnie). Poza zakresem: poprawki Patches/ - zadna
    /// nie dotyka IncidentDef (sprawdzone grepem 2026-09-25).
    /// </summary>
    public static Dictionary<string, (string kategoria, List<string> tagi, float waga)> TorGry(HashSet<string> aktywne, out List<string> wszystkieIncydenty)
    {
        Dictionary<string, ProceduralNarrator.Core.Model.AnomalyGateKind> bramy;
        return TorGry(aktywne, out wszystkieIncydenty, out bramy);
    }

    /// <summary>
    /// Jak wyzej, plus oczekiwana brama Anomaly kazdego incydentu: kategoria z canUseAnomalyChance (IncidentCategoryDef
    /// w danych gry) -> Anomaly przy minAnomalyThreatLevel &gt;= 0 (IsAnomalyIncident), inaczej Regular; bez flagi -> None.
    /// </summary>
    public static Dictionary<string, (string kategoria, List<string> tagi, float waga)> TorGry(HashSet<string> aktywne, out List<string> wszystkieIncydenty,
        out Dictionary<string, ProceduralNarrator.Core.Model.AnomalyGateKind> bramy)
    {
        var nazwane = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var incydenty = new List<XElement>();
        var kategorieZBrama = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (folder, pakiet) in FolderyGry)
        {
            if (!aktywne.Contains(pakiet)) continue;
            string katalog = Path.Combine(DaneGry, folder, "Defs");
            if (!Directory.Exists(katalog)) continue;
            foreach (var plik in Directory.EnumerateFiles(katalog, "*.xml", SearchOption.AllDirectories))
            {
                string tekst = File.ReadAllText(plik);
                if (tekst.IndexOf("Incident", StringComparison.Ordinal) < 0) continue;
                var doc = XDocument.Parse(tekst);
                Loader.Prune(doc, aktywne, plik, false);
                foreach (var el in doc.Root.Elements())
                {
                    var nazwa = (string)el.Attribute("Name");
                    if (nazwa != null) nazwane[nazwa] = el;
                    if (el.Name.LocalName == "IncidentDef" && (string)el.Attribute("Abstract") != "True") incydenty.Add(el);
                    if (el.Name.LocalName == "IncidentCategoryDef" && ((string)el.Element("canUseAnomalyChance"))?.Trim() == "true")
                        kategorieZBrama.Add((string)el.Element("defName"));
                }
            }
        }
        bool royalty = aktywne.Contains("ludeon.rimworld.royalty");
        var tor = new Dictionary<string, (string, List<string>, float)>(StringComparer.Ordinal);
        wszystkieIncydenty = new List<string>();
        bramy = new Dictionary<string, ProceduralNarrator.Core.Model.AnomalyGateKind>(StringComparer.Ordinal);
        foreach (var el in incydenty)
        {
            var r = Rozwiaz(el, nazwane);
            string def = (string)r.Element("defName");
            if (def == null) continue;
            wszystkieIncydenty.Add(def);
            string kat = (string)r.Element("category");
            var tagi = r.Element("targetTags")?.Elements("li").Select(x => x.Value.Trim()).ToList() ?? new List<string>();
            float bc = Liczba(r.Element("baseChance"), 0f);
            float bcr = Liczba(r.Element("baseChanceWithRoyalty"), -1f);
            float waga = royalty && bcr >= 0f ? bcr : bc;   // lustro IncidentWorker.BaseChanceThisGame
            int minAnomalii = r.Element("minAnomalyThreatLevel") == null ? -1 : int.Parse(r.Element("minAnomalyThreatLevel").Value.Trim(), CultureInfo.InvariantCulture);
            bramy[def] = kat != null && kategorieZBrama.Contains(kat)
                ? (minAnomalii >= 0 ? ProceduralNarrator.Core.Model.AnomalyGateKind.Anomaly : ProceduralNarrator.Core.Model.AnomalyGateKind.Regular)
                : ProceduralNarrator.Core.Model.AnomalyGateKind.None;
            if (kat != null && KategorieToru.Contains(kat) && tagi.Contains("Map_PlayerHome") && waga > 0f)
                tor[def] = (kat, tagi, waga);
        }
        return tor;
    }

    static float Liczba(XElement e, float domyslna)
    {
        return e == null ? domyslna : float.Parse(e.Value.Trim(), CultureInfo.InvariantCulture);
    }

    static XElement Rozwiaz(XElement el, Dictionary<string, XElement> nazwane)
    {
        var rodzic = (string)el.Attribute("ParentName");
        if (rodzic == null) return el;
        if (!nazwane.ContainsKey(rodzic)) throw new Exception("Brak rodzica " + rodzic + " dla " + (string)el.Element("defName"));
        var wynik = new XElement(Rozwiaz(nazwane[rodzic], nazwane));
        Scal(wynik, el);
        return wynik;
    }

    static void Scal(XElement cel, XElement zrodlo)
    {
        foreach (var dziecko in zrodlo.Elements())
        {
            if (dziecko.Name.LocalName == "li") { cel.Add(new XElement(dziecko)); continue; }
            var istniejacy = cel.Element(dziecko.Name);
            bool nieDziedzicz = (string)dziecko.Attribute("Inherit") == "False";
            if (istniejacy == null) cel.Add(new XElement(dziecko));
            else if (!nieDziedzicz && dziecko.HasElements && istniejacy.HasElements) Scal(istniejacy, dziecko);
            else istniejacy.ReplaceWith(new XElement(dziecko));
        }
    }

    static void TestPokrycia(string nazwa, HashSet<string> aktywne, string[] plikiKlockow)
    {
        List<string> wszystkie;
        Dictionary<string, ProceduralNarrator.Core.Model.AnomalyGateKind> bramy;
        var tor = TorGry(aktywne, out wszystkie, out bramy);
        var (blocks, _, _) = Loader.Load(aktywne, plikiKlockow);
        var zleBramy = blocks.Where(b => b.Type == BlockType.Action && bramy.ContainsKey(b.Payload) && bramy[b.Payload] != b.AnomalyGate)
                             .Select(b => b.Id + "=" + b.AnomalyGate + " (gra: " + bramy[b.Payload] + ")").ToList();
        T.Ok("20c [" + nazwa + "] brama Anomaly kazdej akcji == dane gry (kategoria i minAnomalyThreatLevel)",
             zleBramy.Count == 0, string.Join(", ", zleBramy));
        T.Ok("20c [" + nazwa + "] STRAZNIK: dane gry maja kategorie z brama i incydenty wszystkich trzech rodzajow",
             bramy.Values.Contains(ProceduralNarrator.Core.Model.AnomalyGateKind.Regular) && bramy.Values.Contains(ProceduralNarrator.Core.Model.AnomalyGateKind.None)
             && (bramy.Values.Contains(ProceduralNarrator.Core.Model.AnomalyGateKind.Anomaly) || !aktywne.Contains("ludeon.rimworld.anomaly")),
             string.Join(",", bramy.Values.Distinct()));
        var nasze = blocks.Where(b => b.Type == BlockType.Action).Select(b => b.Payload).ToList();
        Console.WriteLine("    [" + nazwa + "] incydentow w danych " + wszystkie.Count + ", w torze " + tor.Count + ", naszych akcji " + nasze.Count);

        T.Ok("20c [" + nazwa + "] STRAZNIK: dane gry wczytane (incydentow > 50)", wszystkie.Count > 50, "incydentow " + wszystkie.Count);
        var nieistniejace = nasze.Where(p => !wszystkie.Contains(p)).ToList();
        T.Ok("20c [" + nazwa + "] kazdy payload istnieje w danych gry tego zestawu (inaczej brak MayRequire)",
             nieistniejace.Count == 0, string.Join(", ", nieistniejace));
        var pozaTorem = nasze.Where(p => !tor.ContainsKey(p)).ToList();
        T.Ok("20c [" + nazwa + "] kazda nasza akcja nalezy do toru", pozaTorem.Count == 0, string.Join(", ", pozaTorem));

        var oczekiwane = new List<string>(BrakCore);
        if (aktywne.Contains("ludeon.rimworld.royalty")) oczekiwane.AddRange(BrakRoyalty);
        if (aktywne.Contains("ludeon.rimworld.anomaly")) oczekiwane.AddRange(BrakAnomaly);
        var brak = tor.Keys.Where(k => !nasze.Contains(k)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        oczekiwane.Sort(StringComparer.Ordinal);
        T.EqS("20c [" + nazwa + "] brakujace akcje toru == tabela z rozpoznania (niezalezna droga)",
              string.Join(",", brak), string.Join(",", oczekiwane));
        T.EqI("20c [" + nazwa + "] tor = nasze akcje + brakujace", tor.Count, nasze.Count + oczekiwane.Count);
    }
}
