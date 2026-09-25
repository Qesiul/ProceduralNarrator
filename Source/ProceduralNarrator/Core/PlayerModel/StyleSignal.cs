namespace ProceduralNarrator.Core.PlayerModel
{
    /// <summary>
    /// Osiem POMIAROW stylu (decyzja autora nr 19: dwa na ceche, ataki gracza na osady liczone od razu).
    /// Kazdy pomiar to proporcja albo odsetek, nie suma (decyzja nr 11 - ochrona przed sprzezeniem
    /// zwrotnym: narrator nie moze "wychowac" gracza, ktorego potem mierzy, bo liczba naszych zdarzen
    /// nie wchodzi wprost do zadnego licznika).
    ///
    /// Kolejnosc wartosci jest kolejnoscia par licznik/mianownik w linii D zapisu gry - NIE zmieniac
    /// bez podbicia formatu ksiegi stylu.
    /// </summary>
    public enum StyleSignal
    {
        /// <summary>Walka: udzial wartosci budowli obronnych w wartosci budynkow gracza.</summary>
        Obrona = 0,

        /// <summary>Walka: ataki gracza na osady i obozy na dzien.</summary>
        Inicjatywa = 1,

        /// <summary>Gospodarka: czas produktywny / (produktywny + noszenie + sprzatanie).</summary>
        Praca = 2,

        /// <summary>Gospodarka: udzial warsztatow i zasilania w wartosci budynkow.</summary>
        Produkcja = 3,

        /// <summary>
        /// Ekspansja: odsetek przyjetych ofert dolaczenia (wedrowiec, dzikus; rozbitek - "sukces" to pomoc, nie dolaczenie,
        /// znane ograniczenie). Znany od 3 rozstrzygnietych ofert (decyzja autora po przegladzie S8).
        /// </summary>
        Przyjecia = 4,

        /// <summary>Ekspansja: zwerbowani / schwytani jency.</summary>
        Werbunek = 5,

        /// <summary>Reaktywnosc: sredni udzial kolonistow pod bronia w epizodzie zagrozenia (promile).</summary>
        Poborowi = 6,

        /// <summary>Reaktywnosc: 1 - min(czas do pierwszego poboru, limit)/limit, na epizod (promile).</summary>
        Szybkosc = 7
    }

    /// <summary>
    /// Sposob skladania dni z kolejki w jedna wartosc pomiaru.
    ///
    /// DailyRatioMean - pomiary STANU: kazdy dzien ma rowna wage (decyzja autora nr 8 - "worek" dni
    /// o rownej wadze), wiec srednia z dziennych ilorazow; dzien z mianownikiem 0 jest pomijany.
    /// RatioOfSums - pomiary ZDARZEN: jednostka jest zdarzenie (oferta, jeniec, epizod zagrozenia),
    /// wiec iloraz sum z okna; dni bez zdarzen wnosza 0/0, czyli nic. Srednia z ilorazow dalaby
    /// dniowi z jedna oferta te sama wage co dniowi z trzema.
    /// </summary>
    public enum StyleAggregation
    {
        DailyRatioMean,
        RatioOfSums
    }

    public static class StyleSignals
    {
        public const int Count = 8;

        private static readonly string[] Names =
        {
            "Obrona", "Inicjatywa", "Praca", "Produkcja", "Przyjecia", "Werbunek", "Poborowi", "Szybkosc"
        };

        private static readonly StyleDimension[] Dimensions =
        {
            StyleDimension.Walka, StyleDimension.Walka,
            StyleDimension.Gospodarka, StyleDimension.Gospodarka,
            StyleDimension.Ekspansja, StyleDimension.Ekspansja,
            StyleDimension.Reaktywnosc, StyleDimension.Reaktywnosc
        };

        private static readonly StyleAggregation[] Modes =
        {
            StyleAggregation.DailyRatioMean, StyleAggregation.RatioOfSums,
            StyleAggregation.DailyRatioMean, StyleAggregation.DailyRatioMean,
            StyleAggregation.RatioOfSums, StyleAggregation.RatioOfSums,
            StyleAggregation.RatioOfSums, StyleAggregation.RatioOfSums
        };

        /// <summary>
        /// Skala licznika: pomiary epizodu sa zapisywane w promilach jako liczby calkowite (zapis gry
        /// trzyma wylacznie liczby calkowite), wiec x = suma licznikow / (1000 * suma mianownikow).
        /// </summary>
        private static readonly int[] Scales = { 1, 1, 1, 1, 1, 1, 1000, 1000 };

        public static string Name(StyleSignal s)
        {
            int i = (int)s;
            return i >= 0 && i < Count ? Names[i] : "?";
        }

        public static StyleDimension Dimension(StyleSignal s)
        {
            return Dimensions[(int)s];
        }

        public static StyleAggregation Mode(StyleSignal s)
        {
            return Modes[(int)s];
        }

        public static int Scale(StyleSignal s)
        {
            return Scales[(int)s];
        }

        /// <summary>Dokladne dopasowanie nazwy (ordinalne). Nazwa w XML to tekst, walidowany w Sanitize.</summary>
        public static bool TryParse(string text, out StyleSignal s)
        {
            for (int i = 0; i < Count; i++)
            {
                if (string.Equals(Names[i], text, System.StringComparison.Ordinal))
                {
                    s = (StyleSignal)i;
                    return true;
                }
            }
            s = StyleSignal.Obrona;
            return false;
        }
    }
}
