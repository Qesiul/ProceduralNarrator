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
    /// Krok 1 (pionowy plaster): przepis jest trywialny, a wybor kandydata losowy.
    /// Utility AI (krok 3) i krzywa dramaturgiczna (krok 4) wepna sie w miejsce
    /// oznaczone nizej jako BuildRecipe.
    /// </summary>
    public class StorytellerComp_Generative : StorytellerComp
    {
        /// <summary>Co ile tickow gra wola MakeIntervalIncidents.</summary>
        private const float TicksPerInterval = 1000f;
        private const float TicksPerDay = 60000f;

        private EventComposer composer;

        private StorytellerCompProperties_Generative Props
        {
            get { return (StorytellerCompProperties_Generative)props; }
        }

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            // Krok 1 ogranicza sie do wydarzen mapowych.
            if (!(target is Map))
            {
                yield break;
            }

            if (!Rand.MTBEventOccurs(Props.mtbDays, TicksPerDay, TicksPerInterval))
            {
                yield break;
            }

            EnsureComposer();

            EventRecipe recipe = BuildRecipe();
            IRandomSource rng = new SeededRandom(CurrentSeed());

            ComposedEvent composed = composer.TryCompose(recipe, rng);
            if (composed == null)
            {
                // PASS jest pelnoprawna decyzja, wiec logujemy go tak samo jak wydarzenie.
                PNLog.Decision("PASS - graf nie dopuscil spojnej kompozycji dla " + recipe);
                yield break;
            }

            IncidentDef incident = DefDatabase<IncidentDef>.GetNamedSilentFail(composed.ActionPayload);
            if (incident == null)
            {
                PNLog.Error("Klocek akcji wskazuje na nieistniejacy IncidentDef: " + composed.ActionPayload);
                yield break;
            }

            if (!incident.TargetAllowed(target))
            {
                PNLog.Decision("PASS - " + incident.defName + " niedozwolony dla tego celu");
                yield break;
            }

            IncidentParms parms = GenerateParms(incident.category, target);
            parms.points *= composed.IntensityFactor;

            if (!incident.Worker.CanFireNow(parms))
            {
                PNLog.Decision("PASS - " + incident.defName + " nie moze teraz wystartowac");
                yield break;
            }

            PNLog.Decision(
                "ZLOZONO [" + string.Join(" + ", composed.Blocks.ConvertAll(b => b.ToString()).ToArray()) + "]"
                + " -> " + incident.defName
                + " | punkty=" + parms.points.ToString("0")
                + " | " + composed.Trace);
            PNLog.Decision("  opis: " + composed.Description);

            yield return new FiringIncident(incident, this, parms);
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

        /// <summary>
        /// Ziarno pochodzi z czasu gry, wiec przy tym samym stanie decyzja jest
        /// odtwarzalna (wymaganie niefunkcjonalne: determinizm).
        /// </summary>
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
                // Cichy PASS przy pustym katalogu wyglada identycznie jak "narrator
                // swiadomie odpuscil ture" - dlatego to blad, a nie zwykly komunikat.
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
