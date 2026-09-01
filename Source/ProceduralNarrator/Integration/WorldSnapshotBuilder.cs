using System.Linq;
using ProceduralNarrator.Core.Model;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration
{
    /// <summary>
    /// Tlumaczy stan gry na WorldSnapshot - jedyne miejsce, w ktorym warunki z Core
    /// stykaja sie posrednio z API RimWorlda. Snapshot jest budowany RAZ na decyzje
    /// i zamrozony, dzieki czemu decyzja jest odtwarzalna i w calosci logowalna.
    /// </summary>
    public static class WorldSnapshotBuilder
    {
        /// <summary>
        /// Buduje zrzut stanu swiata. Historia i biezacy dzien sa potrzebne dla pola
        /// DaysSinceLastEvent - wielkosci, ktora pochodzi z pamieci narratora, a nie z gry,
        /// ale MUSI trafic do snapshotu, bo warunki twarde widza wylacznie jego.
        /// </summary>
        public static WorldSnapshot Build(Map map, EventHistory history, float gameDay)
        {
            WorldSnapshot snapshot = Build(map);

            // PASS nie przerywa spokoju, wiec liczymy od ostatniego WYDARZENIA (Newest pomija
            // decyzje o ciszy - bufor ich nie zawiera). Pusta historia: spokoj trwa od zalozenia.
            EventHistoryEntry ostatnie = history != null ? history.Newest : null;
            snapshot.DaysSinceLastEvent = ostatnie == null
                ? gameDay
                : System.Math.Max(0f, gameDay - ostatnie.GameDay);

            return snapshot;
        }

        public static WorldSnapshot Build(Map map)
        {
            if (map == null)
            {
                return new WorldSnapshot();
            }

            int hour = GenLocalDate.HourOfDay(map);
            int dni = GenDate.DaysPassedSinceSettle;

            // PlayerWealthForStoryteller, a NIE WealthTotal - to pierwsze jest miara,
            // ktorej uzywa sam narrator gry (budynki licza sie w polowie, dochodzi
            // ekwipunek kolonistow i zwierzeta). Patrz sekcja o bogactwie w CLAUDE.md.
            float bogactwo = map.PlayerWealthForStoryteller;

            return new WorldSnapshot
            {
                DaysPassed = dni,
                ColonistCount = map.mapPawns.FreeColonistsCount,
                ColonyWealth = bogactwo,
                WealthRelative = WealthReference.Relative(bogactwo, dni),
                MountainRoofCellsNearColony = MountainRoofNearColony(map),
                HasHostileFaction = HasHostileFaction(),
                Season = SeasonIndex(GenLocalDate.Season(map)),
                IsNight = hour < 6 || hour >= 18,
                WildAnimalCount = CountWildAnimals(map)
            };
        }

        /// <summary>Promien wokol srodka kolonii, w ktorym szukamy gorskiego stropu.</summary>
        private const float MountainScanRadius = 25f;

        /// <summary>
        /// Liczy komorki z grubym stropem w poblizu kolonii.
        ///
        /// Sroddek bierzemy z obszaru domowego, bo to on wyznacza, gdzie kolonia faktycznie
        /// mieszka. Skan promieniowy zamiast calej mapy: interesuje nas, czy kolonia STOI
        /// pod gora, a nie czy gora w ogole gdzies istnieje. Ok. 2000 komorek, liczone
        /// najwyzej raz na 1000 tickow - koszt pomijalny.
        /// </summary>
        private static int MountainRoofNearColony(Map map)
        {
            Area home = map.areaManager != null ? map.areaManager.Home : null;
            if (home == null || home.TrueCount == 0)
            {
                return 0;
            }

            long sumX = 0;
            long sumZ = 0;
            int n = 0;
            foreach (IntVec3 cell in home.ActiveCells)
            {
                sumX += cell.x;
                sumZ += cell.z;
                n++;
            }
            if (n == 0)
            {
                return 0;
            }

            var center = new IntVec3((int)(sumX / n), 0, (int)(sumZ / n));

            int count = 0;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, MountainScanRadius, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                RoofDef roof = map.roofGrid.RoofAt(cell);
                if (roof != null && roof.isThickRoof)
                {
                    count++;
                }
            }
            return count;
        }

        private static bool HasHostileFaction()
        {
            Faction player = Faction.OfPlayer;
            if (player == null || Find.FactionManager == null)
            {
                return false;
            }
            return Find.FactionManager.AllFactions
                .Any(f => !f.defeated && !f.IsPlayer && !f.temporary && f.HostileTo(player));
        }

        private static int CountWildAnimals(Map map)
        {
            int n = 0;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.Faction == null && !p.Dead && p.RaceProps != null && p.RaceProps.Animal)
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>Waniliowy enum Season na nasza skale 0=wiosna, 1=lato, 2=jesien, 3=zima.</summary>
        private static int SeasonIndex(Season season)
        {
            switch (season)
            {
                case Season.Spring: return 0;
                case Season.Summer: return 1;
                case Season.PermanentSummer: return 1;
                case Season.Fall: return 2;
                case Season.Winter: return 3;
                case Season.PermanentWinter: return 3;
                default: return 0;
            }
        }
    }
}
