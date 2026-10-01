using System;
using System.Collections.Generic;
using System.Globalization;
using ProceduralNarrator.Core.Evaluation;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration.Evaluation
{
    /// <summary>
    /// [PN-DZIEN] - stan kazdej mapy domowej na koniec doby (krok 9, etap L, decyzja L-1), w kazdej grze. Linia w pierwszym
    /// ticku nowej doby (DayClock). Odczyty bez skutkow ubocznych: bogactwo z pol gry (WealthReader), punkty tylko gdy nie
    /// wymuszaja przeliczenia, zagrozenie z GenHostility (nie DangerWatcher, ktory zapisuje wlasny cache).
    /// </summary>
    internal sealed class DayLogger
    {
        private readonly DayClock zegar = new DayClock();

        public void Tick(int tick)
        {
            int doba = zegar.Advance(tick);
            if (doba < 0 || Find.Maps == null)
            {
                return;
            }
            var mapy = new List<Map>();
            foreach (Map m in Find.Maps)
            {
                if (m != null && m.IsPlayerHome)
                {
                    mapy.Add(m);
                }
            }
            mapy.Sort((a, b) => a.uniqueID.CompareTo(b.uniqueID));
            for (int i = 0; i < mapy.Count; i++)
            {
                PNLog.Day(Policz(mapy[i], doba));
            }
        }

        public void Reset()
        {
            zegar.Reset();
        }

        private static PNLog.DayFields Policz(Map m, int doba)
        {
            var d = new PNLog.DayFields
            {
                Dzien = doba,
                Mapa = m.uniqueID,
                Kolonisci = m.mapPawns == null ? -1 : m.mapPawns.FreeColonistsCount,
                NaMapie = WorldSnapshotBuilder.CountColonistsOnMap(m),
                Powaleni = WorldSnapshotBuilder.CountAcutelyDownedColonists(m),
                Zagrozenie = GenHostility.AnyHostileActiveThreatToPlayer(m) ? 1 : 0,
                Pora = WorldSnapshotBuilder.SeasonIndex(GenLocalDate.Season(m)),
                Temperatura = m.mapTemperature == null ? float.NaN : m.mapTemperature.OutdoorTemp,
                Warunki = Warunki(m),
                Zywnosc = m.resourceCounter == null ? -1f : m.resourceCounter.TotalHumanEdibleNutrition,
                Nastroj = Nastroj(m)
            };
            float b;
            int wiek;
            if (!m.IsPocketMap && WealthReader.TryRead(m, out b, out wiek))
            {
                d.Bogactwo = b;
                d.BogactwoWiek = wiek;
                d.BogactwoWzgl = WealthReference.Relative(b, GenDate.DaysPassedSinceSettle);
                if (WealthReader.NoRecount(m))
                {
                    d.Punkty = StorytellerUtility.DefaultThreatPointsNow(m);
                }
            }
            if (ModsConfig.AnomalyActive && Find.Anomaly != null)
            {
                d.Monolit = Find.Anomaly.Level;
                d.MonolitDef = Find.Anomaly.LevelDef == null ? null : Find.Anomaly.LevelDef.defName;
            }
            if (Find.StoryWatcher != null)
            {
                if (Find.StoryWatcher.watcherAdaptation != null)
                {
                    d.Adaptacja = Find.StoryWatcher.watcherAdaptation.AdaptDays;
                }
                if (Find.StoryWatcher.statsRecord != null)
                {
                    d.Napadow = Find.StoryWatcher.statsRecord.numRaidsEnemy;
                    d.ThreatBig = Find.StoryWatcher.statsRecord.numThreatBigs;
                    d.Poleglych = Find.StoryWatcher.statsRecord.colonistsKilled;
                }
            }
            return d;
        }

        /// <summary>Aktywne warunki gry mapy i swiata: defName bez powtorzen, ordynalnie, po przecinku.</summary>
        private static string Warunki(Map m)
        {
            var zbior = new SortedSet<string>(StringComparer.Ordinal);
            Dodaj(zbior, m.gameConditionManager);
            if (Find.World != null)
            {
                Dodaj(zbior, Find.World.gameConditionManager);
            }
            return string.Join(",", new List<string>(zbior).ToArray());
        }

        private static void Dodaj(SortedSet<string> zbior, GameConditionManager g)
        {
            if (g == null || g.ActiveConditions == null)
            {
                return;
            }
            foreach (GameCondition c in g.ActiveConditions)
            {
                if (c != null && c.def != null)
                {
                    zbior.Add(c.def.defName);
                }
            }
        }

        /// <summary>Sredni nastroj wolnych kolonistow na mapie (0-1); NaN, gdy nikt nie ma potrzeby nastroju.</summary>
        private static float Nastroj(Map m)
        {
            if (m.mapPawns == null)
            {
                return float.NaN;
            }
            float suma = 0f;
            int n = 0;
            foreach (Pawn p in m.mapPawns.FreeColonistsSpawned)
            {
                if (p != null && p.needs != null && p.needs.mood != null)
                {
                    suma += p.needs.mood.CurLevel;
                    n++;
                }
            }
            return n == 0 ? float.NaN : suma / n;
        }
    }
}
