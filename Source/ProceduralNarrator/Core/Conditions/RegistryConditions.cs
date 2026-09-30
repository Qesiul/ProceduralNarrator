using System.Collections.Generic;
using System.Globalization;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Conditions
{
    /// <summary>
    /// Warunek, ktory czyta z WorldSnapshot wartosc liczona dla KLUCZA z XML (rasa, rzecz). Warstwa integracji liczy
    /// tylko klucze, o ktore pyta katalog (SnapshotRequirements) - bez switch po nazwach i bez liczenia wszystkiego.
    /// </summary>
    public interface ISnapshotKeyed
    {
        /// <summary>Rodzaj klucza (SnapshotRequirements.Race / Thing) i sam klucz (defName z gry).</summary>
        IEnumerable<KeyValuePair<string, string>> SnapshotKeys { get; }
    }

    // =====================================================================================================
    //  WARUNKI Z REJESTRU WYMAGAN SNAPSHOTU (krok 9, K1). Kazdy odwzorowuje systematyczna czesc CanFireNowSub
    //  (albo TryExecuteWorker) incydentu gry, czytana w snapshotcie czystym odczytem, bez losowania.
    //  Zrodla (dekompilacja 1.5.4063): Docs/DZIENNIK_ROZWOJU.md, wpis "Krok 9, K1 - rozpoznanie".
    // =====================================================================================================

    /// <summary>
    /// Temperatura sezonowa (MapTemperature.SeasonalTemp - bez dobowej zmiennosci i bez przesuniec warunkow gry).
    /// Fala mrozu: 0 &lt; T &lt; 15 (IncidentWorker_ColdSnap), fala upalu: T &gt;= 20 (IncidentWorker_HeatWave) - dlatego
    /// trzy osobne granice, ostre i nieostra, jak w grze.
    /// </summary>
    public class Cond_SeasonalTemp : NarrativeCondition
    {
        public float greaterThan = float.MinValue;
        public float atLeast = float.MinValue;
        public float lessThan = float.MaxValue;

        public override bool IsMet(WorldSnapshot s)
        {
            float t = s.SeasonalTemp;
            return t > greaterThan && t >= atLeast && t < lessThan;
        }

        public override string Describe()
        {
            var czesci = new List<string>();
            if (greaterThan > float.MinValue) czesci.Add("> " + greaterThan.ToString("0.#", CultureInfo.InvariantCulture));
            if (atLeast > float.MinValue) czesci.Add(">= " + atLeast.ToString("0.#", CultureInfo.InvariantCulture));
            if (lessThan < float.MaxValue) czesci.Add("< " + lessThan.ToString("0.#", CultureInfo.InvariantCulture));
            return "temperatura sezonowa " + (czesci.Count == 0 ? "dowolna" : string.Join(" i ", czesci.ToArray()));
        }
    }

    /// <summary>
    /// Zaden z podanych warunkow gry nie jest aktywny. mapOnly = false: mapa i swiat (GameConditionManager.ConditionIsActive
    /// siega do rodzica - tak gra sprawdza "ten sam warunek juz trwa"); mapOnly = true: tylko mapa (tak gra sprawdza
    /// CanCoexistWith wobec warunkow aktywnych na mapie).
    /// </summary>
    public class Cond_GameConditionAbsent : NarrativeCondition
    {
        public List<string> defs = new List<string>();
        public bool mapOnly;

        public override bool IsMet(WorldSnapshot s)
        {
            string aktywne = mapOnly ? s.GameConditionsMap : s.GameConditionsAll;
            foreach (string d in defs)
            {
                if (CanonicalSet.Contains(aktywne, d))
                {
                    return false;
                }
            }
            return true;
        }

        public override string Describe()
        {
            return "brak warunkow gry " + string.Join(",", defs.ToArray()) + (mapOnly ? " (mapa)" : " (mapa i swiat)");
        }
    }

    /// <summary>
    /// Pogoda znosna dla rasy (MapTemperature.SeasonAndOutdoorTemperatureAcceptableFor): alfabobry, thrumbo.
    /// </summary>
    public class Cond_RaceWeatherOk : NarrativeCondition, ISnapshotKeyed
    {
        public string race;

        public override bool IsMet(WorldSnapshot s)
        {
            return CanonicalSet.Contains(s.WeatherOkRaces, race);
        }

        public IEnumerable<KeyValuePair<string, string>> SnapshotKeys
        {
            get { yield return new KeyValuePair<string, string>(SnapshotRequirements.Race, race); }
        }

        public override string Describe()
        {
            return "pogoda znosna dla " + race;
        }
    }

    /// <summary>
    /// Liczba rzeczy danego rodzaju na mapie w [min, max] (ListerThings.ThingsOfDef): brak drugiego defoliatora, brak
    /// wraku-emanatora przy falach psychicznych.
    /// </summary>
    public class Cond_ThingCount : NarrativeCondition, ISnapshotKeyed
    {
        public string thing;
        public int min;
        public int max = int.MaxValue;

        public override bool IsMet(WorldSnapshot s)
        {
            int n = CanonicalSet.Count(s.ThingCounts, thing);
            return n >= min && n <= max;
        }

        public IEnumerable<KeyValuePair<string, string>> SnapshotKeys
        {
            get { yield return new KeyValuePair<string, string>(SnapshotRequirements.Thing, thing); }
        }

        public override string Describe()
        {
            return thing + " na mapie " + min.ToString(CultureInfo.InvariantCulture) + "-"
                   + (max == int.MaxValue ? "*" : max.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Istnieje frakcja mechanoidow (Faction.OfMechanoids) - IncidentWorker_CrashedShipPart.</summary>
    public class Cond_MechanoidFaction : NarrativeCondition
    {
        public bool want = true;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.MechanoidFactionExists == want;
        }

        public override string Describe()
        {
            return want ? "frakcja mechanoidow istnieje" : "brak frakcji mechanoidow";
        }
    }

    /// <summary>
    /// Stado zdolne do zbiorowego szalu (IncidentWorker_AnimalInsanityMass.TryExecuteWorker): gatunek zwierzat, ktorego
    /// co najmniej 3 zdrowe dzikie osobniki stoja na mapie, o combatPower nie wiekszym niz punkty po korekcie gry
    /// (p &gt; 250 -&gt; 250 + (p - 250) / 2). Snapshot niesie NAJMNIEJSZE combatPower takiego gatunku; punkty to
    /// ThreatPoints razy pointsFactor = maksymalny mnoznik intensywnosci (1.35, IntensityTable) - SITO ZGRUBNE jak
    /// Cond_MinThreatPoints: przepuszcza kazdego kandydata, ktorego moc koncowa moze wystarczyc.
    /// </summary>
    public class Cond_WildHerd : NarrativeCondition
    {
        public float pointsFactor = 1f;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.WildHerdMinCombatPower <= AdjustedPoints(s.ThreatPoints * pointsFactor);
        }

        /// <summary>Korekta punktow z IncidentWorker_AnimalInsanityMass (1.5.4063).</summary>
        public static float AdjustedPoints(float points)
        {
            return points > 250f ? 250f + (points - 250f) * 0.5f : points;
        }

        public override string Describe()
        {
            return "stado zdolne do szalu (mnoznik punktow " + pointsFactor.ToString("0.##", CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>
    /// Istnieje gatunek hodowlany znoszacy obecna pogode (IncidentWorker_FarmAnimalsWanderIn.TryFindRandomPawnKind).
    /// </summary>
    public class Cond_FarmAnimalKind : NarrativeCondition
    {
        public bool want = true;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.FarmAnimalKindAvailable == want;
        }

        public override string Describe()
        {
            return want ? "jest gatunek hodowlany na te pogode" : "brak gatunku hodowlanego na te pogode";
        }
    }

    /// <summary>Dzikie zwierzeta zdolne same dolaczyc do kolonii (IncidentWorker_SelfTame.Candidates).</summary>
    public class Cond_SelfTameCandidate : NarrativeCondition
    {
        public int min = 1;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.SelfTameCandidates >= min;
        }

        public override string Describe()
        {
            return "zwierzat do samooswojenia >= " + min.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Uprawy podatne na zaraze (Plant.BlightableNow - IncidentWorker_CropBlight.CanFireNowSub).</summary>
    public class Cond_BlightablePlants : NarrativeCondition
    {
        public int min = 1;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.BlightablePlants >= min;
        }

        public override string Describe()
        {
            return "upraw podatnych na zaraze >= " + min.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Zwarcie mozliwe: zwykly przewod w sieci z aktywnym zrodlem energii
    /// (ShortCircuitUtility.GetShortCircuitablePowerConduits - IncidentWorker_ShortCircuit.CanFireNowSub).
    /// </summary>
    public class Cond_ShortCircuitPossible : NarrativeCondition
    {
        public bool want = true;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.ShortCircuitPossible == want;
        }

        public override string Describe()
        {
            return want ? "zwarcie mozliwe" : "zwarcie niemozliwe";
        }
    }

    // =====================================================================================================
    //  WARUNKI K2 (Royalty i Anomaly, 2026-09-28). Zrodla: dziennik "Krok 9, K2 - rozpoznanie" i Docs/K2_PROPOZYCJA.md.
    // =====================================================================================================

    /// <summary>
    /// Istnieje frakcja danego rodzaju (FactionManager.FirstFactionOfDef) - kult Horaksa: IncidentWorker_HateChanters
    /// i PsychicRitualSiege biora Faction.OfHoraxCult bez sprawdzenia, wiec bez frakcji wykonanie by padlo.
    /// </summary>
    public class Cond_FactionExists : NarrativeCondition, ISnapshotKeyed
    {
        public string faction;
        public bool want = true;

        public override bool IsMet(WorldSnapshot s)
        {
            return CanonicalSet.Contains(s.FactionDefsPresent, faction) == want;
        }

        public IEnumerable<KeyValuePair<string, string>> SnapshotKeys
        {
            get { yield return new KeyValuePair<string, string>(SnapshotRequirements.Faction, faction); }
        }

        public override string Describe()
        {
            return (want ? "frakcja " : "brak frakcji ") + faction;
        }
    }

    /// <summary>
    /// Nie trwa zadanie z danym skryptem (QuestState.Ongoing). ProblemCauser: QuestNode_QuestUnique odrzuca, gdy trwa
    /// zadanie z tagiem "ProblemCauser_&lt;frakcja miejsca&gt;". Warunek jest SUROWSZY niz gra (blokuje przy dowolnym
    /// trwajacym zadaniu tego skryptu, bez wzgledu na frakcje) - swiadomie: frakcje wybiera test gry, nie snapshot.
    /// </summary>
    public class Cond_NoOngoingQuest : NarrativeCondition, ISnapshotKeyed
    {
        public string script;

        public override bool IsMet(WorldSnapshot s)
        {
            return !CanonicalSet.Contains(s.OngoingQuestScripts, script);
        }

        public IEnumerable<KeyValuePair<string, string>> SnapshotKeys
        {
            get { yield return new KeyValuePair<string, string>(SnapshotRequirements.QuestScript, script); }
        }

        public override string Describe()
        {
            return "nie trwa zadanie " + script;
        }
    }

    /// <summary>
    /// Liczba pionkow danego rodzaju na mapie w [min, max] (MapPawns.AllPawns - takze trzymane, np. na platformie).
    /// Nocisfera: IncidentWorker_Nociosphere.TryExecuteWorker odrzuca, gdy na mapie jest juz pionek tego rodzaju.
    /// </summary>
    public class Cond_PawnKindCount : NarrativeCondition, ISnapshotKeyed
    {
        public string kind;
        public int min;
        public int max = int.MaxValue;

        public override bool IsMet(WorldSnapshot s)
        {
            int n = CanonicalSet.Count(s.PawnKindCounts, kind);
            return n >= min && n <= max;
        }

        public IEnumerable<KeyValuePair<string, string>> SnapshotKeys
        {
            get { yield return new KeyValuePair<string, string>(SnapshotRequirements.PawnKind, kind); }
        }

        public override string Describe()
        {
            return "pionkow " + kind + " na mapie " + min.ToString(CultureInfo.InvariantCulture) + "-"
                   + (max == int.MaxValue ? "*" : max.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Na mapie jest chodliwe pole rzeki albo morza - warunek KONIECZNY wynurzenia z wody
    /// (PawnsArrivalModeWorker_EmergeFromWater.TryResolveRaidSpawnCenter). Gra nie sprawdza tego w CanFireNow
    /// i pada dopiero przy wykonaniu. Zasieg do kolonii i wielkosc zbiornika zostaja przy grze (akceptor).
    /// </summary>
    public class Cond_WalkableWater : NarrativeCondition
    {
        public bool want = true;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.WalkableWater == want;
        }

        public override string Describe()
        {
            return want ? "chodliwa woda na mapie" : "brak chodliwej wody na mapie";
        }
    }

    /// <summary>
    /// Kregoslup zjawy, ktory jeszcze nie buczy (IncidentWorker_RevenantEmergence: CanFireNowSub wymaga kregoslupa,
    /// a TryExecuteWorker - takiego z CompSpawnsRevenant.spawnTick &lt; 0; inaczej wykonanie pada).
    /// </summary>
    public class Cond_IdleRevenantSpine : NarrativeCondition
    {
        public int min = 1;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.IdleRevenantSpines >= min;
        }

        public override string Describe()
        {
            return "cichych kregoslupow zjawy >= " + min.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Kolonista zdolny przyjac zloty szescian (QuestNode_Root_MysteriousCargoUnnaturalCube.TestRunInt).</summary>
    public class Cond_CubeCandidate : NarrativeCondition
    {
        public int min = 1;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.CubeCandidates >= min;
        }

        public override string Describe()
        {
            return "kandydatow do szescianu >= " + min.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Kolonista zdolny dostac nienaturalne zwloki (QuestNode_Root_MysteriousCargoUnnaturalCorpse.TestRunInt).</summary>
    public class Cond_UnnaturalCorpseCandidate : NarrativeCondition
    {
        public int min = 1;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.UnnaturalCorpseCandidates >= min;
        }

        public override string Describe()
        {
            return "kandydatow do nienaturalnych zwlok >= " + min.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Minelo 30 dni od ostatniej nowej biosygnatury metalhorroru (GameComponent_Anomaly
    /// .CanNewMetalhorrorBiosignatureImplantOccur) - wspolna brama przybysza z metalhorrorem i wszczepu.
    /// </summary>
    public class Cond_MetalhorrorGate : NarrativeCondition
    {
        public bool want = true;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.MetalhorrorGateOpen == want;
        }

        public override string Describe()
        {
            return want ? "brama biosygnatury metalhorroru otwarta" : "brama biosygnatury metalhorroru zamknieta";
        }
    }

    /// <summary>
    /// Pionek mapy zdolny przyjac implant metalhorroru (IncidentWorker_MetalhorrorImplantation.GetPossiblePawns:
    /// MetalhorrorUtility.CanBeInfected i droga zakazenia w infectionVectors).
    /// </summary>
    public class Cond_InfectablePawn : NarrativeCondition
    {
        public int min = 1;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.InfectablePawns >= min;
        }

        public override string Describe()
        {
            return "pionkow zdolnych do zakazenia metalhorrorem >= " + min.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Rejestr wymagan snapshotu (krok 9, K1): klucze (rasy, rzeczy), o ktore pytaja warunki katalogu - klockow, wariantow
    /// tekstu i warunkow startu lukow. Warstwa integracji liczy w snapshotcie wylacznie te klucze. K2 doklada frakcje,
    /// skrypty zadan i rodzaje pionkow.
    /// </summary>
    public sealed class SnapshotRequirements
    {
        public const string Race = "rasa";
        public const string Thing = "rzecz";
        public const string Faction = "frakcja";
        public const string QuestScript = "zadanie";
        public const string PawnKind = "rodzaj";

        public readonly SortedSet<string> Races = new SortedSet<string>(System.StringComparer.Ordinal);
        public readonly SortedSet<string> Things = new SortedSet<string>(System.StringComparer.Ordinal);
        public readonly SortedSet<string> Factions = new SortedSet<string>(System.StringComparer.Ordinal);
        public readonly SortedSet<string> QuestScripts = new SortedSet<string>(System.StringComparer.Ordinal);
        public readonly SortedSet<string> PawnKinds = new SortedSet<string>(System.StringComparer.Ordinal);

        public static SnapshotRequirements Collect(IEnumerable<NarrativeCondition> conditions)
        {
            var r = new SnapshotRequirements();
            if (conditions == null)
            {
                return r;
            }
            foreach (NarrativeCondition c in conditions)
            {
                var k = c as ISnapshotKeyed;
                if (k == null)
                {
                    continue;
                }
                foreach (var p in k.SnapshotKeys)
                {
                    if (!CanonicalSet.IsValidKey(p.Value))
                    {
                        continue;
                    }
                    if (p.Key == Race) r.Races.Add(p.Value);
                    else if (p.Key == Thing) r.Things.Add(p.Value);
                    else if (p.Key == Faction) r.Factions.Add(p.Value);
                    else if (p.Key == QuestScript) r.QuestScripts.Add(p.Value);
                    else if (p.Key == PawnKind) r.PawnKinds.Add(p.Value);
                }
            }
            return r;
        }

        /// <summary>Wszystkie warunki klockow (twarde, miekkie, wariantow tekstu) - wejscie Collect.</summary>
        public static IEnumerable<NarrativeCondition> ConditionsOf(IEnumerable<Block> blocks)
        {
            if (blocks == null)
            {
                yield break;
            }
            foreach (Block b in blocks)
            {
                foreach (NarrativeCondition c in b.Conditions) yield return c;
                foreach (NarrativeCondition c in b.Preferences) yield return c;
                foreach (TextVariant v in b.TextVariants)
                {
                    if (v.conditions == null) continue;
                    foreach (NarrativeCondition c in v.conditions) yield return c;
                }
            }
        }

        public override string ToString()
        {
            return "rasy=" + Lista(Races) + " rzeczy=" + Lista(Things) + " frakcje=" + Lista(Factions)
                   + " zadania=" + Lista(QuestScripts) + " rodzaje=" + Lista(PawnKinds);
        }

        private static string Lista(SortedSet<string> zbior)
        {
            return string.Join(",", new List<string>(zbior).ToArray());
        }
    }
}
