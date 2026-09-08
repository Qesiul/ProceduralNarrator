using System.Globalization;

namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// "Przepis" na wydarzenie - wyjscie warstwy planowania, wejscie warstwy kompozycji.
    /// W kroku 1 wypelniany trywialnie; od kroku 4 produkuje go krzywa dramaturgiczna.
    /// </summary>
    public class EventRecipe
    {
        /// <summary>Wymagany tag klocka akcji (np. "militarny"). null = dowolny.</summary>
        public string RequiredActionTag;

        /// <summary>
        /// Docelowa intensywnosc wydarzenia wyznaczona przez krzywa dramaturgiczna, wyrazona
        /// NA SKALI IntensityLevel: -2 = VeryLow, 0 = Normal, +2 = VeryHigh. Typ jest float,
        /// bo krzywa interpoluje plynnie, a kandydaci maja wartosci calkowite.
        ///
        /// SEMANTYKA ZMIENILA SIE W KROKU 4 i jest to zmiana swiadoma. Do kroku 3 pole mialo
        /// wartosc 1f i NIE MIALO ZADNEGO KONSUMENTA - nikt go nie czytal poza ToString().
        /// Przy tym 1f na skali IntensityLevel znaczy "High", wiec martwe pole nioslo wartosc
        /// mylaca: kto by je wtedy podlaczyl, dostalby narratora domyslnie podkrecajacego
        /// kazde zdarzenie. Redefinicja byla darmowa wlasnie dlatego, ze konsumenta nie bylo.
        ///
        /// Czyta je Factor_IntentAlignment jako skladnik "zgodnosci mocy".
        /// </summary>
        public float TargetIntensity;

        /// <summary>
        /// Intencja narracyjna na te ture. Przepis NIE filtruje po niej kandydatow - intencja
        /// jest przekazywana dalej do kontekstu decyzyjnego i wchodzi do scoringu jako jeden
        /// z czynnikow, ktory moze zostac przegloszowany przez pozostale.
        ///
        /// Rozdzial jest celowy: filtrowanie po intencji odcinaloby kandydatow, zanim
        /// ktokolwiek policzy ich uzytecznosc, wiec slad decyzji nie pokazywalby, ile
        /// narrator poswiecil, zeby posluchac krzywej. Utility AI ma wazyc, a nie wykluczac.
        ///
        /// W kroku 3 warstwa planowania jeszcze nie istnieje i BuildRecipe() ustawia stale
        /// Hold; krok 4 podmieni te stala na wyjscie krzywej dramaturgicznej. Domyslna wartosc
        /// pola jest tu po to, zeby przepis zbudowany w tescie albo w walidatorze offline nigdy
        /// nie mial intencji nieokreslonej.
        /// </summary>
        public Intent Intent = Intent.Hold;

        public override string ToString()
        {
            // Liczba przez InvariantCulture, bo ta linia trafia do logu, a przy polskiej
            // kulturze separatorem dziesietnym jest przecinek - ten sam, ktorym rozdzielone
            // sa pola w linii maszynowej. Parser w Pythonie rozjechalby sie po cichu.
            return "Recipe(tag=" + (RequiredActionTag ?? "any")
                   + ", intensity=" + TargetIntensity.ToString("0.00", CultureInfo.InvariantCulture)
                   + ", intencja=" + Intent + ")";
        }
    }
}
