namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Poziom BIEZACEGO zagrozenia na mapie - odwzorowanie waniliowego RimWorld.StoryDanger.
    ///
    /// Dlaczego wlasny enum, skoro gra ma swoj: rdzen NIE MOZE znac API gry (twarda regula
    /// z sekcji 9 CLAUDE.md). Warstwa integracji przemapowuje StoryDanger na ten typ przy
    /// budowie snapshotu; wartosci sa celowo w tej samej kolejnosci, wiec mapowanie jest
    /// jednoznaczne i nie wymaga tablicy.
    ///
    /// DLACZEGO CZYTANIE DANGER NIE JEST PODWOJNYM LICZENIEM. Waniliowe punkty zagrozenia
    /// (StorytellerUtility.DefaultThreatPointsNow) NIE uwzgledniaja DangerRating - sprawdzone
    /// dekompilacja calego assembly. Wanilia uzywa go wylacznie do bramki minDanger przy
    /// RaidFriendly oraz do rzeczy nienarracyjnych (muzyka, auto-przyspieszenie czasu, blokada
    /// imprez). Jest to wiec sygnal REAKTYWNY o stanie mapy, nie mnoznik trudnosci - i dlatego
    /// wolno go wziac do wlasnej miary napiecia bez lamania reguly "nie liczyc bogactwa dwa razy".
    ///
    /// Co wnosi, czego nie ma historia: historia narratora mowi, CO ZROBIL, ale nie wie,
    /// czy skutki juz minely. Napad sprzed dwoch dni to dla historii jeden wpis niezaleznie
    /// od tego, czy najezdzcy leza martwi, czy wlasnie wywazaja drzwi. Danger odpowiada
    /// dokladnie na pytanie "czy to jeszcze trwa".
    /// </summary>
    public enum DangerLevel
    {
        /// <summary>Brak aktywnych wrogow na mapie.</summary>
        None = 0,

        /// <summary>Wrogowie sa, ale sila niewielka wobec liczebnosci kolonii.</summary>
        Low = 1,

        /// <summary>Powazny szturm albo kolonista ranny w ostatnich chwilach.</summary>
        High = 2
    }
}
