using System.Collections.Generic;
using ProceduralNarrator.Core.Model;
using Verse;

namespace ProceduralNarrator.Integration.Defs
{
    /// <summary>
    /// Deklaratywna definicja klocka (sekcja 5.8 - warstwa DANYCH).
    /// Rozbudowa tresci = dopisanie kolejnego NarrativeBlockDef w XML,
    /// bez dotykania logiki decyzyjnej.
    /// </summary>
    public class NarrativeBlockDef : Def
    {
        /// <summary>Typ klocka: Trigger / Actor / Action / Target / Modifier / Consequence.</summary>
        public BlockType blockType = BlockType.Action;

        /// <summary>Tagi opisujace klocek (militarny, ekonomiczny, nadprzyrodzony...).</summary>
        public List<string> tags = new List<string>();

        /// <summary>Dla klocka akcji: defName incydentu RimWorlda, ktory realizuje ten klocek.</summary>
        public string payload;

        /// <summary>Wklad w intensywnosc zlozonego wydarzenia (mnoznik punktow).</summary>
        public float intensityFactor = 1f;

        /// <summary>Fragment opisu narracyjnego wnoszony przez ten klocek.</summary>
        public string textFragment;

        /// <summary>defName-y klockow, z ktorymi ten klocek NIE moze wystapic razem.</summary>
        public List<string> incompatibleWith = new List<string>();
    }
}
