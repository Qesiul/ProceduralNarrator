using System.Collections.Generic;
using ProceduralNarrator.Core.Evaluation;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration.Evaluation
{
    /// <summary>
    /// [PN-KOLONISTA] - zmiany skladu wolnych kolonistow (krok 9, etap L, decyzja L-5), w kazdej grze. Zbior ze wszystkich
    /// map, karawan i kapsul co 250 tickow (siatka zegara jak obserwator stylu); pierwszy przeglad = kotwica. Diff i kolejnosc
    /// licza sie w Core (RosterTracker); tu tylko odczyt gry i rodzaj ubytku z ostatniego znanego stanu pionka.
    /// </summary>
    internal sealed class ColonistWatcher
    {
        public const int SampleTicks = 250;

        private readonly RosterTracker sklad = new RosterTracker();
        private readonly Dictionary<int, Pawn> pionki = new Dictionary<int, Pawn>();
        private readonly List<RosterMember> czlonkowie = new List<RosterMember>();

        public void Tick(int tick)
        {
            if (sklad.Anchored && tick % SampleTicks != 0)
            {
                return;
            }
            czlonkowie.Clear();
            var obecni = new Dictionary<int, Pawn>();
            foreach (Pawn p in PawnsFinder.AllMapsCaravansAndTravelingTransportPods_Alive_FreeColonists)
            {
                if (p == null || obecni.ContainsKey(p.thingIDNumber))
                {
                    continue;
                }
                obecni[p.thingIDNumber] = p;
                Map m = p.MapHeld;
                czlonkowie.Add(new RosterMember(p.thingIDNumber, m == null ? -1 : m.uniqueID));
            }
            List<RosterEvent> zmiany = sklad.Update(czlonkowie, id =>
            {
                Pawn p;
                return pionki.TryGetValue(id, out p) ? Rodzaj(p) : null;
            });
            pionki.Clear();
            foreach (KeyValuePair<int, Pawn> kv in obecni)
            {
                pionki[kv.Key] = kv.Value;
            }
            for (int i = 0; i < zmiany.Count; i++)
            {
                PNLog.Colonist(tick, zmiany[i]);
            }
        }

        /// <summary>Nastepny przeglad bedzie kotwica (start gry ewaluacyjnej).</summary>
        public void Reset()
        {
            sklad.Reset();
            pionki.Clear();
        }

        /// <summary>Rodzaj ubytku: smierc, porwanie, niewola innej frakcji, zdziczenie (bez frakcji, na mapie), odejscie.</summary>
        private static string Rodzaj(Pawn p)
        {
            if (p == null)
            {
                return RosterTracker.Other;
            }
            if (p.Dead)
            {
                return RosterTracker.Died;
            }
            if (Find.FactionManager != null)
            {
                foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
                {
                    if (f != null && f.kidnapped != null && f.kidnapped.KidnappedPawnsListForReading.Contains(p))
                    {
                        return RosterTracker.Kidnapped;
                    }
                }
            }
            if ((p.IsPrisoner || p.IsSlave) && p.HostFaction != Faction.OfPlayer)
            {
                return RosterTracker.Imprisoned;
            }
            if (p.Faction == null)
            {
                return p.Spawned ? RosterTracker.WentWild : RosterTracker.Left;
            }
            if (p.Faction != Faction.OfPlayer)
            {
                return RosterTracker.Left;
            }
            return RosterTracker.Other;
        }
    }
}
