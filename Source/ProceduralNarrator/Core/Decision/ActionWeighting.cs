using System.Collections.Generic;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Jedna AKCJA reprezentowana w pasmie near-best razem ze wszystkimi swoimi wariantami.
    /// </summary>
    public sealed class ActionGroup
    {
        /// <summary>Identyfikator klocka akcji - klucz grupowania.</summary>
        public string ActionId;

        /// <summary>
        /// Warianty tej akcji, ktore przeszly weto, prog jakosci i pasmo near-best.
        /// Kolejnosc jest odziedziczona po posortowanej puli, wiec indeks 0 to wariant najlepszy.
        /// </summary>
        public List<ScoredCandidate> Variants = new List<ScoredCandidate>();

        /// <summary>
        /// Ocena AKCJI = MAKSIMUM uzytecznosci jej wariantow W PASMIE.
        ///
        /// Wybor maksimum, a nie sredniej ani sumy, jest rozstrzygniety i ma uzasadnienie:
        /// suma przywrocilaby dokladnie te premie licznosci, ktora ten podzial usuwa, a srednia
        /// KARALABY akcje za posiadanie slabszych wariantow - czyli za bogactwo katalogu, ktore
        /// jest celem projektu. Maksimum mowi "tak dobra moze byc ta akcja tutaj", a etap drugi
        /// decyduje, w ktorej oprawie.
        /// </summary>
        public float Score;

        /// <summary>p(ta akcja | brama wybrala dzialanie). Wypelnia SelectionPolicy.</summary>
        public double Probability;
    }

    /// <summary>
    /// Grupowanie puli pasma po klocku AKCJI - podstawa dwustopniowego wyboru
    /// "ktora akcja" -> "w ktorej oprawie".
    ///
    /// PROBLEM, KTORY TO ROZWIAZUJE. Plaski softmax po wszystkich kandydatach dodaje kazdej
    /// akcji ukryta premie T*ln(K), gdzie K to liczba jej wariantow w pasmie. Przy T = 0.1
    /// i K = 8 premia wynosi 0.2079, podczas gdy zmierzony rozstep uzytecznosci w czole
    /// rankingu to 0.036-0.039 - premia byla wiec OKOLO PIEC RAZY WIEKSZA niz roznica, ktora
    /// scoring faktycznie mierzy. Liczba opraw narracyjnych jest cecha KATALOGU, a nie
    /// wlasnoscia sytuacji w kolonii, wiec nie ma prawa wchodzic do decyzji jako waga.
    ///
    /// To ta sama rodzina bledu co piaty dlug (PASS dzielony przez licznosc puli) i naprawa
    /// jest ta sama co tam: rozdzielic pytania, ktore dotycza roznych przestrzeni.
    ///     etap A  BRAMA:   czy w ogole dzialac        (cisza kontra najlepsze zdarzenie)
    ///     etap B1 AKCJA:   co zrobic                  (akcja kontra akcja, po MAKSIMUM)
    ///     etap B2 WARIANT: w jakiej oprawie           (wariant kontra wariant W AKCJI)
    /// </summary>
    public static class ActionWeighting
    {
        /// <summary>
        /// Grupuje pule pasma po ActionBlockId.
        ///
        /// KONTRAKT WEJSCIA: lista MUSI byc posortowana malejaco po Utility (tak jak wychodzi
        /// z C1-C4 polityki). Dzieki temu kolejnosc PIERWSZYCH WYSTAPIEN akcji jest juz
        /// kolejnoscia malejacych ocen grup i nie trzeba sortowac drugi raz - a brak drugiego
        /// sortowania to o jedno miejsce mniej, w ktorym determinizm moglby sie zepsuc.
        ///
        /// Kandydat bez identyfikatora akcji dostaje GRUPE WLASNA po SortKey. Taki katalog jest
        /// bledem okablowania, ale degradacja idzie wtedy do zachowania jednostopniowego
        /// (kazdy wariant sam w swojej grupie), a nie do zlania wszystkiego w jedna grupe -
        /// czyli do wersji ZNANEJ, a nie do nowej, nieprzewidzianej.
        /// </summary>
        public static List<ActionGroup> Group(List<ScoredCandidate> pasmo)
        {
            var grupy = new List<ActionGroup>();
            if (pasmo == null || pasmo.Count == 0)
            {
                return grupy;
            }

            var indeks = new Dictionary<string, ActionGroup>(pasmo.Count);
            for (int i = 0; i < pasmo.Count; i++)
            {
                ScoredCandidate k = pasmo[i];
                if (k == null)
                {
                    continue;
                }

                string klucz = k.Event == null ? null : k.Event.ActionBlockId;
                if (string.IsNullOrEmpty(klucz))
                {
                    klucz = "?" + (k.SortKey ?? i.ToString());
                }

                ActionGroup g;
                // Ocena = SelectionScore (Utility + premia lukowa z etapu C6; bez luku rowna Utility).
                // Pula jest posortowana po Utility, wiec od kroku 5 maksimum NIE musi siedziec
                // w pierwszym wariancie - pasujacy do luku wariant slabszy bazowo moze miec wyzsza
                // ocene koncowa. Galaz "else if" ponizej przestala byc nieosiagalna.
                if (!indeks.TryGetValue(klucz, out g))
                {
                    g = new ActionGroup { ActionId = klucz, Score = k.SelectionScore };
                    indeks.Add(klucz, g);
                    grupy.Add(g);
                }
                else if (k.SelectionScore > g.Score)
                {
                    g.Score = k.SelectionScore;
                }

                g.Variants.Add(k);
            }

            return grupy;
        }
    }
}
