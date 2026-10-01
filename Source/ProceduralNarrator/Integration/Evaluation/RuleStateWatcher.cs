using System.Collections.Generic;
using ProceduralNarrator.Core.Evaluation;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration.Evaluation
{
    /// <summary>
    /// [PN-STAN] - ekspozycja map domowych na warunki regul RN (krok 9, etap L, PLAN_EWALUACJI.md 5-6), w kazdej grze.
    /// Probka co 250 tickow na siatce zegara (pierwsze wywolanie = kotwica), odczyty te same co w [PN-DZIEN] i [PN-FIRED]:
    /// kolonisci na mapie, powaleni ostro, GenHostility. Kiedy pisac linie, rozstrzyga Core (RuleStateTracker).
    /// </summary>
    internal sealed class RuleStateWatcher
    {
        public const int SampleTicks = 250;

        private readonly RuleStateTracker stan = new RuleStateTracker();
        private bool zakotwiczony;

        public void Tick(int tick)
        {
            if (zakotwiczony && tick % SampleTicks != 0)
            {
                return;
            }
            zakotwiczony = true;
            var mapy = new List<Map>();
            if (Find.Maps != null)
            {
                foreach (Map m in Find.Maps)
                {
                    if (m != null && m.IsPlayerHome)
                    {
                        mapy.Add(m);
                    }
                }
            }
            mapy.Sort((a, b) => a.uniqueID.CompareTo(b.uniqueID));
            var obecne = new List<int>();
            for (int i = 0; i < mapy.Count; i++)
            {
                Map m = mapy[i];
                obecne.Add(m.uniqueID);
                int naMapie = WorldSnapshotBuilder.CountColonistsOnMap(m);
                int powaleni = WorldSnapshotBuilder.CountAcutelyDownedColonists(m);
                RuleState s = RuleStateTracker.Of(naMapie, powaleni, GenHostility.AnyHostileActiveThreatToPlayer(m));
                string zdarzenie = stan.Update(m.uniqueID, s);
                if (zdarzenie != null)
                {
                    PNLog.State(tick, m.uniqueID, zdarzenie, naMapie, powaleni, s);
                }
            }
            List<int> koniec = stan.Ended(obecne);
            for (int i = 0; i < koniec.Count; i++)
            {
                PNLog.StateEnd(tick, koniec[i]);
            }
        }

        /// <summary>Nastepny przeglad bedzie kotwica ze "start" dla kazdej mapy (start gry ewaluacyjnej).</summary>
        public void Reset()
        {
            stan.Reset();
            zakotwiczony = false;
        }
    }
}
