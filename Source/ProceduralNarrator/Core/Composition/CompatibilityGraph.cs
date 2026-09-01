using System.Collections.Generic;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Composition
{
    /// <summary>
    /// Graf kompatybilnosci klockow. Okresla, ktore klocki moga wystapic razem
    /// w jednym wydarzeniu. Krawedz zabroniona = kombinacja bez sensu narracyjnego.
    /// Relacja jest symetryczna, wiec klucz normalizujemy leksykograficznie.
    /// </summary>
    public class CompatibilityGraph
    {
        private readonly HashSet<string> forbidden = new HashSet<string>();

        public void Forbid(string blockA, string blockB)
        {
            if (blockA == null || blockB == null)
            {
                return;
            }
            forbidden.Add(Key(blockA, blockB));
        }

        public bool Allows(string blockA, string blockB)
        {
            return !forbidden.Contains(Key(blockA, blockB));
        }

        /// <summary>Czy kandydat jest zgodny ze wszystkimi juz wybranymi klockami.</summary>
        public bool AllowsWithAll(IEnumerable<string> chosenIds, string candidateId)
        {
            foreach (string id in chosenIds)
            {
                if (!Allows(id, candidateId))
                {
                    return false;
                }
            }
            return true;
        }

        public int ForbiddenEdgeCount
        {
            get { return forbidden.Count; }
        }

        /// <summary>
        /// Przepisuje caly graf na macierz logiczna po INDEKSACH katalogu:
        /// matrix[i][j] == Allows(catalog[i].Id, catalog[j].Id).
        ///
        /// Po co, skoro Allows juz dziala: Key() sklada nowy string przy KAZDYM sprawdzeniu
        /// zgodnosci. W przelocie po drzewie wariantow sprawdzen jest rzedu N * P * D, czyli
        /// przy katalogu 100 akcji okolo 240 tysiecy - tyle samo alokacji na jedna decyzje
        /// narratora, co oznacza rzad 10 ms i presje na GC w watku gry. Macierz kosztuje
        /// |katalog|^2 bajtow raz, w konstruktorze EventComposera, a w petli goracej daje
        /// zerowa liczbe alokacji i odczyt w czasie stalym.
        ///
        /// Macierz liczymy tylko dla polowy nad przekatna i lustrzemy, bo relacja jest
        /// symetryczna z konstrukcji (Key normalizuje pare leksykograficznie). Przekatna
        /// liczona jest jawnie tym samym wywolaniem - gdyby ktos kiedys zabronil klocka
        /// samego ze soba, macierz ma to odwzorowac, a nie zalozyc.
        /// </summary>
        public bool[][] BuildMatrix(IList<Block> catalog)
        {
            int n = catalog == null ? 0 : catalog.Count;
            var matrix = new bool[n][];
            for (int i = 0; i < n; i++)
            {
                matrix[i] = new bool[n];
            }

            for (int i = 0; i < n; i++)
            {
                string idI = catalog[i] != null ? catalog[i].Id : null;
                matrix[i][i] = Allows(idI, idI);
                for (int j = i + 1; j < n; j++)
                {
                    string idJ = catalog[j] != null ? catalog[j].Id : null;
                    bool allowed = Allows(idI, idJ);
                    matrix[i][j] = allowed;
                    matrix[j][i] = allowed;
                }
            }

            return matrix;
        }

        private static string Key(string a, string b)
        {
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }
    }
}
