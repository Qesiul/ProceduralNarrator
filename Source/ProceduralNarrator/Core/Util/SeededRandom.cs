using System;
using System.Collections.Generic;

namespace ProceduralNarrator.Core.Util
{
    /// <summary>
    /// Deterministyczne zrodlo losowosci oparte o ziarno.
    ///
    /// =====================================================================================
    ///  DLACZEGO ZIARNO JEST MIESZANE, A NIE PODAWANE WPROST
    /// =====================================================================================
    /// System.Random inicjalizuje swoj generator subtraktywny WPROST z ziarna, przez co
    /// PIERWSZA wylosowana wartosc jest silnie skorelowana z ziarnem. Dla ziaren rosnacych
    /// o staly krok pierwsze losowania tworza ciag Weyla - arytmetyczny modulo zakres.
    ///
    /// Dla nas to nie jest ciekawostka, tylko awaria calego wyboru zwyciezcy. Narrator
    /// dostaje ziarno wyprowadzone z numeru ticku, a tick rosnie DOKLADNIE o 1000 miedzy
    /// decyzjami; SelectionPolicy pobiera przy tym DOKLADNIE JEDNO losowanie na runde.
    /// Zmierzone na System.Random dla ziaren tick+104729, tick co 1000:
    ///
    ///     338391, 763706, 189020, 614334, 39648, 464962, 890277, ...
    ///     delty: +425315, -574686, +425314, -574686, +425314, ...
    ///
    /// Czyli softmax nie losowal - obracal wskaznikiem po stalym skoku. Rozklad BRZEGOWY
    /// wygladal przy tym poprawnie (na 5 rownych kubelkow: 60/60/61/59/60 z 300 decyzji),
    /// wiec zwykly test histogramu tego NIE wykrywa.
    ///
    /// Najgorsza konsekwencja: ticki narratora sa takie same w KAZDEJ rozgrywce, wiec
    /// sekwencja wyborow byla identyczna w kazdej partii. To wysadza metryke "unikalnosc
    /// w serii rozgrywek", ktora jest rdzeniem ewaluacji porownawczej z sekcji 12 koncepcji.
    ///
    /// Naprawa: ziarno przechodzi przez funkcje lawinowa (finalizer w stylu splitmix/murmur)
    /// zanim trafi do System.Random. Zmiana jednego bitu ziarna zmienia srednio polowe bitow
    /// wyniku, wiec sasiednie ziarna daja nieskorelowane strumienie. Determinizm zostaje
    /// nienaruszony - mieszanie jest czysta funkcja, wiec to samo ziarno nadal daje ten sam ciag.
    /// =====================================================================================
    /// </summary>
    public class SeededRandom : IRandomSource
    {
        private readonly Random random;

        /// <summary>Ziarno po wymieszaniu - wylacznie do logu i diagnostyki odtwarzalnosci.</summary>
        public readonly int EffectiveSeed;

        /// <summary>Ziarno podane przez wolajacego, przed wymieszaniem.</summary>
        public readonly int RawSeed;

        public SeededRandom(int seed)
        {
            RawSeed = seed;
            EffectiveSeed = Avalanche(seed);
            random = new Random(EffectiveSeed);
        }

        /// <summary>
        /// Funkcja lawinowa na 32 bitach (finalizer splitmix32). Stale sa dobrane tak, by
        /// pojedynczy zmieniony bit wejscia zmienial okolo polowy bitow wyjscia - to wlasnie
        /// zrywa korelacje miedzy sasiednimi ziarnami.
        ///
        /// Liczymy na uint, bo przesuniecia w prawo maja byc LOGICZNE. Na int operator >>
        /// jest arytmetyczny i kopiowalby bit znaku, psujac rozklad dla ziaren ujemnych.
        /// Mnozenia sa w unchecked, bo przepelnienie jest tu zamierzone i stanowi czesc algorytmu.
        /// </summary>
        public static int Avalanche(int seed)
        {
            unchecked
            {
                uint x = (uint)seed;
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;

                // System.Random rzuca dla int.MinValue (bierze Math.Abs z ziarna), a
                // Math.Abs(int.MinValue) nie miesci sie w int. Zbijamy ten jeden przypadek.
                int wynik = (int)x;
                return wynik == int.MinValue ? 0 : wynik;
            }
        }

        public int Next(int maxExclusive)
        {
            return maxExclusive <= 0 ? 0 : random.Next(maxExclusive);
        }

        public T Pick<T>(IReadOnlyList<T> items)
        {
            if (items == null || items.Count == 0)
            {
                return default(T);
            }
            return items[random.Next(items.Count)];
        }
    }
}
