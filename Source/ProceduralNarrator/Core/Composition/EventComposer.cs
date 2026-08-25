using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Composition
{
    /// <summary>
    /// Warstwa kompozycji (sekcja 5.7). Sklada konkretne wydarzenie z klockow
    /// zgodnie z przepisem, grafem kompatybilnosci i twardymi warunkami kontekstu.
    ///
    /// Sloty w kolejnosci narracyjnej:
    ///     Trigger (opcjonalny) -> Actor -> Action -> Target -> Modifier (opcjonalny)
    /// Kazdy kolejny klocek musi byc zgodny ze WSZYSTKIMI juz wybranymi.
    ///
    /// Dobor klockow jest LOSOWY sposrod dopuszczalnych - kontekst wchodzi dopiero
    /// przy ocenie gotowego kandydata (ContextEvaluator), zgodnie z podzialem warstw
    /// z koncepcji: kompozycja sklada, warstwa decyzyjna ocenia.
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
        /// Sklada wydarzenie albo zwraca null, jesli graf i warunki nie dopuszczaja
        /// zadnej spojnej kombinacji. Snapshot moze byc null - wtedy twarde warunki
        /// nie filtruja (przydatne w testach samej kompozycji).
        /// </summary>
        public ComposedEvent TryCompose(EventRecipe recipe, IRandomSource rng, WorldSnapshot snapshot)
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
            Block action = PickCandidate(BlockType.Action, recipe.RequiredActionTag, chosen, rng, snapshot);
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
                Block block = PickCandidate(slot, null, chosen, rng, snapshot);
                if (block == null)
                {
                    // Brak dopuszczalnego klocka w wymaganym slocie = wydarzenie niespojne.
                    return null;
                }
                chosen.Add(block);
                trace.Append(", ").Append(slot).Append('=').Append(block.Id);
            }

            foreach (BlockType slot in OptionalSlots)
            {
                Block block = PickCandidate(slot, null, chosen, rng, snapshot);
                if (block != null)
                {
                    chosen.Add(block);
                    trace.Append(", ").Append(slot).Append('=').Append(block.Id);
                }
            }

            ComposedEvent composed = Assemble(chosen, action, trace.ToString());
            ContextEvaluator.Evaluate(composed, snapshot);
            return composed;
        }

        /// <summary>Losuje klocek danego typu: zgodny z tagiem, grafem i twardymi warunkami.</summary>
        private Block PickCandidate(BlockType type, string requiredTag, List<Block> chosen,
                                    IRandomSource rng, WorldSnapshot snapshot)
        {
            List<string> chosenIds = chosen.Select(b => b.Id).ToList();

            List<Block> candidates = catalog
                .Where(b => b.Type == type)
                .Where(b => b.HasTag(requiredTag))
                .Where(b => b.IsAvailable(snapshot))
                .Where(b => graph.AllowsWithAll(chosenIds, b.Id))
                .ToList();

            return candidates.Count == 0 ? null : rng.Pick(candidates);
        }

        private static ComposedEvent Assemble(List<Block> chosen, Block action, string trace)
        {
            List<Block> ordered = chosen
                .OrderBy(b => SlotOrder(b.Type))
                .ToList();

            var description = new StringBuilder();
            foreach (Block block in ordered)
            {
                if (!string.IsNullOrEmpty(block.TextFragment))
                {
                    if (description.Length > 0)
                    {
                        description.Append(' ');
                    }
                    description.Append(block.TextFragment);
                }
            }

            IntensityLevel intensity = ContextEvaluator.AggregateIntensity(ordered);

            return new ComposedEvent
            {
                Blocks = ordered,
                ActionPayload = action.Payload,
                // Charakter wydarzenia dziedziczymy po klocku akcji - to on decyduje,
                // czym zdarzenie JEST; pozostale sloty je doprecyzowuja.
                Theme = action.Theme,
                Valence = action.Valence,
                Scale = action.Scale,
                Intensity = intensity,
                Description = description.ToString(),
                Trace = trace + " -> intensywnosc=" + intensity
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
