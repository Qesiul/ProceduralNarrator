using System.Globalization;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Conditions
{
    // ============== WARUNKI MIEKKIE - dopasowanie kontekstowe ==============
    // Uzywane w <preferences>. Nie blokuja niczego; zwracaja 0..1 i skladaja sie
    // na contextFit zlozonego wydarzenia, ktory zasila scoring (krok 3).
    //
    // Konwencja: from -> 0, to -> 1. Zeby preferencja MALALA wraz z wartoscia,
    // wystarczy podac from wieksze od to.

    /// <summary>
    /// Preferuje kolonie bogate (albo ubogie) MIERZONE WZGLEDEM normy na dany dzien.
    /// from/to to krotnosci: 1.0 = kolonia typowa, 3.0 = trzykrotnie bogatsza od normy.
    /// Odporne na scenariusz startowy i na wiek kolonii.
    /// </summary>
    public class Pref_WealthRelative : NarrativeCondition
    {
        public float from = 0.5f;
        public float to = 3f;

        public override float Fit(WorldSnapshot s)
        {
            return Ramp(s.WealthRelative, from, to);
        }

        public override string Describe()
        {
            return "bogactwo wzgl " + from.ToString("0.##", CultureInfo.InvariantCulture) + "->" + to.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Preferuje duze (albo male) kolonie.</summary>
    public class Pref_Colonists : NarrativeCondition
    {
        public float from = 1f;
        public float to = 12f;

        public override float Fit(WorldSnapshot s)
        {
            return Ramp(s.ColonistCount, from, to);
        }

        public override string Describe()
        {
            return "kolonistow " + from.ToString("0", CultureInfo.InvariantCulture) + "->" + to.ToString("0", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Preferuje pore dnia. Np. modyfikator "po zmroku" pasuje w nocy.</summary>
    public class Pref_Night : NarrativeCondition
    {
        public bool wantNight = true;

        public override float Fit(WorldSnapshot s)
        {
            return s.IsNight == wantNight ? 1f : 0.2f;
        }

        public override string Describe()
        {
            return wantNight ? "pasuje noca" : "pasuje za dnia";
        }
    }

    /// <summary>Preferuje konkretna pore roku (0=wiosna, 1=lato, 2=jesien, 3=zima).</summary>
    public class Pref_Season : NarrativeCondition
    {
        public int season = 3;

        public override float Fit(WorldSnapshot s)
        {
            return s.Season == season ? 1f : 0.3f;
        }

        public override string Describe()
        {
            return "pora roku " + season;
        }
    }

    /// <summary>Preferuje mape bogata w dzika zwierzyne.</summary>
    public class Pref_WildAnimals : NarrativeCondition
    {
        public float from = 0f;
        public float to = 25f;

        public override float Fit(WorldSnapshot s)
        {
            return Ramp(s.WildAnimalCount, from, to);
        }

        public override string Describe()
        {
            return "dzikich zwierzat " + from.ToString("0", CultureInfo.InvariantCulture) + "->" + to.ToString("0", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Preferuje dojrzala rozgrywke (albo wczesna, gdy from > to).</summary>
    public class Pref_GameAge : NarrativeCondition
    {
        public float from = 0f;
        public float to = 60f;

        public override float Fit(WorldSnapshot s)
        {
            return Ramp(s.DaysPassed, from, to);
        }

        public override string Describe()
        {
            return "dzien gry " + from.ToString("0", CultureInfo.InvariantCulture) + "->" + to.ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
