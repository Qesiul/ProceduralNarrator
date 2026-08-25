using System.Collections.Generic;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Model;
using Verse;

namespace ProceduralNarrator.Integration.Defs
{
    /// <summary>
    /// Adapter danych: tlumaczy NarrativeBlockDef (typ RimWorlda) na Block (typ rdzenia).
    /// Tutaj konczy sie zaleznosc od gry - dalej plyna juz tylko czyste struktury.
    /// Obiekty warunkow przechodza przez referencje, bo same z siebie sa typami z Core.
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
                    Theme = def.theme,
                    Valence = def.valence,
                    Scale = def.scale,
                    Intensity = def.intensity,
                    Payload = def.payload,
                    TextFragment = def.textFragment
                };

                if (def.tags != null)
                {
                    foreach (string tag in def.tags)
                    {
                        block.Tags.Add(tag);
                    }
                }

                if (def.conditions != null)
                {
                    block.Conditions.AddRange(def.conditions);
                }

                if (def.preferences != null)
                {
                    block.Preferences.AddRange(def.preferences);
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
