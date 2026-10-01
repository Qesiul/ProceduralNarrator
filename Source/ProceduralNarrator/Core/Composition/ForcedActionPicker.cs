using System.Collections.Generic;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Composition
{
    /// <summary>Stan akcji dla menu "PN: wymus akcje": dostepna albo pierwszy powod niedostepnosci.</summary>
    public sealed class ActionAvailability
    {
        public const string Available = "dostepna";
        public const string Tag = "tag";
        public const string Conditions = "warunki";
        public const string Mirror = "lustro";

        public Block Action;
        public string Status;

        public bool IsAvailable
        {
            get { return Status == Available; }
        }
    }

    /// <summary>
    /// Wybor wariantu dla akcji debugowej "PN: wymus akcje" (krok 9, etap L, decyzja L-3): bez bramy i bez
    /// losowania - najwyzsza uzytecznosc sposrod wariantow tej akcji, remis po kluczu sortowania (ordynalnie).
    /// Warianty zawetowane tylko wtedy, gdy innych nie ma: akcja jest wymuszona, weto kontekstu nie moze jej
    /// zablokowac, ale nie wygrywa z wariantem niezawetowanym.
    /// </summary>
    public static class ForcedActionPicker
    {
        public static ScoredCandidate Pick(IEnumerable<ScoredCandidate> scored, string actionBlockId)
        {
            ScoredCandidate najlepszy = null;
            if (scored == null)
            {
                return null;
            }
            foreach (ScoredCandidate c in scored)
            {
                if (c == null || c.IsPass || c.Event == null || c.Event.ActionBlockId != actionBlockId)
                {
                    continue;
                }
                if (najlepszy == null || Lepszy(c, najlepszy))
                {
                    najlepszy = c;
                }
            }
            return najlepszy;
        }

        private static bool Lepszy(ScoredCandidate a, ScoredCandidate b)
        {
            if (a.Vetoed != b.Vetoed)
            {
                return !a.Vetoed;
            }
            if (a.Utility != b.Utility)
            {
                return a.Utility > b.Utility;
            }
            return string.CompareOrdinal(a.SortKey ?? string.Empty, b.SortKey ?? string.Empty) < 0;
        }
    }
}
