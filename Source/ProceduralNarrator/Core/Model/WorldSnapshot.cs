namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Zrzut stanu swiata w chwili podejmowania decyzji.
    ///
    /// Dlaczego zrzut, a nie zapytania do gry na zadanie:
    ///  - determinizm - stan jest ZAMROZONY, wiec ta sama decyzja da sie odtworzyc,
    ///  - log - caly kontekst decyzji da sie zapisac jednym obiektem (wymog ewaluacji),
    ///  - testowalnosc - test w Core sklada snapshot recznie, bez uruchamiania gry.
    ///
    /// Buduje go WYLACZNIE warstwa integracji (WorldSnapshotBuilder). Core go tylko czyta.
    /// </summary>
    public class WorldSnapshot
    {
        /// <summary>Dni od rozpoczecia rozgrywki.</summary>
        public int DaysPassed;

        /// <summary>Liczba wolnych kolonistow.</summary>
        public int ColonistCount;

        /// <summary>
        /// Bogactwo kolonii wg miary, ktorej uzywa sam narrator gry
        /// (Map.PlayerWealthForStoryteller - budynki licza sie w POLOWIE).
        /// Trzymane surowo wylacznie do logu i pozniejszej analizy w Pythonie;
        /// warunki powinny siegac po WealthRelative.
        /// </summary>
        public float ColonyWealth;

        /// <summary>
        /// Bogactwo jako KROTNOSC normy oczekiwanej dla biezacego dnia gry.
        /// 1.0 = kolonia dokladnie tak zamozna, jak gra sie spodziewa; 2.0 = dwa razy bogatsza.
        ///
        /// Prog absolutny nie ma sensu, bo scenariusz startowy potrafi zmienic bogactwo
        /// dnia zerowego dwukrotnie (zmierzone: Crashlanded 13673 vs Naked Brutality 6407),
        /// a po roku gry obie kolonie beda warte setki tysiecy. Miara wzgledna jest
        /// odporna na scenariusz i sama skaluje sie z wiekiem kolonii.
        /// Norme wyznacza WealthReference w warstwie integracji.
        /// </summary>
        public float WealthRelative = 1f;

        /// <summary>
        /// Liczba komorek z grubym (gorskim) stropem w poblizu kolonii.
        ///
        /// Celowo NIE jest to "czy gdziekolwiek na mapie jest gora" - prawie kazda mapa ma
        /// gore w rogu, wiec taki warunek przepuszczal infestacje w koloniach w szczerym polu
        /// i marnowal proby kompozycji na incydent, ktory i tak odrzucal CanFireNow.
        /// Liczymy w promieniu wokol srodka obszaru domowego, bo tam waniliowy
        /// InfestationCellFinder faktycznie szuka miejsca.
        /// </summary>
        public int MountainRoofCellsNearColony;

        /// <summary>Czy istnieje niepokonana frakcja wroga graczowi.</summary>
        public bool HasHostileFaction;

        /// <summary>Pora roku: 0=wiosna, 1=lato, 2=jesien, 3=zima (0 gdy brak por roku).</summary>
        public int Season;

        /// <summary>Czy jest noc (poza godzinami 6-18).</summary>
        public bool IsNight;

        /// <summary>Liczba dzikich zwierzat na mapie.</summary>
        public int WildAnimalCount;

        public override string ToString()
        {
            return "dzien=" + DaysPassed
                   + " kolonistow=" + ColonistCount
                   + " bogactwo=" + ColonyWealth.ToString("0")
                   + " wzgl=" + WealthRelative.ToString("0.00")
                   + " gorstrop=" + MountainRoofCellsNearColony
                   + " wrog=" + HasHostileFaction
                   + " pora=" + Season
                   + " noc=" + IsNight
                   + " zwierzat=" + WildAnimalCount;
        }
    }
}
