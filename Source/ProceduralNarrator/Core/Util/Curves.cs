using System;

namespace ProceduralNarrator.Core.Util
{
    /// <summary>
    /// JEDYNE zrodlo elementarnych krzywych rdzenia: klamrowanie do [0,1], rampa liniowa
    /// i zanik polowiczny. Zero zaleznosci - ani od API gry, ani od reszty rdzenia.
    ///
    /// Dlaczego osobny plik, a nie kopie w kazdym czynniku: krok 3 wprowadza cztery
    /// niezalezne czynniki scoringu, a kazdy z nich potrzebuje tych samych trzech funkcji.
    /// Trzy kopie Clamp01 to trzy potencjalnie rozne zachowania na wartosciach zdegenerowanych
    /// (NaN), czyli rozjazd, ktory nie objawia sie bledem kompilacji, tylko cicha roznica
    /// w wynikach ewaluacji. NarrativeCondition.Clamp01/Ramp sa CIENKIMI DELEGATAMI do tej klasy.
    ///
    /// Zakaz prywatnych kopii tych funkcji gdziekolwiek indziej w projekcie.
    /// </summary>
    public static class Curves
    {
        /// <summary>
        /// Klamruje do [0,1] i JAWNIE zbija NaN do zera.
        ///
        /// Jawny test na NaN nie jest ostroznoscia na wyrost: kazde porownanie z NaN jest
        /// falszywe, wiec naiwne "v &lt; 0 ? 0 : (v &gt; 1 ? 1 : v)" przepuszcza NaN nietkniete.
        /// NaN wpuszczony w scoring zatruwa sume wazona, a potem exp(NaN) rozsypuje kwantyzacje
        /// rozkladu wyboru i losowanie nie trafia w zaden kubelek. Ta sama pulapka jest lapana
        /// osobno w ScoreMath.Sanitize01 - tam z dodatkowa flaga, bo tam trzeba o niej krzyczec
        /// w logu; tutaj cicha korekta wystarczy, bo Curves jest uzywane w gestych petlach.
        ///
        /// Nieskonczonosci obsluguja sie same: +Inf &gt; 1 daje 1, -Inf &lt; 0 daje 0.
        /// </summary>
        public static float Clamp01(float v)
        {
            if (float.IsNaN(v))
            {
                return 0f;
            }
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>
        /// Rampa liniowa: from -&gt; 0, to -&gt; 1 (dziala tez malejaco, gdy from &gt; to).
        ///
        /// Przypadek from == to jest zdegenerowany (dzielenie przez zero), wiec rampa
        /// zamienia sie w prog skokowy - to zachowanie jest odziedziczone bez zmian po
        /// NarrativeCondition.Ramp, zeby zaden istniejacy warunek miekki nie zmienil wyniku.
        /// </summary>
        public static float Ramp(float value, float from, float to)
        {
            if (from == to)
            {
                return value >= to ? 1f : 0f;
            }
            return Clamp01((value - from) / (to - from));
        }

        /// <summary>
        /// Zanik polowiczny: 0.5^(max(0,delta) / halfLife). Wynik w (0,1], dla delta = 0 rowny 1.
        ///
        /// Delta jest klamrowana do zera od dolu, bo ujemny "wiek" nie ma sensu, a bez klamry
        /// dawalby wage &gt; 1 i mogl przestrzelic sumy gestosci powyzej zakresu. Ujemna delta
        /// jest realna: po wczytaniu zapisu albo przy rozjezdzie licznikow dnia gry wpis w
        /// historii moze wygladac na "z przyszlosci".
        ///
        /// halfLife &lt;= 0 to konfiguracja zdegenerowana (dzielenie przez zero w wykladniku).
        /// Interpretujemy ja jako zanik natychmiastowy - identycznie jak konwencja przyjeta
        /// dla wag recencji w czynniku swiezosci, zeby dwa miejsca nie rozumialy tego samego
        /// parametru inaczej. Wlasciwa obrona przed taka konfiguracja jest Sanitize() parametrow,
        /// tutaj jest tylko siatka bezpieczenstwa.
        /// </summary>
        public static float HalfLifeDecay(float delta, float halfLife)
        {
            if (float.IsNaN(delta) || float.IsNaN(halfLife))
            {
                // Wiek nieokreslony nie moze wnosic wagi do zadnej sumy - inaczej NaN
                // przeciekloby do gestosci i dalej do calego rozkladu wyboru.
                return 0f;
            }
            if (delta < 0f)
            {
                delta = 0f;
            }
            if (halfLife <= 0f)
            {
                return delta <= 0f ? 1f : 0f;
            }
            // Wykladnik liczony w double: przy dlugiej rozgrywce delta/halfLife siega
            // kilkudziesieciu, a Math.Pow i tak pracuje w double - liczenie w float
            // dokladalo by tylko jedno zaokraglenie przed potegowaniem.
            return Clamp01((float)Math.Pow(0.5, delta / (double)halfLife));
        }
    }
}
