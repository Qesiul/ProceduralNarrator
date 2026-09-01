using System.Globalization;
using System.Text;

namespace ProceduralNarrator.Core.Composition
{
    /// <summary>
    /// Sprawozdanie z jednego przelotu po przestrzeni wariantow POJEDYNCZEJ akcji.
    ///
    /// Po co osobny typ, skoro enumeracja i tak zwraca liste kandydatow: sama lista nie mowi,
    /// czy zwrocono CALA przestrzen, czy jej probke. Bez tego rozroznienia pole "wyczerpano"
    /// w logu ewaluacyjnym byloby zgadywane, a na nim stoi metryka pokrycia przestrzeni
    /// (kandydatow / przestrzen) z rozdzialu o ewaluacji.
    ///
    /// Wszystkie liczby sa surowe - agregacja po akcjach zyje w CandidateSet.
    /// </summary>
    public class VariantEnumerationStats
    {
        /// <summary>Id klocka akcji, ktorego dotyczy przelot. Klucz laczacy z PerAction w logu.</summary>
        public string ActionId;

        /// <summary>
        /// N_i: ile LISCI drzewa faktycznie odwiedzono.
        ///
        /// Gdy Truncated == false, to jest DOKLADNY rozmiar przestrzeni wariantow tej akcji
        /// w tym kontekscie. Gdy Truncated == true, przelot przerwano na TraversalCap, wiec
        /// wartosc jest tylko DOLNYM OGRANICZENIEM - policzone z niej pokrycie wyjdzie
        /// zawyzone (moze przekroczyc 1). To ostrzezenie musi tu stac, bo pole wyglada
        /// na zwykly licznik i az prosi sie o uzycie jako mianownika.
        /// </summary>
        public int VariantsSeen;

        /// <summary>Ile wariantow faktycznie zwrocono (rozmiar proby, == wklad tej akcji do Candidates).</summary>
        public int Returned;

        /// <summary>Limit, z jakim wywolano enumeracje (K w przebiegu 1, K + extra w przebiegu 2).</summary>
        public int Limit;

        /// <summary>
        /// Czy przestrzen wariantow tej akcji zostala przebadana W CALOSCI.
        /// Definicja: Returned == VariantsSeen AND NOT Truncated.
        ///
        /// Uwaga na przypadek N_i == 0 (slot wymagany bez zgodnego klocka): to jest
        /// wyczerpanie, a nie awaria - przestrzen naprawde jest pusta.
        /// Przeciwnie przy limicie <= 0: tam nie zajrzelismy w przestrzen ANI RAZU,
        /// wiec twierdzenie o wyczerpaniu byloby falszem wpisanym do danych badawczych.
        /// </summary>
        public bool Exhausted;

        /// <summary>Czy przelot przerwano na zaworze bezpieczenstwa TraversalCap.</summary>
        public bool Truncated;

        /// <summary>
        /// Ile razy siegnieto po rng.Next w tym przelocie. Zero oznacza pelna enumeracje
        /// w kolejnosci kanonicznej, czyli wynik niezalezny od ziarna.
        /// Wzor: RandomDraws = max(0, VariantsSeen - Limit).
        /// </summary>
        public int RandomDraws;

        /// <summary>
        /// Znacznik trybu przelotu, gdy odbiegal on od normalnego: "nie-akcja", "bezRng",
        /// "akcja spoza katalogu", "limit&lt;=0". Puste w przypadku typowym.
        ///
        /// Osobne pole, a nie doklejanie do ActionId, bo ActionId jest kluczem laczenia
        /// wierszy w analizie i musi zostac czystym identyfikatorem klocka.
        /// </summary>
        public string Trace;

        /// <summary>
        /// Jedna linia do logu czytelnego (w CandidateSet poprzedzona wcieciem).
        /// Liczby przez InvariantCulture - ta sama linia bywa parsowana w Pythonie,
        /// a polski separator dziesietny rozjechalby podzial na pola.
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(ActionId ?? "?")
              .Append(" N=").Append(VariantsSeen.ToString(CultureInfo.InvariantCulture))
              .Append(" wziete=").Append(Returned.ToString(CultureInfo.InvariantCulture))
              .Append(" limit=").Append(Limit.ToString(CultureInfo.InvariantCulture))
              .Append(" wyczerpano=").Append(Flag(Exhausted))
              .Append(" losowan=").Append(RandomDraws.ToString(CultureInfo.InvariantCulture));
            if (Truncated)
            {
                // Dopisywane tylko przy przerwaniu, zeby typowa linia zgadzala sie
                // co do znaku z przykladem w specyfikacji, a wyjatek rzucal sie w oczy.
                sb.Append(" ucieto=true");
            }
            if (!string.IsNullOrEmpty(Trace))
            {
                sb.Append(" (").Append(Trace).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Jedno miejsce zamiany bool na tekst dla calej warstwy kompozycji.
        /// bool.ToString() daje "True" z wielkiej litery, a format logu ewaluacyjnego
        /// (i parser w Pythonie) oczekuje "true" - literowka nie do wykrycia w kodzie,
        /// za to psujaca kolumne logiczna w calej serii rozgrywek.
        /// </summary>
        internal static string Flag(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
