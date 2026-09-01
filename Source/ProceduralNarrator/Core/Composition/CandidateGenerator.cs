using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Composition
{
    /// <summary>
    /// Buduje ZBIOR kandydatow na jedna ture narratora - wejscie warstwy decyzyjnej (utility AI).
    ///
    /// Krok 1 i 2 skladaly jedno losowe wydarzenie i pytaly gry, czy da sie je odpalic. To jest
    /// wybor przed ocena, czyli dokladnie odwrotnie niz zaklada koncepcja: utility AI ma dostac
    /// zbior mozliwosci i wybrac najlepsza, a nie ocenic jedyna, ktora los podsunal.
    ///
    /// Zbior nie moze byc jednak "wszystkim, co da sie zlozyc", bo kazdy kandydat kosztuje
    /// zlozenie i ocene kontekstowa, a katalog ma rosnac. Stad BUDZET OCEN B i jego rozdzial:
    ///   przebieg 1 - kazda z m dostepnych akcji dostaje rowna dzialke K = max(1, B/m),
    ///   przebieg 2 - niewykorzystana reszta wraca do akcji, ktorych przestrzeni nie przebadano.
    /// Gwarancja z przebiegu 1 jest wazniejsza niz oszczednosc: KAZDY temat ma reprezentanta
    /// w rankingu, bo roznorodnosc typow jest metryka ewaluacji.
    ///
    /// Na dzisiejszym katalogu (m=12, N=84, B=400) budzet w ogole nie tnie: K=33, kazde N_i &lt;= 16,
    /// wiec przebieg 1 wyczerpuje przestrzen, przebieg 2 sie nie odbywa, a caly zbior powstaje
    /// bez ANI JEDNEGO siegniecia po losowosc. To najlepszy mozliwy punkt wyjscia do ewaluacji:
    /// ranking jest odtwarzalny nawet przy zmianie ziarna.
    /// </summary>
    public class CandidateGenerator
    {
        /// <summary>
        /// Domyslny budzet ocen na ture. Dobrany tak, zeby dzisiejsza przestrzen (84) miescila sie
        /// w calosci z duzym zapasem, a jednoczesnie zeby przy katalogu rzedu 50 akcji limit na
        /// akcje nie spadl ponizej kilku wariantow.
        ///
        /// Sugestia poza zakresem kroku 3, zapisana zeby nie zaginela: gdy m przekroczy okolo 40,
        /// warto przejsc na B = max(400, 8*m), zeby K nie spadlo ponizej 8. Stala 400 przy
        /// 100 akcjach daje K = 4, co jeszcze dziala (kazda akcja ma reprezentanta), ale
        /// eksploracja wariantow robi sie plytka.
        /// </summary>
        public const int DefaultBudget = 400;

        private readonly EventComposer composer;

        public CandidateGenerator(EventComposer composer)
        {
            this.composer = composer;
        }

        /// <summary>
        /// Generuje zbior kandydatow razem ze sprawozdaniem z rozdzialu budzetu.
        ///
        /// KONTRAKT DETERMINIZMU: wszystkie akcje czerpia z JEDNEGO, wspoldzielonego strumienia
        /// losowosci, wiec kolejnosc ich przetwarzania jest czescia kontraktu - tak samo jak to,
        /// ze przebieg 2 nastepuje po przebiegu 1. Zrownoleglenie, leniwa ewaluacja przez
        /// yield return albo przestawienie petli zabija odtwarzalnosc BEZ bledu kompilacji.
        /// (Rozwiazaniem docelowym byloby wyprowadzanie osobnego ziarna na akcje, ale to nowy
        /// interfejs fabryki i nie nalezy do kroku 3.)
        /// </summary>
        public CandidateSet Generate(EventRecipe recipe, WorldSnapshot snapshot, IRandomSource rng, int budget)
        {
            var set = new CandidateSet();

            if (composer == null)
            {
                // Blad programistyczny, nie stan gry - dlatego Exhausted zostaje false
                // (nie zbadano niczego) i nie udajemy poprawnej decyzji o ciszy.
                set.Trace = "brak kompozytora";
                return set;
            }

            // B1. Akcje dostepne w tym kontekscie, w kolejnosci kanonicznej.
            List<Block> actions = composer.AvailableActions(snapshot, recipe);
            int m = actions.Count;

            int B = budget > 0 ? budget : DefaultBudget;
            set.Budget = B;
            set.ActionCount = m;

            // B2. Zaden klocek akcji nie przechodzi twardych warunkow albo tagu z przepisu.
            // To NIE jest awaria: warstwa decyzyjna dostanie sam pseudo-kandydat PASS i PASS
            // wygra, bo nie ma z czym konkurowac. Poprawna decyzja narratora o milczeniu.
            if (m == 0)
            {
                set.PerActionQuota = 0;
                set.Exhausted = true;
                set.Trace = "brak dostepnych akcji";
                return set;
            }

            // B3. Rowna dzialka. Wymuszenie K >= 1 jest istotne: przy m > B czysta dzielna dalaby
            // K = 0, czyli ZERO kandydatow i trwaly PASS bez zadnego komunikatu - z zewnatrz
            // objaw identyczny jak pusty katalog klockow z kroku 1 ("narrator dziala, tylko nic
            // nie robi"). Lepiej przekroczyc budzet o m - B ocen i to zaraportowac, niz oslepnac.
            int K = B / m;
            if (K < 1)
            {
                K = 1;
            }
            set.PerActionQuota = K;

            // WYBORY, nie gotowi kandydaci. Materializacja (Assemble + Evaluate) nastepuje
            // DOPIERO po ustaleniu koncowych przydzialow - patrz B8b. Dzieki temu akcja uciete
            // w przebiegu 1 i wybrana ponownie w przebiegu 2 placi za oceny RAZ, a nie dwa razy,
            // wiec candidateBudget jest twardym limitem takze dla WYKONANYCH ocen, nie tylko
            // dla zwroconych kandydatow.
            var sel = new List<EventComposer.BuiltVariant>[m];
            var lists = new List<ComposedEvent>[m];
            var stats = new VariantEnumerationStats[m];
            var space = new int[m];

            // Licznik zuzytej losowosci zbierany po WSZYSTKICH wywolaniach enumeracji, takze
            // po tych, ktorych sprawozdanie zostanie za chwile podmienione w przebiegu 2.
            // Suma po PerAction pokazalaby mniej, bo tam zostaje tylko ostatni przebieg danej
            // akcji - a pytanie, na ktore ta liczba odpowiada, brzmi "czy i ile ta decyzja
            // zawdziecza ziarnu", wiec musi obejmowac rowniez proby odrzucone.
            // Niezmiennik testowy: losowan == liczba wywolan IRandomSource.Next w tej turze.
            int totalDraws = 0;

            // B4. PRZEBIEG 1 - kazda akcja dostaje tyle samo.
            int used = 0;
            for (int i = 0; i < m; i++)
            {
                sel[i] = composer.SelectVariants(actions[i], snapshot, K, rng, out stats[i]);
                space[i] = stats[i].VariantsSeen;
                used += sel[i].Count;
                totalDraws += stats[i].RandomDraws;
            }

            // B5. Reszta budzetu. Ujemna tylko przy wymuszonym K = 1 dla m > B.
            int remaining = B - used;
            if (remaining < 0)
            {
                remaining = 0;
            }

            // B6. Deficyt: ile jeszcze DA SIE wziac z danej akcji. Dla akcji wyczerpanej zero,
            // wiec splitter nigdy nie przydzieli jej nic ponad to, co juz ma. Gdy przelot zostal
            // uciety, N_i jest dolnym ograniczeniem, wiec deficyt tez - nadal poprawny jako
            // "da sie wziac jeszcze co najmniej tyle".
            var deficits = new int[m];
            for (int i = 0; i < m; i++)
            {
                deficits[i] = stats[i].Exhausted ? 0 : (space[i] - sel[i].Count);
            }

            // B7-B8. PRZEBIEG 2 - rozdzial reszty i POWTORNA enumeracja z wieksza pojemnoscia.
            int[] extra = BudgetSplitter.Distribute(remaining, deficits);
            for (int i = 0; i < m; i++)
            {
                if (extra[i] <= 0)
                {
                    continue;
                }

                int cap = sel[i].Count + extra[i];

                // PODMIANA, nie doklejenie. Sklejenie proby rozmiaru K z niezalezna proba rozmiaru
                // extra dawaloby duplikaty i rozklad, ktorego nie da sie obronic - a jest to
                // najbardziej kuszacy skrot w calym komponencie, bo oszczedza drugi przelot.
                // Powtorka kosztuje drugie O(N_i) sprawdzen zgodnosci, ale ZERO dodatkowych
                // Assemble/Evaluate ponad limit, wiec budzet OCEN pozostaje szczelny.
                sel[i] = composer.SelectVariants(actions[i], snapshot, cap, rng, out stats[i]);
                space[i] = stats[i].VariantsSeen;
                totalDraws += stats[i].RandomDraws;
            }

            // B8b. MATERIALIZACJA - jedyne miejsce, w ktorym placimy Assemble + Evaluate,
            // i placimy dokladnie tyle razy, ile kandydatow faktycznie zwrocimy.
            for (int i = 0; i < m; i++)
            {
                lists[i] = composer.MaterializeVariants(actions[i], sel[i], snapshot);
            }

            // B9-B10. Sklejenie w kolejnosci kanonicznej akcji plus podsumowanie.
            bool exhausted = true;
            bool truncated = false;
            int totalVariants = 0;

            for (int i = 0; i < m; i++)
            {
                set.Candidates.AddRange(lists[i]);
                set.PerAction.Add(stats[i]);

                totalVariants += space[i];
                exhausted = exhausted && stats[i].Exhausted;
                truncated = truncated || stats[i].Truncated;
            }

            set.TotalVariants = totalVariants;
            set.Exhausted = exhausted;
            set.Truncated = truncated;
            set.BudgetExceeded = set.Candidates.Count > B;
            set.Trace = BuildTrace(set, totalDraws);
            return set;
        }

        /// <summary>
        /// Jedna linia sladu do logu czytelnego. Wiersze per akcja produkuje juz
        /// VariantEnumerationStats.ToString() - warstwa integracji wypisuje je z wcieciem.
        /// Liczby przez InvariantCulture, bo ta sama linia bywa czytana skryptem.
        /// </summary>
        private static string BuildTrace(CandidateSet set, int totalDraws)
        {
            var sb = new StringBuilder();
            sb.Append("budzet=").Append(set.Budget.ToString(CultureInfo.InvariantCulture))
              .Append(" akcji=").Append(set.ActionCount.ToString(CultureInfo.InvariantCulture))
              .Append(" K=").Append(set.PerActionQuota.ToString(CultureInfo.InvariantCulture))
              .Append(" wziete=").Append(set.Candidates.Count.ToString(CultureInfo.InvariantCulture))
              .Append(" przestrzen=").Append(set.TotalVariants.ToString(CultureInfo.InvariantCulture))
              .Append(" wyczerpano=").Append(VariantEnumerationStats.Flag(set.Exhausted))
              .Append(" losowan=").Append(totalDraws.ToString(CultureInfo.InvariantCulture));

            if (set.Truncated)
            {
                sb.Append(" ucieto=true");
            }
            if (set.BudgetExceeded)
            {
                // Dopisywane tylko w przypadku nietypowym (m > B), zeby nie ginelo w linii,
                // ktora poza tym wyglada identycznie w kazdej turze.
                sb.Append(" przekroczonoBudzet=true");
            }
            return sb.ToString();
        }
    }
}
