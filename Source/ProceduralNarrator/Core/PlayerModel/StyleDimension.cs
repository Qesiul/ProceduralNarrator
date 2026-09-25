using System.Text;

namespace ProceduralNarrator.Core.PlayerModel
{
    /// <summary>
    /// Cztery cechy STYLU GRACZA (krok 7, decyzja autora nr 5 - cechy z koncepcji 5.3: agresja,
    /// ekonomia, ekspansja, reaktywnosc). Styl, nie umiejetnosci: umiejetnosci gracza modeluje juz
    /// wanilia (StoryWatcher_Adaptation.adaptDays -> mnoznik punktow zagrozenia), wiec styl nie moze
    /// opierac sie na zgonach, stratach ani bezwzglednym bogactwie.
    ///
    /// Nazwy wartosci sa jednoczesnie nazwami w XML, w kolumnach danych i w postaci kanonicznej
    /// snapshotu - dlatego ASCII bez polskich znakow.
    /// </summary>
    public enum StyleDimension
    {
        Walka = 0,
        Gospodarka = 1,
        Ekspansja = 2,
        Reaktywnosc = 3
    }

    public static class StyleDimensions
    {
        public const int Count = 4;

        private static readonly string[] Names = { "Walka", "Gospodarka", "Ekspansja", "Reaktywnosc" };

        public static string Name(StyleDimension d)
        {
            int i = (int)d;
            return i >= 0 && i < Count ? Names[i] : "?";
        }

        /// <summary>
        /// Dokladne dopasowanie nazwy (ordinalne, bez wielkosci liter i bez przycinania). Warunek
        /// XML trzyma ceche jako TEKST, a nie enum: parser Defow przy blednej wartosci enuma loguje blad
        /// i podstawia wartosc DOMYSLNA, czyli warunek po cichu testowalby Walke. Tekst waliduje katalog.
        /// </summary>
        public static bool TryParse(string text, out StyleDimension d)
        {
            for (int i = 0; i < Count; i++)
            {
                if (string.Equals(Names[i], text, System.StringComparison.Ordinal))
                {
                    d = (StyleDimension)i;
                    return true;
                }
            }
            d = StyleDimension.Walka;
            return false;
        }

        /// <summary>
        /// Postac KANONICZNA zbioru mocnych stron dla WorldSnapshot: ";Walka;Ekspansja;" w stalej
        /// kolejnosci cech; pusty zbior = "". Warunek sprawdza podciag ";X;", wiec nazwa jednej cechy
        /// nie moze trafic w srodek innej.
        /// </summary>
        public static string Canonical(bool[] strong)
        {
            if (strong == null)
            {
                return string.Empty;
            }
            var sb = new StringBuilder();
            for (int i = 0; i < Count && i < strong.Length; i++)
            {
                if (strong[i])
                {
                    if (sb.Length == 0)
                    {
                        sb.Append(';');
                    }
                    sb.Append(Names[i]).Append(';');
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Postac DANYCH (kolumny [PN-DATA], linie [PN-GRACZ]): "Walka/Ekspansja", pusty zbior = "-".
        /// Separator "/", bo ";" rozdziela pola wiersza danych.
        /// </summary>
        public static string DataForm(bool[] strong)
        {
            var sb = new StringBuilder();
            if (strong != null)
            {
                for (int i = 0; i < Count && i < strong.Length; i++)
                {
                    if (strong[i])
                    {
                        if (sb.Length > 0)
                        {
                            sb.Append('/');
                        }
                        sb.Append(Names[i]);
                    }
                }
            }
            return sb.Length == 0 ? "-" : sb.ToString();
        }

        /// <summary>Postac kanoniczna -> tablica flag (odwrotnosc Canonical). Nieznane nazwy sa pomijane.</summary>
        public static bool[] FromCanonical(string canonical)
        {
            var wynik = new bool[Count];
            if (string.IsNullOrEmpty(canonical))
            {
                return wynik;
            }
            for (int i = 0; i < Count; i++)
            {
                wynik[i] = canonical.IndexOf(";" + Names[i] + ";", System.StringComparison.Ordinal) >= 0;
            }
            return wynik;
        }
    }
}
