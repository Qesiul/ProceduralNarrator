using System;
using System.Globalization;
using System.Text;

namespace ProceduralNarrator.Core.Evaluation
{
    /// <summary>
    /// Licznik czasu do linii [PN-PERF] (krok 9, etap L): liczba pomiarow, suma, maksimum i kubelki o stalych
    /// gornych granicach. Kubelek i obejmuje wartosci v z przedzialu [granica(i-1), granica(i)); ostatni -
    /// wszystko od najwyzszej granicy w gore. Jednostke (ms, us) niesie nazwa pola w linii.
    /// </summary>
    public sealed class PerfAccumulator
    {
        private readonly double[] granice;
        private readonly int[] kubelki;

        public PerfAccumulator(params double[] upperBounds)
        {
            granice = upperBounds ?? new double[0];
            for (int i = 0; i < granice.Length; i++)
            {
                if (!(granice[i] > 0) || (i > 0 && !(granice[i] > granice[i - 1])))
                {
                    throw new ArgumentException("granice kubelkow musza byc dodatnie i rosnace");
                }
            }
            kubelki = new int[granice.Length + 1];
        }

        public int Count { get; private set; }

        public double Sum { get; private set; }

        public double Max { get; private set; }

        public int BucketCount
        {
            get { return kubelki.Length; }
        }

        public int Bucket(int i)
        {
            return kubelki[i];
        }

        /// <summary>Indeks kubelka dla wartosci (ujemna i NaN licza sie jako 0).</summary>
        public int BucketIndex(double v)
        {
            double x = double.IsNaN(v) || v < 0 ? 0 : v;
            for (int i = 0; i < granice.Length; i++)
            {
                if (x < granice[i])
                {
                    return i;
                }
            }
            return granice.Length;
        }

        public void Add(double v)
        {
            double x = double.IsNaN(v) || v < 0 ? 0 : v;
            Count++;
            Sum += x;
            if (x > Max)
            {
                Max = x;
            }
            kubelki[BucketIndex(x)]++;
        }

        public void Reset()
        {
            Count = 0;
            Sum = 0;
            Max = 0;
            Array.Clear(kubelki, 0, kubelki.Length);
        }

        /// <summary>
        /// Pola "; p=N; pSumaU=..; pMaxU=.." i przy kubelkach "; pKubelki=a/b/..; pGranice=x/y/..".
        /// </summary>
        public string Fragment(string prefix, string unit, bool withBuckets)
        {
            var sb = new StringBuilder(96);
            sb.Append("; ").Append(prefix).Append('=').Append(Count.ToString(CultureInfo.InvariantCulture))
              .Append("; ").Append(prefix).Append("Suma").Append(unit).Append('=').Append(Num(Sum))
              .Append("; ").Append(prefix).Append("Max").Append(unit).Append('=').Append(Num(Max));
            if (withBuckets)
            {
                sb.Append("; ").Append(prefix).Append("Kubelki=");
                for (int i = 0; i < kubelki.Length; i++)
                {
                    sb.Append(i == 0 ? string.Empty : "/").Append(kubelki[i].ToString(CultureInfo.InvariantCulture));
                }
                sb.Append("; ").Append(prefix).Append("Granice=");
                for (int i = 0; i < granice.Length; i++)
                {
                    sb.Append(i == 0 ? string.Empty : "/").Append(Num(granice[i]));
                }
            }
            return sb.ToString();
        }

        private static string Num(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
