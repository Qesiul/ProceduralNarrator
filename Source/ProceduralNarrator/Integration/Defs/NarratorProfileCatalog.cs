using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Tension;
using Verse;

namespace ProceduralNarrator.Integration.Defs
{
    /// <summary>
    /// Dostep do katalogu osobowosci narratora i losowanie profilu na starcie rozgrywki.
    ///
    /// Cala klasa jest bezstanowa: profil raz wylosowany zyje w NarratorMemoryComponent
    /// i to ON jest zrodlem prawdy, a nie ta klasa. Gdyby katalog cache'owal "biezacy profil",
    /// wyjscie do menu i wczytanie innego zapisu zostawialoby cache wskazujacy na profil
    /// poprzedniej rozgrywki - dokladnie ten sam blad, przed ktorym chroni brak cache'owania
    /// referencji do komponentu w StorytellerComp_Generative.
    /// </summary>
    public static class NarratorProfileCatalog
    {
        public static List<NarratorProfileDef> AllDefs()
        {
            return DefDatabase<NarratorProfileDef>.AllDefsListForReading;
        }

        /// <summary>
        /// Losuje profil wazony przez selectionWeight, deterministycznie z podanego ziarna.
        ///
        /// Determinizm jest wymogiem sekcji 11: ta sama rozgrywka ma dawac ten sam profil.
        /// Ziarno pochodzi z runId (patrz NarratorMemoryComponent), a NIE z Verse.Rand -
        /// waniliowy generator jest wspoldzielony z cala gra i pobranie z niego jednej liczby
        /// przesunelo by kazdy pozniejszy losowy wynik w tej sesji.
        ///
        /// Zwraca null, gdy katalog jest pusty albo wszystkie wagi sa zerowe. Wolajacy ma
        /// wtedy uzyc NarratorProfile.Fallback() i zglosic to glosno.
        /// </summary>
        public static NarratorProfileDef PickWeighted(int seed)
        {
            List<NarratorProfileDef> defy = AllDefs();
            if (defy == null || defy.Count == 0)
            {
                return null;
            }

            double suma = 0.0;
            for (int i = 0; i < defy.Count; i++)
            {
                suma += Waga(defy[i]);
            }

            if (suma <= 0.0)
            {
                return null;
            }

            // Ruletka na liczbie z przedzialu [0, suma). Uzywamy Next(int) z wlasnego
            // generatora i skalujemy, zeby nie wprowadzac drugiego sposobu losowania
            // liczb rzeczywistych do projektu.
            var rng = new Core.Util.SeededRandom(seed);
            const int Rozdzielczosc = 1000000;
            double los = suma * (rng.Next(Rozdzielczosc) / (double)Rozdzielczosc);

            double biezaca = 0.0;
            for (int i = 0; i < defy.Count; i++)
            {
                biezaca += Waga(defy[i]);
                if (los < biezaca)
                {
                    return defy[i];
                }
            }

            // Nieosiagalne przy sumie > 0, ale bledy zaokraglen double bywaja zlosliwe.
            return defy[defy.Count - 1];
        }

        public static NarratorProfileDef ById(string defName)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return null;
            }
            return DefDatabase<NarratorProfileDef>.GetNamedSilentFail(defName);
        }

        /// <summary>
        /// Zwraca profil rdzenia dla podanego defName, albo profil awaryjny, gdy go nie ma.
        /// Sanityzuje kopie i zglasza poprawki - ta sama konwencja co przy parametrach compa.
        /// </summary>
        public static NarratorProfile Resolve(string defName)
        {
            NarratorProfileDef def = ById(defName);
            if (def == null)
            {
                if (!string.IsNullOrEmpty(defName))
                {
                    PNLog.Error("Profil narratora '" + defName + "' nie istnieje w katalogu - "
                                + "uzywam profilu awaryjnego. Najczestsza przyczyna: profil usunieto "
                                + "z XML miedzy sesjami, a zapis gry nadal go wskazuje.");
                }
                return NarratorProfile.Fallback();
            }

            NarratorProfile profil = def.ToProfile();
            string poprawki = profil.Tension.Sanitize();
            if (!string.IsNullOrEmpty(poprawki))
            {
                PNLog.Warn("Profil " + profil.Id + " - poprawiono parametry krzywej: " + poprawki);
            }
            return profil;
        }

        public static string DescribeCatalog()
        {
            List<NarratorProfileDef> defy = AllDefs();
            if (defy == null || defy.Count == 0)
            {
                return "BRAK PROFILI";
            }

            double suma = 0.0;
            for (int i = 0; i < defy.Count; i++)
            {
                suma += Waga(defy[i]);
            }

            var sb = new StringBuilder(200);
            sb.Append(defy.Count.ToString(CultureInfo.InvariantCulture)).Append(" profili: ");
            for (int i = 0; i < defy.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                float w = Waga(defy[i]);
                sb.Append(defy[i].defName).Append(' ');
                sb.Append(suma > 0.0
                              ? (100.0 * w / suma).ToString("0.#", CultureInfo.InvariantCulture) + "%"
                              : "waga 0");
            }
            return sb.ToString();
        }

        private static float Waga(NarratorProfileDef def)
        {
            if (def == null)
            {
                return 0f;
            }
            float w = def.selectionWeight;
            if (float.IsNaN(w) || float.IsInfinity(w) || w < 0f)
            {
                return 0f;
            }
            return w;
        }
    }
}
