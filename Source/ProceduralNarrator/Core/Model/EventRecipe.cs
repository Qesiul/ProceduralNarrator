namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// "Przepis" na wydarzenie - wyjscie warstwy decyzyjnej, wejscie warstwy kompozycji.
    /// W kroku 1 wypelniany trywialnie; od kroku 3 produkuje go utility AI.
    /// </summary>
    public class EventRecipe
    {
        /// <summary>Wymagany tag klocka akcji (np. "militarny"). null = dowolny.</summary>
        public string RequiredActionTag;

        /// <summary>Docelowa intensywnosc wydarzenia wyznaczona przez krzywa dramaturgiczna.</summary>
        public float TargetIntensity = 1f;

        public override string ToString()
        {
            return "Recipe(tag=" + (RequiredActionTag ?? "any") + ", intensity=" + TargetIntensity + ")";
        }
    }
}
