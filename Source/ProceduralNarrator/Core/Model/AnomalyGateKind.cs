namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Strona bramy Anomaly (krok 9, etap K0; E-8). Na klocku AKCJI: do ktorej puli incydentow nalezy payload.
    /// W przepisie tury (EventRecipe.AnomalySide): ktora pule tura dopuszcza.
    ///
    /// Lustro gry (RimWorld/StorytellerComp.cs:45-64, Storyteller.cs:75-86): dla kategorii z canUseAnomalyChance
    /// (ThreatBig, ThreatSmall) gra z prawdopodobienstwem AnomalyIncidentChanceNow bierze WYLACZNIE incydenty
    /// Anomaly (IncidentDef.IsAnomalyIncident), a w przeciwnym razie WYLACZNIE pozostale. Kategoria bez tej flagi
    /// (Misc) bierze wszystkie naraz.
    /// </summary>
    public enum AnomalyGateKind
    {
        /// <summary>Klocek: kategoria bez bramy (Misc). Przepis: tura bez filtra.</summary>
        None,

        /// <summary>Zwykly incydent zagrozenia (brama dotyczy, IsAnomalyIncident == false).</summary>
        Regular,

        /// <summary>Incydent Anomaly (IsAnomalyIncident == true).</summary>
        Anomaly
    }

    /// <summary>Regula dopuszczania (w modelu, bo korzysta z niej kompozycja; losowanie strony - Decision/AnomalyGate).</summary>
    public static class AnomalyGateRules
    {
        /// <summary>Czy przepis tury dopuszcza klocek o danej bramie. Klocek bez bramy przechodzi zawsze.</summary>
        public static bool Allows(AnomalyGateKind side, AnomalyGateKind blockGate)
        {
            if (side == AnomalyGateKind.None || blockGate == AnomalyGateKind.None)
            {
                return true;
            }
            return side == blockGate;
        }
    }
}
