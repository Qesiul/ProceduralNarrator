using System;
using System.Collections.Generic;

namespace ProceduralNarrator.Core.Composition
{
    /// <summary>
    /// Rozdzial RESZTY budzetu ocen miedzy akcje, ktore w pierwszym przebiegu zostaly przyciete.
    ///
    /// Czysta funkcja arytmetyczna: zero stanu, zero losowosci, zero zaleznosci od modelu.
    /// Wydzielona do osobnego typu wylacznie po to, zeby dalo sie ja przetestowac na wartosciach
    /// z palca (bez katalogu klockow i bez snapshotu) - to jest jedyny kawalek warstwy kompozycji,
    /// w ktorym latwo o cichy blad podzialu, a jednoczesnie da sie go w calosci pokryc asercjami.
    /// </summary>
    public static class BudgetSplitter
    {
        /// <summary>
        /// Water-filling z ROWNA dzialka i przelewaniem nadwyzki, w kolejnosci kanonicznej.
        ///
        /// Dlaczego rowno, a nie proporcjonalnie do deficytu: podzial proporcjonalny premiowalby
        /// akcje o najwiekszej przestrzeni wariantow, czyli dokladnie te, ktore juz sa najlepiej
        /// reprezentowane w zbiorze kandydatow. Rowna dzialka wyrownuje glebokosc eksploracji
        /// miedzy tematami, a to roznorodnosc typow jest metryka ewaluacji - nie liczebnosc.
        ///
        /// To, czego akcja nie wchlonie (bo jej deficyt jest mniejszy od dzialki), wraca do puli
        /// i jest dzielone ponownie, wiec nic sie nie marnuje dopoki ktokolwiek ma deficyt.
        ///
        /// Dowod stopu: w kazdym obrocie petli zewnetrznej grantBase &gt;= 1 i lista C jest niepusta,
        /// wiec pierwszy jej element dostaje co najmniej 1 i rem maleje SCISLE. Petla konczy sie
        /// najpozniej po `remaining` obrotach (praktycznie po 2-3).
        ///
        /// Niezmienniki (asercje testu jednostkowego):
        ///   sum(wynik) == min(remaining, sum(deficits))   oraz   0 &lt;= wynik[i] &lt;= deficits[i].
        /// </summary>
        /// <param name="remaining">Reszta budzetu do rozdania. Wartosci &lt;= 0 daja same zera.</param>
        /// <param name="deficits">Ile jeszcze DA SIE wziac z kazdej akcji (0 dla wyczerpanych).</param>
        public static int[] Distribute(int remaining, int[] deficits)
        {
            if (deficits == null)
            {
                return new int[0];
            }

            int n = deficits.Length;
            var extra = new int[n];
            if (remaining <= 0 || n == 0)
            {
                return extra;
            }

            int rem = remaining;

            // C: indeksy akcji, ktore jeszcze maja niezaspokojony deficyt. Kolejnosc kanoniczna
            // (rosnaco po i), bo to ona rozstrzyga, kto dostaje ostatnie pojedyncze jednostki,
            // gdy rem < |C| - a wiec wspoltworzy determinizm calego zbioru kandydatow.
            var C = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                if (deficits[i] > 0)
                {
                    C.Add(i);
                }
            }

            while (rem > 0 && C.Count > 0)
            {
                // Math.Max(1, ...) obsluguje przypadek rem < |C|: bez niego dzialka wyszlaby 0
                // i petla krecilaby sie w nieskonczonosc, nic nie rozdajac.
                int grantBase = Math.Max(1, rem / C.Count);

                for (int p = 0; p < C.Count; )
                {
                    int i = C[p];
                    int grant = Math.Min(grantBase, Math.Min(deficits[i] - extra[i], rem));
                    extra[i] += grant;
                    rem -= grant;

                    if (extra[i] == deficits[i])
                    {
                        // Akcja nasycona - wypada z podzialu, a p NIE rosnie, bo RemoveAt
                        // przesunelo nastepny element na to samo miejsce.
                        C.RemoveAt(p);
                    }
                    else
                    {
                        p++;
                    }

                    if (rem == 0)
                    {
                        break;
                    }
                }
            }

            return extra;
        }
    }
}
