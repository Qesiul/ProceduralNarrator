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
        /// Dzikie zwierzeta, ktore NAPRAWDE moga oszalec pojedynczo - czyli spelniajace pelny
        /// predykat waniliowego IncidentWorker_AnimalInsanitySingle, a nie samo "jest dzikie".
        ///
        /// Osobne pole, a nie prog na WildAnimalCount, bo to INNY ZBIOR, nie inna liczebnosc.
        /// Worker odrzuca zwierzeta mutanckie, zbyt silne (combatPower powyzej 150, a przed
        /// siodmym dniem od zalozenia powyzej 40), stojace w mgle wojny, powalone oraz te,
        /// ktore juz sa w agresywnym stanie psychicznym. WildAnimalCount nie zna zadnego
        /// z tych warunkow, wiec jest NADZBIOREM - a warunek twardy zbudowany na nadzbiorze
        /// przepuszcza kandydatow, ktorych CanFireNow i tak odrzuci. To dokladnie ten wzorzec
        /// straty, ktory projekt zamykal juz przy Cond_MountainRoof i Cond_MinThreatPoints.
        /// </summary>
        public int MaddenableAnimalCount;

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
        /// Ilu kolonistow obecnych na mapie lezy powalonych OSTRO - czyli powalonych I wymagajacych
        /// pomocy teraz (szok bolowy, krwawienie albo nieopatrzone rany lub choroby).
        ///
        /// Wchodzi do krzywej napiecia (czlon sytuacyjny) i do predykatu kryzysu skrajnego, nie do
        /// zadnego warunku klocka. Powod doboru: to najbardziej bezposredni sygnal "kolonii dzieje
        /// sie zle TERAZ", ten sam, na ktory reaguje waniliowa adaptacja - ktora tez liczy
        /// wylacznie powalenia z przemocy (StoryWatcher_Adaptation: "violently downed").
        ///
        /// DLACZEGO "OSTRO", A NIE SAMO Pawn.Downed - i dlaczego pole zmienilo nazwe razem ze
        /// znaczeniem. Poprzednie DownedColonistCount liczylo kazdy stan Downed, a ten obejmuje
        /// stany TRWALE, ktore z kryzysem nie maja nic wspolnego: kazde niemowle (HumanlikeBaby ma
        /// alwaysDowned), brak obu nog, abazje, sen smierci sanguofaga, spiaczke, katatonie.
        /// Zmierzone w przegladzie: 2 kolonistow i jeden taki pionek dawaly profilowi
        /// powsciagliwemu TRWALE Breathe; kolonia z czworgiem niemowlat miala napiecie sytuacyjne
        /// 1.0 NA STALE. Snapshot podaje fakt "ilu lezy i potrzebuje pomocy", a nie "ilu lezy".
        ///
        /// Liczba, a nie ulamek: normalizacja nalezy do krzywej i do predykatu kryzysu. Mianownik
        /// jest w ColonistsOnMap - Z TEJ SAMEJ listy pionkow.
        /// </summary>
        public int AcuteDownedCount;

        /// <summary>
        /// Ilu kolonistow jest OBECNYCH na mapie (bez niemowlat) - mianownik dla AcuteDownedCount.
        ///
        /// Osobne pole, a nie ColonistCount, bo to INNY ZBIOR: ColonistCount (FreeColonistsCount)
        /// liczy takze pionki trzymane niespawnowane - w kriokomorach, noszone, w transporterach -
        /// i niemowleta. Licznik powalonych liczy wylacznie obecnych, wiec ulamek z ColonistCount
        /// bylby zanizony dokladnie wtedy, gdy czesc kolonii jest poza gra. ColonistCount zostaje
        /// bez zmian dla preferencji klockow - jego zmiana ruszylaby contextFit calego katalogu.
        /// </summary>
        public int ColonistsOnMap;

        /// <summary>
        /// Biezacy poziom zagrozenia na mapie (odwzorowanie waniliowego StoryDanger).
        ///
        /// Uzupelnia historie o wymiar, ktorego ona z definicji nie ma: historia wie, jakie
        /// zdarzenie narrator wyslal, ale nie wie, czy jego skutki juz minely. Uzasadnienie
        /// braku podwojnego liczenia - w komentarzu przy DangerLevel.
        /// </summary>
        public DangerLevel Danger;

        /// <summary>
        /// Punkty zagrozenia, ktore gra przyznalaby zdarzeniu W TEJ CHWILI
        /// (odwzorowanie StorytellerUtility.DefaultThreatPointsNow).
        ///
        /// PO CO TO TU JEST. Bazowy IncidentWorker.CanFireNow zawiera bramke
        ///     if (parms.points >= 0f &amp;&amp; parms.points &lt; def.minThreatPoints) return false;
        /// a dwa nasze incydenty maja minThreatPoints = 400: PsychicEmanatorShipPartCrash
        /// i Infestation. Swieza kolonia ma okolo 35-70 punktow, wiec te dwa klocki byly
        /// STRUKTURALNIE niemozliwe do odpalenia, a mimo to wygrywaly rundy wyboru.
        ///
        /// Zmierzone w grze na czystym przebiegu: 44 z 56 odmow silnika pochodzilo z samego
        /// emanatora, czyli jedna akcja zjadala polowe calej pracy petli wyboru. Mechanizm
        /// byl samonapedzajacy: akcja odrzucona nie trafia do historii, wiec jej swiezosc
        /// zostaje na maksimum, wiec wraca na czolo rankingu, wiec znowu jest odrzucana.
        ///
        /// To jest ten sam wzorzec co Cond_MountainRoof: warunek twardy ma odwzorowywac to,
        /// czego NAPRAWDE wymaga IncidentWorker, inaczej kompozycja produkuje kandydatow
        /// skazanych na odrzucenie.
        ///
        /// Wartosc jest porownywalna co do liczby z ta, ktorej uzyje CanFireNow w tej samej
        /// turze: Map.IncidentPointsRandomFactorRange to FloatRange.One (czynnik losowy = 1),
        /// a podloga GlobalPointsMin jest zaseedowana tickiem/2500, wiec w obrebie jednego
        /// interwalu narratora (1000 tickow) nie zmienia sie.
        /// </summary>
        public float ThreatPoints;

        /// <summary>
        /// PAMIEC NARRATORA W POSTACI KANONICZNEJ - trzy pola ponizej sa jedynym kanalem, ktorym
        /// blackboard (watki, fakty, wiek tematow) dociera do warunkow. Wzorzec jest ten sam co przy
        /// DaysSinceLastEvent: warunek widzi WYLACZNIE snapshot, wiec wielkosc z pamieci musi trafic
        /// wlasnie tutaj, i to zamrozona na cala ture.
        ///
        /// DLACZEGO TEKST, A NIE KOLEKCJA. Snapshot jest plaski i kopiowalny pole po polu (pilnuje
        /// tego straznik roznicowy walidatora, ktory na nieobslugiwanym typie pola RZUCA, oraz plytka
        /// Kopia(), ktora przy polu referencyjnym dzielilaby stan miedzy scenariuszami testow).
        /// Tekst jest niemutowalny, wiec plytka kopia jest dla niego poprawna, a postac kanoniczna
        /// (posortowana, z separatorami takze na brzegach) daje warunkom jednoznaczne dopasowanie
        /// jednym IndexOf, bez alokacji. Buduje je NarratorBlackboard, czyta - klasy Cond_*.
        /// </summary>
        public string Threads = string.Empty;

        /// <summary>Fakty o kolonii: ";klucz=wartosc@wiekWdniach;", juz bez wygaslych (WIEK, nie dzien ustawienia).</summary>
        public string Facts = string.Empty;

        /// <summary>Wiek tematow w TURACH: ";Temat=N;", gdzie -1 znaczy "poza horyzontem pamieci".</summary>
        public string TurnsSinceThemes = string.Empty;

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
                   + " zdolnychDoAmoku=" + MaddenableAnimalCount
                   + " odOstatniego=" + DaysSinceLastEvent.ToString("0.00", CultureInfo.InvariantCulture)
                   + " porwanych=" + KidnappedColonistCount
                   + " konsola=" + HasPoweredCommsConsole
                   + " powalonych=" + AcuteDownedCount + "/" + ColonistsOnMap
                   + " zagrozenie=" + Danger
                   + " punkty=" + ThreatPoints.ToString("0", CultureInfo.InvariantCulture)
                   + " watki=" + (string.IsNullOrEmpty(Threads) ? "-" : Threads)
                   + " fakty=" + (string.IsNullOrEmpty(Facts) ? "-" : Facts);
        }
    }
}
