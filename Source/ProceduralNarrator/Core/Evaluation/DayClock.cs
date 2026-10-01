namespace ProceduralNarrator.Core.Evaluation
{
    /// <summary>
    /// Granica doby dla linii dobowych ([PN-DZIEN], [PN-PERF]; krok 9, etap L). Doba = tick / 60000. Pierwsze
    /// wywolanie (nowa gra, wczytanie, reset) tylko ustawia dobe biezaca. Zmiana doby zwraca dobe WLASNIE
    /// ZAKONCZONA, czyli (doba biezaca - 1); przy skoku zegara o kilka dob pominiete doby nie maja linii.
    /// Cofniecie zegara ustawia dobe od nowa bez linii.
    /// </summary>
    public sealed class DayClock
    {
        public const int TicksPerDay = 60000;

        private int doba = int.MinValue;

        /// <summary>Doba zamknieta w tym ticku albo -1.</summary>
        public int Advance(int tick)
        {
            if (tick < 0)
            {
                return -1;
            }
            int d = tick / TicksPerDay;
            if (doba == int.MinValue || d <= doba)
            {
                doba = d;
                return -1;
            }
            doba = d;
            return d - 1;
        }

        public void Reset()
        {
            doba = int.MinValue;
        }
    }
}
