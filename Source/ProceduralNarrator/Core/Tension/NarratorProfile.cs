using System.Text;
using ProceduralNarrator.Core.Decision;

namespace ProceduralNarrator.Core.Tension
{
    /// <summary>
    /// OSOBOWOSC NARRATORA - wzorzec Strategia z sekcji 4 koncepcji, w postaci danych.
    ///
    /// Profil niesie WYLACZNIE to, co odroznia jednego narratora od drugiego: wektor wag
    /// scoringu i ksztalt krzywej napiecia. Cala reszta konfiguracji - budzet ocen, progi
    /// jakosci, temperatury softmaksu, parametry gestosci PASS - jest WSPOLNA i zostaje
    /// w StorytellerCompProperties_Generative.
    ///
    /// To jest rozstrzygniecie metodologiczne, nie oszczednosciowe. Gdyby profil mogl
    /// zmieniac takze maszynerie (inny budzet, inna temperatura), roznica miedzy dwoma
    /// przebiegami przestalaby byc przypisywalna osobowosci - a caly sens tego typu polega
    /// na tym, ze przy tym samym ziarnie i tym samym stanie swiata JEDYNA zmienna jest profil.
    ///
    /// Daje to takze policzalne porownanie do pracy. Zmierzone dekompilacja: Cassandra
    /// i Phoebe maja 16 z 20 compow identycznych co do bajta, a wszystkie trzy waniliowe
    /// narratory dziela DOKLADNIE TE SAME krzywe adaptacji - cala ich osobowosc to siedem
    /// liczb sterujacych TEMPEM. Nasza to cztery wagi narracyjne plus ksztalt krzywej napiecia.
    ///
    /// PROFIL NIE JEST WYBIERANY PRZEZ GRACZA. Menu wyboru narratora jest dokladnie tym
    /// modelem, ktory ta praca krytykuje (Cassandra/Phoebe/Randy to trzy pozycje na liscie).
    /// Profil jest stanem WEWNETRZNYM systemu, losowanym raz na rozgrywke i utrwalanym
    /// w zapisie gry - patrz NarratorMemoryComponent.
    /// </summary>
    public class NarratorProfile
    {
        /// <summary>Identyfikator profilu (defName). Trafia do zapisu gry i do kolumny logu.</summary>
        public string Id = string.Empty;

        /// <summary>Nazwa czytelna dla czlowieka - wylacznie do logu, nie do UI gracza.</summary>
        public string Label = string.Empty;

        /// <summary>
        /// Waga w losowaniu profilu na starcie rozgrywki. Wieksza = czesciej wybierany.
        /// Zero wylacza profil, nie usuwajac go z katalogu - przydatne przy strojeniu.
        /// </summary>
        public float SelectionWeight = 1f;

        /// <summary>Wagi czynnikow ZDARZENIOWYCH. Nigdy null.</summary>
        public ScoringWeights Weights = new ScoringWeights();

        /// <summary>Ksztalt krzywej napiecia. Nigdy null.</summary>
        public TensionParams Tension = new TensionParams();

        /// <summary>
        /// Profil awaryjny: uzywany, gdy katalog profili jest pusty albo gdy zapis wskazuje
        /// profil, ktorego juz nie ma (np. po usunieciu go z XML miedzy sesjami).
        /// Odpowiada domyslnym wartosciom z kodu, wiec narrator dziala dalej sensownie.
        /// </summary>
        public static NarratorProfile Fallback()
        {
            return new NarratorProfile
            {
                Id = "PN_Profil_Awaryjny",
                Label = "awaryjny (domyslne wartosci z kodu)",
                SelectionWeight = 0f,
                Weights = new ScoringWeights(),
                Tension = TensionParams.Default()
            };
        }

        public override string ToString()
        {
            var sb = new StringBuilder(160);
            sb.Append(string.IsNullOrEmpty(Id) ? "(bez id)" : Id);
            if (!string.IsNullOrEmpty(Label))
            {
                sb.Append(" \"").Append(Label).Append('"');
            }
            sb.Append(" | wagi: ").Append(Weights == null ? "(brak)" : Weights.Describe());
            sb.Append(" | ").Append(Tension == null ? "(brak krzywej)" : Tension.ToString());
            return sb.ToString();
        }
    }
}
