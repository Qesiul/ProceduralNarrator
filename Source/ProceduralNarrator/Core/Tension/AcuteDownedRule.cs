namespace ProceduralNarrator.Core.Tension
{
    /// <summary>
    /// Stan zdrowia jednego pionka, ktory warstwa integracji odczytuje z gry dla reguly "powalony ostro".
    /// Pola sa FAKTAMI z gry; decyzja, co z nich wynika, zapada w AcuteDownedRule (testowalna offline).
    /// </summary>
    public struct PawnAcuteFlags
    {
        /// <summary>Pawn.Downed.</summary>
        public bool Downed;

        /// <summary>Stan trwaly (niemowle - LifeStageUtility.AlwaysDowned) - nigdy ostry.</summary>
        public bool AlwaysDowned;

        /// <summary>Pawn_HealthTracker.InPainShock.</summary>
        public bool InPainShock;

        /// <summary>HediffSet.BleedRateTotal &gt; 0.</summary>
        public bool Bleeding;

        /// <summary>Pawn_HealthTracker.HasHediffsNeedingTend().</summary>
        public bool NeedsTend;

        /// <summary>Porod w toku (Biotech: PregnancyLabor albo PregnancyLaborPushing).</summary>
        public bool InLabor;

        /// <summary>
        /// Najwiekszy stosunek Severity / lethalSeverity po stanach z progiem smierci (lethalSeverity &gt; 0):
        /// BEZ chorob przewleklych: hipotermia, udar cieplny, zatrucie toksynami, niedozywienie... 0, gdy zadnego nie ma.
        /// </summary>
        public float MaxLethalFraction;
    }

    /// <summary>
    /// "POWALONY OSTRO" - czy pionek lezy w sytuacji, ktora wymaga pomocy TERAZ (krok 8, dlug 12, decyzja
    /// autora K8-8). Licznik zasila czlon sytuacyjny napiecia i predykat kryzysu skrajnego (CrisisDetector).
    ///
    /// Pionek powalony (i nie trwale - niemowle) jest liczony, gdy:
    ///   - krwawi albo ma cos do opatrzenia (rany, choroby) - takze przy porodzie;
    ///   - jest w szoku bolowym, ALE NIE z powodu porodu (bol porodowy 0.85 przekracza prog szoku 0.8;
    ///     w kolonii dwuosobowej porod wlaczal kryzys skrajny - to nie nagly wypadek, decyzja K8-8);
    ///   - ma stan z progiem smierci osiagniety w co najmniej lethalFraction (0,5): hipotermia, udar cieplny,
    ///     zatrucie toksynami i kazdy podobny - bez wyliczania nazw. Tych stanow NIE widzialo dawne kryterium
    ///     (brak szoku, krwi i czegokolwiek do opatrzenia), choc pionek umiera. Choroby PRZEWLEKLE (blokada
    ///     tetnicy, rozpad narzadow - HediffDef.chronic) nie wchodza do MaxLethalFraction (przeglad S10): nie sa
    ///     nagla sytuacja, a trwale powalony z taka choroba bylby liczony zawsze.
    /// Stany trwale po opatrzeniu (brak nog, abazja, spiaczka) dalej nie sa liczone - nie spelniaja zadnego
    /// warunku, chyba ze dojdzie do nich cos ostrego.
    /// </summary>
    public static class AcuteDownedRule
    {
        public static bool IsAcute(PawnAcuteFlags f, float lethalFraction)
        {
            if (!f.Downed || f.AlwaysDowned)
            {
                return false;
            }
            if (f.Bleeding || f.NeedsTend)
            {
                return true;
            }
            if (f.InPainShock && !f.InLabor)
            {
                return true;
            }
            return lethalFraction > 0f && f.MaxLethalFraction >= lethalFraction;
        }
    }
}
