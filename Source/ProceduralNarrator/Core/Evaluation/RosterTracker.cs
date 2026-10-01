using System;
using System.Collections.Generic;

namespace ProceduralNarrator.Core.Evaluation
{
    /// <summary>Wolny kolonista w przegladzie: identyfikator pionka i mapa (-1 = karawana, kapsula).</summary>
    public struct RosterMember
    {
        public int Id;
        public int Map;

        public RosterMember(int id, int map)
        {
            Id = id;
            Map = map;
        }
    }

    /// <summary>Zmiana w skladzie kolonii - jedna linia [PN-KOLONISTA].</summary>
    public struct RosterEvent
    {
        public string Kind;
        public int Id;
        public int Map;
        public int CountAfter;
    }

    /// <summary>
    /// Sklad wolnych kolonistow miedzy przegladami (krok 9, etap L, decyzja L-5). Pierwszy przeglad (nowa gra,
    /// wczytanie, start gry ewaluacyjnej) daje kotwice "start" z liczebnoscia. Kolejne: najpierw ubytki, potem
    /// dolaczenia, kazda grupa rosnaco po Id, liczebnosc po kazdej zmianie o 1. Rodzaj ubytku podaje wolajacy
    /// (tylko gra wie, czy pionek zginal, zostal porwany...); spoza listy = "inne". Mapa ubytku = ostatnia znana.
    /// </summary>
    public sealed class RosterTracker
    {
        public const string Start = "start";
        public const string Joined = "dolaczyl";
        public const string Died = "zginal";
        public const string Kidnapped = "porwany";
        public const string Imprisoned = "uwieziony";
        public const string WentWild = "zdziczal";
        public const string Left = "odszedl";
        public const string Other = "inne";

        private static readonly string[] Ubytki = { Died, Kidnapped, Imprisoned, WentWild, Left, Other };

        private readonly Dictionary<int, int> znani = new Dictionary<int, int>();
        private bool zakotwiczony;

        public bool Anchored
        {
            get { return zakotwiczony; }
        }

        public int Count
        {
            get { return znani.Count; }
        }

        public static bool IsLossKind(string kind)
        {
            return Array.IndexOf(Ubytki, kind) >= 0;
        }

        public void Reset()
        {
            znani.Clear();
            zakotwiczony = false;
        }

        public List<RosterEvent> Update(IList<RosterMember> current, Func<int, string> lossKind)
        {
            var wynik = new List<RosterEvent>();
            var teraz = new Dictionary<int, int>();
            if (current != null)
            {
                for (int i = 0; i < current.Count; i++)
                {
                    teraz[current[i].Id] = current[i].Map;
                }
            }

            if (!zakotwiczony)
            {
                zakotwiczony = true;
                Zastap(teraz);
                wynik.Add(new RosterEvent { Kind = Start, Id = -1, Map = -1, CountAfter = teraz.Count });
                return wynik;
            }

            var odeszli = new List<int>();
            foreach (KeyValuePair<int, int> kv in znani)
            {
                if (!teraz.ContainsKey(kv.Key))
                {
                    odeszli.Add(kv.Key);
                }
            }
            var przybyli = new List<int>();
            foreach (KeyValuePair<int, int> kv in teraz)
            {
                if (!znani.ContainsKey(kv.Key))
                {
                    przybyli.Add(kv.Key);
                }
            }
            odeszli.Sort();
            przybyli.Sort();

            int liczba = znani.Count;
            for (int i = 0; i < odeszli.Count; i++)
            {
                string rodzaj = lossKind == null ? null : lossKind(odeszli[i]);
                liczba--;
                wynik.Add(new RosterEvent
                {
                    Kind = IsLossKind(rodzaj) ? rodzaj : Other,
                    Id = odeszli[i],
                    Map = znani[odeszli[i]],
                    CountAfter = liczba
                });
            }
            for (int i = 0; i < przybyli.Count; i++)
            {
                liczba++;
                wynik.Add(new RosterEvent { Kind = Joined, Id = przybyli[i], Map = teraz[przybyli[i]], CountAfter = liczba });
            }

            Zastap(teraz);
            return wynik;
        }

        private void Zastap(Dictionary<int, int> teraz)
        {
            znani.Clear();
            foreach (KeyValuePair<int, int> kv in teraz)
            {
                znani[kv.Key] = kv.Value;
            }
        }
    }
}
