using System;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// BRAMA ANOMALY (krok 9, etap K0; decyzje E-8 i E0-2) - odwzorowanie tego, jak gra rozdziela zagrozenia miedzy
    /// pule Anomaly i pozostala (patrz AnomalyGateKind).
    ///
    /// Losowanie: RAZ na ture, na WLASNYM ziarnie wyprowadzonym z ticku (i soli powtorzenia symulatora). Nie zuzywa
    /// losowan generatora ani wyboru (rngGen, rngSel), wiec nie przesuwa ich strumieni ani niezmiennika
    /// "rundy = (losowan - 1) / 2". Bez DLC Anomaly - zawsze strona zwykla, bez losowania (StorytellerComp.cs:51).
    ///
    /// Roznica wobec gry (do ograniczen): gra losuje osobno w kazdym compie kategorii (ThreatBig, ThreatSmall),
    /// my raz na ture dla obu. Tura po stronie Anomaly bez dostepnej akcji Anomaly zostawia tylko akcje bez bramy
    /// (Misc) - tak jak w grze comp zagrozen w takim losowaniu nic nie odpala.
    /// </summary>
    public static class AnomalyGate
    {
        /// <summary>Sol ziarna bramy ("ANOM"), zeby strumien bramy nie pokrywal sie z innymi ziarnami z ticku.</summary>
        public const int Salt = 0x414E4F4D;

        /// <summary>Rozdzielczosc losowania prawdopodobienstwa.</summary>
        public const int Resolution = 1000000;

        /// <summary>Strona tury. seedSalt = 0 w grze; symulator podaje sol powtorzenia.</summary>
        public static AnomalyGateKind Draw(WorldSnapshot snapshot, int tick, int seedSalt)
        {
            if (snapshot == null || !snapshot.AnomalyActive)
            {
                return AnomalyGateKind.Regular;
            }
            float p = snapshot.AnomalyIncidentChance;
            if (float.IsNaN(p) || p <= 0f)
            {
                return AnomalyGateKind.Regular;
            }
            if (p >= 1f)
            {
                return AnomalyGateKind.Anomaly;
            }
            int prog = (int)Math.Round(p * Resolution);
            return new SeededRandom(Seed(tick, seedSalt)).Next(Resolution) < prog ? AnomalyGateKind.Anomaly : AnomalyGateKind.Regular;
        }

        /// <summary>Ziarno bramy z ticku i soli (mieszanie Avalanche, nie tick + sol).</summary>
        public static int Seed(int tick, int seedSalt)
        {
            unchecked
            {
                return SeededRandom.Avalanche(SeededRandom.Avalanche(tick ^ Salt) ^ seedSalt);
            }
        }
    }
}
