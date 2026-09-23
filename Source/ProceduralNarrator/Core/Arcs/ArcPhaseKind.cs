using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Arcs
{
    /// <summary>
    /// Rodzaj fazy luku narracyjnego (sekcja 5.4 koncepcji: zasiew -> eskalacja -> kulminacja
    /// -> rozwiazanie). Kolejnosc wartosci jest KANONICZNA: fazy luku musza isc scisle rosnaco,
    /// co pilnuje ArcCatalog.Validate. Eskalacja jest opcjonalna (luk 3-fazowy, np. Scigani),
    /// Zasiew i Rozwiazanie sa obowiazkowe - pierwszy otwiera watek, drugie go zamyka.
    /// </summary>
    public enum ArcPhaseKind
    {
        Seed,
        Escalation,
        Climax,
        Resolution
    }

    /// <summary>
    /// Regula "krzywa decyduje KIEDY, luk decyduje CO" (decyzja autora nr 2, krok 5).
    ///
    /// Faza luku steruje wyborem tylko wtedy, gdy walencja zdarzenia, ktorego oczekuje, jest
    /// zgodna z biezaca intencja krzywej dramaturgicznej. Inaczej luk CZEKA - nie walczy z krzywa
    /// o tempo i nie zmienia osobowosci profilu. Kryzys skrajny nie potrzebuje osobnej reguly:
    /// wymusza Breathe, wiec fazy negatywne czekaja same, a pozytywne rozwiazanie moze przyjsc.
    ///
    ///                Escalate   Hold   Breathe
    ///   Negative        tak      tak      -
    ///   Neutral         tak      tak     tak
    ///   Positive         -       tak     tak
    ///
    /// Intent.Pass jest zarezerwowany i nigdy nie jest zwracany; gdyby sie pojawil, traktujemy go
    /// jak Breathe (najbardziej zachowawcze odczytanie "nie dzialaj").
    /// </summary>
    public static class ArcIntentRules
    {
        public static bool Compatible(Valence valence, Intent intent)
        {
            switch (valence)
            {
                case Valence.Negative:
                    return intent == Intent.Escalate || intent == Intent.Hold;
                case Valence.Positive:
                    return intent == Intent.Hold || intent == Intent.Breathe || intent == Intent.Pass;
                default:
                    return true;
            }
        }
    }
}
