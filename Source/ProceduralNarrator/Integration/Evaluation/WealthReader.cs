using System.Reflection;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration.Evaluation
{
    /// <summary>
    /// Bogactwo mapy domowej dla linii [PN-FIRED] i [PN-DZIEN] BEZ przeliczania (krok 9, etap L, decyzja L-7).
    /// Map.PlayerWealthForStoryteller mapy domowej = przedmioty + 0,5 budynki + pionki (Map.cs:302-313), a gettery
    /// WealthWatcher przeliczaja, gdy od ostatniego liczenia minelo > 5000 tickow - odczyt w grze Cassandry
    /// przestawialby wtedy faze przeliczania. Czytamy wiec pola z ostatniego liczenia gry i podajemy ich wiek.
    /// Gra liczy co najmniej co 30 000 tickow (zapis historii bogactwa).
    /// </summary>
    internal static class WealthReader
    {
        private const BindingFlags Pole = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo Przedmioty = typeof(WealthWatcher).GetField("wealthItems", Pole);
        private static readonly FieldInfo Budynki = typeof(WealthWatcher).GetField("wealthBuildings", Pole);
        private static readonly FieldInfo Pionki = typeof(WealthWatcher).GetField("wealthPawns", Pole);
        private static readonly FieldInfo OstatnieLiczenie = typeof(WealthWatcher).GetField("lastCountTick", Pole);

        /// <summary>Takie samo progowanie jak WealthWatcher.RecountIfNeeded.</summary>
        private const int OkresPrzeliczania = 5000;

        public static bool Available
        {
            get
            {
                return JestFloat(Przedmioty) && JestFloat(Budynki) && JestFloat(Pionki) && JestFloat(OstatnieLiczenie);
            }
        }

        private static bool JestFloat(FieldInfo f)
        {
            return f != null && f.FieldType == typeof(float);
        }

        /// <summary>
        /// Bogactwo dla narratora z ostatniego liczenia gry i wiek odczytu w tickach. False = brak pomiaru (mapa
        /// niedomowa, pola nieznane, gra jeszcze nie liczyla). Tryb stalego bogactwa: wartosc z krzywej, wiek 0.
        /// </summary>
        public static bool TryRead(Map m, out float bogactwo, out int wiek)
        {
            bogactwo = -1f;
            wiek = -1;
            if (m == null || !m.IsPlayerHome || m.wealthWatcher == null || Find.TickManager == null)
            {
                return false;
            }
            if (Find.Storyteller != null && Find.Storyteller.difficulty != null && Find.Storyteller.difficulty.fixedWealthMode)
            {
                bogactwo = m.PlayerWealthForStoryteller;
                wiek = 0;
                return true;
            }
            if (!Available)
            {
                return false;
            }
            WealthWatcher w = m.wealthWatcher;
            float ostatnie = (float)OstatnieLiczenie.GetValue(w);
            if (ostatnie < 0f)
            {
                return false;
            }
            bogactwo = (float)Przedmioty.GetValue(w) + (float)Budynki.GetValue(w) * 0.5f + (float)Pionki.GetValue(w);
            wiek = Find.TickManager.TicksGame - (int)ostatnie;
            return true;
        }

        /// <summary>Czy getter bogactwa (a wiec i punkty zagrozenia) nie wymusi teraz przeliczenia.</summary>
        public static bool NoRecount(Map m)
        {
            float b;
            int wiek;
            return TryRead(m, out b, out wiek) && wiek <= OkresPrzeliczania;
        }
    }
}
