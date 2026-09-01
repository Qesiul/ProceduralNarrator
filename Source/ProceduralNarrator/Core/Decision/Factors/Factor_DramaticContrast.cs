using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Pokretla czynnika kontrastu dramaturgicznego. Wszystkie w jednym miejscu, bo kazde
    /// z nich jest ukrytym parametrem wynikow ewaluacji i kazde musi miec w pracy
    /// uzasadnienie albo pomiar.
    ///
    /// W kroku 3 wartosci NIE ida do XML - czynnik powstaje na domyslnych. Pola sa publiczne
    /// i z inicjalizatorami, wiec wystawienie ich w StorytellerCompProperties bedzie
    /// dopisaniem wezla, a nie zmiana logiki.
    /// </summary>
    public class ContrastTuning
    {
        /// <summary>
        /// Wspolczynnik zaniku sredniej wykladniczej rytmu: wpis i-ty od konca wazy lambda^i.
        ///
        /// 0.75 daje polowe masy po 2.41 wpisu (ln 0.5 / ln 0.75), a najnowszy wpis trzyma
        /// 25% wagi calego szeregu. Rytm ma wiec pamiec rzedu trzech ostatnich zdarzen -
        /// tyle, ile trzeba, zeby "seria katastrof" byla seria, a nie pojedynczym wypadkiem.
        /// </summary>
        public float lambda = 0.75f;

        /// <summary>
        /// Ile ostatnich wpisow wchodzi do sredniej rytmu.
        ///
        /// Osiem wyrazow to 90% masy szeregu nieskonczonego przy lambda = 0.75
        /// ((1 - 0.75^8) / 0.25 = 3.5996 wobec 4.0), a wyraz osmy wnosi 3.7%. Ogon ponizej
        /// 10% odcinamy, zeby bufor pozostal maly - krok 6 musi go serializowac.
        /// </summary>
        public int rhythmWindow = 8;

        /// <summary>Ile ostatnich wpisow bada sie pod katem oscylacji walencji.</summary>
        public int volatilityWindow = 6;

        /// <summary>
        /// Udzial skladnika LADUNKU (walencja razy skala) w surowym kontrascie.
        /// Przewrot walencji jest wiekszym bitem narracyjnym niz sama zmiana glosnosci.
        /// </summary>
        public float chargeWeight = 0.70f;

        /// <summary>
        /// Udzial skladnika GLOSNOSCI (sama skala). Mniejszy, bo ladunek c = walencja * skala
        /// juz zawiera skale - glosnosc wnosi nowa informacje tylko dla zdarzen neutralnych
        /// i dla zmian skali w obrebie tego samego znaku, wiec wieksza waga liczylaby skale
        /// dwa razy.
        /// </summary>
        public float magnitudeWeight = 0.30f;

        /// <summary>Wzmocnienie kontrastu, gdy kandydat jest LZEJSZY od rytmu (ulga).</summary>
        public float reliefGain = 1.00f;

        /// <summary>
        /// Wzmocnienie kontrastu, gdy kandydat jest CIEZSZY od rytmu (cios).
        ///
        /// Mniejsze od ulgi CELOWO: po serii katastrof kolejny cios jest slabszym bitem
        /// narracyjnym niz oddech, a narrator, ktory tylko dokreca srube, jest nuzacy
        /// i frustrujacy. Asymetria mnozy WYLACZNIE skladnik ladunku - glosnosc nie ma kierunku.
        /// </summary>
        public float strikeGain = 0.75f;

        /// <summary>
        /// Ile wpisow musi byc w historii, zeby rytm liczyl sie z pelnym glosem.
        /// Rytm z jednego wpisu nie jest rytmem: jeden wedrowiec w dniu 3 nie ma prawa
        /// dawac pelnowartosciowego wolania o napad.
        /// </summary>
        public int minConfidentEntries = 3;

        /// <summary>
        /// Do ilu dni gry bez naszego zdarzenia rytm uznajemy za w pelni aktualny.
        /// Przy mtbDays = 2.5 przerwa 8 dni ma prawdopodobienstwo exp(-8/2.5) = 4.1%.
        /// </summary>
        public float freshRhythmDays = 8f;

        /// <summary>
        /// Od ilu dni gry rytm uznajemy za calkowicie nieaktualny (wynik wraca do neutralu).
        /// Przerwa 24 dni ma prawdopodobienstwo 0.007% - taka cisza znaczy, ze rozgrywka
        /// zmienila charakter i stary rytm nie opisuje juz niczego.
        /// </summary>
        public float staleRhythmDays = 24f;

        /// <summary>
        /// Sila tlumienia antyoscylacyjnego: przy calkowicie naprzemiennej historii
        /// zaufanie spada do 1 - 0.60 = 0.40 i czynnik niemal milknie.
        ///
        /// UWAGA: wartosc powyzej 1 dala by zaufanie UJEMNE, czyli ODWROCENIE rankingu
        /// kandydatow - dokladnie ten blad, przed ktorym ten mechanizm ma chronic.
        /// Kod klamruje ja do [0,1], patrz ComputeVolatility.
        /// </summary>
        public float dampingStrength = 0.60f;

        /// <summary>
        /// Martwa strefa znaku ladunku przy liczeniu oscylacji. Zdarzenie o ladunku
        /// mniejszym co do modulu przerywa lancuch przeskokow zamiast go tworzyc.
        /// </summary>
        public float valenceDeadzone = 0.10f;

        /// <summary>Umowna wartosc "brak informacji", wspolna dla calej warstwy decyzyjnej.</summary>
        public float neutralValue = 0.5f;

        public static ContrastTuning Default()
        {
            return new ContrastTuning();
        }
    }

    /// <summary>
    /// Punkt rytmu w przestrzeni (ladunek, glosnosc) - srednia wykladnicza po ostatnich
    /// zdarzeniach. DRUGA WSPOLRZEDNA JEST KONIECZNA: sam ladunek c = walencja * skala
    /// sklejalby zdarzenie neutralne wielkie i neutralne drobne w ten sam punkt 0.
    /// </summary>
    public class RhythmPoint
    {
        /// <summary>Sredni ladunek Rc, zakres [-1, 1].</summary>
        public float Charge;

        /// <summary>Srednia glosnosc Rm, zakres [0.25, 1].</summary>
        public float Magnitude;

        /// <summary>Ile wpisow faktycznie weszlo do sredniej.</summary>
        public int Entries;
    }

    /// <summary>
    /// Czynnik POTENCJALU DRAMATURGICZNEGO liczony jako KONTRAST kandydata wobec rytmu
    /// ostatnich zdarzen, w przestrzeni (walencja x skala).
    ///
    /// TEZA CZYNNIKA: wartosc narracyjna zdarzenia nie jest jego wlasnoscia wlasna, tylko
    /// RELACJA do tego, co bylo. Ta sama ulga po serii katastrof jest wydarzeniem, a po
    /// serii darow - szumem. Dlatego czynnik nie ocenia kandydata w prozni, tylko mierzy
    /// jego odleglosc od sredniej wykladniczej ostatnich emisji.
    ///
    /// TRZY MECHANIZMY, KAZDY Z INNYM ZADANIEM:
    ///  1. RYTM (srednia wykladnicza) - punkt odniesienia. Srednia wykladnicza, a nie okno
    ///     prostokatne, bo okno daje SKOK wartosci w chwili wypadniecia wpisu z bufora,
    ///     co psulo by odtwarzalnosc sladu miedzy sasiednimi decyzjami.
    ///  2. ASYMETRIA (ulga vs cios) - po serii katastrof oddech jest lepszym bitem niz
    ///     kolejny cios. Mnozy wylacznie skladnik ladunku.
    ///  3. TLUMIENIE ANTYOSCYLACYJNE - gdy historia sama w sobie skacze, "kontrast wobec
    ///     rytmu" przestaje cokolwiek znaczyc, wiec czynnik MILKNIE (jest ciagniety do
    ///     wartosci neutralnej), zamiast dokladac sie do skakania.
    ///
    /// NIEZMIENNIK, NA KTORYM STOI CALY MECHANIZM 3: pewnosc, aktualnosc i tlumienie NIE
    /// ZALEZA OD KANDYDATA - sa wspolnym skalarem calej tury. Formalnie: dla dwoch kandydatow
    /// A i B w tym samym kontekscie zachodzi wynik(A) - wynik(B) = (ksztalt(A) - ksztalt(B))
    /// * zaufanie. Tlumienie moze wiec wylacznie SPLASZCZYC roznice miedzy kandydatami,
    /// nigdy ich nie odwrocic. Kto sprobuje "naprawic" oscylacje karzac kierunek przeciwny
    /// do ostatniego zdarzenia, zlamie ten niezmiennik i wprowadzi dokladnie te oscylacje,
    /// ktorej czynnik ma unikac.
    ///
    /// TLUMIENIE CIAGNIE DO 0.5, A NIE DO 0. Gdyby milknacy czynnik zjezdzal do zera,
    /// systematycznie obnizalby uzytecznosc WSZYSTKICH zdarzen wobec PASS-a, ktory ma wlasna
    /// uzytecznosc i przez ten czynnik nie przechodzi - narrator zamilklby przy kazdym
    /// poszarpanym rytmie, a przyczyny nie bylo by widac w logu. Sterowanie cisza nalezy
    /// do krzywej dramaturgicznej (krok 4), nie do czynnika mierzacego kontrast.
    ///
    /// INTENSITY LEVEL JEST SWIADOMIE POMINIETY. To DAWKA (IntensityTable tlumaczy ja na
    /// punkty incydentu), a nie ksztalt narracyjny; wmieszanie jej tutaj lamalo by te sama
    /// zasade, ktora CLAUDE.md zapisuje przy bogactwie - nie liczyc tej samej rzeczy dwa razy.
    /// Skutek uboczny: kandydaci rozniacy sie tylko modyfikatorem maja identyczny kontrast,
    /// a rozroznia ich dopasowanie kontekstowe.
    ///
    /// OGRANICZENIE ROZDZIELCZOSCI, KTORE TRZEBA OPISAC W PRACY: dzisiejszy katalog daje
    /// tylko CZTERY osiagalne pary (walencja, skala) - (Neg,Major), (Neg,Moderate),
    /// (Pos,Minor), (Neu,Minor) - wiec czynnik zwraca najwyzej cztery rozne wartosci na 84
    /// kandydatow. To wlasciwosc, nie blad: kontrast rozstrzyga MIEDZY grupami, a wewnatrz
    /// grupy decyduja dopasowanie i swiezosc. Jesli w ewaluacji kontrast wyjdzie statystycznie
    /// nieistotny, przyczyna bedzie w KATALOGU (brak par Negative/Minor, Positive/Major,
    /// Neutral/Moderate), a nie we wzorze.
    /// </summary>
    public class Factor_DramaticContrast : IScoringFactor
    {
        /// <summary>
        /// Nazwa wyprowadzona z nazwy pola wagi, nie literal - zmiana nazwy pola przenosi
        /// sie tu sama, a literal zostalby cicho przy starej i czynnik dostalby wage 0.
        /// </summary>
        public const string FactorName = nameof(ScoringWeights.dramaticContrast);

        /// <summary>
        /// Wartosc skali Minor. NIE MOZE BYC ZEREM: przy zerze ladunek c = walencja * skala
        /// zerowalby sie i drobne zdarzenie pozytywne bylo by nieodroznialne od neutralnego.
        /// </summary>
        public const float ScaleMinor = 0.25f;

        public const float ScaleModerate = 0.60f;

        public const float ScaleMajor = 1.00f;

        /// <summary>
        /// Rozpietosc osi glosnosci (Major - Minor). Dzielnik normalizujacy kontrast glosnosci.
        /// </summary>
        public const float ScaleSpan = ScaleMajor - ScaleMinor;

        /// <summary>Rozpietosc osi ladunku: od -1 do +1. Dzielnik normalizujacy kontrast ladunku.</summary>
        public const float ChargeSpan = 2.0f;

        /// <summary>Ponizej tego progu maksimum surowego kontrastu uznajemy za zdegenerowane.</summary>
        private const float DegenerateMaxRaw = 1e-6f;

        private readonly ContrastTuning tuning;
        private readonly float chargeWeight;
        private readonly float magnitudeWeight;
        private readonly float maxRaw;

        public Factor_DramaticContrast()
            : this(null)
        {
        }

        /// <summary>
        /// Ustawienia sa traktowane jak konfiguracja NIEZMIENNA: wagi skladnikow i wynikajace
        /// z nich maksimum sa przepisane w konstruktorze. Podmiana pol obiektu ContrastTuning
        /// po zbudowaniu czynnika NIE jest wspierana - rozjechala by normalizacje z wagami,
        /// czyli zepsula wynik po cichu.
        /// </summary>
        public Factor_DramaticContrast(ContrastTuning tuning)
        {
            this.tuning = tuning ?? ContrastTuning.Default();

            // Wagi skladnikow klamrujemy do wartosci nieujemnych DOKLADNIE TAK SAMO, jak robi
            // to ComputeMaxRaw. Gdyby licznik (raw) i mianownik (maxRaw) czytaly wagi inaczej,
            // ujemna waga z konfiguracji dala by wynik znormalizowany wobec innego maksimum
            // niz to, ktorym faktycznie liczono - blad niewidoczny w zadnym logu.
            chargeWeight = NonNegative(this.tuning.chargeWeight);
            magnitudeWeight = NonNegative(this.tuning.magnitudeWeight);

            // Maksimum surowego kontrastu LICZYMY z wag, raz, w konstruktorze. Wpisanie
            // tu stalej 0.7375 (wartosci dla domyslnych wag 70/30) sprawiloby, ze kazde
            // przestrojenie wag po cichu psuje normalizacje: czynnik albo przestaje siegac
            // 1.0, albo scina sie na plaskim suficie. Wyprowadzenie wzoru - w ComputeMaxRaw.
            maxRaw = ComputeMaxRaw(chargeWeight, magnitudeWeight);
        }

        /// <summary>Ustawienia efektywnie uzyte przez ten czynnik - do logu startowego.</summary>
        public ContrastTuning Tuning
        {
            get { return tuning; }
        }

        /// <summary>
        /// Kres gorny surowego kontrastu przy biezacych wagach (dla 70/30 wynosi 0.7375).
        /// Wystawiony publicznie, bo test regresyjny ma prawo sprawdzic, ze jest LICZONY.
        /// </summary>
        public float MaxRaw
        {
            get { return maxRaw; }
        }

        public string Name
        {
            get { return FactorName; }
        }

        public float Evaluate(ScoredCandidate candidate, DecisionContext context, out string explanation)
        {
            float neutral = tuning.neutralValue;

            if (candidate == null)
            {
                explanation = Neutral("brak kandydata", neutral);
                return neutral;
            }

            if (candidate.IsPass || candidate.Event == null)
            {
                // MARTWA ASERCJA OBRONNA - PASS ma wlasna, rozlaczna tablice czynnikow.
                // Cisza nie ma osi walencji ani skali, wiec czynnik nie ma o niej nic
                // do powiedzenia; jej uzytecznosc liczy sie zupelnie innym wzorem.
                explanation = Neutral("PASS - czynnik nie dotyczy", neutral);
                return neutral;
            }

            EventHistory history = context == null ? null : context.History;
            if (history == null || history.Count == 0)
            {
                // Formalnie wynik i tak wyszedlby neutralny (pewnosc = 0 daje zaufanie = 0),
                // ale jawne wyjscie zostawia w logu czytelna przyczyne zamiast lancucha zer,
                // ktory analiza w Pythonie mogla by wziac za awarie czynnika.
                explanation = Neutral("brak historii - czynnik nie zabiera glosu", neutral);
                return neutral;
            }

            if (maxRaw <= DegenerateMaxRaw)
            {
                // Obie wagi skladnikow zerowe: surowy kontrast jest tozsamosciowo zerem,
                // a normalizacja bylaby dzieleniem 0/0. Glosno i neutralnie.
                explanation = Neutral("OBIE WAGI SKLADNIKOW ZEROWE - czynnik nie mierzy niczego", neutral);
                return neutral;
            }

            ComposedEvent ev = candidate.Event;
            float cCand = Charge(ev.Valence, ev.Scale);
            float mCand = ScaleValue(ev.Scale);

            RhythmPoint rhythm = ComputeRhythm(history);

            float dCharge = Math.Abs(cCand - rhythm.Charge) / ChargeSpan;
            float dMag = Math.Abs(mCand - rhythm.Magnitude) / ScaleSpan;

            // Porownanie DOMYKAJACE od gory (>=), zeby remis mial jednoznaczna etykiete
            // w sladzie. Przy remisie dCharge = 0, wiec wzmocnienie i tak nie ma wplywu
            // na wynik - chodzi wylacznie o to, zeby ten sam stan zawsze opisywal sie tak samo.
            bool relief = cCand >= rhythm.Charge;
            float gain = relief ? tuning.reliefGain : tuning.strikeGain;

            float raw = chargeWeight * dCharge * gain + magnitudeWeight * dMag;

            // Klamra min(1, ...) jest zaworem na przyszlosc: przy dzisiejszym mapowaniu skal
            // raw nigdy nie przekracza maxRaw (to supremum po CALEJ osiagalnej przestrzeni,
            // a nie po dzisiejszym katalogu), ale rozszerzenie mapowania moglo by to zmienic.
            float shaped = Math.Min(1f, raw / maxRaw);

            float confidence;
            float recency;
            float damping;
            float volatility;
            float trust = ComputeTrust(history, context, out confidence, out recency, out damping, out volatility);

            // Tlumienie ciagnie w strone wartosci neutralnej, nie w strone zera - patrz opis klasy.
            float result = Curves.Clamp01(neutral + (shaped - neutral) * trust);

            explanation = BuildTrace(result, rhythm, cCand, mCand, dCharge, relief, gain, dMag,
                                     raw, maxRaw, shaped, trust, confidence, recency, damping, volatility);
            return result;
        }

        /// <summary>
        /// Os walencji jako liczba ZNAKOWANA z naturalnym zerem - odleglosc od neutralnosci
        /// jest taka sama w obie strony, wiec ulga i cios sa mierzone ta sama miara
        /// (a rozroznia je dopiero wzmocnienie kierunkowe).
        /// </summary>
        public static float ValenceValue(Valence v)
        {
            switch (v)
            {
                case Valence.Negative:
                    return -1f;
                case Valence.Positive:
                    return 1f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Os skali. Stosunek 1 : 2.4 : 4 oddaje, ze zdarzenie Major (napad, rojenie, wrak
        /// statku) przebudowuje kolonie, a Minor (wedrowiec, ambrozja) jest przypisem.
        /// </summary>
        public static float ScaleValue(EventScale s)
        {
            switch (s)
            {
                case EventScale.Minor:
                    return ScaleMinor;
                case EventScale.Moderate:
                    return ScaleModerate;
                default:
                    return ScaleMajor;
            }
        }

        /// <summary>Ladunek zdarzenia: walencja razy skala, zakres [-1, 1].</summary>
        public static float Charge(Valence v, EventScale s)
        {
            return ValenceValue(v) * ScaleValue(s);
        }

        /// <summary>
        /// Kres gorny surowego kontrastu przy zadanych wagach - WYPROWADZONY, nie zgadniety.
        ///
        /// Maksimum raw przy wzmocnieniu 1 osiaga sie dla kandydata o ladunku c = +m
        /// i rytmie o ladunku Rc = -Rm (pelny przewrot znaku przy skrajnych glosnosciach).
        /// Podstawiajac P = chargeWeight / ChargeSpan oraz Q = magnitudeWeight / ScaleSpan:
        ///     raw = P * (m + Rm) + Q * |m - Rm| = (P + Q) * max(m, Rm) + (P - Q) * min(m, Rm)
        /// Dla P &gt;= Q wyrazenie rosnie po obu wspolrzednych, wiec maksimum wypada w
        /// m = Rm = ScaleMajor i wynosi 2 * P * ScaleMajor. Dla P &lt; Q wspolczynnik przy
        /// mniejszej wspolrzednej jest ujemny, wiec maksimum wypada na rogu
        /// (ScaleMajor, ScaleMinor) - i oba ramiona daja te sama wartosc.
        ///
        /// Dla wag domyslnych: P = 0.35, Q = 0.40, wiec P &lt; Q i maxRaw = 0.75 * 1.00
        /// + (-0.05) * 0.25 = 0.7375.
        /// </summary>
        public static float ComputeMaxRaw(float chargeWeight, float magnitudeWeight)
        {
            float p = NonNegative(chargeWeight) / ChargeSpan;
            float q = NonNegative(magnitudeWeight) / ScaleSpan;

            if (p >= q)
            {
                return 2f * p * ScaleMajor;
            }
            return (p + q) * ScaleMajor + (p - q) * ScaleMinor;
        }

        /// <summary>
        /// Srednia wykladnicza ostatnich zdarzen w przestrzeni (ladunek, glosnosc).
        ///
        /// KONTRAKT KRYTYCZNY: Recent() zwraca NAJNOWSZY WPIS NA INDEKSIE 0. Wpis i-ty wazy
        /// lambda^i, wiec przy odwroconej kolejnosci wagi lezalyby od zlej strony - kod
        /// dzialalby, log wygladalby sensownie, a wyniki ewaluacji bylyby smieciami.
        /// To jest najczestszy blad integracyjny tej warstwy i dlatego stoi tu ten komentarz.
        ///
        /// Rytm NIE ZALEZY od kandydata, wiec dalo by sie go policzyc raz na ture i podac
        /// czynnikowi. Swiadomie tego nie robimy: kontrakt IScoringFactor wymaga funkcji
        /// CZYSTEJ, a osiem potegowan razy 84 kandydatow to kilkanascie mikrosekund na
        /// decyzje - cena za brak stanu jest tu pomijalna wobec ryzyka, jakie stan wnosi
        /// do determinizmu.
        /// </summary>
        public RhythmPoint ComputeRhythm(EventHistory history)
        {
            var point = new RhythmPoint();
            if (history == null || history.Count == 0)
            {
                return point;
            }

            int window = tuning.rhythmWindow > 0 ? tuning.rhythmWindow : 1;
            IReadOnlyList<EventHistoryEntry> recent = history.Recent(window);
            if (recent.Count == 0)
            {
                return point;
            }

            // Lambda ujemna dawala by wagi zmieniajace znak (rytm "odejmowalby" starsze
            // zdarzenia), a NaN zatrulby cala srednia. Zero jest bezpiecznym zwyrodnieniem:
            // 0^0 = 1, wiec liczy sie wtedy wylacznie najnowszy wpis.
            double lambda = tuning.lambda;
            if (double.IsNaN(lambda) || lambda < 0.0)
            {
                lambda = 0.0;
            }

            double sumW = 0.0;
            double sumC = 0.0;
            double sumM = 0.0;

            for (int i = 0; i < recent.Count; i++)
            {
                EventHistoryEntry e = recent[i];
                if (e == null)
                {
                    continue;
                }

                double w = Math.Pow(lambda, i);
                sumW += w;
                sumC += w * Charge(e.Valence, e.Scale);
                sumM += w * ScaleValue(e.Scale);
            }

            if (sumW <= 0.0)
            {
                // Nieosiagalne przy poprawnym wejsciu (waga wpisu zerowego to lambda^0 = 1),
                // ale dzielenie przez sume wag musi byc bezwarunkowo bezpieczne.
                return point;
            }

            point.Charge = (float)(sumC / sumW);
            point.Magnitude = (float)(sumM / sumW);
            point.Entries = recent.Count;
            return point;
        }

        /// <summary>
        /// Zmiennosc historii: udzial sasiednich par o PRZECIWNYCH, niezerowych znakach
        /// ladunku. Zakres [0,1].
        ///
        /// Wpis w martwej strefie (znak 0) PRZERYWA lancuch przeskokow, a nie tworzy go:
        /// sekwencja pozytyw - neutral - negatyw to modulacja, a nie oscylacja, i nie ma
        /// powodu, zeby uciszala czynnik.
        /// </summary>
        public float ComputeVolatility(EventHistory history)
        {
            if (history == null || history.Count < 2)
            {
                return 0f;
            }

            int window = tuning.volatilityWindow > 0 ? tuning.volatilityWindow : 1;
            IReadOnlyList<EventHistoryEntry> recent = history.Recent(window);
            if (recent.Count < 2)
            {
                return 0f;
            }

            int pairs = recent.Count - 1;
            int flips = 0;
            for (int i = 0; i < pairs; i++)
            {
                EventHistoryEntry a = recent[i];
                EventHistoryEntry b = recent[i + 1];
                if (a == null || b == null)
                {
                    continue;
                }

                int sa = Sign0(Charge(a.Valence, a.Scale), tuning.valenceDeadzone);
                int sb = Sign0(Charge(b.Valence, b.Scale), tuning.valenceDeadzone);
                if (sa != 0 && sb != 0 && sa != sb)
                {
                    flips++;
                }
            }

            return Curves.Clamp01(flips / (float)pairs);
        }

        /// <summary>
        /// Zaufanie do rytmu: iloczyn trzech NIEZALEZNYCH testow informatywnosci -
        /// czy historia jest dosc dluga (pewnosc), dosc swieza (aktualnosc) i dosc spokojna,
        /// zeby "rytm" w ogole cos znaczyl (tlumienie).
        ///
        /// ZADEN Z TRZECH SKLADNIKOW NIE ZALEZY OD KANDYDATA - to warunek niezmiennika
        /// opisanego przy klasie: zaufanie jest wspolnym skalarem tury i moze tylko
        /// splaszczyc roznice miedzy kandydatami, nigdy ich odwrocic.
        ///
        /// ODSTEPSTWO OD PIERWOTNEJ SYGNATURY (przyjete w kontroli spojnosci): wiek rytmu
        /// mierzymy przez DecisionContext.GameDay (float), a nie WorldSnapshot.DaysPassed
        /// (int). Ta sama wielkosc, wyzsza rozdzielczosc - odstepy miedzy decyzjami narratora
        /// to ulamki dnia - i o jedna zaleznosc mniej (czynnik przestaje potrzebowac snapshotu).
        /// Progi 8 i 24 dnia pozostaja bez zmian.
        ///
        /// Zmiennosc wychodzi CZWARTYM parametrem out, mimo ze da sie ja odtworzyc z tlumienia:
        /// slad ma niesc wszystkie wielkosci posrednie, a odtwarzanie jej dzieleniem przez sile
        /// tlumienia wywracalo by sie przy sile rownej zeru. Drugi przelot po historii tylko
        /// dla logu bylby z kolei praca wykonana dwa razy dla kazdego z 84 kandydatow.
        /// </summary>
        public float ComputeTrust(EventHistory history, DecisionContext context,
                                  out float confidence, out float recency, out float damping,
                                  out float volatility)
        {
            confidence = 1f;
            recency = 1f;
            damping = 1f;
            volatility = 0f;

            if (history == null || history.Count == 0)
            {
                confidence = 0f;
                return 0f;
            }

            confidence = tuning.minConfidentEntries <= 0
                ? 1f
                : Curves.Clamp01(history.Count / (float)tuning.minConfidentEntries);

            EventHistoryEntry newest = history.Newest;
            if (context != null && newest != null)
            {
                float gapDays = context.GameDay - newest.GameDay;
                if (gapDays < 0f || float.IsNaN(gapDays))
                {
                    // Wpis "z przyszlosci" jest realny po wczytaniu zapisu albo przy rozjezdzie
                    // licznikow dnia. Klamrujemy do zera, zeby zaden ujemny wiek nie przeciekl
                    // przez rampe - kara za blad okablowania nie moze udawac pomiaru.
                    gapDays = 0f;
                }
                recency = 1f - Curves.Ramp(gapDays, tuning.freshRhythmDays, tuning.staleRhythmDays);
            }

            // Sila tlumienia klamrowana do [0,1]: wartosc powyzej 1 dala by zaufanie ujemne,
            // czyli ODWROCENIE rankingu kandydatow - awarie, przed ktora ten mechanizm chroni.
            float strength = Curves.Clamp01(tuning.dampingStrength);
            volatility = ComputeVolatility(history);
            damping = 1f - strength * volatility;

            return Curves.Clamp01(confidence * recency * damping);
        }

        /// <summary>
        /// Znak ladunku z martwa strefa: +1, -1 albo 0 dla wartosci bliskich zeru.
        /// Martwa strefa istnieje po to, by drobne zdarzenie o ladunku 0.25 nie liczylo sie
        /// jako pelnoprawny przewrot walencji wobec zdarzenia neutralnego.
        /// </summary>
        private static int Sign0(float charge, float deadzone)
        {
            float d = deadzone;
            if (float.IsNaN(d) || d < 0f)
            {
                d = 0f;
            }
            if (charge > d)
            {
                return 1;
            }
            if (charge < -d)
            {
                return -1;
            }
            return 0;
        }

        private static float NonNegative(float v)
        {
            // NaN i nieskonczonosc do zera: kazde porownanie z NaN jest falszywe, wiec
            // naiwne klamrowanie przepuscilo by je prosto do wzoru na maxRaw.
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f)
            {
                return 0f;
            }
            return v;
        }

        private static string Neutral(string powod, float neutral)
        {
            return "kontrast=" + neutral.ToString("0.00", CultureInfo.InvariantCulture) + " | " + powod;
        }

        /// <summary>
        /// Slad w jednej linii, ze WSZYSTKIMI wielkosciami posrednimi - to sa dane wejsciowe
        /// czesci badawczej, wiec musi dac sie z niego odtworzyc kazdy krok rachunku.
        /// Liczby formatowane przez InvariantCulture: polski separator dziesietny rozbilby
        /// parsowanie w Pythonie w kroku 8.
        /// </summary>
        private static string BuildTrace(float result, RhythmPoint rhythm, float cCand, float mCand,
                                         float dCharge, bool relief, float gain, float dMag,
                                         float raw, float maxRaw, float shaped, float trust,
                                         float confidence, float recency, float damping, float volatility)
        {
            var sb = new StringBuilder();
            sb.Append("kontrast=").Append(F(result));
            sb.Append(" | rytm n=").Append(rhythm.Entries.ToString(CultureInfo.InvariantCulture))
              .Append(" Rc=").Append(F(rhythm.Charge))
              .Append(" Rm=").Append(F(rhythm.Magnitude));
            sb.Append(" | kandydat c=").Append(F(cCand)).Append(" m=").Append(F(mCand));
            sb.Append(" | dC=").Append(F(dCharge))
              .Append(" kier=").Append(relief ? "ulga" : "cios")
              .Append(" g=").Append(F(gain))
              .Append(" dM=").Append(F(dMag));
            // maxRaw idzie do sladu obok surowego kontrastu, bo bez niego nie da sie
            // odtworzyc normalizacji z danych - a jest ona liczona z wag, wiec przy
            // przestrojeniu wag zmienia sie razem z nimi.
            sb.Append(" | surowy=").Append(F(raw))
              .Append(" max=").Append(F(maxRaw))
              .Append(" ksztalt=").Append(F(shaped));
            sb.Append(" | zaufanie=").Append(F(trust))
              .Append(" (pewnosc=").Append(F(confidence))
              .Append(" aktualnosc=").Append(F(recency))
              .Append(" tlumienie=").Append(F(damping))
              .Append(" zmiennosc=").Append(F(volatility)).Append(')');
            return sb.ToString();
        }

        private static string F(float v)
        {
            return v.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
