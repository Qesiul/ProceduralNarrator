namespace ProceduralNarrator.Core.PlayerModel
{
    /// <summary>
    /// Surowe liczniki JEDNEGO dnia gry: para licznik/mianownik na kazdy pomiar (StyleSignal).
    /// Tylko liczby calkowite - trafiaja do zapisu gry (linie D i A ksiegi stylu), a iloraz liczy
    /// dopiero ewaluator, wiec zapis nie traci precyzji na zaokragleniach.
    /// </summary>
    public class StyleDaySample
    {
        /// <summary>Numer dnia gry (tick / 60000), nie indeks w kolejce.</summary>
        public int Day;

        public readonly long[] Num = new long[StyleSignals.Count];
        public readonly long[] Den = new long[StyleSignals.Count];

        public StyleDaySample()
        {
        }

        public StyleDaySample(int day)
        {
            Day = day;
        }

        public void Add(StyleSignal s, long num, long den)
        {
            Num[(int)s] += num;
            Den[(int)s] += den;
        }

        public void Set(StyleSignal s, long num, long den)
        {
            Num[(int)s] = num;
            Den[(int)s] = den;
        }

        public StyleDaySample Clone()
        {
            var c = new StyleDaySample(Day);
            for (int i = 0; i < StyleSignals.Count; i++)
            {
                c.Num[i] = Num[i];
                c.Den[i] = Den[i];
            }
            return c;
        }

        public bool SameAs(StyleDaySample o)
        {
            if (o == null || o.Day != Day)
            {
                return false;
            }
            for (int i = 0; i < StyleSignals.Count; i++)
            {
                if (o.Num[i] != Num[i] || o.Den[i] != Den[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
