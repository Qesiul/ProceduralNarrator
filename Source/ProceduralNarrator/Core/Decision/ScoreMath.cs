using System;
using System.Collections.Generic;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Arytmetyka warstwy decyzyjnej: sanityzacja wartosci czynnikow, progi domykajace
    /// i losowanie wazone rozstrzygajace miedzy kandydatami "prawie najlepszymi".
    ///
    /// Dlaczego to osobna, statyczna i CZYSTA klasa: kazda z tych operacji jest zrodlem
    /// cichego bledu numerycznego (NaN przepuszczony przez klamre, kandydat odrzucony
    /// przez zaokraglenie dokladnie na progu, obciazenie kwantyzacji rozkladu). Trzymane
    /// razem daja sie przetestowac jednostkowo bez zadnego kontekstu decyzyjnego,
    /// a UtilityScorer i SelectionPolicy nie powtarzaja tej matematyki u siebie.
    ///
    /// Zero stanu, zero zaleznosci od RimWorlda, zero zegara - wylacznie liczby.
    /// </summary>
    public static class ScoreMath
    {
        /// <summary>
        /// Tolerancja progow. WSZYSTKIE progi warstwy decyzyjnej (weto 0.15, prog jakosci 0.35,
        /// pasmo 0.75*best) sa DOMYKAJACE z ta sama tolerancja - kandydat dokladnie na progu
        /// jest przyjmowany. Jedna stala, bo trzy rozne tolerancje w trzech miejscach to trzy
        /// rozne odpowiedzi na to samo pytanie graniczne.
        /// </summary>
        public const float Epsilon = 1e-6f;

        /// <summary>
        /// Mianownik kwantyzacji rozkladu wyboru. IRandomSource udostepnia wylacznie
        /// Next(int), wiec losowanie wazone MUSI isc po liczbach calkowitych - nie ma
        /// NextDouble, po ktorym poszlaby klasyczna metoda dystrybuanty na liczbach
        /// zmiennoprzecinkowych. Milion daje rozdzielczosc prawdopodobienstwa 1e-6, czyli
        /// o rzad lepsza niz najmniejsze prawdopodobienstwa spotykane w praktyce.
        /// </summary>
        public const int ProbabilityScale = 1000000;

        /// <summary>
        /// Ponizej tej temperatury softmax degeneruje sie do argmaksu. Bramka istnieje po to,
        /// zeby temperatura 0 z XML nie dala dzielenia przez zero (wykladnik plus/minus
        /// nieskonczonosc, a po normalizacji NaN w calym rozkladzie).
        /// </summary>
        public const double MinTemperature = 1e-4;

        /// <summary>
        /// Sprowadza surowa wartosc czynnika do [0,1] i RAPORTUJE, czy wejscie bylo w ogole
        /// liczba. NaN i nieskonczonosc to nie sa wartosci do sklamrowania - to sygnal bledu
        /// w czynniku, ktory ma trafic do sladu decyzji.
        ///
        /// Rozdzial jest celowy: zwykle klamrowanie (np. 1.2 na 1.0) NIE ustawia flagi,
        /// bo to normalna, spodziewana korekta zakresu. Flaga oznacza wylacznie wartosc,
        /// ktorej nie da sie porownac - a takie porownania sa zawsze falszywe, wiec bez
        /// jawnego testu NaN przeszedlby przez kazda klamre oparta na mniejszosci i wiekszosci.
        /// </summary>
        public static float Sanitize01(float v, out bool invalid)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                invalid = true;
                return 0f;
            }
            invalid = false;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>
        /// Prog DOMYKAJACY: value jest co najmniej rowne threshold z tolerancja Epsilon.
        ///
        /// Powod tolerancji: progi sa iloczynami liczb zmiennoprzecinkowych policzonymi
        /// w innym miejscu niz porownanie (0.75f razy 0.8f nie musi dac dokladnie 0.6f),
        /// wiec gole porownanie na granicy jest rzutem moneta zaleznym od kolejnosci mnozen.
        /// Kandydat dokladnie na progu ma byc przyjmowany deterministycznie.
        ///
        /// NaN po lewej stronie daje false - wartosc nieokreslona nie przechodzi zadnego progu.
        /// </summary>
        public static bool AtLeast(double value, double threshold)
        {
            return value >= threshold - Epsilon;
        }

        /// <summary>
        /// Nieznormalizowane wagi softmaksu: w[k] = exp((u[k] - uRef) / T), gdzie uRef to maksimum.
        ///
        /// Odejmowanie maksimum (stabilizacja softmaksu) nie jest kosmetyka: bez niego
        /// exp(u/T) przy T = 0.1 i uzytecznosci 0.9 to exp(9), a przy mniejszej temperaturze
        /// wykladnik przekracza zakres typu double i zwraca nieskonczonosc, po czym
        /// normalizacja daje NaN. Po odjeciu maksimum kazde w[k] lezy w przedziale (0,1],
        /// najwieksze rowna sie dokladnie 1, a suma jest w [1, n] - nigdy zero,
        /// nigdy nieskonczonosc.
        ///
        /// Zwraca wagi, a nie prawdopodobienstwa, bo normalizacja i tak dzieje sie dopiero
        /// w kwantyzacji skumulowanej - dzielenie dwa razy dokladalo by tylko zaokraglen.
        /// </summary>
        public static double[] SoftmaxWeights(IReadOnlyList<double> utilities, double temperature)
        {
            if (utilities == null || utilities.Count == 0)
            {
                return new double[0];
            }

            int n = utilities.Count;
            double[] weights = new double[n];

            // Maksimum liczone z pominieciem wartosci nieokreslonych; gdyby uRef wyszlo NaN,
            // KAZDA waga wyszlaby NaN i zdegenerowany bylby caly rozklad, a nie jeden kandydat.
            double uRef = double.NegativeInfinity;
            bool anyFinite = false;
            for (int k = 0; k < n; k++)
            {
                double u = utilities[k];
                if (double.IsNaN(u) || double.IsInfinity(u))
                {
                    continue;
                }
                anyFinite = true;
                if (u > uRef)
                {
                    uRef = u;
                }
            }

            if (!anyFinite)
            {
                // Zadnej uzytecznej informacji - zwracamy same zera i zostawiamy decyzje
                // o zachowaniu zdegenerowanym kwantyzacji (patrz CumulativeThresholds).
                return weights;
            }

            if (temperature <= MinTemperature)
            {
                // Granica T dazacego do zera: cala masa na PIERWSZYM maksimum. Remis
                // rozstrzyga pierwszy indeks, bo pula wchodzi tu juz posortowana malejaco
                // po uzytecznosci, z deterministycznym rozstrzyganiem remisow po sygnaturze.
                for (int k = 0; k < n; k++)
                {
                    double u = utilities[k];
                    if (!double.IsNaN(u) && !double.IsInfinity(u) && u >= uRef)
                    {
                        weights[k] = 1.0;
                        return weights;
                    }
                }
                return weights;
            }

            for (int k = 0; k < n; k++)
            {
                double u = utilities[k];
                if (double.IsNaN(u) || double.IsInfinity(u))
                {
                    // Kandydat zdegenerowany wypada z losowania, reszta puli gra dalej.
                    weights[k] = 0.0;
                    continue;
                }
                weights[k] = Math.Exp((u - uRef) / temperature);
            }
            return weights;
        }

        /// <summary>
        /// Kwantyzacja SKUMULOWANA wag do progow calkowitych na skali ProbabilityScale.
        /// Zwraca tablice niemalejaca, ktorej ostatni element rowna sie dokladnie
        /// ProbabilityScale. Szerokosc kubelka k to thr[k] - thr[k-1], przy thr[-1] = 0.
        ///
        /// Dlaczego kumulacja, a nie osobne zaokraglenie p[k] * Scale dla kazdego kandydata:
        /// przy osobnym zaokraglaniu szerokosci nie sumuja sie do Scale i zostaje reszta,
        /// ktora trzeba komus dokleic - a doklejanie reszty (zwykle do pierwszego albo do
        /// ostatniego kubelka) jest klasycznym zrodlem systematycznego obciazenia losowania.
        /// Przy kumulacji szerokosci sumuja sie DOKLADNIE do Scale z konstrukcji, a kazda
        /// z nich rozni sie od idealnej o mniej niz jedna jednostke, czyli o mniej niz 1e-6
        /// prawdopodobienstwa.
        ///
        /// Zaokraglenie jest do najblizszej liczby calkowitej (floor z dodanym 0.5), nie
        /// obcinajace - obciecie systematycznie zanizaloby kazdy prog i przerzucalo cala
        /// zbiorcza reszte na ostatni kubelek przy domknieciu do Scale.
        ///
        /// Monotonicznosc jest wymuszana jawnie, bo domkniecie ostatniego progu do Scale
        /// moglo by teoretycznie ustawic go ponizej poprzedniego przy przestrzeleniu sumy
        /// przez blad zaokraglenia - a niemalejace progi to warunek konieczny nieujemnych
        /// szerokosci, czyli nieujemnych prawdopodobienstw w logu ewaluacji.
        ///
        /// UWAGA DLA TESTOW - rozbieznosc o jedna jednostke wobec specyfikacji.
        /// Dla puli odniesienia {0.90, 0.80, PASS 0.35} przy T = 0.1 ta implementacja daje
        /// progi 728881 / 997021 / 1000000, a specyfikacja podaje w tym miejscu 728880.
        /// Prawdopodobienstwo idealne pierwszego kubelka to 0.7288809, czyli 728880.92
        /// jednostek - zaokraglenie do najblizszej daje 728881, a 728880 wychodzi tylko
        /// przy obcieciu. Rozstrzyga drugi przypadek testowy z tej samej specyfikacji:
        /// dla trzech ROWNYCH wag wymaga ona progow 333333 / 666667 / 1000000 i szerokosci
        /// 333333 / 333334 / 333333, a obciecie dalo by tam 666666 i szerokosci
        /// 333333 / 333333 / 333334. Obcieciem nie da sie spelnic obu przypadkow naraz,
        /// zaokragleniem - jednego z dokladnoscia do jednej jednostki na milion.
        /// Trzymamy sie wzoru zapisanego w specyfikacji (floor z dodanym 0.5).
        /// </summary>
        public static int[] CumulativeThresholds(IReadOnlyList<double> weights)
        {
            if (weights == null || weights.Count == 0)
            {
                return new int[0];
            }

            int n = weights.Count;
            int[] thresholds = new int[n];

            double sum = 0.0;
            for (int k = 0; k < n; k++)
            {
                double w = weights[k];
                if (double.IsNaN(w) || double.IsInfinity(w) || w < 0.0)
                {
                    // Waga niepoprawna liczy sie jak zero i nie truje sumy.
                    continue;
                }
                sum += w;
            }

            if (sum <= 0.0)
            {
                // Wejscie zdegenerowane (same zera albo same wartosci niepoprawne).
                // Rozklad jednostajny jest odpowiedzia neutralna: brak informacji o preferencji
                // nie moze udawac preferencji dla kandydata numer zero. Kazdy kubelek zostaje
                // osiagalny, wiec zaden element puli nie znika po cichu z losowania.
                for (int k = 0; k < n; k++)
                {
                    thresholds[k] = (int)Math.Floor((k + 1) / (double)n * ProbabilityScale + 0.5);
                }
            }
            else
            {
                double cumulative = 0.0;
                for (int k = 0; k < n; k++)
                {
                    double w = weights[k];
                    if (!double.IsNaN(w) && !double.IsInfinity(w) && w > 0.0)
                    {
                        cumulative += w;
                    }
                    thresholds[k] = (int)Math.Floor(cumulative / sum * ProbabilityScale + 0.5);
                }
            }

            // Domkniecie i wymuszenie niezmiennikow: zakres [0, Scale] oraz niemalejacosc.
            thresholds[n - 1] = ProbabilityScale;
            for (int k = 0; k < n; k++)
            {
                if (thresholds[k] < 0)
                {
                    thresholds[k] = 0;
                }
                if (thresholds[k] > ProbabilityScale)
                {
                    thresholds[k] = ProbabilityScale;
                }
                if (k > 0 && thresholds[k] < thresholds[k - 1])
                {
                    thresholds[k] = thresholds[k - 1];
                }
            }
            thresholds[n - 1] = ProbabilityScale;
            return thresholds;
        }

        /// <summary>
        /// Losuje indeks zgodnie z progami skumulowanymi. Wykonuje DOKLADNIE JEDNO wywolanie
        /// rng.Next(ProbabilityScale) - zawsze, niezaleznie od rozmiaru puli i od tego, czy
        /// rozklad zdegenerowal sie do jednego kandydata.
        ///
        /// Ta niezmiennosc liczby pobran jest wymaganiem determinizmu, nie mikrooptymalizacja:
        /// gdyby losowanie bylo pomijane dla pul jednoelementowych, dwa przebiegi ewaluacji
        /// rozniace sie jednym kandydatem rozjechalyby caly dalszy strumien liczb losowych
        /// i przestaly byc porownywalne. Liczba pobran ma zalezec WYLACZNIE od liczby rund wyboru.
        ///
        /// Zwraca -1 dla pustych progow - to blad okablowania po stronie wolajacego (pusta pula
        /// nie ma prawa trafic do losowania) i ma byc widoczny, a nie zamaskowany zerem.
        /// Przy rng rownym null zwraca 0, czyli kandydata o najwyzszej uzytecznosci; ten tryb
        /// istnieje po to, zeby walidator offline przeszedl cala sciezke decyzyjna bez zrodla
        /// losowosci - i jako jedyny lamie regule jednego pobrania, bo pobierac nie ma skad.
        /// </summary>
        public static int PickByThresholds(int[] thresholds, IRandomSource rng)
        {
            if (thresholds == null || thresholds.Length == 0)
            {
                return -1;
            }
            if (rng == null)
            {
                return 0;
            }

            int roll = rng.Next(ProbabilityScale);
            for (int k = 0; k < thresholds.Length; k++)
            {
                if (roll < thresholds[k])
                {
                    return k;
                }
            }
            // Nieosiagalne przy poprawnych progach: ostatni rowna sie Scale, a Next zwraca
            // najwyzej Scale-1. Zabezpieczenie na wypadek recznie podanej tablicy w tescie.
            return thresholds.Length - 1;
        }

        /// <summary>
        /// Odtwarza prawdopodobienstwa z progow skumulowanych - do logu decyzji i do sladu
        /// scoringu. Liczy je z tych samych liczb calkowitych, ktorymi losowal
        /// PickByThresholds, wiec log pokazuje rozklad FAKTYCZNIE uzyty, a nie idealny
        /// rozklad sprzed kwantyzacji. Suma wynosi dokladnie 1.0, o ile ostatni prog
        /// rowna sie ProbabilityScale.
        /// </summary>
        public static double[] ProbabilitiesFromThresholds(int[] thresholds)
        {
            if (thresholds == null || thresholds.Length == 0)
            {
                return new double[0];
            }

            double[] probabilities = new double[thresholds.Length];
            int previous = 0;
            for (int k = 0; k < thresholds.Length; k++)
            {
                int width = thresholds[k] - previous;
                if (width < 0)
                {
                    width = 0;
                }
                probabilities[k] = width / (double)ProbabilityScale;
                previous = thresholds[k];
            }
            return probabilities;
        }
    }
}
