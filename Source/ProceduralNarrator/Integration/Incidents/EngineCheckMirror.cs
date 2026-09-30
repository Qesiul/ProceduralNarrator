using System.Collections.Generic;
using ProceduralNarrator.Core.Composition;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration.Incidents
{
    /// <summary>
    /// LUSTRO SPRAWDZEN GRY - czesc integracyjna (krok 9, etap K0; decyzja autora E0-2). Rdzen: EngineMirror.
    ///
    /// Odwzorowuje czesc IncidentWorker.CanFireNow (1.5.4063, RimWorld/IncidentWorker.cs:35-117), ktora NIE zalezy
    /// od parametrow incydentu i lezy PRZED pamiecia podreczna werdyktow: cel, najwczesniejszy dzien, ustawienia
    /// trudnosci, biom, scenariusz, populacja, odstep od poprzedniego razu (FiredTooRecently), poziom zagrozenia
    /// Anomaly, warunki gry z preventIncidents, koniec gry, okno po nowych wedrowcach oraz przebudzenie pustki.
    ///
    /// Czego NIE odwzorowuje (celowo):
    ///  - progi punktow (minThreatPoints/maxThreatPoints) - pilnuje ich sito dokladne w IncidentParmsBuilder,
    ///    bo zaleza od mocy kandydata;
    ///  - CanFireNowSub - zalezy od parametrow i stanu mapy (komorki wejscia, zwierzeta itd.), zostaje
    ///    akceptorowi (jedno pytanie do gry); warunki twarde klockow odwzorowuja jego systematyczna czesc.
    ///
    /// Czyste odczyty: bez CanFireNow (nie dotyka pamieci podrecznej werdyktow), bez Rand. Wyjatek w odczycie
    /// wylacza lustro na sesje (jedno ostrzezenie) - narrator wraca wtedy do zachowania sprzed kroku 9.
    /// </summary>
    internal static class EngineCheckMirror
    {
        /// <summary>Zablokowane payloady w postaci kanonicznej EngineMirror (pusta = nic).</summary>
        public static string BlockedCanonical(IList<string> payloads, Map map)
        {
            if (payloads == null || map == null)
            {
                return string.Empty;
            }
            var zablokowane = new List<string>();
            for (int i = 0; i < payloads.Count; i++)
            {
                IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail(payloads[i]);
                if (def != null && Blocked(def, map))
                {
                    zablokowane.Add(def.defName);
                }
            }
            return EngineMirror.Canonical(zablokowane);
        }

        /// <summary>Czy gra odrzucilaby incydent niezaleznie od parametrow (kolejnosc jak w CanFireNow).</summary>
        public static bool Blocked(IncidentDef def, Map map)
        {
            if (!def.TargetAllowed(map))
            {
                return true;
            }
            if (GenDate.DaysPassedSinceSettle < def.earliestDay)
            {
                return true;
            }
            Difficulty trudnosc = Find.Storyteller != null ? Find.Storyteller.difficulty : null;
            if (trudnosc != null)
            {
                if (!trudnosc.AllowedBy(def.disabledWhen))
                {
                    return true;
                }
                if (def.category == IncidentCategoryDefOf.ThreatBig && !trudnosc.allowBigThreats)
                {
                    return true;
                }
            }
            if (def.allowedBiomes != null || def.disallowedBiomes != null)
            {
                BiomeDef biom = Find.WorldGrid[map.Tile].biome;
                if (def.allowedBiomes != null && !def.allowedBiomes.Contains(biom))
                {
                    return true;
                }
                if (def.disallowedBiomes != null && def.disallowedBiomes.Contains(biom))
                {
                    return true;
                }
            }
            if (Find.Scenario != null)
            {
                foreach (ScenPart czesc in Find.Scenario.AllParts)
                {
                    var wylaczenie = czesc as ScenPart_DisableIncident;
                    if (wylaczenie != null && wylaczenie.Incident == def)
                    {
                        return true;
                    }
                }
            }
            if (def.minPopulation > 0
                && PawnsFinder.AllMapsCaravansAndTravelingTransportPods_Alive_FreeColonists.Count < def.minPopulation)
            {
                return true;
            }
            if (def.Worker.FiredTooRecently(map))
            {
                return true;
            }
            if (def.minGreatestPopulation > 0 && Find.StoryWatcher.statsRecord.greatestPopulation < def.minGreatestPopulation)
            {
                return true;
            }
            if (ModsConfig.AnomalyActive && def.IsAnomalyIncident
                && Find.Anomaly.LevelDef.anomalyThreatTier < def.minAnomalyThreatLevel)
            {
                return true;
            }
            foreach (GameCondition warunek in map.gameConditionManager.ActiveConditions)
            {
                if (warunek.def.preventIncidents)
                {
                    return true;
                }
            }
            bool zagrozenie = def.category == IncidentCategoryDefOf.ThreatBig || def.category == IncidentCategoryDefOf.ThreatSmall;
            if (Find.GameEnder.gameEnding && zagrozenie)
            {
                return true;
            }
            if (Find.TickManager.TicksGame < Find.GameEnder.newWanderersCreatedTick + 300000
                && def.category == IncidentCategoryDefOf.ThreatBig)
            {
                return true;
            }
            if (ModsConfig.AnomalyActive && Find.Anomaly.VoidAwakeningActive())
            {
                return true;
            }
            return false;
        }
    }
}
