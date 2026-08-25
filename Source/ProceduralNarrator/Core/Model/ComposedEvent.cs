using System.Collections.Generic;

namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Wydarzenie zlozone z klockow - wyjscie warstwy kompozycji, wejscie warstwy integracji.
    /// </summary>
    public class ComposedEvent
    {
        /// <summary>Uporzadkowany zbior klockow, z ktorych powstalo wydarzenie.</summary>
        public List<Block> Blocks = new List<Block>();

        /// <summary>Zagregowany ladunek klocka akcji (dla integracji: defName incydentu).</summary>
        public string ActionPayload;

        /// <summary>Iloczyn wkladow intensywnosci wszystkich klockow.</summary>
        public float IntensityFactor = 1f;

        /// <summary>Zlozony tekst narracyjny.</summary>
        public string Description;

        /// <summary>Slad kompozycji - do logu decyzji i ewaluacji (sekcja 12).</summary>
        public string Trace;
    }
}
