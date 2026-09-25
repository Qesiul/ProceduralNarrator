using System;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.PlayerModel
{
    /// <summary>
    /// EWALUATOR STYLU (krok 7): kolejka dni -&gt; pomiary -&gt; cechy na wspolnej skali -&gt; profil
    /// wzgledny -&gt; mocne strony i etykieta archetypu. Czysta funkcja: ta sama ksiega i te same
    /// parametry daja zawsze ten sam odczyt (wymaganie determinizmu z sekcji 11).
    ///
    /// Decyzje autora, ktore tu siedza:
    ///  - nr 8/10: kazdy dzien kolejki ma rowna wage (pomiary stanu) - bez zaniku;
    ///  - nr 9: rozgrzewka - przed nia styl jest nieaktywny (cechy nieznane);
    ///  - nr 7: wspolna skala z norma (przecietna = 0.5); wybor zdarzen wedlug profilu WZGLEDNEGO,
    ///    etykieta wedlug wartosci BEZWZGLEDNYCH;
    ///  - nr 22: etykieta = najblizszy prototyp po znanych cechach, remis = kolejnosc w XML.
    /// </summary>
    public static class PlayerStyleModel
    {
        public static StyleReading Evaluate(PlayerStyleLedger ledger, PlayerStyleParams p)
        {
            if (p == null)
            {
                p = PlayerStyleParams.Default();
            }
            var r = new StyleReading();
            int dni = ledger == null ? 0 : ledger.Days.Count;
            r.Days = dni;
            r.Active = dni >= p.warmupDays;

            for (int i = 0; i < StyleSignals.Count; i++)
            {
                var s = (StyleSignal)i;
                StyleSignalParams sp = p.Signal(s);
                double x;
                bool known;
                long sumaMian;
                Aggregate(ledger, s, sp, out x, out known, out sumaMian);
                r.SignalX[i] = known ? (float)x : float.NaN;
                r.SignalKnown[i] = known;
                r.SignalZ[i] = known ? MapToScale((float)x, sp) : float.NaN;
                if (s == StyleSignal.Poborowi) r.EpisodesInWindow = sumaMian;
                else if (s == StyleSignal.Przyjecia) r.OffersInWindow = sumaMian;
                else if (s == StyleSignal.Werbunek) r.CapturesInWindow = sumaMian;
            }

            for (int d = 0; d < StyleDimensions.Count; d++)
            {
                double sumaW = 0.0, sumaWZ = 0.0;
                for (int i = 0; i < StyleSignals.Count; i++)
                {
                    var s = (StyleSignal)i;
                    if ((int)StyleSignals.Dimension(s) != d || !r.SignalKnown[i])
                    {
                        continue;
                    }
                    float w = p.Signal(s).weight;
                    if (w <= 0f)
                    {
                        continue;
                    }
                    sumaW += w;
                    sumaWZ += w * r.SignalZ[i];
                }
                bool maPomiar = sumaW > 0.0;
                r.Z[d] = maPomiar ? (float)(sumaWZ / sumaW) : float.NaN;
                r.Known[d] = r.Active && maPomiar;
            }

            Finish(r, p);
            return r;
        }

        /// <summary>
        /// Odczyt z NARZUCONEGO wektora cech (ramiona syntetycznych graczy w symulatorze i testy).
        /// Te same reguly profilu wzglednego, mocnych stron i etykiety co Evaluate.
        /// </summary>
        public static StyleReading FromVector(float[] z, bool[] known, int days, PlayerStyleParams p)
        {
            if (p == null)
            {
                p = PlayerStyleParams.Default();
            }
            var r = new StyleReading { Days = days, Imposed = true };
            r.Active = days >= p.warmupDays;
            for (int i = 0; i < StyleSignals.Count; i++)
            {
                r.SignalX[i] = float.NaN;
                r.SignalZ[i] = float.NaN;
            }
            for (int d = 0; d < StyleDimensions.Count; d++)
            {
                bool k = known == null || (d < known.Length && known[d]);
                float v = z != null && d < z.Length ? z[d] : float.NaN;
                r.Z[d] = v;
                r.Known[d] = r.Active && k && !float.IsNaN(v);
            }
            Finish(r, p);
            return r;
        }

        /// <summary>
        /// Mapowanie na wspolna skale: z = clamp01(0.5 + 0.5*(x - norma)/rozpietosc). Przecietna kolonia
        /// (x = norma) daje 0.5; norma +/- rozpietosc daje 1 / 0. Postac z rozpietoscia, a nie
        /// "norma -&gt; 0.5, 0 -&gt; 0": pomiar o typowej wartosci 0 (ataki na osady) ciagnalby wtedy kazdego
        /// gracza w dol.
        /// </summary>
        public static float MapToScale(float x, StyleSignalParams sp)
        {
            float spread = sp.spread > 0f ? sp.spread : 1e-6f;
            return Curves.Clamp01(0.5f + 0.5f * (x - sp.norm) / spread);
        }

        private static void Aggregate(PlayerStyleLedger ledger, StyleSignal s, StyleSignalParams sp,
                                      out double x, out bool known, out long sumaMian)
        {
            x = 0.0;
            known = false;
            sumaMian = 0;
            if (ledger == null)
            {
                return;
            }
            int i = (int)s;
            if (StyleSignals.Mode(s) == StyleAggregation.DailyRatioMean)
            {
                // Pomiar STANU: kazdy dzien ma rowna wage; dzien bez mianownika nic nie mowi.
                double suma = 0.0;
                int n = 0;
                foreach (StyleDaySample d in ledger.Days)
                {
                    sumaMian += d.Den[i];
                    if (d.Den[i] > 0)
                    {
                        suma += d.Num[i] / (double)d.Den[i];
                        n++;
                    }
                }
                known = n > 0;
                x = known ? suma / n : 0.0;
                return;
            }

            // Pomiar ZDARZEN: iloraz sum z okna - jednostka jest zdarzenie, nie dzien.
            long sumaLicz = 0;
            foreach (StyleDaySample d in ledger.Days)
            {
                sumaLicz += d.Num[i];
                sumaMian += d.Den[i];
            }
            int min = sp.minEvents < 1 ? 1 : sp.minEvents;
            // minNumerator (przeglad S8): pomiar, ktorego norma to "zero zdarzen" (Inicjatywa), liczy sie dopiero
            // przy zdarzeniu w oknie - inaczej stale z = 0,5 sciskaloby ceche.
            known = sumaMian >= min && sumaLicz >= sp.minNumerator;
            x = known ? sumaLicz / ((double)StyleSignals.Scale(s) * sumaMian) : 0.0;
        }

        private static void Finish(StyleReading r, PlayerStyleParams p)
        {
            // ---- profil wzgledny po ZNANYCH cechach
            int znane = 0;
            double suma = 0.0;
            for (int d = 0; d < StyleDimensions.Count; d++)
            {
                if (r.Known[d])
                {
                    znane++;
                    suma += r.Z[d];
                }
            }
            float srednia = znane > 0 ? (float)(suma / znane) : 0f;
            for (int d = 0; d < StyleDimensions.Count; d++)
            {
                // Przy jednej znanej cesze nie ma "mocnej" ani "slabej" strony - nie ma z czym porownac.
                r.C[d] = r.Known[d] && znane >= 2
                    ? Curves.ClampSigned((r.Z[d] - srednia) / p.relativeScale)
                    : 0f;
                r.Strong[d] = r.Known[d] && znane >= 2 && r.C[d] >= p.strongSideThreshold;
            }

            // ---- etykieta: najblizszy prototyp po znanych cechach (wartosci bezwzgledne)
            r.Label = string.Empty;
            r.SecondLabel = string.Empty;
            r.Margin = 0f;
            if (!r.Active || znane == 0 || p.prototypes == null)
            {
                return;
            }
            double best = double.MaxValue, second = double.MaxValue;
            string bestL = string.Empty, secondL = string.Empty;
            foreach (StylePrototype pr in p.prototypes)
            {
                if (pr == null)
                {
                    continue;
                }
                double dist = 0.0;
                for (int d = 0; d < StyleDimensions.Count; d++)
                {
                    if (!r.Known[d]) continue;
                    double roz = r.Z[d] - pr.Get((StyleDimension)d);
                    dist += roz * roz;
                }
                dist = Math.Sqrt(dist);
                // Scisle "<": przy remisie wygrywa prototyp wczesniejszy w XML (kolejnosc jest danymi).
                if (dist < best)
                {
                    second = best;
                    secondL = bestL;
                    best = dist;
                    bestL = pr.label;
                }
                else if (dist < second)
                {
                    second = dist;
                    secondL = pr.label;
                }
            }
            r.Label = bestL;
            r.SecondLabel = secondL;
            r.Margin = second == double.MaxValue ? 0f : (float)(second - best);
        }
    }
}
