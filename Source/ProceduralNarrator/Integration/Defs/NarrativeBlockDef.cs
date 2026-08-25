using System.Collections.Generic;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;
using Verse;

namespace ProceduralNarrator.Integration.Defs
{
    /// <summary>
    /// Deklaratywna definicja klocka (sekcja 5.8 - warstwa DANYCH).
    /// Rozbudowa tresci = dopisanie kolejnego Defa w XML, bez zmian w logice decyzyjnej.
    ///
    /// UWAGA: wezel XML musi uzywac PELNEJ nazwy typu z namespace'em, czyli
    /// <ProceduralNarrator.Integration.Defs.NarrativeBlockDef>. Sama nazwa klasy jest
    /// po cichu ignorowana przez DirectXmlLoader - patrz sekcja 2 CLAUDE.md.
    /// </summary>
    public class NarrativeBlockDef : Def
    {
        /// <summary>Slot: Trigger / Actor / Action / Target / Modifier / Consequence.</summary>
        public BlockType blockType = BlockType.Action;

        // ---- osie klasyfikacji, czytane przez scoring ----
        public Theme theme = Theme.Natural;
        public Valence valence = Valence.Neutral;
        public EventScale scale = EventScale.Moderate;

        /// <summary>Intencja co do sily: Low / Normal / High. Na punkty tlumaczy IntensityTable.</summary>
        public IntensityLevel intensity = IntensityLevel.Normal;

        /// <summary>Wolne tagi na klimat i warianty. Scoring ich nie uzywa.</summary>
        public List<string> tags = new List<string>();

        /// <summary>Dla klocka akcji: defName incydentu RimWorlda realizujacego ten klocek.</summary>
        public string payload;

        /// <summary>Fragment opisu narracyjnego wnoszony przez ten klocek.</summary>
        public string textFragment;

        /// <summary>defName-y klockow, z ktorymi ten klocek NIE moze wystapic razem.</summary>
        public List<string> incompatibleWith = new List<string>();

        /// <summary>Twarde bramki - klocek niedostepny, gdy ktorakolwiek nie przejdzie.</summary>
        public List<NarrativeCondition> conditions = new List<NarrativeCondition>();

        /// <summary>Miekkie preferencje - nie blokuja, zasilaja contextFit kandydata.</summary>
        public List<NarrativeCondition> preferences = new List<NarrativeCondition>();
    }
}
