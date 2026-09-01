using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Composition
{
    /// <summary>
    /// Wynik jednej tury generowania kandydatow: lista gotowych wydarzen PLUS sprawozdanie
    /// z tego, jak powstala.
    ///
    /// Sprawozdanie nie jest ozdoba. Warstwa decyzyjna dostaje skonczona liste, ale sensownosc
    /// jej wyboru zalezy od tego, czy lista jest CALA przestrzenia mozliwosci, czy jej probka -
    /// a tego z samej listy nie widac. Kandydatow 84 z przestrzeni 84 i kandydatow 400
    /// z przestrzeni 6000 to dwie rozne sytuacje badawcze zapisane tym samym polem Count.
    ///
    /// Zbior NIE filtruje kandydatow po jakosci: kandydaci o zerowym dopasowaniu tez tu sa.
    /// Weto i odsiew to odpowiedzialnosc UtilityScorer, bo odrzuceni musza byc widoczni
    /// w pelnym rankingu w logu - inaczej znika mianownik metryk z sekcji 12 koncepcji.
    /// </summary>
    public class CandidateSet
    {
        /// <summary>Gotowe wydarzenia, w kolejnosci kanonicznej akcji, a w obrebie akcji po sygnaturze.</summary>
        public List<ComposedEvent> Candidates = new List<ComposedEvent>();

        /// <summary>B: budzet OCEN na ture (z XML). Nie mylic z liczba wariantow w przestrzeni.</summary>
        public int Budget;

        /// <summary>K z pierwszego przebiegu: ile wariantow wolno wziac z jednej akcji.</summary>
        public int PerActionQuota;

        /// <summary>m: liczba akcji DOSTEPNYCH w tym kontekscie (po twardych warunkach i tagu).</summary>
        public int ActionCount;

        /// <summary>
        /// Suma N_i po wszystkich akcjach - rozmiar calej przestrzeni wariantow tej tury.
        /// Mianownik pokrycia: pokrycie = Candidates.Count / TotalVariants.
        /// Gdy Truncated == true, jest to DOLNE ograniczenie, a pokrycie liczone z niego
        /// jest GORNYM ograniczeniem i moze wyjsc wieksze od 1.
        /// </summary>
        public int TotalVariants;

        /// <summary>
        /// Czy przestrzen wariantow WSZYSTKICH dostepnych akcji przebadano w calosci
        /// (koniunkcja po akcjach; pusty zbior akcji daje true - nie ma czego badac).
        /// </summary>
        public bool Exhausted;

        /// <summary>Czy ktorakolwiek akcja uderzyla w TraversalCap (alternatywa po akcjach).</summary>
        public bool Truncated;

        /// <summary>
        /// Czy liczba kandydatow przekroczyla budzet. Moze sie zdarzyc WYLACZNIE przy m &gt; B,
        /// gdy K wymuszono na 1, zeby kazda akcja miala reprezentanta. Swiadomy kompromis:
        /// utrata calego tematu z rozwazan jest gorsza niz m - B nadmiarowych ocen,
        /// a roznorodnosc typow jest metryka ewaluacji.
        /// </summary>
        public bool BudgetExceeded;

        /// <summary>Sprawozdania per akcja. Niezmiennik: PerAction.Count == ActionCount.</summary>
        public List<VariantEnumerationStats> PerAction = new List<VariantEnumerationStats>();

        /// <summary>Slad rozdzialu budzetu, jedna linia do logu czytelnego.</summary>
        public string Trace;

        /// <summary>
        /// Fragment linii maszynowej [PN-DATA]. Staly zestaw i stala KOLEJNOSC pol -
        /// zmiana ktoregokolwiek z nich to zmiana formatu danych badawczych, wiec
        /// wymaga podbicia markera wersji logu w warstwie integracji.
        ///
        /// Pole "przestrzen" jest obowiazkowe obok "wyczerpano": bez niego wpis
        /// wyczerpano=false nie mowi, czy zbadano 95% czy 5% przestrzeni.
        /// </summary>
        public string DataLogFragment()
        {
            var sb = new StringBuilder();
            sb.Append("kandydatow=").Append(Candidates.Count.ToString(CultureInfo.InvariantCulture))
              .Append("; budzet=").Append(Budget.ToString(CultureInfo.InvariantCulture))
              .Append("; akcji=").Append(ActionCount.ToString(CultureInfo.InvariantCulture))
              .Append("; limitNaAkcje=").Append(PerActionQuota.ToString(CultureInfo.InvariantCulture))
              .Append("; przestrzen=").Append(TotalVariants.ToString(CultureInfo.InvariantCulture))
              .Append("; wyczerpano=").Append(VariantEnumerationStats.Flag(Exhausted))
              .Append("; ucieto=").Append(VariantEnumerationStats.Flag(Truncated));
            return sb.ToString();
        }

        public override string ToString()
        {
            return Trace ?? DataLogFragment();
        }
    }
}
