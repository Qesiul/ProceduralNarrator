using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Blackboard;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Tension;
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

        /// <summary>
        /// Wersja z PAMIECIA NARRATORA (krok 6): poza stanem gry doklada postacie kanoniczne
        /// blackboardu - watki, fakty i wiek tematow.
        ///
        /// Snapshot pyta BLACKBOARD, a nie trzech magazynow po kolei. Dzieki temu istnieje jedno
        /// miejsce odpowiadajace sekcji 5.2 koncepcji, a warstwa integracji nie musi znac zasad
        /// wnioskowania (co znaczy "poza horyzontem", ktore zamkniecie luku jest aktualne).
        ///
        /// Ksiega faktow moze byc null, dopoki nie ma jej w pamieci gry - wtedy pole faktow jest
        /// puste, a warunki faktowe po prostu nie sa spelnione. To jest kierunek bledu bezpieczny:
        /// brak pamieci ZABIERA klocki z puli, zamiast wpuszczac do niej klocki bez pokrycia.
        /// </summary>
        public static WorldSnapshot Build(Map map, EventHistory history, ArcLedger arcs, FactLedger facts, float gameDay)
        {
            WorldSnapshot snapshot = Build(map, history, gameDay);

            var blackboard = new NarratorBlackboard(history, arcs, facts, gameDay);
            snapshot.Threads = blackboard.ThreadsCanonical();
            snapshot.Facts = blackboard.FactsCanonical();
            snapshot.TurnsSinceThemes = blackboard.TurnsSinceThemesCanonical();

            return snapshot;
        }

        public static WorldSnapshot Build(Map map)
        {
            if (map == null)
            {
                return new WorldSnapshot();
            }

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
                // NOC = CIEMNOSC (krok 8, decyzja autora po przegladzie S10), a nie zegar. Dawniej: godzina < 6 albo
                // >= 18 - a gra rozroznia dzien po jasnosci slonca (GenCelestial): na rowniku o 18:00 jasnosc 0,84
                // (pelny dzien), latem przy biegunie jasno cala "noc". Teksty mowia o ciemnosci ("po zmroku",
                // "w ciemnosciach"), a fakt w tekscie ma byc warunkiem twardym. Prog 0,3 = ciemnosc wedlug GlowGrid.
                // GenCelestial liczy z pozycji kafla i TicksAbs - deterministycznie, bez RNG.
                IsNight = GenCelestial.CurCelestialSunGlow(map) <= NocnaJasnosc,
                // Krok 8 (dlug 6): ten sam odczyt co IncidentWorker_WildManWandersIn.CanFireNowSub (bez RNG).
                SeasonAcceptableForHumans = map.mapTemperature == null || map.mapTemperature.SeasonAcceptableFor(ThingDefOf.Human),
                // Przeglad S10 (dlug 6): skazone powietrze - dwa kolejne warunki tego samego CanFireNowSub.
                ToxicAirActive = SkazonePowietrze(map),
                // Krok 9, K1-a: ten sam odczyt co CanFireNowSub gry (czysty odczyt ticku, bez RNG).
                GrowthSeasonOutdoors = map.weatherManager == null || map.weatherManager.growthSeasonMemory == null
                                       || map.weatherManager.growthSeasonMemory.GrowthSeasonOutdoorsNow,
                WildAnimalCount = CountWildAnimals(map),
                MaddenableAnimalCount = CountMaddenableAnimals(map),
                AcuteDownedCount = CountAcutelyDownedColonists(map),
                ColonistsOnMap = CountColonistsOnMap(map),
                Danger = MapDanger(map),
                // Ta sama wielkosc, ktora bazowy IncidentWorker.CanFireNow porownuje
                // z def.minThreatPoints. Liczona RAZ na ture (snapshot jest zamrazany),
                // a nie raz na kandydata - DefaultThreatPointsNow obchodzi wszystkie pionki
                // gracza, wiec 84 wywolania na ture bylyby marnotrawstwem.
                ThreatPoints = StorytellerUtility.DefaultThreatPointsNow(map),
                KidnappedColonistCount = CountKidnappedColonists(),
                HasPoweredCommsConsole = CommsConsoleUtility.PlayerHasPoweredCommsConsole(map),
                // Brama Anomaly (krok 9, K0): szansa jak w StorytellerComp.UsableIncidentsInCategory (bez RNG).
                AnomalyActive = ModsConfig.AnomalyActive,
                AnomalyIncidentChance = ModsConfig.AnomalyActive && Find.Storyteller != null
                    ? Find.Storyteller.AnomalyIncidentChanceNow : 0f,
                // Rejestr wymagan snapshotu (krok 9, K1): czyste odczyty gry, bez RNG. Rasy i rzeczy tylko z rejestru.
                SeasonalTemp = map.mapTemperature == null ? 10f : map.mapTemperature.SeasonalTemp,
                GameConditionsMap = WarunkiGry(map, false),
                GameConditionsAll = WarunkiGry(map, true),
                WeatherOkRaces = RasyZnoszacePogode(map),
                ThingCounts = LiczbyRzeczy(map),
                MechanoidFactionExists = Faction.OfMechanoids != null,
                WildHerdMinCombatPower = NajslabszeStado(map),
                FarmAnimalKindAvailable = JestGatunekHodowlany(map),
                SelfTameCandidates = KandydaciDoOswojenia(map),
                BlightablePlants = UprawyPodatneNaZaraze(map),
                ShortCircuitPossible = ShortCircuitUtility.GetShortCircuitablePowerConduits(map).Any(),
                // Rejestr, czesc K2 (Royalty i Anomaly): te same zasady - czyste odczyty gry, bez RNG, bez zmiany stanu.
                // Odczyty Anomaly tylko z aktywnym DLC (bez niego klocki Anomaly nie sa zaladowane).
                FactionDefsPresent = FrakcjeZRejestru(),
                OngoingQuestScripts = TrwajaceZadania(),
                PawnKindCounts = LiczbyRodzajow(map),
                WalkableWater = ModsConfig.AnomalyActive && ChodliwaWoda(map),
                IdleRevenantSpines = ModsConfig.AnomalyActive ? CicheKregoslupy(map) : 0,
                CubeCandidates = ModsConfig.AnomalyActive ? KandydaciLadunku(false) : 0,
                UnnaturalCorpseCandidates = ModsConfig.AnomalyActive ? KandydaciLadunku(true) : 0,
                MetalhorrorGateOpen = ModsConfig.AnomalyActive && Find.Anomaly != null
                                      && Find.Anomaly.CanNewMetalhorrorBiosignatureImplantOccur,
                InfectablePawns = ModsConfig.AnomalyActive ? ZdolniDoZakazenia(map) : 0
            };
        }

        /// <summary>Frakcje z rejestru, ktore istnieja (FactionManager.FirstFactionOfDef - tak gra bierze np. OfHoraxCult).</summary>
        private static string FrakcjeZRejestru()
        {
            var sa = new List<string>();
            if (Find.FactionManager == null)
            {
                return string.Empty;
            }
            foreach (string f in Requirements.Factions)
            {
                FactionDef d = DefDatabase<FactionDef>.GetNamedSilentFail(f);
                if (d != null && Find.FactionManager.FirstFactionOfDef(d) != null)
                {
                    sa.Add(f);
                }
            }
            return CanonicalSet.Of(sa);
        }

        /// <summary>Skrypty zadan z rejestru, ktorych zadanie trwa (QuestState.Ongoing - jak QuestNode_QuestUnique).</summary>
        private static string TrwajaceZadania()
        {
            var trwa = new List<string>();
            if (Requirements.QuestScripts.Count == 0 || Find.QuestManager == null)
            {
                return string.Empty;
            }
            foreach (Quest q in Find.QuestManager.QuestsListForReading)
            {
                if (q != null && q.State == QuestState.Ongoing && q.root != null && Requirements.QuestScripts.Contains(q.root.defName))
                {
                    trwa.Add(q.root.defName);
                }
            }
            return CanonicalSet.Of(trwa);
        }

        /// <summary>Liczby pionkow rodzajow z rejestru na mapie (MapPawns.AllPawns - jak IncidentWorker_Nociosphere).</summary>
        private static string LiczbyRodzajow(Map map)
        {
            var liczby = new Dictionary<string, int>();
            foreach (string k in Requirements.PawnKinds)
            {
                PawnKindDef d = DefDatabase<PawnKindDef>.GetNamedSilentFail(k);
                int n = 0;
                if (d != null)
                {
                    List<Pawn> pionki = map.mapPawns.AllPawns;
                    for (int i = 0; i < pionki.Count; i++)
                    {
                        if (pionki[i] != null && pionki[i].kindDef == d)
                        {
                            n++;
                        }
                    }
                }
                liczby[k] = n;
            }
            return CanonicalSet.OfCounts(liczby);
        }

        /// <summary>
        /// Chodliwe pole rzeki albo morza (TerrainDef.IsRiver/IsOcean i Walkable - pierwsze dwa czlony predykatu
        /// PawnsArrivalModeWorker_EmergeFromWater; zasieg do kolonii i wielkosc zbiornika zostaja przy grze).
        /// </summary>
        private static bool ChodliwaWoda(Map map)
        {
            if (map.terrainGrid == null)
            {
                return false;
            }
            foreach (IntVec3 c in map.AllCells)
            {
                TerrainDef t = map.terrainGrid.TerrainAt(c);
                if (t != null && (t.IsRiver || t.IsOcean) && c.Walkable(map))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Kregoslupy zjawy, ktore jeszcze nie buczy (IncidentWorker_RevenantEmergence: spawnTick &lt; 0).</summary>
        private static int CicheKregoslupy(Map map)
        {
            ThingDef d = ThingDefOf.RevenantSpine;
            if (d == null)
            {
                return 0;
            }
            int n = 0;
            List<Thing> rzeczy = map.listerThings.ThingsOfDef(d);
            for (int i = 0; i < rzeczy.Count; i++)
            {
                CompSpawnsRevenant comp = rzeczy[i] == null ? null : rzeczy[i].TryGetComp<CompSpawnsRevenant>();
                if (comp != null && comp.spawnTick < 0)
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>
        /// Kandydaci tajemniczego ladunku (QuestUtility.TryGetIdealColonist na poziomie najluzniejszym, bez losowania
        /// wyboru): pionki ludzkie na mapach, nie niemowleta, grywalni kolonisci albo niewolnicy kolonii, i walidator
        /// ladunku - szescian: bez zainteresowania i spiaczki szescianu; zwloki: rasa z nienaturalnymi zwlokami i bez nich.
        /// Pionki swiata nigdy nie przechodza (IsColonistPlayerControlled wymaga pionka na mapie).
        /// </summary>
        private static int KandydaciLadunku(bool zwloki)
        {
            int n = 0;
            if (Find.Maps == null || Find.Anomaly == null)
            {
                return 0;
            }
            foreach (Map m in Find.Maps)
            {
                List<Pawn> pionki = m.mapPawns.AllHumanlikeSpawned;
                for (int i = 0; i < pionki.Count; i++)
                {
                    Pawn p = pionki[i];
                    if (p == null || p.DevelopmentalStage.Baby() || !(p.IsColonist || p.IsSlaveOfColony) || !p.IsColonistPlayerControlled)
                    {
                        continue;
                    }
                    bool ok = zwloki
                        ? p.RaceProps.unnaturalCorpseDef != null && !Find.Anomaly.PawnHasUnnaturalCorpse(p)
                        : !p.health.hediffSet.HasHediff(HediffDefOf.CubeInterest) && !p.health.hediffSet.HasHediff(HediffDefOf.CubeComa);
                    if (ok)
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        /// <summary>
        /// Pionki zdolne przyjac implant metalhorroru (IncidentWorker_MetalhorrorImplantation.GetPossiblePawns - ten sam
        /// predykat, ale BEZ usuwania z listy gry: worker gry modyfikuje wspoldzielona liste, my tylko liczymy).
        /// </summary>
        private static int ZdolniDoZakazenia(Map map)
        {
            int n = 0;
            List<Pawn> pionki = map.mapPawns.FreeColonistsAndPrisoners;
            for (int i = 0; i < pionki.Count; i++)
            {
                Pawn p = pionki[i];
                if (p != null && MetalhorrorUtility.CanBeInfected(p) && p.infectionVectors != null
                    && p.infectionVectors.AnyPathwayForHediff(HediffDefOf.MetalhorrorImplant))
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>
        /// Rejestr wymagan snapshotu (krok 9, K1): rasy i rzeczy, o ktore pytaja warunki katalogu. Ustawia go comp po
        /// zaladowaniu katalogu (EnsureRuntime); bez niego pola z kluczami zostaja puste (warunki niespelnione).
        /// </summary>
        public static SnapshotRequirements Requirements = new SnapshotRequirements();

        /// <summary>Nazwy aktywnych warunkow gry: sama mapa albo mapa z rodzicem (swiatem) - jak GetActiveCondition.</summary>
        private static string WarunkiGry(Map map, bool zRodzicem)
        {
            var nazwy = new List<string>();
            GameConditionManager m = map.gameConditionManager;
            while (m != null)
            {
                foreach (GameCondition c in m.ActiveConditions)
                {
                    if (c != null && c.def != null)
                    {
                        nazwy.Add(c.def.defName);
                    }
                }
                m = zRodzicem ? m.Parent : null;
            }
            return CanonicalSet.Of(nazwy);
        }

        private static string RasyZnoszacePogode(Map map)
        {
            var rasy = new List<string>();
            if (map.mapTemperature == null)
            {
                return string.Empty;
            }
            foreach (string r in Requirements.Races)
            {
                ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(r);
                if (d != null && d.race != null && map.mapTemperature.SeasonAndOutdoorTemperatureAcceptableFor(d))
                {
                    rasy.Add(r);
                }
            }
            return CanonicalSet.Of(rasy);
        }

        private static string LiczbyRzeczy(Map map)
        {
            var liczby = new Dictionary<string, int>();
            foreach (string r in Requirements.Things)
            {
                ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(r);
                liczby[r] = d == null ? 0 : map.listerThings.ThingsOfDef(d).Count;
            }
            return CanonicalSet.OfCounts(liczby);
        }

        /// <summary>
        /// Najmniejsze combatPower gatunku zwierzat, ktorego co najmniej 3 osobniki spelniaja AnimalUsable gry
        /// (IncidentWorker_AnimalInsanityMass.TryExecuteWorker, 1.5.4063); brak = +nieskonczonosc.
        /// </summary>
        private static float NajslabszeStado(Map map)
        {
            var liczby = new Dictionary<PawnKindDef, int>();
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.kindDef != null && p.kindDef.RaceProps != null && p.kindDef.RaceProps.Animal
                    && IncidentWorker_AnimalInsanityMass.AnimalUsable(p))
                {
                    int n;
                    liczby.TryGetValue(p.kindDef, out n);
                    liczby[p.kindDef] = n + 1;
                }
            }
            float min = float.PositiveInfinity;
            foreach (var kv in liczby)
            {
                if (kv.Value >= 3 && kv.Key.combatPower < min)
                {
                    min = kv.Key.combatPower;
                }
            }
            return min;
        }

        /// <summary>IncidentWorker_FarmAnimalsWanderIn.TryFindRandomPawnKind (wagi wyboru zawsze dodatnie, wiec "istnieje").</summary>
        private static bool JestGatunekHodowlany(Map map)
        {
            if (map.mapTemperature == null)
            {
                return false;
            }
            foreach (PawnKindDef x in DefDatabase<PawnKindDef>.AllDefsListForReading)
            {
                if (x.RaceProps != null && x.RaceProps.Animal && x.RaceProps.wildness < 0.35f
                    && map.mapTemperature.SeasonAndOutdoorTemperatureAcceptableFor(x.race)
                    && !x.race.tradeTags.NullOrEmpty() && x.race.tradeTags.Contains("AnimalFarm") && !x.RaceProps.Dryad)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>IncidentWorker_SelfTame.Candidates (prywatna w grze - ten sam predykat).</summary>
        private static int KandydaciDoOswojenia(Map map)
        {
            int n = 0;
            foreach (Pawn x in map.mapPawns.AllPawnsSpawned)
            {
                if (x.IsNonMutantAnimal && x.Faction == null && !x.Position.Fogged(x.Map) && !x.InMentalState && !x.Downed
                    && x.RaceProps.wildness > 0f)
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>IncidentWorker_CropBlight.TryFindRandomBlightablePlant - liczba zamiast losowania.</summary>
        private static int UprawyPodatneNaZaraze(Map map)
        {
            int n = 0;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant))
            {
                var p = t as Plant;
                if (p != null && p.BlightableNow)
                {
                    n++;
                }
            }
            return n;
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
        internal static int CountKidnappedColonists()
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

        /// <summary>
        /// Odwzorowuje predykat waniliowego IncidentWorker_AnimalInsanitySingle.TryFindRandomAnimal.
        ///
        /// CZESC PREDYKATU WOLAMY, A NIE KOPIUJEMY. IncidentWorker_AnimalInsanityMass.AnimalUsable
        /// jest publiczna i statyczna, wiec zamiast przepisywac jej piec warunkow (spawned,
        /// poza mgla, nie powalone, nie juz oszalale, bez frakcji) wolamy ja wprost. Kopia
        /// zgnilaby po cichu przy pierwszej aktualizacji gry, a objawem bylby brak zdarzen -
        /// czyli nic, co widac w logu. Odtwarzamy wylacznie te czesc, ktora siedzi w prywatnej
        /// lambdzie workera i nie da sie jej wywolac: gatunek niemutancki plus prog combatPower.
        ///
        /// Prog przelacza sie na SIODMYM dniu od zalozenia (40 przed, 150 po) i czytamy go
        /// z tego samego zrodla co worker - GenDate.DaysPassedSinceSettle. Klocek, ktory
        /// z tego korzysta, ma i tak prog 11 dnia (kontrola eksperymentu wobec Cassandry),
        /// wiec nizsza galaz jest dla niego martwa - ale pole snapshotu ma opisywac STAN
        /// SWIATA, a nie zalozenia jednego klocka.
        /// </summary>
        private static int CountMaddenableAnimals(Map map)
        {
            int maxPoints = GenDate.DaysPassedSinceSettle < 7 ? 40 : 150;

            int n = 0;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (!p.IsNonMutantAnimal)
                {
                    continue;
                }
                if (p.kindDef == null || p.kindDef.combatPower > maxPoints)
                {
                    continue;
                }
                if (!IncidentWorker_AnimalInsanityMass.AnimalUsable(p))
                {
                    continue;
                }
                n++;
            }
            return n;
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
        /// Ilu kolonistow OBECNYCH na mapie lezy powalonych OSTRO. Sygnal dla krzywej napiecia
        /// i dla predykatu kryzysu skrajnego; zaden warunek twardy tego nie czyta.
        ///
        /// "OSTRO" = Pawn.Downed ORAZ jeden z trzech sladow sytuacji, ktora wymaga pomocy teraz:
        ///   - szok bolowy (Pawn_HealthTracker.InPainShock),
        ///   - krwawienie (HediffSet.BleedRateTotal &gt; 0 - rany opatrzone i trwale nie krwawia,
        ///     zdekompilowane Hediff_Injury.BleedRate zwraca 0 dla IsTended() i IsPermanent()),
        ///   - cokolwiek do opatrzenia (HasHediffsNeedingTend -&gt; Hediff.TendableNow dla KAZDEGO
        ///     hediffu: nieopatrzone rany, choroby nigdy nieopatrzone - tendTicksLeft = -1 - oraz
        ///     choroby od 3 h PRZED wygasnieciem opatrunku, tendOverlapHours).
        ///
        /// DLACZEGO NIE SAMO Downed - zmierzone w przegladzie etapu 4. Stan Down obejmuje stany
        /// TRWALE: kazde niemowle (HumanlikeBaby ma alwaysDowned; wanilia sama je odfiltrowuje
        /// w Caravan.cs: Downed &amp;&amp; !alwaysDowned), brak obu nog, abazje, sen smierci sanguofaga,
        /// spiaczke, katatonie. Przy dwoch kolonistach i jednym takim pionku profil powsciagliwy
        /// mial TRWALE Breathe; kolonia z czworgiem niemowlat miala napiecie sytuacyjne 1.0 na
        /// stale. Wszystkie te stany sa po opatrzeniu "ciche" - nie krwawia, nie maja nic do
        /// opatrzenia, nie sa w szoku - wiec kryterium je pomija bez wyliczania ich z nazwy.
        ///
        /// Precedens w wanilii: StoryWatcher_Adaptation liczy wylacznie powalenia z przemocy
        /// ("violently downed", dinfo.Def.ExternalViolenceFor). Wanilia robi to ZDARZENIOWO,
        /// w chwili powalenia; snapshot jest STANEM, wiec pytamy o slady, ktore przemoc
        /// (i kazdy inny ostry kryzys) zostawia na pionku, dopoki ktos go nie opatrzy.
        ///
        /// KROK 8 (dlug 12, decyzja autora K8-8) - decyzja przeniesiona do Core (AcuteDownedRule, testowana
        /// offline); tutaj tylko odczyt flag z gry:
        ///   - LICZONY od kroku 8: powalony przez stan z progiem smierci osiagnietym w &gt;= lethalFraction
        ///     (0,5) - udar cieplny, hipotermia, zatrucie toksynami (ToxicBuildup nie jest tendable i nie boli,
        ///     wiec dawne kryterium go nie widzialo) i kazdy podobny, bez wyliczania nazw;
        ///   - NIE LICZONY od kroku 8: porod (PregnancyLabor/Pushing - bol 0.85 &gt;= prog szoku 0.8; w kolonii
        ///     dwuosobowej wlaczal kryzys skrajny). Rana albo krwawienie przy porodzie dalej sie licza.
        ///
        /// ZNANE OGRANICZENIA I ZACHOWANIA, swiadome (zweryfikowane dekompilacja w przegladzie):
        ///   - LICZONY OKRESOWO: trwale powalony z przewlekla choroba do opatrywania (np. astma) -
        ///     wraca do licznika na kilka godzin przed kazdym koncem opatrunku;
        ///   - LICZONY ZAWSZE: pionek w trwalym szoku bolowym (przewlekle bolesne blizny).
        /// Wszystkie sa rzadkie; liste da sie zmienic tutaj, bez zmian w Core. Weryfikacja
        /// wylacznie w grze - walidator offline tej warstwy nie widzi.
        ///
        /// Pionek NOSZONY (akcja ratunkowa) nie jest spawnowany, wiec wypada z licznika i z
        /// mianownika jednoczesnie - ulamek zostaje dobrze okreslony.
        /// </summary>
        internal static int CountAcutelyDownedColonists(Map map)
        {
            List<Pawn> kolonisci = map.mapPawns.FreeColonistsSpawned;
            float prog = ProgSmiertelnosci();
            int n = 0;
            for (int i = 0; i < kolonisci.Count; i++)
            {
                Pawn p = kolonisci[i];
                // Tanie odsianie przed odczytem hediffow: niepowalony nie jest ostry w zadnej galezi reguly.
                if (p == null || !p.Downed || p.health == null)
                {
                    continue;
                }
                if (AcuteDownedRule.IsAcute(FlagiPionka(p), prog))
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>Flagi zdrowia pionka dla AcuteDownedRule (krok 8) - sam odczyt, bez decyzji.</summary>
        private static PawnAcuteFlags FlagiPionka(Pawn p)
        {
            var f = new PawnAcuteFlags
            {
                Downed = p.Downed,
                AlwaysDowned = LifeStageUtility.AlwaysDowned(p),
                InPainShock = p.health.InPainShock,
                Bleeding = p.health.hediffSet.BleedRateTotal > 0f,
                NeedsTend = p.health.HasHediffsNeedingTend()
            };
            List<Hediff> hediffy = p.health.hediffSet.hediffs;
            for (int i = 0; i < hediffy.Count; i++)
            {
                Hediff h = hediffy[i];
                if (h == null || h.def == null)
                {
                    continue;
                }
                // Przeglad S10: choroby PRZEWLEKLE (blokada tetnicy, rozpad narzadow - HediffDef.chronic) nie sa
                // nagla sytuacja; bez tego trwale powalony z przewlekla choroba >= 0,5 byl liczony zawsze
                // (w kolonii dwuosobowej: staly kryzys skrajny).
                if (h.def.lethalSeverity > 0f && !h.def.chronic)
                {
                    float ulamek = h.Severity / h.def.lethalSeverity;
                    if (ulamek > f.MaxLethalFraction)
                    {
                        f.MaxLethalFraction = ulamek;
                    }
                }
                // HediffDefOf.PregnancyLabor* sa [MayRequireBiotech] - bez Biotechu sa null i nic nie pasuje.
                if ((HediffDefOf.PregnancyLabor != null && h.def == HediffDefOf.PregnancyLabor)
                    || (HediffDefOf.PregnancyLaborPushing != null && h.def == HediffDefOf.PregnancyLaborPushing))
                {
                    f.InLabor = true;
                }
            }
            return f;
        }

        /// <summary>
        /// Prog smiertelnosci z bloku &lt;crisis&gt; NASZEGO narratora - takze w grze z innym narratorem
        /// (obserwator [PN-FIRED] liczy powalonych ta sama miara). Konfiguracja, nie stan rozgrywki:
        /// czytana z Defa przy kazdym uzyciu (raz na ture i raz na 1000 tickow).
        /// </summary>
        /// <summary>Jasnosc slonca, ponizej ktorej (wlacznie) jest noc - ciemnosc wedlug GlowGrid (krok 8).</summary>
        internal const float NocnaJasnosc = 0.3f;

        /// <summary>
        /// Opad toksyczny albo toksyczna mgla (Biotech) na mapie - te same warunki gry, przy ktorych
        /// IncidentWorker_WildManWandersIn.CanFireNowSub odmawia (przeglad S10, dlug 6). Czysty odczyt listy
        /// aktywnych warunkow, bez RNG. NoxiousHaze jest [MayRequireBiotech] - bez Biotechu null.
        /// </summary>
        private static bool SkazonePowietrze(Map map)
        {
            GameConditionManager gcm = map.GameConditionManager;
            if (gcm == null)
            {
                return false;
            }
            if (GameConditionDefOf.ToxicFallout != null && gcm.ConditionIsActive(GameConditionDefOf.ToxicFallout))
            {
                return true;
            }
            return ModsConfig.BiotechActive && GameConditionDefOf.NoxiousHaze != null
                   && gcm.ConditionIsActive(GameConditionDefOf.NoxiousHaze);
        }

        internal static float ProgSmiertelnosci()
        {
            StorytellerDef def = DefDatabase<StorytellerDef>.GetNamedSilentFail("PN_GenerativeNarrator");
            if (def != null && def.comps != null)
            {
                for (int i = 0; i < def.comps.Count; i++)
                {
                    var g = def.comps[i] as Storyteller.StorytellerCompProperties_Generative;
                    if (g != null && g.crisis != null)
                    {
                        return g.crisis.lethalFraction;
                    }
                }
            }
            return CrisisParams.Default().lethalFraction;
        }

        /// <summary>
        /// Mianownik dla CountAcutelyDownedColonists: kolonisci OBECNI na mapie, bez niemowlat.
        ///
        /// Z TEJ SAMEJ listy co licznik (FreeColonistsSpawned). Wczesniej mianownikiem bylo
        /// ColonistCount = FreeColonistsCount, ktore liczy tez pionki trzymane niespawnowane -
        /// w kriokomorach, noszone, w transporterach - a licznik tylko obecnych. Stary komentarz
        /// uzasadnial rozjazd karawana, ale karawany nie ma w zadnej z tych list, wiec
        /// uzasadnienie bylo bledne, a ulamek zanizony dokladnie wtedy, gdy czesc kolonii
        /// byla poza gra. Niemowleta wypadaja z obu stron, bo zadne z nich nie moze byc ani
        /// "zdolne do dzialania", ani "powalone w kryzysie".
        /// </summary>
        internal static int CountColonistsOnMap(Map map)
        {
            List<Pawn> kolonisci = map.mapPawns.FreeColonistsSpawned;
            int n = 0;
            for (int i = 0; i < kolonisci.Count; i++)
            {
                if (kolonisci[i] != null && !LifeStageUtility.AlwaysDowned(kolonisci[i]))
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
        internal static DangerLevel MapDanger(Map map)
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
