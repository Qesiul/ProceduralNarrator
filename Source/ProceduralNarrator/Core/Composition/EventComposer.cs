using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Composition
{
    /// <summary>
    /// Warstwa kompozycji (sekcja 5.7). Sklada konkretne wydarzenie z klockow
    /// zgodnie z przepisem i grafem kompatybilnosci.
    ///
    /// Klocki wypelniaja sloty w ustalonej kolejnosci narracyjnej:
    ///     Trigger (opcjonalny) -> Actor -> Action -> Target -> Modifier (opcjonalny)
    /// Kazdy kolejny klocek musi byc zgodny ze WSZYSTKIMI juz wybranymi.
    ///
    /// Consequence dochodzi w kroku 6, razem z blackboardem.
    /// </summary>
    public class EventComposer
    {
        private static readonly BlockType[] RequiredSlots = { BlockType.Actor, BlockType.Action, BlockType.Target };
        private static readonly BlockType[] OptionalSlots = { BlockType.Trigger, BlockType.Modifier };

        private readonly List<Block> catalog;
        private readonly CompatibilityGraph graph;

        public EventComposer(IEnumerable<Block> catalog, CompatibilityGraph graph)
        {
            this.catalog = catalog != null ? catalog.ToList() : new List<Block>();
            this.graph = graph ?? new CompatibilityGraph();
        }

        public int CatalogSize
        {
            get { return catalog.Count; }
        }

        /// <summary>
        /// Sklada wydarzenie albo zwraca null, jesli graf nie dopuszcza zadnej
        /// spojnej kombinacji dla tego przepisu.
        /// </summary>
        public ComposedEvent TryCompose(EventRecipe recipe, IRandomSource rng)
        {
            if (recipe == null || rng == null)
            {
                return null;
            }

            var chosen = new List<Block>();
            var trace = new StringBuilder();
            trace.Append(recipe).Append(" | ");

            // Klocek akcji wybieramy pierwszy, bo to on niesie ladunek wydarzenia
            // i najmocniej zawezia reszte kompozycji.
            Block action = PickCandidate(BlockType.Action, recipe.RequiredActionTag, chosen, rng);
            if (action == null)
            {
                return null;
            }
            chosen.Add(action);
            trace.Append("akcja=").Append(action.Id);

            foreach (BlockType slot in RequiredSlots)
            {
                if (slot == BlockType.Action)
                {
                    continue;
                }
                Block block = PickCandidate(slot, null, chosen, rng);
                if (block == null)
                {
                    // Brak zgodnego klocka w wymaganym slocie = wydarzenie niespojne.
                    return null;
                }
                chosen.Add(block);
                trace.Append(", ").Append(slot).Append('=').Append(block.Id);
            }

            foreach (BlockType slot in OptionalSlots)
            {
                Block block = PickCandidate(slot, null, chosen, rng);
                if (block != null)
                {
                    chosen.Add(block);
                    trace.Append(", ").Append(slot).Append('=').Append(block.Id);
                }
            }

            return Assemble(chosen, action, trace.ToString());
        }

        /// <summary>Losuje klocek danego typu, zgodny z tagiem i z juz wybranymi klockami.</summary>
        private Block PickCandidate(BlockType type, string requiredTag, List<Block> chosen, IRandomSource rng)
        {
            List<string> chosenIds = chosen.Select(b => b.Id).ToList();

            List<Block> candidates = catalog
                .Where(b => b.Type == type)
                .Where(b => b.HasTag(requiredTag))
                .Where(b => graph.AllowsWithAll(chosenIds, b.Id))
                .ToList();

            return candidates.Count == 0 ? null : rng.Pick(candidates);
        }

        private static ComposedEvent Assemble(List<Block> chosen, Block action, string trace)
        {
            var ordered = chosen
                .OrderBy(b => SlotOrder(b.Type))
                .ToList();

            float intensity = 1f;
            var description = new StringBuilder();

            foreach (Block block in ordered)
            {
                intensity *= block.IntensityFactor;
                if (!string.IsNullOrEmpty(block.TextFragment))
                {
                    if (description.Length > 0)
                    {
                        description.Append(' ');
                    }
                    description.Append(block.TextFragment);
                }
            }

            return new ComposedEvent
            {
                Blocks = ordered,
                ActionPayload = action.Payload,
                IntensityFactor = intensity,
                Description = description.ToString(),
                Trace = trace + " -> intensywnosc=" + intensity.ToString("0.00")
            };
        }

        private static int SlotOrder(BlockType type)
        {
            switch (type)
            {
                case BlockType.Trigger: return 0;
                case BlockType.Actor: return 1;
                case BlockType.Action: return 2;
                case BlockType.Target: return 3;
                case BlockType.Modifier: return 4;
                default: return 5;
            }
        }
    }
}
