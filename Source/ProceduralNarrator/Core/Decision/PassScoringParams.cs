using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Parametry uzytecznosci pseudo-kandydata PASS: ksztalt pamieci o zdarzeniach, progi rampy
    /// powsciagliwosci, straznik serii oraz WAGI trzech czynnikow PASS.
    ///
    /// PASS jest pelnoprawna decyzja narratora ("swiadomy brak wydarzenia to element sterowania
    /// tempem"), wiec nie ma stalej uzytecznosci z XML - liczy ja tak samo jak zdarzenia:
    /// przez wlasne czynniki, agregowane ta sama metoda i z tym samym sladem. Stala byla by
    /// sladem zdegenerowanym, czyli lamalaby wymog rozbicia kazdego wyniku na czynniki.
    ///
    /// TO JEST DRUGA, ROZLACZNA PRZESTRZEN WAG (pierwsza to ScoringWeights dla zdarzen).
    /// Obie sa walidowane OSOBNO, kazda przeciwko swoim czynnikom. Nazwa czynnika
    /// "intentAlignment" wystepuje w OBU przestrzeniach CELOWO - to dwa rozne pytania
    /// (czy ZDARZENIE pasuje do intencji i czy CISZA pasuje do intencji) o niezaleznej
    /// kalibracji. Zunifikowanie ich "dla porzadku" zepnie ze soba dwie osobne kalibracje.
    ///
    /// KAZDE POLE MA INICJALIZATOR i to jest wymog, nie styl: RimWorld tworzy obiekt i wypelnia
    /// wylacznie wezly OBECNE w XML. Gdyby bloku &lt;pass&gt; zabraklo albo nie zdeserializowal sie
    /// poprawnie, pola zostana na tych wartosciach i awaria bedzie CICHA - narrator zadziala,
    /// tylko kalibracja z XML nie bedzie miala zadnego wplywu. Dlatego ToString() jest logowany
    /// na starcie: log EFEKTYWNYCH wartosci jest jedynym dowodem, ze XML zadzialal.
    ///
    /// WYPROWADZENIE DOMYSLNYCH WARTOSCI (mtbDays = 2.5 wynika z sumy czestotliwosci trzech
    /// podmienionych compow Cassandry, 0.14 + 0.06 + 0.21 = 0.40 zdarzenia/dzien):
    ///   halfLifeDays       = 2 * mtbDays = 5.0  - pamiec o dwa typowe odstepy miedzy zdarzeniami
    ///   E[D] w stanie ustalonym = halfLifeDays / (mtbDays * ln 2) = 5 / (2.5 * 0.6931) = 2.886
    ///   densityFloor       = 0.52 * E[D] = 1.5  - ponizej sredniej cisza nie ma czego rownowazyc
    ///   densitySaturation  = 1.56 * E[D] = 4.5  - wyraznie powyzej sredniej, okolo 10-12% tur
    ///   weightRestraint / weightBaseline = 0.75 / 0.25, suma wag = 1.0, wiec waga bazowa
    ///   czyta sie WPROST jako podloga uzytecznosci ciszy (0.25).
    /// </summary>
    public class PassScoringParams
    {
        /// <summary>Polowiczny zanik pamieci o zdarzeniu, w dniach gry.</summary>
        public float halfLifeDays = 5f;

        /// <summary>Gestosc, ponizej ktorej cisza nie jest nic warta (restraint = 0).</summary>
        public float densityFloor = 1.5f;

        /// <summary>Gestosc, przy ktorej cisza jest maksymalnie cenna (restraint = 1).</summary>
        public float densitySaturation = 4.5f;

        /// <summary>
        /// Ile PASS-ow z rzedu wolno, zanim straznik wygasi PASS w puli wyboru (0 = brak straznika).
        /// Straznik jest UBEZPIECZENIEM przed zla konfiguracja XML, a nie normalnym trybem pracy -
        /// przy domyslnych wartosciach nie powinien zadzialac ani razu. Nie ma prawa wymusic
        /// wydarzenia, ktorego nie ma: przy pustej puli zdarzen PASS wygrywa mimo straznika.
        /// </summary>
        public int maxStreak = 3;

        /// <summary>Waga czynnika "powsciagliwosc" (gestosc ostatnich zdarzen).</summary>
        public float weightRestraint = 0.75f;

        /// <summary>
        /// Waga czynnika stalego - bazowa sklonnosc narratora do ciszy.
        /// Czynnik ma wartosc stala 1.0, a regulowana jest WYLACZNIE waga: konfigurowanie
        /// jednoczesnie wartosci i wagi stalego wyrazu jest nadmiarowe, bo liczy sie tylko
        /// ich iloczyn. Przy sumie wag rownej 1.0 ta liczba to wprost podloga U_pass.
        /// </summary>
        public float weightBaseline = 0.25f;

        /// <summary>
        /// Waga zgodnosci ciszy z intencja narratora. 0 w kroku 3 (intencja jest stale Hold),
        /// 1.0 od kroku 4. Czynnik mimo zerowej wagi jest liczony i trafia do sladu, zeby
        /// format danych badawczych byl identyczny w obu krokach.
        /// </summary>
        public float weightIntentAlignment = 0f;

        /// <summary>Nazwa czynnika powsciagliwosci. Musi zgadzac sie z Factor_PassRestraint.FactorName.</summary>
        public const string RestraintFactorName = "restraint";

        /// <summary>Nazwa czynnika bazowego. Musi zgadzac sie z Factor_PassBaseline.FactorName.</summary>
        public const string BaselineFactorName = "baseline";

        /// <summary>Nazwa czynnika intencji. Musi zgadzac sie z Factor_PassIntent.FactorName.</summary>
        public const string IntentAlignmentFactorName = "intentAlignment";

        /// <summary>
        /// Kanoniczna KOLEJNOSC czynnikow PASS - ustala kolejnosc wierszy sladu i kolumn logu.
        /// Ta sama rola co ScoringWeights.KnownNames po stronie zdarzen.
        /// </summary>
        private static readonly string[] KnownNamesArray =
        {
            RestraintFactorName,
            BaselineFactorName,
            IntentAlignmentFactorName
        };

        /// <summary>Nazwy czynnikow PASS w kolejnosci kanonicznej.</summary>
        public static IReadOnlyList<string> KnownNames
        {
            get { return KnownNamesArray; }
        }

        /// <summary>
        /// Suma wag PASS - mianownik normalizacji U_pass. Wagi ujemne i nieliczbowe licza sie
        /// jako zero (patrz komentarz przy ScoringWeights.For - ujemna waga odwraca pasmo
        /// near-best i wyprowadza uzytecznosc poza [0,1]).
        /// </summary>
        public float SumWeights
        {
            get { return NonNegative(weightRestraint) + NonNegative(weightBaseline) + NonNegative(weightIntentAlignment); }
        }

        /// <summary>
        /// PODLOGA uzytecznosci PASS: w_baseline / suma wag (0.25 przy domyslnych wartosciach).
        /// Bierze sie stad, ze czynnik bazowy ma stala wartosc 1.0, a pozostale dwa moga spasc
        /// do zera (gestosc ponizej progu, intencja Escalate). Ponizej tej wartosci cisza
        /// nie zejdzie nigdy.
        /// </summary>
        public float UtilityFloor
        {
            get
            {
                float suma = SumWeights;
                return suma <= ScoreMath.Epsilon ? 0f : NonNegative(weightBaseline) / suma;
            }
        }

        /// <summary>
        /// SUFIT uzytecznosci PASS: 1.0, o ile suma wag jest dodatnia.
        ///
        /// Wynika to wprost z konstrukcji: wszystkie trzy czynniki moga osiagnac 1 JEDNOCZESNIE
        /// (powsciagliwosc przy gestosci &gt;= saturation, wyraz bazowy zawsze, zgodnosc z intencja
        /// przy Breathe/Pass), a wynik jest kombinacja wypukla. Konsekwencja jest istotna i ma
        /// byc powiedziana wprost: PASS moze przebic KAZDEGO kandydata zdarzeniowego, lacznie
        /// z idealnym. Sciecie sufitu wymagaloby piatego pokretla bez wyprowadzenia, a wtedy
        /// zdanie "PASS konkuruje na rownych prawach" przestaloby byc prawda. Udzial PASS stroi
        /// sie JEDNYM pokretlem - densitySaturation (przesuwa moment osiagniecia sufitu) -
        /// a nie obnizaniem weightBaseline, ktore PODNOSI podloge i daje efekt odwrotny.
        /// </summary>
        public float UtilityCeiling
        {
            get { return SumWeights <= ScoreMath.Epsilon ? 0f : 1f; }
        }

        /// <summary>Egzemplarz z wartosciami domyslnymi - punkt odniesienia dla Sanitize i testow.</summary>
        public static PassScoringParams Defaults()
        {
            return new PassScoringParams();
        }

        /// <summary>
        /// Waga czynnika PASS o podanej nazwie, zawsze nieujemna. Nieznana nazwa daje 0.
        ///
        /// UWAGA NA NIEZMIENNIK NAZW: po stronie zdarzen nazwa czynnika jest identyczna z nazwa
        /// pola i wezla XML. Tutaj czynnik nazywa sie "restraint", a pole "weightRestraint",
        /// bo blok &lt;pass&gt; miesza wagi z parametrami ksztaltu (halfLifeDays, densityFloor...)
        /// i bez przedrostka nie dalo by sie ich odroznic w XML. Odwzorowanie nazw jest wiec
        /// JAWNE i zyje w tej jednej metodzie - to jest cena za czytelny blok konfiguracji.
        /// </summary>
        public float For(string factorName)
        {
            if (string.IsNullOrEmpty(factorName))
            {
                return 0f;
            }
            switch (factorName)
            {
                case RestraintFactorName:
                    return NonNegative(weightRestraint);
                case BaselineFactorName:
                    return NonNegative(weightBaseline);
                case IntentAlignmentFactorName:
                    return NonNegative(weightIntentAlignment);
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Czy czynnik o tej nazwie ma wage w TEJ przestrzeni. Uzywane przez walidacje startowa,
        /// ktora dla tablicy PASS pyta wylacznie tego obiektu - nigdy ScoringWeights.
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
        /// Doprowadza parametry do stanu, w ktorym matematyka PASS jest okreslona, i ZWRACA OPIS
        /// poprawek (null, gdy nie bylo czego poprawiac). Opis idzie do logu startowego - cicha
        /// korekta bylaby gorsza od bledu, bo kalibracja przestalaby odpowiadac plikowi XML.
        ///
        /// Dwie reguly, konsekwentnie:
        ///  - wartosc NIELICZBOWA (NaN, nieskonczonosc) = brak sensownej wartosci w XML,
        ///    wiec przywracamy wartosc DOMYSLNA (skalibrowana), a nie zero,
        ///  - wartosc liczbowa poza dopuszczalnym zakresem klamrujemy do najblizszego brzegu.
        ///
        /// Konfiguracja zdegenerowana, ktorej NIE poprawiamy: saturation &lt;= floor zamienia rampe
        /// w prog skokowy. To jest legalny, swiadomy wybor (ostry narrator), wiec tylko go
        /// odnotowujemy. Podobnie zerowa suma wag: U_pass wychodzi wtedy 0, PASS zostaje w puli
        /// i nadal dziala jako sciezka awaryjna przy braku kandydatow.
        /// </summary>
        public string Sanitize()
        {
            PassScoringParams d = Defaults();
            var poprawki = new List<string>();

            if (!IsFinite(halfLifeDays))
            {
                poprawki.Add("halfLifeDays nieliczbowe -> " + Fmt(d.halfLifeDays));
                halfLifeDays = d.halfLifeDays;
            }
            if (halfLifeDays <= 0f)
            {
                // Zero albo wartosc ujemna to dzielenie przez zero w wykladniku zaniku.
                // Podnosimy do minimum zamiast wylaczac czynnik: zanik niemal natychmiastowy
                // jest interpretowalny ("pamiec jednej tury"), a NaN w gestosci nie jest.
                poprawki.Add("halfLifeDays " + Fmt(halfLifeDays) + " -> " + Fmt(MinHalfLifeDays));
                halfLifeDays = MinHalfLifeDays;
            }

            if (!IsFinite(densityFloor))
            {
                poprawki.Add("densityFloor nieliczbowe -> " + Fmt(d.densityFloor));
                densityFloor = d.densityFloor;
            }
            if (densityFloor < 0f)
            {
                poprawki.Add("densityFloor " + Fmt(densityFloor) + " -> 0");
                densityFloor = 0f;
            }

            if (!IsFinite(densitySaturation))
            {
                poprawki.Add("densitySaturation nieliczbowe -> " + Fmt(d.densitySaturation));
                densitySaturation = d.densitySaturation;
            }
            if (densitySaturation < 0f)
            {
                poprawki.Add("densitySaturation " + Fmt(densitySaturation) + " -> 0");
                densitySaturation = 0f;
            }

            if (maxStreak < 0)
            {
                poprawki.Add("maxStreak " + maxStreak.ToString(CultureInfo.InvariantCulture) + " -> 0 (straznik wylaczony)");
                maxStreak = 0;
            }

            weightRestraint = SanitizeWeight(weightRestraint, d.weightRestraint, nameof(weightRestraint), poprawki);
            weightBaseline = SanitizeWeight(weightBaseline, d.weightBaseline, nameof(weightBaseline), poprawki);
            weightIntentAlignment = SanitizeWeight(weightIntentAlignment, d.weightIntentAlignment, nameof(weightIntentAlignment), poprawki);

            if (densitySaturation <= densityFloor)
            {
                poprawki.Add("densitySaturation " + Fmt(densitySaturation) + " <= densityFloor " + Fmt(densityFloor)
                             + ": rampa powsciagliwosci degeneruje sie do progu skokowego (dozwolone, ale sprawdz XML)");
            }
            if (SumWeights <= ScoreMath.Epsilon)
            {
                poprawki.Add("suma wag PASS = 0: U_pass bedzie stale 0, PASS zostaje wylacznie sciezka awaryjna");
            }

            return poprawki.Count == 0 ? null : string.Join("; ", poprawki.ToArray());
        }

        /// <summary>
        /// Jedna linia z EFEKTYWNYMI parametrami PASS - logowana na starcie jako dowod, ze blok
        /// &lt;pass&gt; z XML w ogole sie zdeserializowal. Podloga i sufit sa policzone, a nie
        /// przepisane, wiec rozjazd miedzy komentarzem a rzeczywistoscia jest widoczny od razu.
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("PASS: halfLifeDays=").Append(Fmt(halfLifeDays))
              .Append(" densityFloor=").Append(Fmt(densityFloor))
              .Append(" densitySaturation=").Append(Fmt(densitySaturation))
              .Append(" maxStreak=").Append(maxStreak.ToString(CultureInfo.InvariantCulture))
              .Append(" weightRestraint=").Append(Fmt(weightRestraint))
              .Append(" weightBaseline=").Append(Fmt(weightBaseline))
              .Append(" weightIntentAlignment=").Append(Fmt(weightIntentAlignment))
              .Append(" sumaWag=").Append(Fmt(SumWeights))
              .Append(" podloga=").Append(UtilityFloor.ToString("0.000", CultureInfo.InvariantCulture))
              .Append(" sufit=").Append(UtilityCeiling.ToString("0.000", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>
        /// Minimalny dopuszczalny czas polowicznego zaniku. Nie zero, bo zero jest dzieleniem
        /// przez zero w wykladniku; 0.1 dnia to okolo 2.4 godziny gry, czyli zanik szybszy
        /// niz jakikolwiek sensowny interwal narratora.
        /// </summary>
        private const float MinHalfLifeDays = 0.1f;

        private static float SanitizeWeight(float value, float defaultValue, string name, List<string> poprawki)
        {
            if (!IsFinite(value))
            {
                poprawki.Add(name + " nieliczbowe -> " + Fmt(defaultValue));
                return defaultValue;
            }
            if (value < 0f)
            {
                poprawki.Add(name + " " + Fmt(value) + " -> 0 (waga ujemna odwraca pasmo near-best)");
                return 0f;
            }
            return value;
        }

        private static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        private static float NonNegative(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f)
            {
                return 0f;
            }
            return v;
        }

        /// <summary>
        /// Formatowanie liczb ZAWSZE przez InvariantCulture: polski separator dziesietny
        /// rozwalilby parsowanie logow w Pythonie na etapie agregacji ewaluacji (krok 8).
        /// </summary>
        private static string Fmt(float v)
        {
            return v.ToString("0.0##", CultureInfo.InvariantCulture);
        }
    }
}
