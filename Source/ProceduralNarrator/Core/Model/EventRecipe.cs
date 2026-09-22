namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// "Przepis" na wydarzenie - OGRANICZENIA KOMPOZYCJI przekazywane warstwie kompozycji.
    /// Dzis jedno: wymagany tag klocka akcji.
    ///
    /// Intencja i docelowa moc NIE jada w przepisie, tylko w DecisionContext (TurnPlanner:
    /// napiecie -> intencja -> regula kryzysu -> kontekst), bo konsumuje je scoring, a nie
    /// kompozycja. Do polerowania etapu 4 przepis mial jeszcze pola TargetIntensity i Intent,
    /// wypelniane w BuildRecipe, ale NIE MIALY ZADNEGO CZYTELNIKA - ani w grze, ani w sladzie
    /// (TryCompose, jedyne miejsce wypisujace przepis, nie jest wolane przez mod). Komentarz
    /// przy polu twierdzil przy tym, ze czyta je Factor_IntentAlignment, co bylo nieprawda.
    /// Pola usunieto zamiast je "dopiac": filtrowanie po intencji w kompozycji odcinaloby
    /// kandydatow, zanim ktokolwiek policzy ich uzytecznosc, a utility AI ma wazyc, a nie
    /// wykluczac - slad decyzji ma pokazywac, ile narrator poswiecil, zeby posluchac krzywej.
    /// </summary>
    public class EventRecipe
    {
        /// <summary>Wymagany tag klocka akcji (np. "militarny"). null = dowolny.</summary>
        public string RequiredActionTag;

        public override string ToString()
        {
            return "Recipe(tag=" + (RequiredActionTag ?? "any") + ")";
        }
    }
}
