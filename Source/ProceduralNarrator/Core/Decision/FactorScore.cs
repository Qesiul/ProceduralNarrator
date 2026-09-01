using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Jeden wiersz sladu scoringu: ile dal pojedynczy czynnik i dlaczego.
    /// To jest realizacja pola "slad" z modelu danych (sekcja 8 koncepcji) - wymaganego,
    /// nie opcjonalnego, bo bez rozbicia wyniku na czynniki nie ma czesci badawczej pracy.
    ///
    /// DWA POLA WKLADU, CELOWO:
    ///  - Contribution = Value * Weight (SUROWO). Mowi, jaka sile ma czynnik SAM W SOBIE,
    ///    niezaleznie od tego, ile innych czynnikow jest akurat wpietych. Dolozenie piatego
    ///    czynnika w kroku 4 nie zmieni tej liczby dla pozostalych, wiec da sie porownywac
    ///    serie ewaluacyjne miedzy krokami.
    ///  - Share = Value * Weight / sumaWag (ZNORMALIZOWANY). Mowi, ile czynnik wniosl
    ///    do koncowej uzytecznosci kandydata.
    /// NIEZMIENNIK TESTOWY: suma Share po wszystkich czynnikach kandydata rowna sie
    /// jego RawUtility z tolerancja 1e-5. Ten jeden niezmiennik pilnuje, ze slad opisuje
    /// TE SAMA liczbe, ktora zdecydowala o wyborze - czyli ze log nie klamie.
    ///
    /// Obiekt jest NIEZMIENNY (same pola readonly): wiersz sladu powstaje raz, przy ocenie,
    /// i nikt po drodze do logu nie ma prawa go poprawic.
    /// </summary>
    public class FactorScore
    {
        /// <summary>Nazwa czynnika - zarazem nazwa kolumny w danych badawczych.</summary>
        public readonly string Name;

        /// <summary>Wartosc czynnika w [0,1], juz po sanityzacji.</summary>
        public readonly float Value;

        /// <summary>Waga czynnika, zawsze &gt;= 0 (ujemna zostaje scieta do zera).</summary>
        public readonly float Weight;

        /// <summary>Value * Weight - wklad surowy, nieznormalizowany.</summary>
        public readonly float Contribution;

        /// <summary>Value * Weight / sumaWag - udzial w uzytecznosci kandydata.</summary>
        public readonly float Share;

        /// <summary>Uzasadnienie wartosci, prosto od czynnika. Nigdy null.</summary>
        public readonly string Explanation;

        /// <summary>Czynnik zwrocil NaN albo nieskonczonosc - wartosc zostala zbita do 0.</summary>
        public readonly bool Invalid;

        /// <summary>
        /// Buduje wiersz sladu. Czwarty argument to SUMA WAG wszystkich czynnikow tej samej
        /// przestrzeni (mianownik normalizacji), a nie gotowy udzial - dzieki temu Contribution
        /// i Share licza sie w jednym miejscu i nie moga sie rozjechac miedzy wywolaniami.
        ///
        /// Nazwa parametru pochodzi wprost z zamrozonego kontraktu miedzywarstwowego kroku 3
        /// (sekcja "WSPOLNE TYPY") i celowo nie jest tlumaczona - jest czescia uzgodnienia,
        /// na ktore powoluja sie pozostale warstwy.
        /// </summary>
        public FactorScore(string name, float value, float weight, float sumaWag, string explanation, bool invalid)
        {
            Name = string.IsNullOrEmpty(name) ? "?" : name;

            // Druga linia obrony zakresow. Na sciezce zgodnej ze specyfikacja to operacja
            // tozsamosciowa (UtilityScorer podaje wartosc po ScoreMath.Sanitize01, a wage po
            // ScoringWeights.For, ktore juz scina ujemne), ale pola readonly maja gwarantowac
            // swoj zakres SAME - inaczej gwarancja obowiazuje tylko dopoki nikt nie dopisze
            // drugiego wolajacego. Ujemna waga jest szczegolnie grozna: przy ujemnym maksimum
            // warunek "u >= 0.75 * best" przepuszcza kandydatow GORSZYCH od najlepszego,
            // czyli pasmo near-best odwraca sie i polityka wyboru przestaje miec sens.
            Value = Curves.Clamp01(value);
            Weight = (float.IsNaN(weight) || weight < 0f) ? 0f : weight;

            Contribution = Value * Weight;

            // Dzielenie w double: przy czterech czynnikach i wagach rzedu jednosci roznica jest
            // kosmetyczna, ale niezmiennik "suma Share == RawUtility" jest sprawdzany z tolerancja
            // 1e-5, a float ma tylko okolo 7 cyfr znaczacych - nie ma powodu zjadac zapasu.
            if (float.IsNaN(sumaWag) || sumaWag <= ScoreMath.Epsilon)
            {
                // Wagi zdegenerowane (wszystkie zerowe albo blednie zdeserializowane z XML).
                // Udzial nie jest wtedy zdefiniowany; zero jest jedyna odpowiedzia, ktora nie
                // wprowadza dzielenia przez zero i nie udaje informacji. Kandydat i tak dostaje
                // RawUtility = 0 oraz flage WeightsDegenerate.
                Share = 0f;
            }
            else
            {
                Share = (float)(((double)Value * Weight) / sumaWag);
            }

            Explanation = explanation ?? string.Empty;
            Invalid = invalid;
        }

        /// <summary>
        /// Wiersz do logu CZYTELNEGO, np.
        /// "contextFit=0.75 w=2.0 udzial=0.333 (bogactwo wzgl 1.37)".
        /// InvariantCulture obowiazkowo - polski separator dziesietny rozwalilby parsowanie
        /// w Pythonie na etapie agregacji logow (krok 8), a log czytelny tez bywa parsowany.
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Name)
              .Append('=').Append(Value.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" w=").Append(Weight.ToString("0.0#", CultureInfo.InvariantCulture))
              .Append(" udzial=").Append(Share.ToString("0.000", CultureInfo.InvariantCulture));

            if (Invalid)
            {
                sb.Append(" [WARTOSC NIEPOPRAWNA -> 0]");
            }
            if (Explanation.Length > 0)
            {
                sb.Append(" (").Append(Explanation).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Wiersz do linii MASZYNOWEJ [PN-DATA]: "contextFit:0.750:2.000:0.333".
        /// Dokladnie cztery segmenty rozdzielone dwukropkiem: nazwa, wartosc, waga, udzial.
        ///
        /// Liczba i kolejnosc segmentow sa CZESCIA FORMATU DANYCH - flaga Invalid celowo NIE
        /// jest tu doklejana, bo warunkowy piaty segment zlamalby staly ksztalt kolumny.
        /// Wartosc niepoprawna widac i tak: w linii czytelnej oraz w polu HadInvalidFactor
        /// kandydata, ktore idzie do [PN-DATA] osobno.
        /// </summary>
        public string ToDataString()
        {
            return Name
                   + ":" + Value.ToString("0.000", CultureInfo.InvariantCulture)
                   + ":" + Weight.ToString("0.000", CultureInfo.InvariantCulture)
                   + ":" + Share.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }
}
