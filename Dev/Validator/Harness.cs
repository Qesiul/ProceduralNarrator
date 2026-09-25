using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Xml.Linq;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Tension;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

/// <summary>Minimalna infrastruktura testowa walidatora offline. Zero zaleznosci od Core poza modelem.</summary>
static class T
{
    public static int Passed;
    public static int Failed;
    public static readonly List<string> Failures = new List<string>();

    public static void Section(string name)
    {
        Console.WriteLine();
        Console.WriteLine("================================================================");
        Console.WriteLine("  " + name);
        Console.WriteLine("================================================================");
    }

    public static void Ok(string name, bool cond, string detail)
    {
        if (cond) { Passed++; Console.WriteLine("  [OK  ] " + name + (detail == null ? "" : "   " + detail)); }
        else { Failed++; Failures.Add(name + " :: " + detail); Console.WriteLine("  [BLAD] " + name + "   " + detail); }
    }

    public static void Eq(string name, double got, double exp, double tol)
    {
        bool ok = Math.Abs(got - exp) <= tol;
        Ok(name, ok, "oczekiwano " + F(exp) + ", otrzymano " + F(got) + " (tol " + F(tol) + ")");
    }

    public static void EqI(string name, int got, int exp)
    {
        Ok(name, got == exp, "oczekiwano " + exp + ", otrzymano " + got);
    }

    public static void EqS(string name, string got, string exp)
    {
        Ok(name, string.Equals(got, exp, StringComparison.Ordinal), "oczekiwano \"" + exp + "\", otrzymano \"" + got + "\"");
    }

    public static string F(double v) { return v.ToString("0.######", CultureInfo.InvariantCulture); }
    public static string F4(double v) { return v.ToString("0.0000", CultureInfo.InvariantCulture); }
}

/// <summary>IRandomSource liczacy wywolania - do niezmiennika zuzycia losowosci.</summary>
class CountingRandom : IRandomSource
{
    private readonly IRandomSource inner;
    public int NextCalls;
    public int PickCalls;

    public CountingRandom(int seed) { inner = new SeededRandom(seed); }

    public int Next(int maxExclusive) { NextCalls++; return inner.Next(maxExclusive); }
    public T2 Pick<T2>(IReadOnlyList<T2> items) { PickCalls++; return inner.Pick(items); }
}

/// <summary>Czynnik-atrapa o zadanej nazwie i stalej wartosci.</summary>
class StubFactor : IScoringFactor
{
    private readonly string name;
    private readonly float value;
    public StubFactor(string name, float value) { this.name = name; this.value = value; }
    public string Name { get { return name; } }
    public float Evaluate(ScoredCandidate c, DecisionContext ctx, out string explanation)
    {
        explanation = "atrapa";
        return value;
    }
}

/// <summary>Odczyt EFEKTYWNEJ konfiguracji decyzyjnej z prawdziwego Storyteller_Generative.xml.</summary>
class XmlConfig
{
    public int candidateBudget;
    public float qualityCutoff, nearBestFraction, softmaxTemperature, gateTemperature, vetoContextFitBelow, mtbDays;
    public string configStamp = "";
    public float minDaysPassed;
    public int maxSelectionRounds;
    public ScoringWeights weights = new ScoringWeights();
    public PassScoringParams pass = new PassScoringParams();
    public ContrastTuning contrast = new ContrastTuning();
    public bool useComposedLetter;

    /// <summary>
    /// Parametry kryzysu skrajnego z bloku &lt;crisis&gt; - WSPOLNA maszyneria, nie profil.
    /// Straznik dryfu jak przy gateTemperature: bez odczytu tutaj walidator liczylby na
    /// inicjalizatorach z kodu i nie zauwazylby zmiany progu w XML.
    /// </summary>
    public CrisisParams crisis = new CrisisParams();
    public bool crisisBlockPresent;

    /// <summary>
    /// Parametry warstwy lukow z bloku &lt;arcs&gt; (krok 5) i katalog lukow z Defs/Arcs.
    /// Ten sam straznik dryfu co przy &lt;crisis&gt;: bez odczytu tutaj testy lukow liczylyby
    /// na inicjalizatorach z kodu.
    /// </summary>
    public ProceduralNarrator.Core.Arcs.ArcParams arcs = new ProceduralNarrator.Core.Arcs.ArcParams();
    public bool arcsBlockPresent;
    public List<ProceduralNarrator.Core.Arcs.ArcDefinition> arcDefs = new List<ProceduralNarrator.Core.Arcs.ArcDefinition>();
    public string arcsPath;

    /// <summary>
    /// Blok &lt;playerStyle&gt; (krok 7) czytany SCISLE przez XmlObj: nieznany wezel to wyjatek, a nie
    /// cicha wartosc domyslna. null = bloku brak (TEST 14a to zglosi).
    /// </summary>
    public ProceduralNarrator.Core.PlayerModel.PlayerStyleParams playerStyle;

    /// <summary>
    /// Katalog OSOBOWOSCI narratora, czytany z Profiles_Core.xml.
    ///
    /// Ten sam straznik dryfu co przy parametrach compa: gdyby profile zyly wylacznie w XML,
    /// a walidator znal tylko wartosci domyslne z kodu, kazda literowka w nazwie wezla
    /// przechodzilaby po cichu - profil dostawalby domyslne parametry i wygladalby na sprawny.
    /// To dokladnie ta awaria, ktora przy gateTemperature przespala cala runde kroku 3.
    /// </summary>
    public List<NarratorProfile> profiles = new List<NarratorProfile>();

    public static XmlConfig Load(string path)
    {
        var doc = XDocument.Load(path);
        XElement li = doc.Descendants("li").FirstOrDefault(e =>
        {
            var a = e.Attribute("Class");
            return a != null && a.Value.EndsWith("StorytellerCompProperties_Generative", StringComparison.Ordinal);
        });
        if (li == null) throw new Exception("Nie znaleziono wezla StorytellerCompProperties_Generative w XML");

        var c = new XmlConfig();
        c.mtbDays = Fl(li, "mtbDays", 0f);
        c.candidateBudget = In(li, "candidateBudget", 0);
        c.qualityCutoff = Fl(li, "qualityCutoff", 0f);
        c.nearBestFraction = Fl(li, "nearBestFraction", 0f);
        c.softmaxTemperature = Fl(li, "softmaxTemperature", 0f);
        // gateTemperature byl JEDYNYM parametrem decyzyjnym pominietym przez ten straznik dryfu,
        // i akurat tym najnowszym - wszystkie liczby bramy liczyly sie na twardym defaulcie z kodu,
        // a nie na wartosci wysylanej z modem.
        c.gateTemperature = Fl(li, "gateTemperature", 0f);
        c.configStamp = St(li, "configStamp", "");
        c.minDaysPassed = Fl(li, "minDaysPassed", -1f);
        c.vetoContextFitBelow = Fl(li, "vetoContextFitBelow", 0f);
        c.maxSelectionRounds = In(li, "maxSelectionRounds", 0);

        XElement w = li.Element("weights");
        if (w != null)
        {
            c.weights.contextFit = Fl(w, "contextFit", c.weights.contextFit);
            c.weights.freshness = Fl(w, "freshness", c.weights.freshness);
            c.weights.dramaticContrast = Fl(w, "dramaticContrast", c.weights.dramaticContrast);
            c.weights.intentAlignment = Fl(w, "intentAlignment", c.weights.intentAlignment);
        }
        XElement p = li.Element("pass");
        if (p != null)
        {
            c.pass.halfLifeDays = Fl(p, "halfLifeDays", c.pass.halfLifeDays);
            c.pass.densityFloor = Fl(p, "densityFloor", c.pass.densityFloor);
            c.pass.densitySaturation = Fl(p, "densitySaturation", c.pass.densitySaturation);
            c.pass.maxStreak = In(p, "maxStreak", c.pass.maxStreak);
            c.pass.weightRestraint = Fl(p, "weightRestraint", c.pass.weightRestraint);
            c.pass.weightBaseline = Fl(p, "weightBaseline", c.pass.weightBaseline);
            c.pass.weightIntentAlignment = Fl(p, "weightIntentAlignment", c.pass.weightIntentAlignment);
        }
        XElement k = li.Element("contrast");
        if (k != null)
        {
            c.contrast.reliefGain = Fl(k, "reliefGain", c.contrast.reliefGain);
            c.contrast.strikeGain = Fl(k, "strikeGain", c.contrast.strikeGain);
        }

        c.useComposedLetter = Bo(li, "useComposedLetter", false);

        XElement kr = li.Element("crisis");
        c.crisisBlockPresent = kr != null;
        if (kr != null)
        {
            c.crisis.downedFraction = Fl(kr, "downedFraction", c.crisis.downedFraction);
            c.crisis.minDowned = In(kr, "minDowned", c.crisis.minDowned);
            c.crisis.enabled = Bo(kr, "enabled", c.crisis.enabled);
            c.crisis.lethalFraction = Fl(kr, "lethalFraction", -1f);
        }

        XElement lu = li.Element("arcs");
        c.arcsBlockPresent = lu != null;
        if (lu != null)
        {
            c.arcs.enabled = Bo(lu, "enabled", c.arcs.enabled);
            c.arcs.maxConcurrent = In(lu, "maxConcurrent", c.arcs.maxConcurrent);
        }

        XElement st = li.Element("playerStyle");
        if (st != null)
        {
            c.playerStyle = (ProceduralNarrator.Core.PlayerModel.PlayerStyleParams)XmlObj.Read(
                typeof(ProceduralNarrator.Core.PlayerModel.PlayerStyleParams), st);
        }

        c.profiles = LoadProfiles(Path.Combine(Path.GetDirectoryName(path), "Profiles_Core.xml"));
        c.arcsPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "..", "Arcs", "Arcs_Core.xml"));
        if (File.Exists(c.arcsPath))
        {
            c.arcDefs = Loader.LoadArcs(c.arcsPath);
        }
        return c;
    }

    static bool Bo(XElement e, string name, bool dflt)
    {
        var v = e.Element(name);
        return v == null ? dflt : bool.Parse(v.Value.Trim());
    }

    private static List<NarratorProfile> LoadProfiles(string path)
    {
        var wynik = new List<NarratorProfile>();
        if (!File.Exists(path))
        {
            return wynik;
        }

        var doc = XDocument.Load(path);
        foreach (XElement e in doc.Root.Elements()
                     .Where(x => x.Name.LocalName.EndsWith("NarratorProfileDef", StringComparison.Ordinal)))
        {
            var prof = new NarratorProfile();
            prof.Id = St(e, "defName", "");
            prof.Label = St(e, "label", "");
            prof.SelectionWeight = Fl(e, "selectionWeight", 1f);
            // Brak wezla = NaN, nie 0: 0 to poprawna orientacja Zrownowazonego, wiec domyslne 0
            // ukryloby brak wezla w pliku (TEST 14f porownuje z tabela decyzji autora).
            prof.StyleOrientation = Fl(e, "styleOrientation", float.NaN);

            XElement w = e.Element("weights");
            if (w != null)
            {
                prof.Weights.contextFit = Fl(w, "contextFit", prof.Weights.contextFit);
                prof.Weights.freshness = Fl(w, "freshness", prof.Weights.freshness);
                prof.Weights.dramaticContrast = Fl(w, "dramaticContrast", prof.Weights.dramaticContrast);
                prof.Weights.intentAlignment = Fl(w, "intentAlignment", prof.Weights.intentAlignment);
            }

            XElement t = e.Element("tension");
            if (t != null)
            {
                prof.Tension.narrativeWeight = Fl(t, "narrativeWeight", prof.Tension.narrativeWeight);
                prof.Tension.situationalWeight = Fl(t, "situationalWeight", prof.Tension.situationalWeight);
                prof.Tension.halfLifeDays = Fl(t, "halfLifeDays", prof.Tension.halfLifeDays);
                prof.Tension.downedFractionForMax = Fl(t, "downedFractionForMax", prof.Tension.downedFractionForMax);
                prof.Tension.calmBelow = Fl(t, "calmBelow", prof.Tension.calmBelow);
                prof.Tension.tenseAbove = Fl(t, "tenseAbove", prof.Tension.tenseAbove);
                prof.Tension.intensitySpan = Fl(t, "intensitySpan", prof.Tension.intensitySpan);
            }

            wynik.Add(prof);
        }
        return wynik;
    }

    public SelectionParameters Selection()
    {
        return new SelectionParameters
        {
            qualityCutoff = qualityCutoff,
            nearBestFraction = nearBestFraction,
            softmaxTemperature = softmaxTemperature,
            gateTemperature = gateTemperature
        };
    }

    private static string St(XElement parent, string name, string dflt)
    {
        var e = parent.Element(name);
        return e == null ? dflt : e.Value.Trim();
    }

    private static float Fl(XElement parent, string name, float dflt)
    {
        var e = parent.Element(name);
        return e == null ? dflt : float.Parse(e.Value.Trim(), CultureInfo.InvariantCulture);
    }

    private static int In(XElement parent, string name, int dflt)
    {
        var e = parent.Element(name);
        return e == null ? dflt : int.Parse(e.Value.Trim(), CultureInfo.InvariantCulture);
    }
}
