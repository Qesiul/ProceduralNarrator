using System.Collections.Generic;

namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Wydarzenie zlozone z klockow - wyjscie warstwy kompozycji, wejscie warstwy decyzyjnej.
    /// </summary>
    public class ComposedEvent
    {
        /// <summary>Uporzadkowany zbior klockow, z ktorych powstalo wydarzenie.</summary>
        public List<Block> Blocks = new List<Block>();

        /// <summary>Zagregowany ladunek klocka akcji (dla integracji: defName incydentu).</summary>
        public string ActionPayload;

        /// <summary>Osie odziedziczone po klocku akcji - on definiuje charakter wydarzenia.</summary>
        public Theme Theme;
        public Valence Valence;
        public EventScale Scale;

        /// <summary>Wypadkowa sila zdarzenia. Integration tlumaczy ja na punkty incydentu.</summary>
        public IntensityLevel Intensity = IntensityLevel.Normal;

        /// <summary>
        /// Dopasowanie kontekstowe 0..1 - jak bardzo to wydarzenie pasuje tu i teraz.
        /// Liczone na GOTOWYM kandydacie (decyzja projektowa), zasila scoring w kroku 3.
        /// </summary>
        public float ContextFit = 1f;

        /// <summary>Zlozony tekst narracyjny.</summary>
        public string Description;

        /// <summary>Slad kompozycji - do logu decyzji i ewaluacji (sekcja 12).</summary>
        public string Trace;

        /// <summary>Rozbicie contextFit na skladniki - wymagany slad scoringu.</summary>
        public string FitTrace;
    }
}
