using System.Collections.Generic;

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

        private static string Key(string a, string b)
        {
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }
    }
}
