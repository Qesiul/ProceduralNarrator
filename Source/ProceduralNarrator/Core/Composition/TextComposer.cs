using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Composition
{
    /// <summary>
    /// SKLADA TEKST LISTU GRACZA z klockow zwyciezcy, z wariantami zaleznymi od kontekstu
    /// (krok 8, decyzje autora K8-4 i K8-6).
    ///
    /// ComposedEvent.Description zostaje TEKSTEM BAZOWYM (sklejone textFragment) - na nim stoja slad,
    /// log czytelny i porownywalnosc danych z wczesniejszych krokow. Tekst dla gracza powstaje tutaj,
    /// raz na WYKONANE zdarzenie, bo dopiero po wykonaniu wiadomo, jaka frakcje wybrala gra.
    ///
    /// ZASADA WYBORU (w kazdym klocku osobno, w kolejnosci narracyjnej zdarzenia):
    ///   1. pula = warianty DOPASOWANE (z warunkiem - TextVariant.IsSpecific), ktorych warunki zachodza;
    ///   2. gdy pusta: pula = tekst bazowy klocka (id "baza", jesli niepusty) + zamienniki bez warunkow;
    ///   3. wybor w puli: deterministyczny, z ziarna decyzji i identyfikatora klocka (bez RNG gry -
    ///      rozgrywka i ramiona symulatora sie nie rozjezdzaja; ten sam stan daje ten sam tekst).
    /// Wariant dopasowany wygrywa z bazowym, bo mowi WIECEJ prawdy o tej konkretnej sytuacji; bazowy
    /// jest prawdziwy zawsze (tak go przepisano w rundzie "fakt w tekscie = warunek twardy").
    ///
    /// ZASADA PRAWDY dla wariantow jest ta sama co dla klockow: fakt w tekscie = warunek twardy.
    /// Walidacja (Validate) odrzuca wariant, ktory mogl by stwierdzic cos niesprawdzonego:
    /// warunek miekki (nie nadpisuje IsMet, wiec "zachodzi" zawsze), {FRAKCJA} bez requiresFaction
    /// (surowy znacznik w liscie) i odwrotnie, nieznane nawiasy klamrowe, pusta tresc.
    ///
    /// Warunki pamieci narratora (Cond_Fakt*, Cond_Watek*, Cond_StylMocnaStrona) SA w wariantach
    /// dozwolone, inaczej niz w warunkach klocka: tekst nie zmienia liczby dostepnych akcji m, wiec nie
    /// rusza K = B/m ani puli bramy PASS (powod, dla ktorego klocki ich nie czytaja).
    /// </summary>
    public static class TextComposer
    {
        /// <summary>Znacznik nazwy frakcji wybranej przez gre (tylko w wariancie requiresFaction).</summary>
        public const string FactionPlaceholder = "{FRAKCJA}";

        /// <summary>Id tekstu bazowego klocka w sladzie.</summary>
        public const string BaseId = "baza";

        /// <summary>Id "brak tekstu" w sladzie (klocek bez tekstu i bez pasujacych wariantow).</summary>
        public const string NoneId = "-";

        /// <summary>
        /// Tekst listu dla wykonanego zdarzenia. Slad: "klocek:wariant" po przecinku, w kolejnosci
        /// klockow zdarzenia (tylko klocki z jakimkolwiek tekstem albo wariantem).
        /// </summary>
        public static string Compose(ComposedEvent zdarzenie, WorldSnapshot snapshot, string nazwaFrakcji,
                                     int ziarno, out string slad)
        {
            slad = string.Empty;
            if (zdarzenie == null || zdarzenie.Blocks == null)
            {
                return string.Empty;
            }

            bool akcjaSkalujeSie = false;
            for (int i = 0; i < zdarzenie.Blocks.Count; i++)
            {
                Block b = zdarzenie.Blocks[i];
                if (b != null && b.Type == BlockType.Action)
                {
                    akcjaSkalujeSie = b.ScalesWithPoints;
                    break;
                }
            }

            var tekst = new StringBuilder();
            var sladSb = new StringBuilder();
            for (int i = 0; i < zdarzenie.Blocks.Count; i++)
            {
                Block b = zdarzenie.Blocks[i];
                if (b == null)
                {
                    continue;
                }
                bool maCokolwiek = !string.IsNullOrEmpty(b.TextFragment)
                                   || (b.TextVariants != null && b.TextVariants.Count > 0);
                if (!maCokolwiek)
                {
                    continue;
                }

                string id;
                string fragment = Pick(b, snapshot, zdarzenie.Intensity, akcjaSkalujeSie, nazwaFrakcji, ziarno, out id);
                if (!string.IsNullOrEmpty(fragment))
                {
                    if (tekst.Length > 0)
                    {
                        tekst.Append(' ');
                    }
                    tekst.Append(fragment);
                }
                if (sladSb.Length > 0)
                {
                    sladSb.Append(',');
                }
                sladSb.Append(b.Id).Append(':').Append(id);
            }
            slad = sladSb.ToString();
            return tekst.ToString();
        }

        /// <summary>
        /// Wybor tekstu jednego klocka - zasada z komentarza klasy. Publiczne dla walidatora (TEST 16).
        /// </summary>
        public static string Pick(Block klocek, WorldSnapshot snapshot, IntensityLevel mocKoncowa,
                                  bool akcjaSkalujeSie, string nazwaFrakcji, int ziarno, out string id)
        {
            id = NoneId;
            if (klocek == null)
            {
                return null;
            }

            var dopasowane = new List<TextVariant>();
            var zamienniki = new List<TextVariant>();
            if (klocek.TextVariants != null)
            {
                for (int i = 0; i < klocek.TextVariants.Count; i++)
                {
                    TextVariant v = klocek.TextVariants[i];
                    if (v == null)
                    {
                        continue;
                    }
                    if (v.IsSpecific)
                    {
                        if (v.Matches(snapshot, mocKoncowa, akcjaSkalujeSie, nazwaFrakcji))
                        {
                            dopasowane.Add(v);
                        }
                    }
                    else
                    {
                        zamienniki.Add(v);
                    }
                }
            }

            int indeks;
            if (dopasowane.Count > 0)
            {
                indeks = Indeks(ziarno, klocek.Id, dopasowane.Count);
                TextVariant wybrany = dopasowane[indeks];
                id = wybrany.id;
                return Wypelnij(wybrany, nazwaFrakcji);
            }

            bool maBaze = !string.IsNullOrEmpty(klocek.TextFragment);
            int pula = (maBaze ? 1 : 0) + zamienniki.Count;
            if (pula == 0)
            {
                return null;
            }
            indeks = Indeks(ziarno, klocek.Id, pula);
            if (maBaze && indeks == 0)
            {
                id = BaseId;
                return klocek.TextFragment;
            }
            TextVariant zamiennik = zamienniki[indeks - (maBaze ? 1 : 0)];
            id = zamiennik.id;
            return Wypelnij(zamiennik, nazwaFrakcji);
        }

        private static string Wypelnij(TextVariant v, string nazwaFrakcji)
        {
            if (v.text == null)
            {
                return null;
            }
            // Znacznik tylko w wariancie requiresFaction - ktory pasuje wylacznie przy znanej nazwie.
            return v.requiresFaction ? v.text.Replace(FactionPlaceholder, nazwaFrakcji ?? string.Empty) : v.text;
        }

        /// <summary>
        /// Indeks w puli: z ziarna decyzji i stabilnego hasha identyfikatora klocka (FNV-1a - NIE
        /// string.GetHashCode, ktory w .NET Core jest losowany per proces).
        /// </summary>
        private static int Indeks(int ziarno, string idKlocka, int rozmiar)
        {
            if (rozmiar <= 1)
            {
                return 0;
            }
            unchecked
            {
                int mieszane = SeededRandom.Avalanche(ziarno ^ (StableHash(idKlocka) * 16777619));
                return new SeededRandom(mieszane).Next(rozmiar);
            }
        }

        /// <summary>FNV-1a 32-bit, dodatni.</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                if (s != null)
                {
                    for (int i = 0; i < s.Length; i++)
                    {
                        h = (h ^ s[i]) * 16777619;
                    }
                }
                return (int)(h & 0x7FFFFFFF);
            }
        }

        // =========================================================================================
        //  WALIDACJA KATALOGU WARIANTOW
        // =========================================================================================

        /// <summary>
        /// Sprawdza warianty wszystkich klockow i USUWA niepoprawne (reszta klocka zostaje - zly wariant
        /// nie moze wylaczyc zdarzenia). Zwraca opisy odrzucen; pusta lista = katalog czysty. Wolaja:
        /// audyt startowy (PNStartup) i walidator offline.
        /// </summary>
        public static List<string> Validate(IList<Block> klocki)
        {
            var problemy = new List<string>();
            if (klocki == null)
            {
                return problemy;
            }
            for (int k = 0; k < klocki.Count; k++)
            {
                Block b = klocki[k];
                if (b == null || b.TextVariants == null || b.TextVariants.Count == 0)
                {
                    continue;
                }
                // Przejscie W PRZOD: przy zdublowanym id zostaje wariant WCZESNIEJSZY w pliku,
                // a odpada kazdy kolejny o tym samym id.
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var poprawne = new List<TextVariant>(b.TextVariants.Count);
                for (int i = 0; i < b.TextVariants.Count; i++)
                {
                    TextVariant v = b.TextVariants[i];
                    if (v == null)
                    {
                        problemy.Add(b.Id + ": pusty wariant na pozycji " + i);
                        continue;
                    }
                    string powod = Problem(v, ids);
                    if (powod != null)
                    {
                        problemy.Add(b.Id + ": wariant " + (string.IsNullOrEmpty(v.id) ? "#" + i : v.id) + " odrzucony - " + powod);
                        continue;
                    }
                    ids.Add(v.id);
                    poprawne.Add(v);
                }
                b.TextVariants = poprawne;
            }
            return problemy;
        }

        /// <summary>Powod odrzucenia wariantu albo null.</summary>
        public static string Problem(TextVariant v, HashSet<string> zajeteId)
        {
            if (v == null)
            {
                return "pusty wariant";
            }
            if (string.IsNullOrEmpty(v.id))
            {
                return "brak id";
            }
            if (v.id == BaseId || v.id == NoneId)
            {
                return "id zarezerwowane (" + v.id + ")";
            }
            // BIALA LISTA znakow id (przeglad S10): id trafia do pola warianty= w [PN-EXEC]; separatory sladu
            // (przecinek, dwukropek), pola danych (srednik, znak rownosci) i znaki bialego - takze \n, ktory parser
            // Defow gry zamienia z doslownego "\n" w XML na nowa linie - lamalyby wiersz danych.
            for (int i = 0; i < v.id.Length; i++)
            {
                char c = v.id[i];
                bool dozwolony = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                                 || c == '_' || c == '-' || c == '.';
                if (!dozwolony)
                {
                    return "id ze znakiem spoza [A-Za-z0-9_.-] (psuje slad warianty= w danych)";
                }
            }
            if (zajeteId != null && zajeteId.Contains(v.id))
            {
                return "zdublowane id";
            }
            if (string.IsNullOrEmpty(v.text) || v.text.Trim().Length == 0)
            {
                return "pusta tresc";
            }
            bool maZnacznik = v.text.IndexOf(FactionPlaceholder, StringComparison.Ordinal) >= 0;
            if (maZnacznik && !v.requiresFaction)
            {
                return "znacznik " + FactionPlaceholder + " bez requiresFaction (gracz zobaczylby surowy znacznik)";
            }
            if (v.requiresFaction && !maZnacznik)
            {
                return "requiresFaction bez znacznika " + FactionPlaceholder;
            }
            string bezZnacznika = v.text.Replace(FactionPlaceholder, string.Empty);
            if (bezZnacznika.IndexOf('{') >= 0 || bezZnacznika.IndexOf('}') >= 0)
            {
                return "nieznany nawias klamrowy (dozwolony tylko " + FactionPlaceholder + ")";
            }
            if (v.minIntensity > v.maxIntensity)
            {
                return "minIntensity > maxIntensity";
            }
            if (v.conditions != null)
            {
                for (int i = 0; i < v.conditions.Count; i++)
                {
                    NarrativeCondition c = v.conditions[i];
                    if (c == null)
                    {
                        return "pusty warunek";
                    }
                    if (!JestTwardy(c))
                    {
                        return "warunek " + c.GetType().Name + " nie jest twardy (nie nadpisuje IsMet) - tekst stwierdzalby niesprawdzony fakt";
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Warunek twardy = klasa NADPISUJE (override) IsMet - miekkie nadpisuja tylko Fit. Przez definicje bazowa
        /// (przeglad S10): metoda "public new bool IsMet" ma DeclaringType klasy pochodnej, ale wywolanie wirtualne
        /// przez NarrativeCondition trafia w bazowe "return true" - samo DeclaringType dawalo sie tak oszukac.
        /// Nadpisania zwracajacego zawsze true refleksja nie wykryje; takie klasy pisze tylko autor w C#.
        /// </summary>
        public static bool JestTwardy(NarrativeCondition c)
        {
            if (c == null)
            {
                return false;
            }
            MethodInfo m = c.GetType().GetMethod("IsMet", new[] { typeof(WorldSnapshot) });
            return m != null && m.DeclaringType != typeof(NarrativeCondition)
                   && m.GetBaseDefinition().DeclaringType == typeof(NarrativeCondition);
        }
    }
}
