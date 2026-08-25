using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Integration
{
    /// <summary>
    /// JEDYNE miejsce, w ktorym narracyjna intencja co do sily zdarzenia zamienia sie
    /// w mechanike gry. Klocek deklaruje poziom, nie mnoznik - dzieki temu kalibracja
    /// zyje tutaj i da sie ja skonfrontowac z waniliowymi krzywymi.
    ///
    /// Punkt odniesienia: waniliowy pointsFactorFromAdaptDays z BaseStoryteller siega
    /// od 0.40 (po stracie kolonisty) do 2.00 (po czterech latach gry). Nasz zakres
    /// 0.70-1.35 jest CELOWO wezszy: intensywnosc klockow ma modulowac zdarzenie,
    /// a nie przejmowac sterowanie trudnoscia od waniliowej krzywej adaptacji.
    /// Wlasciwe sterowanie tempem przyjdzie w kroku 4 wraz z krzywa dramaturgiczna.
    /// </summary>
    public static class IntensityTable
    {
        public static float PointsFactor(IntensityLevel level)
        {
            switch (level)
            {
                case IntensityLevel.VeryLow: return 0.70f;
                case IntensityLevel.Low: return 0.85f;
                case IntensityLevel.Normal: return 1.00f;
                case IntensityLevel.High: return 1.18f;
                case IntensityLevel.VeryHigh: return 1.35f;
                default: return 1.00f;
            }
        }
    }
}
