using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Tension
{
    /// <summary>Wynik warstwy planowania dla jednej tury - wszystko, co z niej wychodzi, ze sladami.</summary>
    public class TurnPlan
    {
        public TensionReading Tension;
        public CrisisReading Crisis;

        /// <summary>Intencja i docelowa moc PO regule kryzysu skrajnego.</summary>
        public IntentDecision Intent;

        /// <summary>Kontekst decyzji zbudowany z powyzszych - jedyna droga, ktora kryzys trafia do straznika serii.</summary>
        public DecisionContext Context;
    }

    /// <summary>
    /// WARSTWA PLANOWANIA JEDNEJ TURY: napiecie -&gt; intencja -&gt; regula kryzysu -&gt; kontekst decyzji.
    ///
    /// DLACZEGO TO ZYJE W Core/ - ten sam argument co przy TurnRunner. Do polerowania etapu 4 ten
    /// lancuch byl spinany w StorytellerComp_Generative, czyli w warstwie, ktorej walidator offline
    /// nie kompiluje. Testy musialy wiec podawac flage kryzysu do kontekstu LITERALEM, a pomylenie
    /// kolejnosci (intencja sprzed ApplyCrisis w kontekscie, stale false w Create) nie mialo zadnego
    /// wykrywacza. Teraz integracja wola jedna funkcje, a walidator sprawdza wszystkie jej wejscia:
    /// flage kryzysu (TEST 10h) oraz - od drugiego przegladu etapu 4 - parametry kryzysu z XML
    /// (enabled=false, inny prog), parametry krzywej profilu, historie i dzien gry w napieciu oraz
    /// w kontekscie (TEST 10i). Wczesniej zignorowanie ktoregokolwiek z tych wejsc zostawialo
    /// walidator zielony, bo testy wolaly Plan wylacznie z pusta historia i domyslnym &lt;crisis&gt;.
    ///
    /// Funkcja CZYSTA: zero stanu, zero losowosci - jak TensionModel i CrisisDetector, ktore sklada.
    /// </summary>
    public static class TurnPlanner
    {
        public static TurnPlan Plan(TensionModel tensionModel, CrisisParams crisisParams, EventHistory history,
                                    WorldSnapshot snapshot, float gameDay)
        {
            var plan = new TurnPlan();

            // Liczone PO snapshocie, bo czlon sytuacyjny czyta powalonych i poziom zagrozenia.
            plan.Tension = tensionModel.Compute(history, snapshot, gameDay);
            IntentDecision zamiar = IntentSelector.Select(plan.Tension.Tension, tensionModel.Parameters);

            // KRYZYS SKRAJNY - regula wspolna dla wszystkich profili (maszyneria, nie osobowosc).
            // Nakladana PO krzywej: Select zostaje nietkniety, a nadpisanie intencji i mocy jest
            // widoczne w sladzie. Napiecie w kontekscie zostaje surowe.
            plan.Crisis = CrisisDetector.Evaluate(snapshot, crisisParams);
            plan.Intent = IntentSelector.ApplyCrisis(zamiar, plan.Crisis);

            plan.Context = DecisionContext.Create(snapshot, history, gameDay,
                                                  plan.Intent.Intent, plan.Intent.TargetIntensity,
                                                  plan.Tension.Tension, plan.Crisis.Extreme);
            return plan;
        }
    }
}
