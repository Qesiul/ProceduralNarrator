using RimWorld;

namespace ProceduralNarrator.Integration.Storyteller
{
    /// <summary>
    /// Wlasciwosci komponentu narratora, konfigurowane z XML w StorytellerDef.
    /// compClass spina definicje XML z klasa wykonawcza.
    /// </summary>
    public class StorytellerCompProperties_Generative : StorytellerCompProperties
    {
        /// <summary>Sredni czas miedzy wydarzeniami (mean time between), w dniach gry.</summary>
        public float mtbDays = 1.2f;

        /// <summary>Tag wymagany od klocka akcji. Od kroku 3 wyznacza go utility AI.</summary>
        public string requiredActionTag;

        public StorytellerCompProperties_Generative()
        {
            compClass = typeof(StorytellerComp_Generative);
        }
    }
}
