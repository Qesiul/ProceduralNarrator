using System.Collections.Generic;
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
                WildAnimalCount = CountWildAnimals(map),
                DownedColonistCount = CountDownedColonists(map),
                Danger = MapDanger(map),
                // Ta sama wielkosc, ktora bazowy IncidentWorker.CanFireNow porownuje
                // z def.minThreatPoints. Liczona RAZ na ture (snapshot jest zamrazany),
                // a nie raz na kandydata - DefaultThreatPointsNow obchodzi wszystkie pionki
                // gracza, wiec 84 wywolania na ture bylyby marnotrawstwem.
                ThreatPoints = StorytellerUtility.DefaultThreatPointsNow(map),
                KidnappedColonistCount = CountKidnappedColonists(),
                HasPoweredCommsConsole = CommsConsoleUtility.PlayerHasPoweredCommsConsole(map)
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

        /// <summary>
        /// Odwzorowuje RimWorld.IncidentWorker_RansomDemand.RandomKidnappedColonist(): pionki
        /// humanoidalne frakcji gracza przetrzymywane przez dowolna frakcje.
        ///
        /// Waniliowy worker odejmuje jeszcze tych, dla ktorych list z zadaniem okupu juz wisi
        /// w skrzynce (ChoiceLetter_RansomDemand). Swiadomie tego NIE odwzorowujemy: to stan
        /// interfejsu, a nie swiata, a snapshot ma opisywac swiat. Skutkiem jest warunek
        /// nieznacznie luzniejszy od workera - w takiej sytuacji CanFireNow odrzuci kandydata
        /// w normalnym trybie, a petla rund wezmie kolejnego. Tekst pozostaje przy tym prawdziwy,
        /// bo porwany faktycznie istnieje.
        /// </summary>
        private static int CountKidnappedColonists()
        {
            int n = 0;
            Faction gracz = Faction.OfPlayer;
            if (gracz == null || Find.FactionManager == null)
            {
                return 0;
            }

            List<Faction> frakcje = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < frakcje.Count; i++)
            {
                if (frakcje[i].kidnapped == null)
                {
                    continue;
                }
                // Liczymy WYLACZNIE porwanych trzymanych przez frakcje WROGA. Waniliowy worker
                // wrogosci nie sprawdza (FactionWhichKidnapped moze byc neutralna), ale zlozony
                // tekst brzmi "Wroga grupa zbrojna zada okupu za porwanego czlonka kolonii" -
                // a Cond_HostileFaction gwarantuje tylko, ze JAKAS wroga frakcja istnieje, niekoniecznie
                // ta sama, ktora porwala. Bez tego filtra zdanie nadal potrafiloby byc falszywe,
                // tym razem co do wrogosci zadajacego okup, a nie co do nazwy frakcji.
                // Warunek jest przez to OSTRZEJSZY od workera, co jest bezpieczne: nigdy nie
                // wyprodukuje kandydata, ktoremu silnik odmowi z tego powodu.
                if (!frakcje[i].HostileTo(gracz))
                {
                    continue;
                }
                List<Pawn> porwani = frakcje[i].kidnapped.KidnappedPawnsListForReading;
                for (int j = 0; j < porwani.Count; j++)
                {
                    Pawn p = porwani[j];
                    if (p != null && p.Faction == Faction.OfPlayer && p.RaceProps != null && p.RaceProps.Humanlike)
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        private static bool HasHostileFaction()
        {
            Faction player = Faction.OfPlayer;
            if (player == null || Find.FactionManager == null)
            {
                return false;
            }
            // GetFactions(...) zamiast AllFactions - ten sam wzorzec co przy Cond_MountainRoof:
            // warunek twardy ma odwzorowywac to, co NAPRAWDE moze sie zdarzyc, a nie flage ozdobna.
            //
            // AllFactions zawiera frakcje UKRYTE (FactionManager.AllFactionsVisible filtruje je
            // dopiero osobno). Wsrod nich sa Mechanoid i HoraxCult - obie permanentEnemy=true
            // i requiredCountAtGameStart=1, wiec obecne w KAZDEJ rozgrywce od ticku 0. Na tej
            // liscie flaga byla wiec praktycznie zawsze prawdziwa, niezaleznie od tego, czy
            // istnieje ktokolwiek, kogo klocek PN_Aktor_Piraci moglby opisac jako "wroga grupe
            // zbrojna", i czy RaidEnemy ma z kogo zlozyc napad (mechanoidy maja earliestRaidDays).
            //
            // Uwaga na marginesie: NIE jest prawda, ze gwarantowane frakcje wrogie sa wylacznie
            // nieludzkie - Pirate tez ma requiredCountAtGameStart=1 i permanentEnemy=true, jest
            // widoczna i humanlikeFaction (domyslnie true). Zawezenie zmienia wiec niewiele
            // w typowej rozgrywce; robimy je dlatego, ze warunek ma znaczyc DOKLADNIE to, co glosi
            // tekst klocka, a nie "cokolwiek wrogiego istnieje gdzies w swiecie".
            return Find.FactionManager
                       .GetFactions(allowHidden: false, allowDefeated: false,
                                    allowNonHumanlike: false, allowTemporary: false)
                       .Any(f => f.HostileTo(player));
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

        /// <summary>
        /// Ilu kolonistow lezy powalonych. Sygnal dla krzywej dramaturgicznej (krok 4);
        /// zaden warunek twardy tego nie czyta.
        ///
        /// FreeColonistsSpawned, a NIE FreeColonistsCount: liczymy tych, ktorzy sa NA TEJ MAPIE,
        /// bo napiecie jest wielkoscia lokalna dla kolonii. Kolonista lezacy w karawanie po
        /// drugiej stronie planety nie opisuje sytuacji tej osady.
        ///
        /// UWAGA NA MIANOWNIK: WorldSnapshot.ColonistCount pochodzi z FreeColonistsCount, ktore
        /// powalonych LICZY (Downed jest stanem, nie utrata przynaleznosci). Ulamek powalonych
        /// jest wiec dobrze okreslony i nie moze przekroczyc 1.
        /// </summary>
        private static int CountDownedColonists(Map map)
        {
            List<Pawn> kolonisci = map.mapPawns.FreeColonistsSpawned;
            int n = 0;
            for (int i = 0; i < kolonisci.Count; i++)
            {
                if (kolonisci[i] != null && kolonisci[i].Downed)
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>
        /// Biezacy poziom zagrozenia na mapie, przemapowany z waniliowego StoryDanger.
        ///
        /// Wanilia liczy to za nas i cache'uje co 101 tickow (DangerWatcher.DangerRating),
        /// wiec odczyt jest tani. Co wazniejsze, ta wielkosc NIE wchodzi do
        /// StorytellerUtility.DefaultThreatPointsNow - sprawdzone dekompilacja calego assembly,
        /// wanilia uzywa jej wylacznie do bramki minDanger przy RaidFriendly oraz do rzeczy
        /// nienarracyjnych (muzyka, auto-przyspieszenie czasu, blokada imprez). Czytanie jej
        /// do wlasnej krzywej NIE jest wiec podwojnym liczeniem trudnosci.
        ///
        /// Mapowanie jawnym switchem, a nie rzutowaniem int-int: obie enumeracje maja dzis
        /// zgodna kolejnosc, ale to zbieg okolicznosci po stronie Ludeona, a nie kontrakt.
        /// </summary>
        private static DangerLevel MapDanger(Map map)
        {
            if (map.dangerWatcher == null)
            {
                return DangerLevel.None;
            }

            switch (map.dangerWatcher.DangerRating)
            {
                case StoryDanger.High:
                    return DangerLevel.High;
                case StoryDanger.Low:
                    return DangerLevel.Low;
                default:
                    return DangerLevel.None;
            }
        }

    }
}
