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

            // Jedna kompozycja na ture to za malo: klocek akcji moze wskazac incydent,
            // ktory w tym kontekscie nie ma prawa wystartowac (Infestation bez stropu
            // gorskiego, RaidEnemy bez odpowiedniej frakcji lub przy zbyt niskich punktach).
            // Odmowa CanFireNow to informacja o kontekscie, a nie powod, zeby odpuscic ture.
            // Od kroku 3 te odrzucenia stana sie czynnikiem scoringu ("logiczna zasadnosc
            // w kontekscie"), a nie slepym ponawianiem.
            var odrzucone = new List<string>();

            for (int proba = 0; proba < MaxCompositionAttempts; proba++)
            {
                // Ziarno zalezy od proby, wiec kolejne podejscia sa rozne, ale nadal
                // w pelni odtwarzalne przy tym samym stanie gry.
                IRandomSource rng = new SeededRandom(CurrentSeed() + proba * SeedStride);

                ComposedEvent composed = composer.TryCompose(recipe, rng);
                if (composed == null)
                {
                    odrzucone.Add("graf: brak spojnej kompozycji");
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
                parms.points *= composed.IntensityFactor;

                if (!incident.Worker.CanFireNow(parms))
                {
                    odrzucone.Add(incident.defName + ": CanFireNow=false");
                    continue;
                }

                PNLog.Decision(
                    "ZLOZONO [" + string.Join(" + ", composed.Blocks.ConvertAll(b => b.ToString()).ToArray()) + "]"
                    + " -> " + incident.defName
                    + " | punkty=" + parms.points.ToString("0")
                    + " | proba " + (proba + 1) + "/" + MaxCompositionAttempts
                    + " | " + composed.Trace);
                PNLog.Decision("  opis: " + composed.Description);
                if (odrzucone.Count > 0)
                {
                    PNLog.Decision("  odrzucone po drodze: " + string.Join("; ", odrzucone.ToArray()));
                }

                yield return new FiringIncident(incident, this, parms);
                yield break;
            }

            // PASS jest pelnoprawna decyzja, wiec logujemy go ze sladem tak samo jak wydarzenie.
            PNLog.Decision("PASS po " + MaxCompositionAttempts + " probach | odrzucone: "
                           + string.Join("; ", odrzucone.ToArray()));
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
