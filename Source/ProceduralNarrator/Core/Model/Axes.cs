namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Os TEMATU. Wartosci sa rozlaczne - klocek nalezy do dokladnie jednego tematu.
    /// Uwaga na granice Raid/Military: napad na kolonie to Raid, a sprawy zbrojne
    /// bez ataku (odsiecz sojusznika, wojna frakcji) to Military.
    /// </summary>
    public enum Theme
    {
        Raid,          // bezposredni atak na kolonie
        Military,      // sprawy zbrojne bez ataku na kolonie
        Economic,      // zasoby, bogactwo, produkcja
        Trade,         // wymiana z innymi (karawany, handlarz orbitalny)
        Social,        // ludzie dolaczaja, odwiedzaja, relacje
        Natural,       // pogoda, zwierzeta, biom, insekty
        Disease,       // choroby i plagi
        Supernatural   // psychika, anomalie, monolit
    }

    /// <summary>Os WALENCJI - czy zdarzenie jest dla gracza dobre, zle, czy obojetne.</summary>
    public enum Valence
    {
        Negative,
        Neutral,
        Positive
    }

    /// <summary>Os SKALI - jak duze jest zdarzenie w skali rozgrywki (NIE jego trudnosc).</summary>
    public enum EventScale
    {
        Minor,
        Moderate,
        Major
    }

    /// <summary>
    /// Intencja narracyjna co do sily zdarzenia. Klocek deklaruje POZIOM, a nie mnoznik -
    /// przelozenie poziomu na punkty incydentu zyje w jednym miejscu w warstwie integracji
    /// (IntensityTable), zeby dalo sie je skalibrowac wobec waniliowych krzywych.
    ///
    /// Klocek deklaruje Low/Normal/High. Po zlozeniu wydarzenia poziomy sumuja sie
    /// jako odchylenia i wynik moze siegnac VeryLow albo VeryHigh.
    /// </summary>
    public enum IntensityLevel
    {
        VeryLow = -2,
        Low = -1,
        Normal = 0,
        High = 1,
        VeryHigh = 2
    }
}
