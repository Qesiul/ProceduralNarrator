using System.Globalization;

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

        /// <summary>
        /// Dni gry od OSTATNIEGO WYDARZENIA narratora (decyzje PASS sie nie licza - cisza nie
        /// przerywa spokoju). Przy pustej historii: wiek kolonii, bo spokoj trwa od zalozenia.
        ///
        /// Pole istnieje po to, by klocek stwierdzajacy fakt "po dniach spokoju" mogl ten fakt
        /// SPRAWDZIC. Warunki widza wylacznie WorldSnapshot, wiec wielkosc pochodzaca z historii
        /// musi trafic tutaj - inaczej tekst obiecywalby stan, ktorego nikt nie weryfikuje.
        /// </summary>
        public float DaysSinceLastEvent;

        /// <summary>
        /// Liczba kolonistow przetrzymywanych przez wrogie frakcje (porwanych).
        ///
        /// PO CO: klocek PN_Akcja_Okup stwierdza w tekscie, ze ktos ZOSTAL PORWANY - a to jest
        /// sprawdzalny fakt o stanie swiata, wiec wedlug reguly projektu musi go pilnowac warunek
        /// TWARDY, nie preferencja. Bez tego pola warunku nie dalo sie napisac i narrator mogl
        /// zazadac okupu za nikogo.
        ///
        /// Odwzorowuje RimWorld.IncidentWorker_RansomDemand.RandomKidnappedColonist(): liczy
        /// pionki HUMANOIDALNE nalezace do frakcji gracza, przetrzymywane przez dowolna frakcje.
        /// Waniliowy worker odejmuje jeszcze tych, dla ktorych list z zadaniem okupu juz wisi
        /// w skrzynce - tego NIE odwzorowujemy, bo to stan UI, a nie swiata; skutek jest taki,
        /// ze warunek bywa nieznacznie luzniejszy od workera i wtedy CanFireNow odrzuci kandydata
        /// w normalnym trybie. Tekst pozostaje prawdziwy, bo ktos porwany faktycznie jest.
        /// </summary>
        public int KidnappedColonistCount;

        /// <summary>
        /// Czy kolonia ma zasilana konsole lacznosci.
        ///
        /// To NIE jest fakt stwierdzany przez zaden fragment tekstu - to wymaganie techniczne
        /// waniliowego IncidentWorker_RansomDemand (CommsConsoleUtility.PlayerHasPoweredCommsConsole).
        /// Trafia do snapshotu z tego samego powodu co Cond_MountainRoof: warunek twardy ma
        /// odwzorowywac to, czego NAPRAWDE wymaga IncidentWorker, inaczej kompozycja produkuje
        /// kandydatow skazanych na odrzucenie i marnuje rundy petli wyboru.
        /// </summary>
        public bool HasPoweredCommsConsole;

        /// <summary>
        /// Ilu kolonistow jest POWALONYCH (Downed) w tej chwili.
        ///
        /// Wchodzi WYLACZNIE do krzywej napiecia (krok 4), nie do zadnego warunku klocka.
        /// Powod doboru: to najbardziej bezposredni sygnal "kolonii dzieje sie zle TERAZ",
        /// i jest to ten sam sygnal, na ktory reaguje waniliowa adaptacja
        /// (StoryWatcher_Adaptation.Notify_PawnEvent z AdaptationEvent.Downed).
        ///
        /// Liczba, a nie ulamek: normalizacja nalezy do krzywej, bo to ona zna prog,
        /// przy ktorym kolonia jest "w opalach". Snapshot podaje FAKT, nie interpretacje.
        /// </summary>
        public int DownedColonistCount;

        /// <summary>
        /// Biezacy poziom zagrozenia na mapie (odwzorowanie waniliowego StoryDanger).
        ///
        /// Uzupelnia historie o wymiar, ktorego ona z definicji nie ma: historia wie, jakie
        /// zdarzenie narrator wyslal, ale nie wie, czy jego skutki juz minely. Uzasadnienie
        /// braku podwojnego liczenia - w komentarzu przy DangerLevel.
        /// </summary>
        public DangerLevel Danger;

        public override string ToString()
        {
            return "dzien=" + DaysPassed
                   + " kolonistow=" + ColonistCount
                   + " bogactwo=" + ColonyWealth.ToString("0", CultureInfo.InvariantCulture)
                   + " wzgl=" + WealthRelative.ToString("0.00", CultureInfo.InvariantCulture)
                   + " gorstrop=" + MountainRoofCellsNearColony
                   + " wrog=" + HasHostileFaction
                   + " pora=" + Season
                   + " noc=" + IsNight
                   + " zwierzat=" + WildAnimalCount
                   + " odOstatniego=" + DaysSinceLastEvent.ToString("0.00", CultureInfo.InvariantCulture)
                   + " porwanych=" + KidnappedColonistCount
                   + " konsola=" + HasPoweredCommsConsole
                   + " powalonych=" + DownedColonistCount
                   + " zagrozenie=" + Danger;
        }
    }
}
