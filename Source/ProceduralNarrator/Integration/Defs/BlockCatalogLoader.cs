using System.Collections.Generic;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Model;
using Verse;

namespace ProceduralNarrator.Integration.Defs
{
    /// <summary>
    /// Adapter danych: tlumaczy NarrativeBlockDef (typ RimWorlda) na Block (typ rdzenia).
    /// To tutaj konczy sie zaleznosc od gry - dalej plyna juz tylko czyste struktury.
    /// </summary>
    public static class BlockCatalogLoader
    {
        public static void Load(out List<Block> blocks, out CompatibilityGraph graph)
        {
            blocks = new List<Block>();
            graph = new CompatibilityGraph();

            List<NarrativeBlockDef> defs = DefDatabase<NarrativeBlockDef>.AllDefsListForReading;

            foreach (NarrativeBlockDef def in defs)
            {
                var block = new Block
                {
                    Id = def.defName,
                    Type = def.blockType,
                    Payload = def.payload,
                    IntensityFactor = def.intensityFactor,
                    TextFragment = def.textFragment
                };

                if (def.tags != null)
                {
                    foreach (string tag in def.tags)
                    {
                        block.Tags.Add(tag);
                    }
                }

                blocks.Add(block);

                if (def.incompatibleWith != null)
                {
                    foreach (string otherId in def.incompatibleWith)
                    {
                        graph.Forbid(def.defName, otherId);
                    }
                }
            }
        }
    }
}
