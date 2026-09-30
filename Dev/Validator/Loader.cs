using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using ProceduralNarrator.Core.Blackboard;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;

/// <summary>Czyta prawdziwy Blocks_Core.xml tak, jak zrobilby to RimWorld.</summary>
static class Loader
{
    // ------------------------------------------------------------------------------------------------
    //  ZESTAWY DLC (krok 9, K0). Gra pomija Defy i elementy <li> z niespelnionym MayRequire /
    //  MayRequireAnyOf (Verse/LoadedModManager.cs:394, Verse/DirectXmlToObject.cs:405). Walidator
    //  wczytuje katalog dla zadanego zestawu aktywnych modow, zeby sprawdzic go takze BEZ DLC
    //  (decyzja autora E-8: mod dziala bez DLC, klocki DLC znikaja).
    // ------------------------------------------------------------------------------------------------

    /// <summary>packageId gry podstawowej (zawsze aktywny).</summary>
    public const string Core = "ludeon.rimworld";

    /// <summary>packageId czterech DLC (malymi literami, jak porownuje gra).</summary>
    public static readonly string[] Dlc =
        { "ludeon.rimworld.royalty", "ludeon.rimworld.ideology", "ludeon.rimworld.biotech", "ludeon.rimworld.anomaly" };

    public static HashSet<string> Zestaw(params string[] dlc)
    {
        var s = new HashSet<string>(StringComparer.Ordinal) { Core };
        foreach (var d in dlc) s.Add(d.ToLowerInvariant());
        return s;
    }

    public static readonly HashSet<string> WszystkieDlc = Zestaw(Dlc);
    public static readonly HashSet<string> BezDlc = Zestaw();

    /// <summary>Wezly usuniete przez MayRequire w ostatnim Load/LoadArcs (najwyzsze w drzewie).</summary>
    public static int Przycietych;

    /// <summary>
    /// Usuwa z dokumentu Defy i elementy &lt;li&gt;, ktorych MayRequire (wszystkie wymienione mody aktywne) albo
    /// MayRequireAnyOf (ktorykolwiek aktywny) nie jest spelnione - ta sama semantyka co ModsConfig.AreAllActive
    /// i IsAnyActiveOrEmpty. Tryb SCISLY (nasze Defy): atrybut w innym miejscu niz Def albo &lt;li&gt; to wyjatek
    /// (gra by go po cichu zignorowala), a nieznany packageId to wyjatek (gra loguje "Faulty MayRequire").
    /// Tryb LAGODNY (dane gry): inne miejsca tez przycinamy, nieznany packageId = nieaktywny.
    /// Zwraca liczbe usunietych wezlow najwyzszego poziomu.
    /// </summary>
    public static int Prune(XDocument doc, ISet<string> aktywne, string zrodlo, bool scisle = true)
    {
        var doUsuniecia = new List<XElement>();
        foreach (var el in doc.Root.Descendants().ToList())
        {
            var mr = el.Attribute("MayRequire");
            var mra = el.Attribute("MayRequireAnyOf");
            if (mr == null && mra == null) continue;
            bool naDefie = el.Parent == doc.Root;
            bool naLi = el.Name.LocalName == "li";
            if (scisle && !naDefie && !naLi)
                throw new Exception("MayRequire na wezle <" + el.Name.LocalName + "> (" + zrodlo
                                    + "): gra honoruje go tylko na Defie i na <li>, tu zignorowalaby go po cichu");
            bool ok = true;
            if (mr != null && !string.IsNullOrWhiteSpace(mr.Value)) ok &= Identyfikatory(mr.Value, zrodlo, scisle).All(aktywne.Contains);
            if (mra != null && !string.IsNullOrWhiteSpace(mra.Value)) ok &= Identyfikatory(mra.Value, zrodlo, scisle).Any(aktywne.Contains);
            if (!ok) doUsuniecia.Add(el);
        }
        var zbior = new HashSet<XElement>(doUsuniecia);
        int najwyzszych = doUsuniecia.Count(e => !e.Ancestors().Any(zbior.Contains));
        foreach (var el in doUsuniecia) el.Remove();
        return najwyzszych;
    }

    static List<string> Identyfikatory(string wartosc, string zrodlo, bool scisle)
    {
        var ids = wartosc.Split(',').Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).ToList();
        foreach (var id in ids)
        {
            if (scisle && id != Core && Array.IndexOf(Dlc, id) < 0)
                throw new Exception("Nieznany packageId w MayRequire: '" + id + "' (" + zrodlo + ") - gra: Faulty MayRequire");
        }
        return ids;
    }

    public static (List<Block>, CompatibilityGraph, List<(string,string)>) Load(params string[] paths)
    {
        return Load(WszystkieDlc, paths);
    }

    /// <summary>Problemy list onlyWith z ostatniego Load (CatalogGraphBuilder) - walidator wymaga zera.</summary>
    public static List<string> ProblemyOnlyWith = new List<string>();

    public static (List<Block>, CompatibilityGraph, List<(string,string)>) Load(ISet<string> aktywne, params string[] paths)
    {
        Przycietych = 0;
        var blocks0 = new List<Block>();
        var graph0 = new CompatibilityGraph();
        var inc0 = new List<(string, string)>();
        foreach (var path in paths) { var (b, g, i) = LoadOne(path, graph0, aktywne); blocks0.AddRange(b); inc0.AddRange(i); }
        // Listy dozwolonych (krok 9, K0) - ta sama funkcja co w grze, po wszystkich plikach (odwolania miedzy plikami).
        List<string> problemy;
        foreach (var para in CatalogGraphBuilder.ExpandOnlyWith(blocks0, graph0, out problemy)) inc0.Add((para.Key, para.Value));
        ProblemyOnlyWith = problemy;
        return (blocks0, graph0, inc0);
    }

    static (List<Block>, CompatibilityGraph, List<(string,string)>) LoadOne(string path, CompatibilityGraph graph, ISet<string> aktywne)
    {
        var doc = XDocument.Load(path);
        Przycietych += Prune(doc, aktywne, path);
        var blocks = new List<Block>();
        var incompat = new List<(string, string)>();

        // Wezly klocka: pola Block (malymi literami, jak w NarrativeBlockDef) + pola Defa, ktore nie maja
        // odpowiednika w Block. Nieznany wezel to WYJATEK (krok 8): gra zglosi go "doesn't correspond to any
        // field", a tutaj literowka typu <textVariant> cicho wylaczalaby wszystkie warianty klocka.
        var dozwolone = new HashSet<string>(StringComparer.Ordinal) { "defName", "blockType", "incompatibleWith", "label", "description" };
        foreach (var f in typeof(Block).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.Name == "Id" || f.Name == "Type") continue;
            dozwolone.Add(char.ToLowerInvariant(f.Name[0]) + f.Name.Substring(1));
        }
        foreach (var n in doc.Root.Elements().Where(e => e.Name.LocalName.EndsWith("NarrativeBlockDef")))
        {
            string id = (string)n.Element("defName");
            foreach (var el in n.Elements())
            {
                if (!dozwolone.Contains(el.Name.LocalName))
                    throw new Exception("Klocek " + id + ": wezel <" + el.Name.LocalName + "> nie odpowiada zadnemu polu (" + path + ")");
            }
            var b = new Block {
                Id        = id,
                Type      = Enum2<BlockType>(n, "blockType", BlockType.Action),
                Theme     = Enum2<Theme>(n, "theme", Theme.Natural),
                Valence   = Enum2<Valence>(n, "valence", Valence.Neutral),
                Scale     = Enum2<EventScale>(n, "scale", EventScale.Moderate),
                Intensity = Enum2<IntensityLevel>(n, "intensity", IntensityLevel.Normal),
                Payload   = (string)n.Element("payload"),
                TextFragment = (string)n.Element("textFragment"),
                CarriesFaction = n.Element("carriesFaction") != null && bool.Parse(n.Element("carriesFaction").Value.Trim()),
                ScalesWithPoints = n.Element("scalesWithPoints") != null && bool.Parse(n.Element("scalesWithPoints").Value.Trim()),
                StyleNeutral = n.Element("styleNeutral") != null && bool.Parse(n.Element("styleNeutral").Value.Trim()),
                WorkerMinPoints = n.Element("workerMinPoints") == null ? 0f
                    : float.Parse(n.Element("workerMinPoints").Value.Trim(), System.Globalization.CultureInfo.InvariantCulture),
                AnomalyGate = Enum2<AnomalyGateKind>(n, "anomalyGate", AnomalyGateKind.None),
            };
            // Warianty tekstu (krok 8) - czytane SCISLE przez XmlObj (nieznane pole = wyjatek), potem ta sama
            // walidacja co w grze (BlockCatalogLoader -> TextComposer.Validate). Problemy zbiera TEST 16.
            var tv = n.Element("textVariants");
            if (tv != null)
            {
                b.TextVariants = (List<TextVariant>)XmlObj.Read(typeof(List<TextVariant>), tv);
            }
            var tags = n.Element("tags");
            if (tags != null) foreach (var li in tags.Elements("li")) b.Tags.Add(li.Value);
            var onlyWith = n.Element("onlyWith");
            if (onlyWith != null) foreach (var li in onlyWith.Elements())
            {
                if (li.Name.LocalName != "li") throw new Exception("Klocek " + id + ": onlyWith zawiera <" + li.Name.LocalName + "> zamiast <li>");
                b.OnlyWith.Add(li.Value.Trim());
            }

            b.Conditions.AddRange(Conds(n.Element("conditions")));
            b.Preferences.AddRange(Conds(n.Element("preferences")));
            b.FactsOnExecute.AddRange(Fakty(n.Element("factsOnExecute")));
            // Wagi stylu gracza (krok 7) scisle przez XmlObj: literowka w nazwie cechy to wyjatek.
            var sw = n.Element("styleWeights");
            if (sw != null)
            {
                b.StyleWeights = (ProceduralNarrator.Core.PlayerModel.StyleWeights)XmlObj.Read(
                    typeof(ProceduralNarrator.Core.PlayerModel.StyleWeights), sw);
            }
            blocks.Add(b);

            var inc = n.Element("incompatibleWith");
            if (inc != null)
                foreach (var li in inc.Elements("li")) { graph.Forbid(id, li.Value); incompat.Add((id, li.Value)); }
        }
        return (blocks, graph, incompat);
    }

    /// <summary>
    /// Deklaracje faktow klocka (krok 6). Nieznany wezel w &lt;li&gt; RZUCA, a nie jest pomijany:
    /// literowka w nazwie pola dalaby tu klocek z wartoscia domyslna i test na zielono,
    /// czyli dokladnie to, przed czym broni audyt Def-Block w grze.
    /// </summary>
    static List<FactWrite> Fakty(XElement parent)
    {
        var list = new List<FactWrite>();
        if (parent == null) return list;
        foreach (var li in parent.Elements("li"))
        {
            var w = new FactWrite();
            foreach (var f in li.Elements())
            {
                switch (f.Name.LocalName)
                {
                    case "key": w.key = f.Value.Trim(); break;
                    case "value": w.value = float.Parse(f.Value.Trim(), CultureInfo.InvariantCulture); break;
                    case "accumulate": w.accumulate = bool.Parse(f.Value.Trim()); break;
                    case "lifespanDays": w.lifespanDays = float.Parse(f.Value.Trim(), CultureInfo.InvariantCulture); break;
                    default: throw new Exception("factsOnExecute: nieznane pole " + f.Name.LocalName);
                }
            }
            list.Add(w);
        }
        return list;
    }

    static T Enum2<T>(XElement n, string name, T dflt) where T : struct
    {
        var v = (string)n.Element(name);
        return v == null ? dflt : (T)Enum.Parse(typeof(T), v.Trim());
    }

    /// <summary>Odtwarza to, co robi DirectXmlLoader: Class= -> typ -> pola z dzieci.</summary>
    static List<NarrativeCondition> Conds(XElement parent)
    {
        var list = new List<NarrativeCondition>();
        if (parent == null) return list;
        foreach (var li in parent.Elements("li"))
        {
            string cls = (string)li.Attribute("Class");
            var t = Type.GetType(cls) ?? AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType(cls)).FirstOrDefault(x => x != null);
            if (t == null) throw new Exception("Nie znaleziono typu warunku: " + cls);
            var inst = (NarrativeCondition)Activator.CreateInstance(t);
            foreach (var f in li.Elements())
            {
                var fi = t.GetField(f.Name.LocalName, BindingFlags.Public | BindingFlags.Instance);
                if (fi == null) throw new Exception($"{cls} nie ma pola {f.Name.LocalName}");
                // Enum obsluzony jawnie: gra parsuje go sama, ale ten mini-parser podstawilby surowy
                // string i wywrocil sie dopiero na SetValue, z komunikatem nie wskazujacym przyczyny.
                // Lista tekstow (krok 9, K1: Cond_GameConditionAbsent.defs) - elementy <li>, jak w DirectXmlToObject.
                object val = fi.FieldType == typeof(bool)  ? bool.Parse(f.Value)
                           : fi.FieldType == typeof(int)   ? int.Parse(f.Value)
                           : fi.FieldType == typeof(float) ? float.Parse(f.Value, CultureInfo.InvariantCulture)
                           : fi.FieldType.IsEnum           ? Enum.Parse(fi.FieldType, f.Value, false)
                           : fi.FieldType == typeof(List<string>) ? f.Elements("li").Select(x => x.Value.Trim()).ToList()
                           : (object)f.Value;
                fi.SetValue(inst, val);
            }
            list.Add(inst);
        }
        return list;
    }

    /// <summary>
    /// Katalog LUKOW z prawdziwego Defs/Arcs/*.xml, wczytany prosto do typu rdzenia ArcDefinition
    /// (pola maja te same nazwy co NarrativeArcDef - rozjazd pilnuje audyt startowy w grze).
    /// </summary>
    public static List<ProceduralNarrator.Core.Arcs.ArcDefinition> LoadArcs(string path)
    {
        return LoadArcs(path, WszystkieDlc);
    }

    public static List<ProceduralNarrator.Core.Arcs.ArcDefinition> LoadArcs(string path, ISet<string> aktywne)
    {
        var wynik = new List<ProceduralNarrator.Core.Arcs.ArcDefinition>();
        var doc = XDocument.Load(path);
        Przycietych = Prune(doc, aktywne, path);
        foreach (var n in doc.Root.Elements().Where(e => e.Name.LocalName.EndsWith("NarrativeArcDef")))
        {
            wynik.Add((ProceduralNarrator.Core.Arcs.ArcDefinition)XmlObj.Read(typeof(ProceduralNarrator.Core.Arcs.ArcDefinition), n));
        }
        return wynik;
    }
}

/// <summary>
/// Generyczny odpowiednik Verse.DirectXmlToObject w zakresie, ktorego uzywaja nasze Defy:
/// pola publiczne (string, int, float, bool, enum), klasy zagniezdzone, List&lt;T&gt; przez &lt;li&gt;,
/// polimorfizm przez atrybut Class= (pelna nazwa typu). Wezel bez odpowiadajacego pola to
/// WYJATEK - gra loguje wtedy "doesn't correspond to any field", a walidator ma byc co najmniej
/// tak samo surowy, inaczej literowka w XML przeszlaby tu na zielono.
/// </summary>
static class XmlObj
{
    public static object Read(Type declared, XElement node)
    {
        Type t = declared;
        var cls = node.Attribute("Class");
        if (cls != null)
        {
            t = ResolveType(cls.Value);
            if (!declared.IsAssignableFrom(t)) throw new Exception("Typ " + cls.Value + " nie pasuje do " + declared.Name);
        }

        if (t == typeof(string)) return node.Value;
        if (t == typeof(int)) return int.Parse(node.Value.Trim(), CultureInfo.InvariantCulture);
        if (t == typeof(float)) return float.Parse(node.Value.Trim(), CultureInfo.InvariantCulture);
        if (t == typeof(bool)) return bool.Parse(node.Value.Trim());
        if (t.IsEnum) return Enum.Parse(t, node.Value.Trim());

        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elem = t.GetGenericArguments()[0];
            var list = (System.Collections.IList)Activator.CreateInstance(t);
            foreach (var li in node.Elements())
            {
                if (li.Name.LocalName != "li") throw new Exception("Lista " + node.Name.LocalName + " zawiera wezel " + li.Name.LocalName + " zamiast <li>");
                list.Add(Read(elem, li));
            }
            return list;
        }

        if (t.IsAbstract) throw new Exception("Typ abstrakcyjny " + t.Name + " bez atrybutu Class= w wezle " + node.Name.LocalName);
        object inst = Activator.CreateInstance(t);
        foreach (var child in node.Elements())
        {
            var fi = t.GetField(child.Name.LocalName, BindingFlags.Public | BindingFlags.Instance);
            if (fi == null) throw new Exception(t.Name + " nie ma pola " + child.Name.LocalName + " (wezel XML bez pola)");
            fi.SetValue(inst, Read(fi.FieldType, child));
        }
        return inst;
    }

    static Type ResolveType(string name)
    {
        var t = Type.GetType(name) ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(name)).FirstOrDefault(x => x != null);
        if (t == null) throw new Exception("Nie znaleziono typu: " + name);
        return t;
    }
}
