using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Conditions
{
    // ================== WARUNKI TWARDE - bramki spojnosci ==================
    // Uzywane w <conditions>. Jesli ktorykolwiek nie jest spelniony, klocek
    // nie moze w ogole wejsc do kompozycji.

    /// <summary>
    /// Wymaga odpowiednio duzego stropu gorskiego w poblizu kolonii (np. infestacja).
    /// Prog, a nie flaga - pojedyncza skala w rogu mapy to nie jest gorska baza.
    /// </summary>
    public class Cond_MountainRoof : NarrativeCondition
    {
        public int minCells = 40;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.MountainRoofCellsNearColony >= minCells;
        }

        public override string Describe()
        {
            return "gorski strop >= " + minCells + " komorek";
        }
    }

    /// <summary>Wymaga istnienia niepokonanej frakcji wrogiej graczowi (np. napad).</summary>
    public class Cond_HostileFaction : NarrativeCondition
    {
        public bool required = true;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.HasHostileFaction == required;
        }

        public override string Describe()
        {
            return required ? "wymaga wrogiej frakcji" : "wymaga braku wrogiej frakcji";
        }
    }

    /// <summary>Ogranicza klocek do przedzialu liczby kolonistow.</summary>
    public class Cond_Colonists : NarrativeCondition
    {
        public int min = 0;
        public int max = 9999;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.ColonistCount >= min && s.ColonistCount <= max;
        }

        public override string Describe()
        {
            return "kolonistow " + min + "-" + max;
        }
    }

    /// <summary>
    /// Ogranicza klocek do przedzialu bogactwa WZGLEDNEGO (krotnosc normy na dany dzien).
    /// min = 2.0 znaczy "kolonia dwa razy bogatsza, niz gra sie spodziewa".
    /// </summary>
    public class Cond_WealthRelative : NarrativeCondition
    {
        public float min = 0f;
        public float max = float.MaxValue;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.WealthRelative >= min && s.WealthRelative <= max;
        }

        public override string Describe()
        {
            return "bogactwo wzgl >= " + min.ToString("0.##");
        }
    }

    /// <summary>Nie wpuszcza klocka przed uplywem N dni gry.</summary>
    public class Cond_MinDaysPassed : NarrativeCondition
    {
        public int min = 0;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.DaysPassed >= min;
        }

        public override string Describe()
        {
            return "od dnia " + min;
        }
    }

    /// <summary>Wymaga obecnosci dzikich zwierzat (np. szal zwierzat).</summary>
    public class Cond_WildAnimals : NarrativeCondition
    {
        public int min = 1;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.WildAnimalCount >= min;
        }

        public override string Describe()
        {
            return "dzikich zwierzat >= " + min;
        }
    }
}
