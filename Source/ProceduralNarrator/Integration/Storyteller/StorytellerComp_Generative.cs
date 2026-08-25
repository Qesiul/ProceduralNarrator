using System.Collections.Generic;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;
using ProceduralNarrator.Integration.Defs;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration.Storyteller
{
    /// <summary>
    /// Warstwa integracji (sekcja 5.1) - jedyny punkt styku rdzenia z API gry.
    /// Gra cyklicznie prosi ten komponent o wydarzenia; my zwracamy te ZLOZONE
    /// przez rdzen z klockow, zamiast wybierac gotowce z puli.
    ///
    /// Stan po kroku 2: kompozycja respektuje twarde warunki kontekstu, a kazdy
    /// kandydat ma policzone contextFit. Wybor kandydata jest nadal LOSOWY -
    /// scoring z utility AI wchodzi w kroku 3 i to on zacznie uzywac contextFit.
    /// </summary>
    public class StorytellerComp_Generative : StorytellerComp
    {
        private const float TicksPerInterval = 1000f;
        private const float TicksPerDay = 60000f;

        /// <summary>Ile roznych kompozycji probujemy, zanim uznamy ture za PASS.</summary>
        private const int MaxCompositionAttempts = 6;

        /// <summary>Odstep ziarna miedzy kolejnymi probami (liczba pierwsza, zeby nie cyklowac).</summary>
        private const int SeedStride = 7919;

        private EventComposer composer;

        private StorytellerCompProperties_Generative Props
        {
            get { return (StorytellerCompProperties_Generative)props; }
        }

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            var map = target as Map;
            if (map == null)
            {
                yield break;
            }

            if (!Rand.MTBEventOccurs(Props.mtbDays, TicksPerDay, TicksPerInterval))
            {
                yield break;
            }

            EnsureComposer();

            // Stan swiata zamrazamy RAZ na decyzje - wszystkie proby ocenia ten sam
            // kontekst, wiec decyzja jest odtwarzalna i da sie ja w calosci zalogowac.
            WorldSnapshot snapshot = WorldSnapshotBuilder.Build(map);
            EventRecipe recipe = BuildRecipe();
            var odrzucone = new List<string>();

            for (int proba = 0; proba < MaxCompositionAttempts; proba++)
            {
                IRandomSource rng = new SeededRandom(CurrentSeed() + proba * SeedStride);

                ComposedEvent composed = composer.TryCompose(recipe, rng, snapshot);
                if (composed == null)
                {
                    odrzucone.Add("graf/warunki: brak spojnej kompozycji");
                    continue;
                }

                IncidentDef incident = DefDatabase<IncidentDef>.GetNamedSilentFail(composed.ActionPayload);
                if (incident == null)
                {
                    PNLog.Error("Klocek akcji wskazuje na nieistniejacy IncidentDef: " + composed.ActionPayload);
                    odrzucone.Add(composed.ActionPayload + ": brak IncidentDef");
                    continue;
                }

                if (!incident.TargetAllowed(target))
                {
                    odrzucone.Add(incident.defName + ": cel niedozwolony");
                    continue;
                }

                IncidentParms parms = GenerateParms(incident.category, target);
                parms.points *= IntensityTable.PointsFactor(composed.Intensity);

                if (!incident.Worker.CanFireNow(parms))
                {
                    odrzucone.Add(incident.defName + ": CanFireNow=false");
                    continue;
                }

                LogDecision(composed, incident, parms, snapshot, proba, odrzucone);
                yield return new FiringIncident(incident, this, parms);
                yield break;
            }

            PNLog.Decision("PASS po " + MaxCompositionAttempts + " probach"
                           + " | kontekst: " + snapshot
                           + " | odrzucone: " + string.Join("; ", odrzucone.ToArray()));
        }

        private void LogDecision(ComposedEvent composed, IncidentDef incident, IncidentParms parms,
                                 WorldSnapshot snapshot, int proba, List<string> odrzucone)
        {
            PNLog.Decision(
                "ZLOZONO [" + string.Join(" + ", composed.Blocks.ConvertAll(b => b.ToString()).ToArray()) + "]"
                + " -> " + incident.defName
                + " | " + composed.Theme + "/" + composed.Valence + "/" + composed.Scale
                + " | intensywnosc=" + composed.Intensity
                + " punkty=" + parms.points.ToString("0")
                + " | proba " + (proba + 1) + "/" + MaxCompositionAttempts);
            PNLog.Decision("  kontekst: " + snapshot);
            PNLog.Decision("  dopasowanie: " + composed.FitTrace);
            PNLog.Decision("  opis: " + composed.Description);
            if (odrzucone.Count > 0)
            {
                PNLog.Decision("  odrzucone po drodze: " + string.Join("; ", odrzucone.ToArray()));
            }
        }

        /// <summary>
        /// Miejsce wpiecia warstwy decyzyjnej. Od kroku 3 przepis produkuje utility AI
        /// na podstawie kontekstu, a od kroku 4 intensywnosc wyznacza krzywa dramaturgiczna.
        /// </summary>
        private EventRecipe BuildRecipe()
        {
            return new EventRecipe
            {
                RequiredActionTag = Props.requiredActionTag,
                TargetIntensity = 1f
            };
        }

        private static int CurrentSeed()
        {
            return Find.TickManager != null ? Find.TickManager.TicksGame : 0;
        }

        private void EnsureComposer()
        {
            if (composer != null)
            {
                return;
            }

            List<Block> blocks;
            CompatibilityGraph graph;
            BlockCatalogLoader.Load(out blocks, out graph);

            composer = new EventComposer(blocks, graph);

            if (blocks.Count == 0)
            {
                PNLog.Error("Katalog klockow PUSTY - narrator nie zlozy zadnego wydarzenia. "
                            + "Patrz diagnostyka startowa wyzej w logu.");
            }
            else
            {
                PNLog.Decision("Katalog klockow zaladowany: " + blocks.Count + " klockow, "
                               + graph.ForbiddenEdgeCount + " zabronionych krawedzi.");
            }
        }
    }
}
