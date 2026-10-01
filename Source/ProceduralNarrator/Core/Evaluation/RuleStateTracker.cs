using System.Collections.Generic;

namespace ProceduralNarrator.Core.Evaluation
{
    /// <summary>Stan mapy dla regul niedopasowania RN (PLAN_EWALUACJI.md 6): kryzys, zagrozenie, pusta baza.</summary>
    public struct RuleState
    {
        public bool Crisis;
        public bool Threat;
        public bool Empty;

        public bool SameAs(RuleState o)
        {
            return Crisis == o.Crisis && Threat == o.Threat && Empty == o.Empty;
        }
    }

    /// <summary>
    /// Linia [PN-STAN] (krok 9, etap L): ekspozycja na warunki regul RN w grze - stan probkowany co 250 tickow, linia przy
    /// pierwszym stanie mapy ("start"), przy kazdej zmianie ktoregokolwiek predykatu ("zmiana") i gdy mapa przestaje byc
    /// domem ("koniec"). Stan obowiazuje od linii do nastepnej linii tej mapy, wiec czas w stanie odtwarza sie z samych zmian.
    /// </summary>
    public sealed class RuleStateTracker
    {
        public const string Start = "start";
        public const string Change = "zmiana";
        public const string End = "koniec";

        private readonly Dictionary<int, RuleState> ostatni = new Dictionary<int, RuleState>();

        /// <summary>RN1: co najmniej polowa kolonistow na mapie powalona ostro (i ktos jest na mapie, i ktos powalony).</summary>
        public static bool IsCrisis(int onMap, int downed)
        {
            return onMap >= 1 && downed >= 1 && 2 * downed >= onMap;
        }

        public static RuleState Of(int onMap, int downed, bool threat)
        {
            return new RuleState { Crisis = IsCrisis(onMap, downed), Threat = threat, Empty = onMap == 0 };
        }

        /// <summary>"start" przy pierwszym stanie mapy, "zmiana" przy zmianie predykatu, null gdy bez zmian.</summary>
        public string Update(int map, RuleState s)
        {
            RuleState poprzedni;
            if (!ostatni.TryGetValue(map, out poprzedni))
            {
                ostatni[map] = s;
                return Start;
            }
            if (poprzedni.SameAs(s))
            {
                return null;
            }
            ostatni[map] = s;
            return Change;
        }

        /// <summary>Mapy znane, ktorych nie ma w biezacym przegladzie (porzucona kolonia): usuwa je, rosnaco - linia "koniec".</summary>
        public List<int> Ended(ICollection<int> present)
        {
            var wynik = new List<int>();
            foreach (int m in ostatni.Keys)
            {
                if (!present.Contains(m))
                {
                    wynik.Add(m);
                }
            }
            wynik.Sort();
            for (int i = 0; i < wynik.Count; i++)
            {
                ostatni.Remove(wynik[i]);
            }
            return wynik;
        }

        public void Reset()
        {
            ostatni.Clear();
        }
    }
}
