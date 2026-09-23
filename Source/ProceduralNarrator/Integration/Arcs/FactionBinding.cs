using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Model;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration.Arcs
{
    /// <summary>
    /// Czy frakcja moze TERAZ poprowadzic napad (krok 5, ciaglosc frakcji luku Wendeta).
    ///
    /// DLACZEGO WLASNY FILTR. Z ustawionym parms.faction waniliowe IncidentWorker_PawnsArrive
    /// .CanFireNowSub zwraca true bez sprawdzania kandydatow, a IncidentWorker_RaidEnemy
    /// .TryResolveRaidFaction zatrzymuje kazda WROGA frakcje. Wroga, ale bezuzyteczna frakcja
    /// (bez grupy Combat, za malo punktow, zla temperatura) konczy sie wtedy nieudanym napadem
    /// i bledami w logu gry. Odtwarzamy wiec PELNY waniliowy filtr zrodla napadu w wariancie
    /// NIE-desperackim (dekompilacja 1.5.4063):
    ///   PawnsArrive.FactionCanBeGroupSource: !IsPlayer, !defeated, !temporary, temperatura
    ///       przybycia obejmuje OutdoorTemp i SeasonalTemp mapy;
    ///   RaidEnemy.FactionCanBeGroupSource: HostileTo(gracz), DaysPassedSinceSettle >= earliestRaidDays;
    ///   PawnGroupMakerUtility.UsableFactions: grupa Combat, !raidsForbidden,
    ///       punkty >= MinPointsToGeneratePawnGroup(Combat).
    /// Przeszkoda chwilowa (temperatura, punkty, dni) = frakcja w tej turze nieuzyteczna i luk
    /// czeka; przeszkoda trwala (niewroga, pokonana, zniknela) = straznik skreca luk.
    /// </summary>
    internal static class FactionBinding
    {
        public static bool UsableForRaid(Faction f, Map map, float points, out string why)
        {
            why = null;
            if (f == null) { why = "brak frakcji"; return false; }
            if (f.IsPlayer) { why = "gracz"; return false; }
            if (f.defeated) { why = "pokonana"; return false; }
            if (f.temporary) { why = "tymczasowa"; return false; }
            if (Faction.OfPlayer == null || !f.HostileTo(Faction.OfPlayer)) { why = "niewroga"; return false; }
            if (f.def == null) { why = "brak Defa"; return false; }
            if (map != null && map.mapTemperature != null
                && (!f.def.allowedArrivalTemperatureRange.Includes(map.mapTemperature.OutdoorTemp)
                    || !f.def.allowedArrivalTemperatureRange.Includes(map.mapTemperature.SeasonalTemp)))
            {
                why = "temperatura przybycia";
                return false;
            }
            if (GenDate.DaysPassedSinceSettle < f.def.earliestRaidDays) { why = "za wczesnie"; return false; }
            if (f.def.pawnGroupMakers == null || !f.def.pawnGroupMakers.Any(x => x.kindDef == PawnGroupKindDefOf.Combat))
            {
                why = "brak grupy Combat";
                return false;
            }
            if (f.def.raidsForbidden) { why = "napady zakazane"; return false; }
            if (points < f.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat)) { why = "za malo punktow"; return false; }
            return true;
        }

        /// <summary>
        /// EMULACJA w symulatorze (decyzja autora R4-4): symulator nie wykonuje incydentow, wiec
        /// frakcja napadu nigdy sie nie ustala. Wybor DETERMINISTYCZNY - pierwsza uzyteczna frakcja
        /// wedlug loadID - bez Verse.Rand i bez udawania waniliowych wag. Oznaczana w [PN-EXEC]
        /// (frakcjaZrodlo=emulacja); prawdziwosc wyboru frakcji sprawdza tylko gra.
        /// </summary>
        public static Faction EmulatedRaidFaction(Map map, float points)
        {
            if (Find.FactionManager == null)
            {
                return null;
            }
            List<Faction> wszystkie = Find.FactionManager.AllFactionsListForReading;
            string why;
            return wszystkie.Where(f => f != null).OrderBy(f => f.loadID)
                            .FirstOrDefault(f => UsableForRaid(f, map, points, out why));
        }
    }

    /// <summary>
    /// Implementacja IFactionUsability dla jednej tury: punkty bazowe tury (te same co sito
    /// przed scoringiem) razy mnoznik mocy kandydata - dokladnie to, co trafi do parms.points.
    /// </summary>
    internal sealed class FactionUsability : IFactionUsability
    {
        private readonly Map map;
        private readonly float basePoints;

        public FactionUsability(Map map, float basePoints)
        {
            this.map = map;
            this.basePoints = basePoints;
        }

        public bool UsableAt(string factionId, IntensityLevel intensity)
        {
            Faction f = ArcObservationBuilder.ResolveFaction(factionId);
            string why;
            return f != null && FactionBinding.UsableForRaid(f, map, basePoints * IntensityTable.PointsFactor(intensity), out why);
        }
    }
}
