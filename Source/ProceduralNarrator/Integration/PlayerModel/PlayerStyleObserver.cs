using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.PlayerModel;
using ProceduralNarrator.Integration.Arcs;
using ProceduralNarrator.Integration.Storyteller;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ProceduralNarrator.Integration.PlayerModel
{
    /// <summary>
    /// OBSERWATOR STYLU GRACZA (krok 7, S5) - jedyne miejsce, w ktorym pomiar stylu dotyka API gry.
    /// Czyta stan kolonii i reakcje na zagrozenia BEZ Harmony (decyzja autora nr 6) i podaje je
    /// ksiedze stylu jako liczby (DayObservation, MapThreatSample). Kazda regula "co sie liczy"
    /// zyje w Core (PlayerStyleLedger) i jest testowana offline (TEST 14i) - tutaj tylko odczyt.
    ///
    /// WOLANY Z NarratorMemoryComponent.GameComponentTick, a nie ze StorytellerComp: comp nie jest
    /// wolany przed dniem 5 (minDaysPassed), nie dziala pod innym narratorem, a nasz symulator
    /// wola go poza zegarem gry. GameComponentTick biegnie w KAZDYM ticku DoSingleTick, od dnia 0,
    /// w kazdej grze - takze na Cassandrze (dane do porownan w kroku 9, pole narrator= w [PN-GRACZ]).
    ///
    /// Bez Verse.Rand (kanarek RandCanary wokol kroku). Kazda wspoldzielona lista gry (PawnsFinder,
    /// listy map, zadan i budynkow) jest KOPIOWANA przed uzyciem - PawnsFinder zwraca listy
    /// statyczne czyszczone przy kazdym odczycie.
    /// </summary>
    internal static class PlayerStyleObserver
    {
        /// <summary>Rekordy gry dla pomiaru Werbunek (typ Int). Istnienie sprawdza audyt startowy.</summary>
        internal const string RecruitedRecord = "PrisonersRecruited";
        internal const string CapturedRecord = "PeopleCaptured";

        /// <summary>
        /// Parametry stylu: z aktywnego narratora, gdy uzywa naszego compa; inaczej z pierwszego
        /// StorytellerDefa z naszym compem (po defName, porzadek ordynalny - deterministycznie).
        /// Obserwujemy we WSZYSTKICH grach, wiec pod Cassandra parametry i tak musza skads przyjsc.
        /// Blok jest juz po Sanitize (PNStartup.AuditDecisionConfig dziala na obiekcie z Defa).
        /// </summary>
        internal static PlayerStyleParams ResolveParams()
        {
            StorytellerCompProperties_Generative nasz = null;
            if (Current.Game != null && Current.Game.storyteller != null && Current.Game.storyteller.def != null
                && Current.Game.storyteller.def.comps != null)
            {
                nasz = Current.Game.storyteller.def.comps.OfType<StorytellerCompProperties_Generative>().FirstOrDefault();
            }
            if (nasz == null)
            {
                foreach (StorytellerDef st in DefDatabase<StorytellerDef>.AllDefsListForReading
                                                  .OrderBy(d => d.defName, StringComparer.Ordinal))
                {
                    nasz = st.comps == null ? null : st.comps.OfType<StorytellerCompProperties_Generative>().FirstOrDefault();
                    if (nasz != null)
                    {
                        break;
                    }
                }
            }
            return nasz == null || nasz.playerStyle == null ? PlayerStyleParams.Default() : nasz.playerStyle;
        }

        /// <summary>
        /// Jeden krok w ticku gry: zainicjowanie ksiegi (pierwszy tick nowej gry albo gry wczytanej
        /// bez stylu), zamkniecie doby na jej granicy (tick / 60000) i co threatSampleTicks - probka
        /// zagrozen oraz ataku gracza na osade. Zwraca dzien wepchniety do kolejki (do linii
        /// [PN-GRACZ]) albo null.
        ///
        /// Probki na siatce tick % threatSampleTicks == 0, a nie "co N tickow od ostatniej": siatka
        /// zalezy tylko od zegara gry, wiec gra grana ciagiem i gra po zapisie-wczytaniu probkuja w tych
        /// samych tickach. Skoki zegara (fastEcology, narzedzia dev) omijaja probki - znane ograniczenie.
        /// </summary>
        internal static StyleDaySample Tick(PlayerStyleLedger ledger, PlayerStyleParams p, int tick)
        {
            if (ledger == null || p == null || !p.enabled || tick < 0)
            {
                return null;
            }
            uint rand0 = RandCanary.Read();
            StyleDaySample zamkniety = null;
            try
            {
                if (!ledger.Initialized)
                {
                    // CloseDay na ksiedze niezainicjowanej robi Initialize (bazy, znak wodny, dzicy).
                    ledger.CloseDay(tick, Observe(p, ledger), p);
                }
                else if (tick / PlayerStyleLedger.TicksPerDay != ledger.CurrentDay)
                {
                    zamkniety = ledger.CloseDay(tick, Observe(p, ledger), p);
                }

                int co = p.threatSampleTicks < 1 ? 1 : p.threatSampleTicks;
                if (tick % co == 0)
                {
                    if (Find.History != null)
                    {
                        ledger.ObserveRaidTick(Find.History.lastTickPlayerRaidedSomeone);
                    }
                    ledger.OnThreatSample(tick, ThreatSamples(), p);
                }
            }
            finally
            {
                RandCanary.Check(rand0, "obserwator stylu gracza");
            }
            return zamkniety;
        }

        /// <summary>Mapy domowe gracza rosnaco po uniqueID (kopia listy map).</summary>
        private static List<Map> HomeMaps()
        {
            var wynik = new List<Map>();
            if (Find.Maps == null)
            {
                return wynik;
            }
            foreach (Map m in new List<Map>(Find.Maps))
            {
                if (m != null && m.IsPlayerHome)
                {
                    wynik.Add(m);
                }
            }
            wynik.Sort((a, b) => a.uniqueID.CompareTo(b.uniqueID));
            return wynik;
        }

        /// <summary>
        /// Probka zagrozen z map domowych. ZAGROZENIE = NAPAD (decyzja autora po przegladzie S8): wrogi pionek
        /// Z FRAKCJA (bez zwierzat w szale i drapieznikow - te zanizaly pomiar u gracza, ktory rozsadnie zostaje
        /// w srodku, i w eskalacji zamykaly petle: slaba Reaktywnosc -> wiecej takich zdarzen), aktywne zagrozenie
        /// wedlug GenHostility.IsActiveThreatTo i NIE w obozie przed atakiem (LordToil_Stage, 2-6 godzin gry) - zegar
        /// reakcji startuje przy ataku. Odczyt bez efektow ubocznych (nie DangerWatcher: ten zapisuje wlasny cache).
        /// Liczniki poboru tylko przy zagrozeniu: w probce cichej ksiega ich nie czyta.
        /// </summary>
        private static List<MapThreatSample> ThreatSamples()
        {
            var wynik = new List<MapThreatSample>();
            foreach (Map map in HomeMaps())
            {
                var s = new MapThreatSample { MapId = map.uniqueID, Threat = Napad(map) };
                if (s.Threat && map.mapPawns != null)
                {
                    // Mianownik (decyzja autora po przegladzie S8): DOROSLI koloniscy i sterowalne mechanoidy kolonii.
                    // Dzieci poza pomiarem - rodzina trzymajaca je z dala od walki nie wychodzi mniej reaktywna.
                    foreach (Pawn pawn in new List<Pawn>(map.mapPawns.FreeAdultColonistsSpawned))
                    {
                        if (ZdolnyDoWalki(pawn))
                        {
                            Policz(ref s, pawn);
                        }
                    }
                    foreach (Pawn mech in new List<Pawn>(map.mapPawns.SpawnedColonyMechs))
                    {
                        if (MechZdolnyDoWalki(mech))
                        {
                            Policz(ref s, mech);
                        }
                    }
                }
                wynik.Add(s);
            }
            return wynik;
        }

        private static void Policz(ref MapThreatSample s, Pawn pawn)
        {
            s.Eligible++;
            if (pawn.Drafted)
            {
                s.Drafted++;
            }
        }

        /// <summary>Czy na mapie trwa napad (patrz ThreatSamples). Kopia zbioru celow - cache gry zmienia sie w ticku.</summary>
        private static bool Napad(Map map)
        {
            if (map.attackTargetsCache == null)
            {
                return false;
            }
            foreach (IAttackTarget cel in new List<IAttackTarget>(map.attackTargetsCache.TargetsHostileToFaction(Faction.OfPlayer)))
            {
                Pawn p = cel == null ? null : cel.Thing as Pawn;
                if (p == null || p.Faction == null || p.RaceProps == null || p.RaceProps.Animal)
                {
                    continue;
                }
                if (!GenHostility.IsActiveThreatTo(cel, Faction.OfPlayer))
                {
                    continue;
                }
                Lord lord = p.GetLord();
                if (lord != null && lord.CurLordToil is LordToil_Stage)
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Mianownik pomiaru Poborowi (czlowiek): dorosly kolonista, ktorego gracz MOGLBY powolac. Bez powalonych,
        /// w letargu (deathrest), w stanie psychicznym, pacyfistow (walka wylaczona), niewolnikow i lokatorow zadan -
        /// ich brak pod bronia nic nie mowi o reakcji gracza.
        /// </summary>
        private static bool ZdolnyDoWalki(Pawn p)
        {
            return p != null && p.drafter != null && !p.Downed && !p.Deathresting && !p.InMentalState
                   && !p.IsSlave && !p.IsQuestLodger() && !p.WorkTagIsDisabled(WorkTags.Violent);
        }

        /// <summary>
        /// Mianownik pomiaru Poborowi (mechanoid kolonii, decyzja autora po przegladzie S8): sterowany przez mechanitora,
        /// z przyciskiem powolania i dopuszczony przez MechanitorUtility.CanDraftMech (zasieg, przepustowosc, energia).
        /// </summary>
        private static bool MechZdolnyDoWalki(Pawn m)
        {
            return m != null && m.IsColonyMechPlayerControlled && m.drafter != null && m.drafter.ShowDraftGizmo && !m.Downed
                   && MechanitorUtility.CanDraftMech(m).Accepted;
        }

        /// <summary>Obserwacja kolonii w chwili zamkniecia doby - wszystkie odczyty z gry dla CloseDay.</summary>
        internal static DayObservation Observe(PlayerStyleParams p, PlayerStyleLedger ledger)
        {
            var obs = new DayObservation();
            obs.LastPlayerRaidTick = Find.History == null ? -1 : Find.History.lastTickPlayerRaidedSomeone;
            List<Map> domowe = HomeMaps();

            ObserveBuildings(obs, p, domowe);
            ObservePawns(obs, p);
            ObserveOffers(obs, p);
            ObserveWildMen(obs, ledger, domowe);
            return obs;
        }

        /// <summary>
        /// Wartosc budynkow gracza na mapach domowych (MarketValueIgnoreHp - ta sama miara co
        /// WealthWatcher, uszkodzenie nie obniza wartosci) i jej czesci z kategorii obrony oraz
        /// produkcji/zasilania. Kategoria = designationCategory Defa budynku (po defName, bo
        /// DesignationCategoryDefOf ma tylko Production). Rusztowania (Frame) pomijane - to jeszcze
        /// nie budynek, a ich wartosc jest wartoscia materialu.
        /// </summary>
        private static void ObserveBuildings(DayObservation obs, PlayerStyleParams p, List<Map> domowe)
        {
            var obrona = new HashSet<string>(p.defenseCategories ?? new List<string>(), StringComparer.Ordinal);
            var produkcja = new HashSet<string>(p.productionCategories ?? new List<string>(), StringComparer.Ordinal);
            foreach (Map map in domowe)
            {
                if (map.listerBuildings == null)
                {
                    continue;
                }
                foreach (Building b in new List<Building>(map.listerBuildings.allBuildingsColonist))
                {
                    if (b == null || b.Destroyed || b is Frame || b.def == null)
                    {
                        continue;
                    }
                    long v = (long)Math.Round(b.GetStatValue(StatDefOf.MarketValueIgnoreHp), MidpointRounding.AwayFromZero);
                    if (v <= 0)
                    {
                        continue;
                    }
                    obs.BuildingValue += v;
                    string kat = b.def.designationCategory == null ? null : b.def.designationCategory.defName;
                    if (kat == null)
                    {
                        continue;
                    }
                    if (obrona.Contains(kat))
                    {
                        obs.DefenseValue += v;
                    }
                    if (produkcja.Contains(kat))
                    {
                        obs.ProductionValue += v;
                    }
                }
            }
        }

        /// <summary>
        /// Rekordy kolonistow (na mapach, w karawanach i w transporcie). Eligible = kolonista, nie
        /// niewolnik, nie lokator zadania - reszte (bazy, delty, ujemne delty) rozstrzyga ksiega.
        /// Rekordy czasu sa w tickach (float); suma zaokraglana do liczby calkowitej.
        /// </summary>
        private static void ObservePawns(DayObservation obs, PlayerStyleParams p)
        {
            List<RecordDef> produktywne = Rekordy(p.productiveRecords);
            List<RecordDef> pomocnicze = Rekordy(p.supportRecords);
            RecordDef zwerbowani = DefDatabase<RecordDef>.GetNamedSilentFail(RecruitedRecord);
            RecordDef schwytani = DefDatabase<RecordDef>.GetNamedSilentFail(CapturedRecord);

            var pionki = new List<Pawn>(PawnsFinder.AllMapsCaravansAndTravelingTransportPods_Alive_FreeColonists);
            pionki.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            foreach (Pawn pawn in pionki)
            {
                if (pawn == null || pawn.records == null)
                {
                    continue;
                }
                obs.Pawns.Add(new PawnRecordSample
                {
                    Id = pawn.thingIDNumber,
                    Eligible = pawn.IsColonist && !pawn.IsSlave && !pawn.IsQuestLodger(),
                    Productive = Suma(pawn, produktywne),
                    Support = Suma(pawn, pomocnicze),
                    Recruited = Wartosc(pawn, zwerbowani),
                    Captured = Wartosc(pawn, schwytani)
                });
            }
        }

        private static List<RecordDef> Rekordy(List<string> nazwy)
        {
            var wynik = new List<RecordDef>();
            if (nazwy == null)
            {
                return wynik;
            }
            foreach (string n in nazwy)
            {
                RecordDef d = string.IsNullOrEmpty(n) ? null : DefDatabase<RecordDef>.GetNamedSilentFail(n);
                if (d != null)
                {
                    wynik.Add(d);
                }
            }
            return wynik;
        }

        private static long Suma(Pawn pawn, List<RecordDef> rekordy)
        {
            double suma = 0.0;
            foreach (RecordDef d in rekordy)
            {
                suma += pawn.records.GetValue(d);
            }
            return (long)Math.Round(suma, MidpointRounding.AwayFromZero);
        }

        private static long Wartosc(Pawn pawn, RecordDef rekord)
        {
            return rekord == null ? 0L : (long)Math.Round(pawn.records.GetValue(rekord), MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Zadania-oferty dolaczenia (korzenie z offerQuestRoots). Zadanie "wedrowiec dolacza" ma
        /// autoAccept, wiec do chwili decyzji jest Ongoing; przyjecie konczy je EndedSuccess, odmowa
        /// i uplyw doby - EndedFailed (dekompilacja 1.5.4063, QuestNode_Root_WandererJoin_WalkIn).
        /// ROZBITEK (RefugeePodCrash) to inna semantyka: EndedSuccess juz przy pierwszym opatrzeniu, zwerbowaniu albo
        /// odejsciu w zdrowiu, EndedFailed przy smierci albo odejsciu rannym, bez limitu czasu - czyli "pomoc", nie
        /// "dolaczenie" (znane ograniczenie; poprawione w przegladzie S8 - wczesniej opis sugerowal przyjecie).
        /// Zakonczone zadania zostaja w menedzerze (usuwa je tylko generator podzadan i narzedzie dev).
        /// </summary>
        private static void ObserveOffers(DayObservation obs, PlayerStyleParams p)
        {
            if (Find.QuestManager == null)
            {
                return;
            }
            var korzenie = new HashSet<string>(p.offerQuestRoots ?? new List<string>(), StringComparer.Ordinal);
            foreach (Quest q in new List<Quest>(Find.QuestManager.QuestsListForReading))
            {
                if (q == null || q.root == null || !korzenie.Contains(q.root.defName))
                {
                    continue;
                }
                obs.Offers.Add(new OfferQuestSample { Id = q.id, State = Stan(q.State) });
            }
        }

        private static OfferState Stan(QuestState s)
        {
            switch (s)
            {
                case QuestState.NotYetAccepted:
                case QuestState.Ongoing:
                    return OfferState.Pending;
                case QuestState.EndedSuccess:
                    return OfferState.Success;
                case QuestState.EndedFailed:
                case QuestState.EndedOfferExpired:
                    return OfferState.Fail;
                default:
                    return OfferState.Void;
            }
        }

        /// <summary>
        /// Dzicy ludzie: nowi (dzicy albo jency, na mapach domowych) i stan kazdego sledzonego.
        /// Sledzony, ktorego nie ma wsrod zywych, nie dostaje wpisu - ksiega uzna go za "zniknal".
        /// Kolejnosc: dolaczyl (frakcja gracza, takze jako niewolnik) przed jencem przed dzikim.
        /// </summary>
        private static void ObserveWildMen(DayObservation obs, PlayerStyleLedger ledger, List<Map> domowe)
        {
            var sledzeni = new HashSet<int>(ledger == null ? Enumerable.Empty<int>() : ledger.WildMen.Keys);
            var domoweId = new HashSet<int>(domowe.Select(m => m.uniqueID));
            var zywi = new List<Pawn>(PawnsFinder.AllMapsCaravansAndTravelingTransportPods_Alive);
            zywi.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            foreach (Pawn pawn in zywi)
            {
                if (pawn == null)
                {
                    continue;
                }
                bool sledzony = sledzeni.Contains(pawn.thingIDNumber);
                if (!sledzony && !pawn.IsWildMan())
                {
                    continue;
                }
                // Zdziczaly BYLY kolonista (zalamanie RunWild: ChangeKind(WildMan) i utrata frakcji na mapie domowej) nie jest
                // oferta dolaczenia (decyzja autora po przegladzie S8) - rekord czasu w kolonii > 0 go zdradza.
                if (!sledzony && pawn.records != null && RecordDefOf.TimeAsColonistOrColonyAnimal != null
                    && pawn.records.GetValue(RecordDefOf.TimeAsColonistOrColonyAnimal) > 0f)
                {
                    continue;
                }
                WildManStatus? st = null;
                if (pawn.Faction != null && pawn.Faction.IsPlayer)
                {
                    st = WildManStatus.Joined;
                }
                else if (pawn.IsPrisonerOfColony)
                {
                    st = WildManStatus.Prisoner;
                }
                else if (pawn.Faction == null && pawn.IsWildMan() && pawn.Spawned && pawn.Map != null
                         && domoweId.Contains(pawn.Map.uniqueID))
                {
                    st = WildManStatus.Wild;
                }
                if (st.HasValue && (sledzony || st.Value != WildManStatus.Joined))
                {
                    obs.WildMen.Add(new WildManSample { Id = pawn.thingIDNumber, Status = st.Value });
                }
            }
        }
    }
}
