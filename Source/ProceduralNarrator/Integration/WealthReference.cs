namespace ProceduralNarrator.Integration
{
    /// <summary>
    /// Krzywa odniesienia: ile kolonia POWINNA byc warta w danym dniu gry.
    ///
    /// ==========================================================================
    ///  ZRODLO: RimWorld 1.5.4063 rev1071,
    ///          RimWorld.StorytellerUtility.FixedWealthModeMapWealthFromTimeCurve
    ///          (odczytane ze stalych w .cctor Assembly-CSharp.dll)
    ///
    ///          (   0 ,    10 000 )
    ///          ( 180 ,   180 000 )
    ///          ( 720 , 1 000 000 )
    ///          (1800 , 2 500 000 )
    ///
    ///  Wartosci zaszyte CELOWO na sztywno, a nie czytane z gry w czasie dzialania.
    ///  Powod: gdyby Ludeon zmienil krzywa w kolejnym patchu, nasze progi przesunelyby
    ///  sie po cichu i wyniki ewaluacji przestalyby byc porownywalne miedzy wersjami.
    ///  Praca inzynierska potrzebuje odtwarzalnosci bardziej niz automatycznej
    ///  aktualnosci. Przy zmianie wersji gry: sprawdzic krzywa i zaktualizowac swiadomie.
    /// ==========================================================================
    ///
    /// Ta sama krzywa sluzy w vanilli do trybu "fixed wealth", czyli jest to WLASNA
    /// odpowiedz gry na pytanie "ile kolonia powinna byc warta po N dniach".
    /// </summary>
    public static class WealthReference
    {
        private static readonly float[] Days = { 0f, 180f, 720f, 1800f };
        private static readonly float[] Wealth = { 10000f, 180000f, 1000000f, 2500000f };

        /// <summary>Oczekiwane bogactwo kolonii w danym dniu (interpolacja liniowa).</summary>
        public static float ExpectedWealth(float daysPassed)
        {
            if (daysPassed <= Days[0])
            {
                return Wealth[0];
            }

            for (int i = 1; i < Days.Length; i++)
            {
                if (daysPassed <= Days[i])
                {
                    float t = (daysPassed - Days[i - 1]) / (Days[i] - Days[i - 1]);
                    return Wealth[i - 1] + t * (Wealth[i] - Wealth[i - 1]);
                }
            }

            // Poza ostatnim punktem krzywa vanilli jest plaska.
            return Wealth[Wealth.Length - 1];
        }

        /// <summary>
        /// Bogactwo jako krotnosc normy. 1.0 = kolonia dokladnie taka, jakiej gra oczekuje.
        /// </summary>
        public static float Relative(float wealth, float daysPassed)
        {
            float oczekiwane = ExpectedWealth(daysPassed);
            return oczekiwane <= 0f ? 1f : wealth / oczekiwane;
        }
    }
}
