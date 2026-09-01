using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Wagi czynnikow ZDARZENIOWYCH - jedno z dwoch, ROZLACZNYCH zrodel wag warstwy decyzyjnej.
    /// Drugim jest PassScoringParams (czynniki pseudo-kandydata PASS). Obie przestrzenie nazw
    /// sa niezalezne i walidowane OSOBNO, kazda przeciwko swojemu zrodlu wag.
    ///
    /// UWAGA: nazwa "intentAlignment" wystepuje w OBU przestrzeniach i jest to CELOWE.
    /// To dwa rozne czynniki (zgodnosc ZDARZENIA z intencja i zgodnosc CISZY z intencja),
    /// kalibrowane niezaleznie. Nie wolno ich "dla porzadku" zunifikowac - zepnie to ze soba
    /// dwie osobne kalibracje i zmiana tempa zdarzen zacznie po cichu przestawiac sklonnosc
    /// narratora do milczenia.
    ///
    /// POLA SA camelCase I TO NIE JEST NIEDBALSTWO: RimWorld mapuje wezly XML na nazwy pol
    /// jeden do jednego (jak mtbDays czy requiredActionTag), wiec nazwa pola JEST nazwa wezla.
    /// Kazde pole ma inicjalizator, bo gra tworzy obiekt i wypelnia wylacznie wezly obecne
    /// w XML - brak wezla musi znaczyc "wartosc domyslna", a nie "zero".
    ///
    /// NIEZMIENNIK: Name czynnika == nazwa pola wagi == nazwa wezla XML (w tej przestrzeni).
    /// Klucze w For() sa wyprowadzone przez nameof wprost z pol, wiec niezmiennika pilnuje
    /// kompilator, a nie dyscyplina - zmiana nazwy pola zmienia klucz automatycznie.
    ///
    /// KALIBRACJA (uzasadnienie domyslnych wartosci): contextFit ma wage najwieksza, bo
    /// spojnosc z sytuacja jest podstawowa obietnica systemu; freshness jest tuz obok, bo
    /// roznorodnosc jest wprost metryka ewaluacji; dramaticContrast dostaje 1.0 jako czynnik
    /// modelujacy rytm, a nie trafnosc; intentAlignment ma 0.0 do kroku 4 - jest wpiety,
    /// liczony i logowany, ale neutralny (patrz komentarz przy Total()).
    /// </summary>
    public class ScoringWeights
    {
        public float contextFit = 2.0f;
        public float freshness = 1.5f;
        public float dramaticContrast = 1.0f;

        /// <summary>
        /// Waga 0 w kroku 3: czynnik jest zarejestrowany i trafia do sladu, ale nie wplywa
        /// na wynik. Krok 4 podniesie ja razem z krzywa dramaturgiczna. Dzieki temu format
        /// danych badawczych (zestaw i kolejnosc kolumn) nie zmienia sie miedzy krokami
        /// i skrypty agregujace w Pythonie nie wymagaja przepisania.
        /// </summary>
        public float intentAlignment = 0f;

        /// <summary>
        /// Kanoniczna KOLEJNOSC czynnikow zdarzeniowych. Ustala kolejnosc kolumn w logu
        /// badawczym i kolejnosc wierszy sladu, wiec jej zmiana jest zmiana formatu danych,
        /// a nie kosmetyka. Trzymana w jednym miejscu, zeby rejestracja czynnikow
        /// w UtilityScorer i walidacja startowa nie mialy szansy sie rozjechac.
        /// </summary>
        private static readonly string[] KnownNamesArray =
        {
            // nameof, a nie literaly: dzieki temu niezmiennik "nazwa czynnika == nazwa pola"
            // pilnuje kompilator. Zmiana nazwy pola przenosi sie tu sama, a literal cicho
            // zostalby przy starej nazwie i czynnik dostalby wage 0.
            nameof(contextFit),
            nameof(freshness),
            nameof(dramaticContrast),
            nameof(intentAlignment)
        };

        /// <summary>Nazwy wszystkich znanych czynnikow zdarzeniowych, w kolejnosci kanonicznej.</summary>
        public static IReadOnlyList<string> KnownNames
        {
            get { return KnownNamesArray; }
        }

        /// <summary>
        /// Waga czynnika o podanej nazwie, ZAWSZE nieujemna i zawsze liczbowa.
        /// Nieznana nazwa daje 0, czyli czynnik spoza kontraktu jest neutralny zamiast wywracac
        /// ture; o samym fakcie krzyczy walidacja startowa, ktora ma na to wlasciwe miejsce.
        ///
        /// Sciecie wagi ujemnej do zera jest OBOWIAZKOWE, nie ostrozne: przy ujemnej wadze
        /// uzytecznosc moze wyjsc ujemna, a wtedy prog pasma near-best (0.75 * best) staje sie
        /// WIEKSZY od samego best i pasmo odwraca sens - przepuszcza kandydatow gorszych
        /// od najlepszego. To jedyna droga, ktora wynik moglby opuscic przedzial [0,1].
        /// </summary>
        public float For(string factorName)
        {
            if (string.IsNullOrEmpty(factorName))
            {
                return 0f;
            }
            switch (factorName)
            {
                case nameof(contextFit):
                    return NonNegative(contextFit);
                case nameof(freshness):
                    return NonNegative(freshness);
                case nameof(dramaticContrast):
                    return NonNegative(dramaticContrast);
                case nameof(intentAlignment):
                    return NonNegative(intentAlignment);
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Czy czynnik o tej nazwie ma w ogole pole wagi w TEJ przestrzeni nazw.
        /// Uzywane przez walidacje startowa: czynnik zarejestrowany bez wlasnej wagi zawsze
        /// dostawalby 0, czyli byl by martwy - i to jest blad konfiguracji, ktory ma byc glosny,
        /// a nie cicha strata jednej osi oceny.
        /// </summary>
        public bool HasWeightFor(string factorName)
        {
            if (string.IsNullOrEmpty(factorName))
            {
                return false;
            }
            for (int i = 0; i < KnownNamesArray.Length; i++)
            {
                if (string.CompareOrdinal(KnownNamesArray[i], factorName) == 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Suma wag po WSZYSTKICH znanych czynnikach, liczona przez For(), czyli juz po scieciu
        /// wartosci ujemnych. To jest mianownik normalizacji w UtilityScorer.
        ///
        /// Czynnik z waga 0 wchodzi do sumy jako 0, wiec jest dokladnie NEUTRALNY: nie wnosi
        /// nic do licznika i nic do mianownika. Dlatego intentAlignment moze byc wpiety juz
        /// dzis, nie rozcienczajac pozostalych czynnikow - to nie jest przypadek, tylko powod,
        /// dla ktorego normalizujemy przez sume wag, a nie przez liczbe czynnikow.
        /// </summary>
        public float Total()
        {
            float suma = 0f;
            for (int i = 0; i < KnownNamesArray.Length; i++)
            {
                suma += For(KnownNamesArray[i]);
            }
            return suma;
        }

        /// <summary>
        /// Wykrywa wage niepoprawna (ujemna albo nieliczbowa) ZANIM For() ja po cichu zetnie.
        /// Sciecie chroni matematyke, ale gracz albo my za miesiac musimy sie dowiedziec,
        /// ze wpisana w XML wartosc nie dziala - inaczej strojenie polega na zgadywaniu.
        /// Zwraca pierwsza znaleziona, w kolejnosci kanonicznej.
        /// </summary>
        public bool AnyNegativeInXml(out string which)
        {
            for (int i = 0; i < KnownNamesArray.Length; i++)
            {
                string nazwa = KnownNamesArray[i];
                float surowa = RawValue(nazwa);
                if (float.IsNaN(surowa) || float.IsInfinity(surowa))
                {
                    which = nazwa + " (wartosc nieliczbowa)";
                    return true;
                }
                if (surowa < 0f)
                {
                    which = nazwa + " (" + surowa.ToString("0.00", CultureInfo.InvariantCulture) + ")";
                    return true;
                }
            }
            which = null;
            return false;
        }

        /// <summary>
        /// Jedna linia z EFEKTYWNYMI wartosciami wag - logowana na starcie.
        ///
        /// To jest jedyny dowod, ze blok wag z XML w ogole sie zdeserializowal. Gdy wezel
        /// nie zostanie rozpoznany, pola zostaja na inicjalizatorach C#, narrator dziala
        /// pozornie poprawnie, a kalibracja z XML nie ma zadnego wplywu - dokladnie ten sam
        /// wzorzec CICHEJ awarii, ktory w kroku 1 dal pusty katalog klockow.
        /// Wypisujemy wartosci SUROWE (takie, jakie przyszly), z adnotacja o scieciu -
        /// wartosc po scieciu nie pokazalaby, ze w XML jest literowka.
        /// </summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < KnownNamesArray.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(KnownNamesArray[i]).Append('=')
                  .Append(RawValue(KnownNamesArray[i]).ToString("0.0##", CultureInfo.InvariantCulture));
            }
            sb.Append(" suma=").Append(Total().ToString("0.0##", CultureInfo.InvariantCulture));

            string ktora;
            if (AnyNegativeInXml(out ktora))
            {
                sb.Append(" [UWAGA: ").Append(ktora).Append(" zostanie scieta do 0]");
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            return Describe();
        }

        /// <summary>Wartosc pola BEZ scinania - wylacznie do diagnostyki i logu startowego.</summary>
        private float RawValue(string factorName)
        {
            switch (factorName)
            {
                case nameof(contextFit):
                    return contextFit;
                case nameof(freshness):
                    return freshness;
                case nameof(dramaticContrast):
                    return dramaticContrast;
                case nameof(intentAlignment):
                    return intentAlignment;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// NaN i nieskonczonosc traktujemy jak zero, a nie klamrujemy: porownania z NaN sa
        /// falszywe, wiec "v &lt; 0 ? 0 : v" przepuscilby go nietknietego prosto do sumy wazonej.
        /// </summary>
        private static float NonNegative(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f)
            {
                return 0f;
            }
            return v;
        }
    }
}
