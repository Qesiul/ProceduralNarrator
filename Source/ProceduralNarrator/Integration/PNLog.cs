using System;
using System.Globalization;
using System.IO;
using System.Text;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Tension;
using ProceduralNarrator.Integration.Persistence;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration
{
    /// <summary>
    /// Logowanie decyzji narratora (sekcja 12 koncepcji - dane wejsciowe ewaluacji).
    /// Dwa rozlaczne strumienie, celowo o roznych prefiksach:
    ///
    ///   [PN]       linia CZYTELNA dla czlowieka - slad kompozycji, ranking, powody odrzucen.
    ///              Wolno ja przeformatowac miedzy wersjami, nikt jej nie parsuje.
    ///   [PN-DATA]  linia MASZYNOWA - jeden wiersz na decyzje, staly zestaw i stala KOLEJNOSC
    ///              kolumn, wylacznie InvariantCulture. To jest wejscie skryptow z kroku 8
    ///              i jej format jest kontraktem, a nie wygoda.
    ///
    /// Cala lista kolumn linii maszynowej jest zadeklarowana W JEDNYM MIEJSCU (DataColumns)
    /// i opatrzona numerem wersji. Powod jest praktyczny: kolumny dokladaja cztery rozne
    /// warstwy (kompozycja, scoring, polityka, integracja), a krok 4 dolozy kolejne. Bez
    /// jednej deklaracji kolejnosci skrypt w Pythonie pekalby po cichu - dostalby liczby
    /// pod nazwami, ktorych sie nie spodziewa, i nikt by tego nie zauwazyl w wynikach.
    /// </summary>
    public static class PNLog
    {
        private const string Prefix = "[PN] ";
        private const string DataPrefix = "[PN-DATA] ";
        private const string ColumnsPrefix = "[PN-DATA-COLS] ";
        private const string LoadPrefix = "[PN-LOAD] ";
        private const string ConfigPrefix = "[PN-CONFIG] ";
        private const string ResetPrefix = "[PN-RESET] ";
        private const string WarnDataPrefix = "[PN-WARN] ";
        private const string ErrorDataPrefix = "[PN-ERR] ";

        /// <summary>
        /// Wersja formatu linii maszynowej. PODBIC przy KAZDEJ zmianie zawartosci DataColumns -
        /// dolozeniu, usunieciu albo przestawieniu kolumny. Numer wersji jest pierwsza kolumna
        /// kazdego wiersza, wiec skrypt agregujacy moze odrzucic serie w nieznanym formacie
        /// zamiast wymieszac ja z biezaca.
        /// </summary>
        /// WERSJA 2 (brama dwuetapowa): doszla kolumna "pBrama", a kolumna "wSoftmaksie" zmienila
        /// znaczenie - liczy teraz SAME zdarzenia etapu B, bez pseudo-kandydata PASS, ktory
        /// rozstrzyga sie osobno w bramie. Kolumna "losowan" jest odtad rowna dwukrotnosci
        /// liczby rund, nie liczbie rund.
        ///
        /// WERSJA 3 (trwalosc pamieci): doszla kolumna "runId" - identyfikator ROZGRYWKI,
        /// staly przez cale zycie zapisu gry. Do wersji 2 wlacznie jedyna granica w danych byla
        /// linia [PN-SESSION], ktora oznacza URUCHOMIENIE PROCESU: rozgrywka grana na raty
        /// rozpadala sie wiec na N nieodroznialnych kawalkow, a dwie rozne rozgrywki z jednego
        /// wieczoru zlewaly sie w jedna. Od tej wersji grupowanie idzie po runId, a [PN-SESSION]
        /// zostaje wylacznie znacznikiem technicznym.
        ///
        /// WERSJA 4 (krzywa dramaturgiczna): doszly kolumny "profil" i "napiecie".
        /// "profil" jest NIEZBEDNY, a nie ozdobny: od kroku 4 osobowosc narratora jest losowana
        /// na starcie rozgrywki i nieujawniana graczowi, wiec bez tej kolumny dwie rozgrywki
        /// "naszego narratora" prowadziliby DWAJ ROZNI narratorzy, a analiza nie mialaby jak
        /// tego rozwarstwic - losowy przydzial stalby sie niekontrolowana zmienna w ewaluacji.
        /// "napiecie" jest surowym wejsciem krzywej; bez niego z danych nie da sie odtworzyc,
        /// dlaczego narrator wybral akurat te intencje.
        ///
        /// WERSJA 5 (polerowanie warstwy decyzyjnej - piec zarzutow). Trzy nowe kolumny, jedna
        /// przemianowana, jedna o zmienionym znaczeniu. Wszystko w JEDNYM podbiciu, celowo:
        /// seria v4 jest zamykana, a nie laczona z v5, wiec skrypty agregujace przepisuje sie raz.
        ///
        ///   + "pRunda"        - prawdopodobienstwo zwyciezcy w LOSOWANIU WYBORU, ktore go
        ///                       wskazalo, WARUNKOWE (przy juz rozstrzygnietej bramie). PUSTE,
        ///                       gdy wyniku nie wyprodukowalo losowanie wyboru - czyli przy
        ///                       kazdej ciszy. Wklad bramy ma wlasna kolumne pBrama i nie jest
        ///                       tu niesiony drugi raz.
        ///   ~ "p"             - ZNACZENIE DOPRECYZOWANE, nie zmienione: jest to udzial kandydata
        ///                       w rozkladzie RUNDY PIERWSZEJ (po prewerifikacji, przed odmowami
        ///                       w petli), a suma po calym rankingu wynosi dokladnie 1 (przedtem
        ///                       1.15 przy jednej odmowie i 2.73, gdy silnik odrzucil wszystko).
        ///                       NIE jest to prawdopodobienstwo, ze tura skonczy sie tym wynikiem -
        ///                       takiej liczby w wierszu nie ma, bo zalezy od tego, czemu silnik
        ///                       odmowi, a to wiadomo dopiero po fakcie.
        ///   + "wspoldzielona" - czy potwierdzenie od silnika wspoldzielono z wariantem
        ///                       o IDENTYCZNYCH parametrach wykonania (rozniacym sie samym opisem).
        ///                       To oszczedzone pytanie, nie niepewnosc: warianty o INNYCH
        ///                       parametrach sa odkladane do nastepnej tury, bo cache
        ///                       CanFireNowSub (jeden wynik na IncidentDef i tick) nie pozwala
        ///                       ich sprawdzic. Kolumna jest kanarkiem na rozjazd miedzy
        ///                       IncidentParmsBuilder.Apply a ExecutionKey.
        ///   + "odlozonych"    - ile wariantow odlozono do nastepnej tury (inne parametry
        ///                       wykonania albo niedostepny werdykt).
        ///   + "niedostepnych" - ile razy silnik NIE MOGL dac swiezego werdyktu, bo o dany payload
        ///                       pytano juz w tym ticku. Wartosc > 0 znaczy, ze tura dzieli tick
        ///                       z inna tura narratora - w grze zachodzi to przy WIECEJ NIZ JEDNEJ
        ///                       kolonii, bo Storyteller.MakeIncidentsForInterval iteruje po
        ///                       wszystkich celach. Kolumna jest jedynym miejscem, w ktorym to
        ///                       widac; bez niej druga kolonia wygladala by na w pelni
        ///                       zweryfikowana.
        ///   + "odmowCzola"    - ile odmow silnika padlo PRZED brama, w prewerifikacji czola.
        ///   + "pytanDoGry"    - ile razy w turze zapytano silnik o wykonalnosc. Budzet jest
        ///                       WSPOLNY dla calej tury, wiec obowiazuje niezmiennik
        ///                       pytanDoGry <= maxSelectionRounds.
        ///   ~ "seriaPass" -> "ciszaSwiadoma" - licznik liczy WYLACZNIE cisze wybrana przez brame
        ///                       (PassReason.Competitive). Cisza z przeszkody technicznej nie
        ///                       zuzywa juz limitu swiadomego milczenia. NAZWA zmieniona razem
        ///                       ze znaczeniem: stara nazwa przy nowej semantyce dawalaby kolumne
        ///                       parsujaca sie w obu seriach i znaczaca w nich co innego.
        ///   ~ "wSoftmaksie"   - liczy zdarzenia WYKONALNE, ktore stanely do wyboru. Kandydaci
        ///                       odrzuceni przez silnik odpadaja z tej liczby, ale ZOSTAJA
        ///                       w "kandydatow" (mianownik opisuje ture) i w rankingu ze sladem.
        ///   ~ "losowan"       - odtad 1 + 2 * liczba rund, bo wybor zdarzenia jest dwustopniowy
        ///                       (akcja, potem jej wariant). Przedtem 1 + liczba rund.
        ///
        /// WERSJA 6 (polerowanie etapu 4 - napiecie, kryzys, tryb danych). Jedno podbicie, bo
        /// kolumna "napiecie" ZMIENILA ZNACZENIE, a v5 trafilo juz do pliku (33 wiersze
        /// z symulatora debugowego) - zmiana w obrebie v5 zmieszalaby dwie formuly pod jedna
        /// nazwa kolumny. Seria v5 jest zamykana, nie laczona.
        ///
        ///   + "tryb"          - "gra" albo "symulacja". Wiersze z wlasnej akcji debugowej
        ///                       "PN: test przyszlych incydentow" ida do TEGO SAMEGO pliku, ale
        ///                       odroznia je ta kolumna - analiza filtruje tryb == "gra".
        ///   + "eksperyment"   - pusta w grze; w symulacji "idEksperymentu/ramie", zeby ramiona
        ///                       jednego eksperymentu (profile, ramie kontrolne) dalo sie rozdzielic.
        ///   ~ "napiecie"      - ZNACZENIE ZMIENIONE w OBU czlonach. Narracyjny liczy zanik KAZDEGO
        ///                       wpisu osobno (wczesniej: wspolny mnoznik z wieku najnowszego wpisu).
        ///                       Sytuacyjny liczy powalonych OSTRO (AcuteDownedCount zamiast kazdego
        ///                       Downed) wsrod kolonistow OBECNYCH na mapie (ColonistsOnMap zamiast
        ///                       ColonistCount). Progi profili NIE zostaly przestrojone - rozklad
        ///                       intencji w v6 jest przesuniety ku Escalate (patrz CLAUDE.md).
        ///   + "napiecieNarr", "napiecieSyt" - czlony napiecia. Bez nich rozkladu napiecia nie
        ///                       dalo sie odtworzyc z pliku (wagi profilu sa w kolumnie profil).
        ///   + "powalonych", "kolonistowNaMapie", "zagrozenie" - wejscia czlonu sytuacyjnego
        ///                       i predykatu kryzysu. "powalonych" liczy powalonych OSTRO
        ///                       (szok bolowy, krwawienie albo cokolwiek do opatrzenia - takze
        ///                       choroba nigdy nieopatrzona albo na 3 h przed koncem opatrunku);
        ///                       bez niemowlat, a stany trwale tylko wtedy, gdy pionek ma cos do
        ///                       opatrzenia. Mianownik z tej samej listy pionkow obecnych na mapie.
        ///   + "kryzys"        - czy zachodzil kryzys skrajny (regula wspolna dla profili: Breathe,
        ///                       moc &lt;= 0, straznik serii zawieszony).
        ///   + "straznikZawieszony" - seria ciszy byla na limicie W KRYZYSIE, wiec straznik nie
        ///                       zmuszal do dzialania. Liczona NIEZALEZNIE od tego, czy pula miala
        ///                       zdarzenia (passStlumiony wymaga niepustej puli) - obie kolumny sie
        ///                       wykluczaja, ale nie sa swoim lustrem.
        ///   ~ "odlozonych"    - liczy WYLACZNIE odlozenia sprzed decyzji. W v5 dochodzily do niej
        ///                       odlozenia rodzenstwa PO przyjeciu zwyciezcy w petli rund (36 ze 115
        ///                       w danych v5 z waniliowego symulatora), ktore niczego nie zmienialy.
        ///   ~ "ciszaSwiadoma" - bez zmiany znaczenia kolumny (stan PRZED decyzja), ale cisza
        ///                       wybrana w kryzysie skrajnym NIE podnosi juz licznika. Taka cisza ma
        ///                       nadal powodPass=Competitive (brama ja wybrala) - metryki swiadomego
        ///                       milczenia per profil trzeba wiec filtrowac po kryzys=false.
        ///   ~ ksiegowanie ThreatBig przy wylaczonych duzych zagrozeniach (Peaceful, okno po
        ///                       metalowym piekle): kandydaci odpadaja w sicie przed scoringiem, wiec
        ///                       przechodza z odmowSilnika/odmowCzola/pytanDoGry do roznicy
        ///                       wygenerowanych - kandydatow (i zmniejszaja mianownik "kandydatow").
        ///
        /// KONFIGURACJA W PLIKU DANYCH: od v6 po [PN-DATA-COLS] ida linie [PN-CONFIG] z efektywnymi
        /// parametrami (configStamp, kryzys, PASS, profile). Bez nich wylaczenie reguly kryzysu albo
        /// zmiana progu bylyby w danych niewidoczne - kolumna "kryzys" jest boolem.
        ///
        /// v7 (krok 5, luki narracyjne): kolumny kontekstu (bogactwo, punkty) i stanu lukow w preambule
        /// oraz moc zwyciezcy i kolumny lukowe na koncu czesci decyzyjnej; linie [PN-ARC] i [PN-EXEC]
        /// poza kontraktem.
        ///
        /// v8 (krok 6, blackboard): kolumna "faktow" w preambule (obowiazujace fakty w snapshocie
        /// decyzji) i "konsekwencja" na koncu czesci decyzyjnej; linia [PN-FACT] poza kontraktem;
        /// pole "fakty=" w [PN-LOAD] i [PN-RESET]. Podbicie jest KONIECZNE takze z drugiego powodu:
        /// kolumna "klucz" (sygnatura kompozycji) ma od kroku 6 szesc segmentow zamiast pieciu, a bez
        /// nowego numeru skrypt analizy zszylby obie serie bez ostrzezenia - porownywalnosc pozorna
        /// jest gorsza niz zerwana.
        public const int DataFormatVersion = 8;

        /// <summary>
        /// PELNA lista kolumn linii [PN-DATA] w ich OBOWIAZUJACEJ kolejnosci. Jedyne zrodlo
        /// prawdy o formacie: naglowek wypisywany na starcie powstaje z tej tablicy, a kontrola
        /// spojnosci przy pierwszym wierszu porownuje z nia faktycznie zbudowana linie.
        ///
        /// Grupa trzecia pochodzi z NarratorDecision.ToDataFragment(), czyli z rdzenia - tu jest
        /// tylko jej deklaracja. Rozjazd miedzy rdzeniem a ta lista wychwytuje kontrola w runtime
        /// (VerifyFormatOnce), bo kompilator nie ma jak zwiazac tekstu z tablica.
        /// </summary>
        private static readonly string[] DataColumns =
        {
            // --- preambula: kto, kiedy, w jakim stanie pamieci (warstwa integracji) ---
            "wersjaLogu", "runId", "tryb", "eksperyment", "profil", "tick", "dzien", "decyzjaNr", "mapa",
            "histWpisow", "histDecyzji",
            "napiecie", "napiecieNarr", "napiecieSyt", "powalonych", "kolonistowNaMapie", "zagrozenie",
            "kryzys", "intencja", "docelowaMoc", "odmowSilnika", "odmowCzola", "pytanDoGry",
            // --- krok 5 (v7): kontekst (P1) i stan lukow w chwili decyzji ---
            "bogactwo", "bogactwoWzgl", "punkty",
            "lukiAktywne", "lukFazy", "frakcjaLuku", "lukStosowany", "lukDopasowanych",
            // --- krok 6 (v8): pamiec narratora w chwili decyzji ---
            "faktow",

            // --- generowanie kandydatow (CandidateSet) ---
            "wygenerowanych", "budzet", "akcji", "limitNaAkcje", "przestrzen",
            "wyczerpano", "ucieto", "budzetPrzekroczony",

            // --- decyzja: scoring i polityka wyboru (NarratorDecision.ToDataFragment) ---
            "decyzja", "wybor", "klucz", "wynik", "p", "pRunda",
            "wspoldzielona", "odlozonych", "niedostepnych", "best", "pasmo",
            "kandydatow", "zawetowanych", "odrzuconeCutoff", "odrzuconePasmo",
            "wSoftmaksie", "losowan",
            "powodPass", "passWynik", "pBrama", "passStlumiony", "straznikZawieszony",
            "gestosc", "ciszaSwiadoma",
            "contextFit", "freshness", "dramaticContrast", "intentAlignment",
            "passRestraint", "passBaseline", "passIntent",
            // --- krok 5 (v7): moc zwyciezcy i luk ---
            "intensywnosc", "arcAlignment", "premiaLuku", "lukWPasmie",
            // --- krok 6 (v8): slad zostawiany przez zwyciezce ---
            "konsekwencja"
        };

        private static bool formatVerified;

        // =====================================================================================
        //  KONTEKST EKSPERYMENTU - wlasna akcja debugowa "PN: test przyszlych incydentow"
        // =====================================================================================
        //  W trakcie eksperymentu:
        //    - wiersze [PN-DATA] ida do TEGO SAMEGO pliku co dane z gry, z tryb=symulacja
        //      i niepusta kolumna eksperyment - jedna seria plikow, rozdzielana kolumna;
        //    - linie CZYTELNE ([PN]) NIE ida do Verse.Log, tylko do osobnego pliku
        //      PN_symulacje.log. Powod zmierzony: 33 decyzje symulatora daly 614 wiadomosci,
        //      a Verse.Log wylacza sie po 1000 - trzy ramiona po 100 dni wygasilyby log calej gry
        //      (takze innych modow) w jednym kliknieciu;
        //    - Warn i Error nadal ida do Verse.Log, z prefiksem [PN][SYM], bo sa rzadkie i musza
        //      byc widoczne tam, gdzie szuka sie bledow.
        // =====================================================================================

        private const string SimFileName = "PN_symulacje.log";
        private static StreamWriter simWriter;
        private static bool simSinkBroken;

        /// <summary>Identyfikator biezacego eksperymentu albo null poza eksperymentem.</summary>
        private static string experimentId;

        /// <summary>Etykieta biezacego ramienia eksperymentu ("1-PN_Profil_...").</summary>
        private static string experimentArm;

        /// <summary>Czy trwa eksperyment wlasnej akcji symulacyjnej.</summary>
        public static bool InExperiment
        {
            get { return experimentId != null; }
        }

        public static string SimFilePath
        {
            get { return Path.Combine(GenFilePaths.SaveDataFolderPath, SimFileName); }
        }

        /// <summary>
        /// Otwiera kontekst eksperymentu. Linia [PN-EXP] start idzie do OBU plikow: do pliku
        /// danych jako granica serii (parser widzi, ze nastepne wiersze sa symulacja), do pliku
        /// czytelnego jako naglowek.
        /// </summary>
        public static void BeginExperiment(string id, string naglowek)
        {
            experimentId = string.IsNullOrEmpty(id) ? "exp" : id;
            experimentArm = string.Empty;
            string linia = "[PN-EXP] start; eksperyment=" + experimentId + "; " + (naglowek ?? string.Empty);
            WriteData(linia);
            WriteSim(linia);
        }

        public static void BeginArm(string ramie, string opis)
        {
            experimentArm = ramie ?? string.Empty;
            string linia = "[PN-EXP] ramie; eksperyment=" + experimentId + "; ramie=" + experimentArm
                           + "; " + (opis ?? string.Empty);
            WriteData(linia);
            WriteSim(linia);
        }

        /// <summary>
        /// Zamyka kontekst eksperymentu. Wolane w finally - takze po wyjatku - wiec linia end
        /// niesie stan: "kompletny" albo "przerwany", zeby parser odroznil ramie urwane.
        /// </summary>
        public static void EndExperiment(string podsumowanie)
        {
            if (experimentId == null)
            {
                return;
            }
            string linia = "[PN-EXP] end; eksperyment=" + experimentId + "; " + (podsumowanie ?? string.Empty);
            WriteData(linia);
            WriteSim(linia);
            experimentId = null;
            experimentArm = null;

            // Potwierdzenie zgodnosci formatu ma pasc takze w Player.log przy pierwszym PRAWDZIWYM
            // wierszu - jesli pierwszy wiersz sesji byl symulacja, poszlo tylko do pliku symulacji.
            formatVerified = false;
        }

        private static void WriteSim(string line)
        {
            if (simSinkBroken)
            {
                return;
            }
            try
            {
                if (simWriter == null)
                {
                    simWriter = new StreamWriter(SimFilePath, true, new UTF8Encoding(false));
                    simWriter.AutoFlush = true;
                    simWriter.WriteLine("[PN-SESSION] start; plik czytelny symulacji; wersjaLogu="
                                        + DataFormatVersion.ToString(CultureInfo.InvariantCulture)
                                        + "; wersjaGry=" + VersionControl.CurrentVersionString);
                }
                simWriter.WriteLine(line);
            }
            catch (Exception e)
            {
                simSinkBroken = true;
                Log.Error(Prefix + "Nie udalo sie pisac do pliku symulacji (" + SimFilePath + "): " + e.Message);
            }
        }

        // =====================================================================================
        //  UJSCIE DANYCH BADAWCZYCH - WLASNY PLIK, NIE Player.log
        // =====================================================================================
        //  Verse.Log ma twardy limit 1000 KOMUNIKATOW (zweryfikowane dekompilacja 1.5.4063:
        //  Log.Notify_MessageReceivedThreadedInternal). Przy tysiecznym gra wypisuje "Reached max
        //  messages limit. Stopping logging to avoid spam." i WYLACZA logowanie calkowicie -
        //  Message, Warning i Error koncza sie odtad pustym return. Licznik obejmuje CALY proces
        //  (wanilie i wszystkie mody), a zeruje sie tylko na koncu ladowania gry, przy
        //  przeladowaniu Defow w trybie deweloperskim i przyciskiem Clear w oknie logu -
        //  WCZYTANIE ZAPISU GO NIE ZERUJE, wiec druga rozgrywka w tym samym procesie dziedziczy
        //  nasycenie.
        //
        //  Wczesniejsze wydanie tego komentarza liczylo "szesc linii na decyzje, limit po ok. 150
        //  decyzjach". Zmierzone bylo gorzej: jedna decyzja dawala ok. 19 komunikatow (kazda linia
        //  rankingu osobno; 33 decyzje symulatora = 614 komunikatow), czyli sufit ok. 50 decyzji
        //  w jednej sesji. Od drugiego przegladu etapu 4 log czytelny calej tury idzie JEDNYM
        //  komunikatem wieloliniowym (limit liczy komunikaty, nie linie) - sufit rosnie do rzedu
        //  1000 decyzji minus komunikaty innych modow.
        //
        //  Dlatego:
        //    - strumien MASZYNOWY idzie do wlasnego pliku obok Player.log - jego utrata byla by
        //      cicha awaria w danych badawczych;
        //    - Warn i Error sa LUSTRZANE w pliku danych ([PN-WARN] / [PN-ERR]), bo po nasyceniu
        //      Verse.Log znikalyby dokladnie te komunikaty, ktorych szuka sie przy diagnozie;
        //      parsery filtruja po prefiksie, wiec te linie niczego nie psuja;
        //    - kryteria odbioru testu w grze opieraja sie na pliku danych, a gdy siegaja do
        //      Player.log, to z warunkiem, ze nie padlo w nim "Reached max messages limit".
        // =====================================================================================

        private const string DataFileName = "PN_decyzje.log";
        private static StreamWriter dataWriter;
        private static bool dataSinkBroken;

        /// <summary>Pelna sciezka pliku z danymi badawczymi - wypisywana na starcie, zeby dalo sie ja znalezc.</summary>
        public static string DataFilePath
        {
            get { return Path.Combine(GenFilePaths.SaveDataFolderPath, DataFileName); }
        }

        /// <summary>
        /// Zapisuje jedna linie danych. Otwiera plik leniwie i trzyma go otwartego, bo linii jest
        /// jedna na 1000 tickow - koszt otwierania za kazdym razem bylby wiekszy niz zysk.
        ///
        /// Tryb DOPISYWANIA, nie nadpisywania: seria ewaluacyjna to wiele uruchomien gry i kazde
        /// ma dolozyc swoje dane, a nie skasowac poprzednie. Sesje rozdziela linia [PN-SESSION].
        ///
        /// AutoFlush jest wlaczony celowo. Rozgrywka konczy sie zwykle zabiciem procesu albo
        /// wyjsciem do pulpitu, wiec finalizator moze nigdy nie dobiec - bez flushowania ostatnie
        /// decyzje siedzialyby w buforze i przepadly. Utrata wydajnosci jest zerowa przy jednej
        /// linii na 1000 tickow.
        /// </summary>
        private static void WriteData(string line)
        {
            if (dataSinkBroken)
            {
                return;
            }

            try
            {
                if (dataWriter == null)
                {
                    string sciezka = DataFilePath;
                    // UTF8Encoding(false), a NIE Encoding.UTF8 - ten drugi dopisuje BOM przy
                    // TWORZENIU pliku. Znacznik ladowal wtedy przed pierwsza linia [PN-SESSION]
                    // i naiwne open(..., encoding='utf-8') w Pythonie zwracalo pierwsza linie
                    // z doklejonym znakiem U+FEFF, wiec startswith('[PN-SESSION]') nie trafialo.
        // (Znaku BOM celowo NIE wklejamy tu doslownie - komentarz opisujacy usterke
        //  nie moze sam byc nosnikiem tej samej pulapki dla narzedzi czytajacych plik.)
                    // Awaria dotyczy WYLACZNIE pierwszego uruchomienia po skasowaniu pliku -
                    // czyli dokladnie tego przypadku, od ktorego zaczyna sie kazda czysta seria
                    // pomiarowa. Zlapane przy pierwszym uruchomieniu skryptu analizujacego.
                    dataWriter = new StreamWriter(sciezka, true, new UTF8Encoding(false));
                    dataWriter.AutoFlush = true;
                    dataWriter.WriteLine("[PN-SESSION] start; wersjaLogu="
                                         + DataFormatVersion.ToString(CultureInfo.InvariantCulture)
                                         + "; wersjaGry=" + VersionControl.CurrentVersionString);
                    Decision("Dane badawcze pisane do pliku: " + sciezka);
                }

                dataWriter.WriteLine(line);
            }
            catch (Exception e)
            {
                // Jedno glosne ostrzezenie i koniec prob. Brak danych badawczych nie moze
                // przewrocic rozgrywki, ale nie moze tez zostac niezauwazony - inaczej gracz
                // przechodzi cala seriee i dopiero potem odkrywa, ze plik jest pusty.
                dataSinkBroken = true;
                Error("Nie udalo sie pisac do pliku danych badawczych (" + DataFilePath + "): "
                      + e.Message + ". Dalsze linie [PN-DATA] beda pomijane.");
            }
        }

        public static void Decision(string message)
        {
            if (InExperiment)
            {
                WriteSim(Prefix + message);
                return;
            }
            Log.Message(Prefix + message);
        }

        public static void Warn(string message)
        {
            Log.Warning((InExperiment ? "[PN][SYM] " : Prefix) + message);
            if (InExperiment)
            {
                WriteSim("[PN][SYM][WARN] " + message);
            }
            MirrorToData(WarnDataPrefix, message);
        }

        public static void Error(string message)
        {
            Log.Error((InExperiment ? "[PN][SYM] " : Prefix) + message);
            if (InExperiment)
            {
                WriteSim("[PN][SYM][ERROR] " + message);
            }
            MirrorToData(ErrorDataPrefix, message);
        }

        /// <summary>
        /// Kopia ostrzezenia albo bledu w pliku danych - odporna na limit 1000 komunikatow
        /// Verse.Log (patrz komentarz przy ujsciu danych). Jedna linia: tekst jest OSTATNIM polem
        /// i ciagnie sie do konca linii (moze zawierac ';' i '='), znaki nowej linii (np. slad
        /// stosu wyjatku) sa zamieniane na " | ". Poza kontraktem [PN-DATA], bez numeru wersji.
        /// Rekurencji nie ma: blad zapisu ustawia dataSinkBroken PRZED wolaniem Error.
        /// </summary>
        private static void MirrorToData(string prefix, string message)
        {
            string tekst = (message ?? string.Empty).Replace("\r\n", " | ").Replace("\n", " | ").Replace("\r", " ");
            WriteData(prefix + "runId=" + CurrentRunId()
                      + "; tick=" + CurrentTickText()
                      + "; tryb=" + (InExperiment ? "symulacja" : "gra")
                      + "; tekst=" + tekst);
        }

        private static string CurrentTickText()
        {
            return Current.Game != null && Find.TickManager != null
                ? Find.TickManager.TicksGame.ToString(CultureInfo.InvariantCulture)
                : "-1";
        }

        /// <summary>
        /// Naglowek formatu, wypisywany RAZ przy starcie gry. Dzieki niemu parser z kroku 8
        /// nie musi miec kolejnosci kolumn zaszytej w kodzie - czyta ja z tego samego pliku,
        /// z ktorego czyta dane, wiec nie da sie ich rozjechac.
        /// </summary>
        public static void DataHeader()
        {
            WriteData(ColumnsPrefix + "wersja=" + DataFormatVersion.ToString(CultureInfo.InvariantCulture)
                      + "; kolumny=" + string.Join(",", DataColumns));
        }

        /// <summary>
        /// Jedna linia efektywnej konfiguracji w pliku danych ([PN-CONFIG]). Wolane przez PNStartup
        /// zaraz po naglowku kolumn - parser nie musi zgadywac, z jakimi parametrami powstala seria.
        /// Poza kontraktem kolumn [PN-DATA], wiec bez wlasnego numeru wersji.
        /// </summary>
        public static void Config(string opis)
        {
            WriteData(ConfigPrefix + (opis ?? string.Empty));
        }

        /// <summary>
        /// Identyfikator biezacej rozgrywki, czytany wprost z magazynu trwalego stanu.
        ///
        /// Czytany za kazdym razem, a nie cache'owany w polu statycznym: PNLog zyje tak dlugo
        /// jak PROCES gry, a rozgrywka moze sie w tym czasie zmienic (wyjscie do menu, wczytanie
        /// innego zapisu). Cache przezylby te zmiane i po cichu podpisywalby wiersze nowej
        /// rozgrywki identyfikatorem poprzedniej - czyli produkowalby dane gorsze niz ich brak.
        /// Koszt to skan listy komponentow raz na 1000 tickow, czyli zero.
        /// </summary>
        private static string CurrentRunId()
        {
            if (Current.Game == null)
            {
                return string.Empty;
            }

            NarratorMemoryComponent pamiec = Current.Game.GetComponent<NarratorMemoryComponent>();
            return pamiec == null ? string.Empty : pamiec.RunId;
        }

        /// <summary>
        /// Profil, na ktorym narrator FAKTYCZNIE liczy (NarratorMemoryComponent.EffectiveProfileId),
        /// a nie defName zapisany w pamieci. Rozjazd zachodzi, gdy zapis wskazuje profil usuniety
        /// albo przemianowany w XML: wagi i krzywa ida wtedy z profilu awaryjnego, a kolumna
        /// niosla dawniej stara nazwe - czyli dokladnie te pomylke, przed ktora kolumna ma chronic
        /// ("dwie rozgrywki prowadzili dwaj rozni narratorzy"). Szczegol podmiany (ktory profil
        /// zastapiono) idzie linia [PN-ERR] z NarratorProfileCatalog.Resolve.
        /// Czytany za kazdym razem z tego samego powodu co runId.
        /// </summary>
        private static string CurrentProfileId()
        {
            if (Current.Game == null)
            {
                return string.Empty;
            }

            NarratorMemoryComponent pamiec = Current.Game.GetComponent<NarratorMemoryComponent>();
            return pamiec == null ? NarratorProfile.FallbackId : pamiec.EffectiveProfileId;
        }

        /// <summary>
        /// Kanarek wczytania pamieci narratora - jedna linia na uruchomienie rozgrywki.
        ///
        /// Pelni dwie role naraz i obie sa potrzebne:
        ///   1. DIAGNOSTYCZNA - rozstrzyga, czy pamiec przetrwala zapis. Bez niej "narrator
        ///      zaczyna od zera po wczytaniu" i "narrator ma pusta pamiec, bo to nowa kolonia"
        ///      wygladaja w danych identycznie, a to jest dokladnie ta awaria, przed ktora
        ///      warstwa trwalosci ma chronic.
        ///   2. STRUKTURALNA - jest GRANICA WCZYTANIA dla skryptu agregujacego. Wiersze
        ///      [PN-DATA] po tej linii naleza do tej samej rozgrywki (ten sam runId) co przed
        ///      zapisem. Nowej linii [PN-SESSION] moze miedzy nimi NIE BYC: wczytanie zapisu
        ///      w tym samym procesie nie otwiera pliku na nowo.
        ///
        /// REGULA PARSERA - WCZYTANIE ROZWIDLA ROZGRYWKE. Dwa wczytania jednego zapisu daja ten
        /// sam runId i te same numery decyzji od chwili zapisu w gore, wiec wiersze "porzuconej
        /// galezi" (grane po zapisie, a przed ponownym wczytaniem) sa nieodroznialne kolumnami.
        /// Rozstrzyga kolejnosc w pliku: w obrebie runId kazda linia [PN-LOAD] UNIEWAZNIA
        /// wczesniejsze wiersze mapy m z decyzjaNr >= decyzji tej mapy z pola "mapy"
        /// (uid:decyzji). Ograniczenie: rozwidlenia grane NAPRZEMIENNIE (dwa zapisy z jednego
        /// punktu, wczytywane na zmiane) sa nierozstrzygalne bez identyfikatora galezi w zapisie.
        /// Ta sama regula dotyczy akcji "PN: wymus profil" (linia [PN-RESET]) - seria kontrolna
        /// "ten sam zapis trzema profilami" produkuje wlasnie takie galezie.
        ///
        /// POLA: profil = profil FAKTYCZNIE uzyty (patrz CurrentProfileId), profilZapisany = to,
        /// co wskazuje zapis; wersjaPamieci = wersja formatu Z ZAPISU (0 przy nowej grze i przy
        /// zapisie bez pamieci); narrator = defName aktywnego StorytellerDefa - komponent pamieci
        /// dziala w KAZDEJ grze, takze na Cassandrze, wiec zliczanie rozgrywek po [PN-LOAD] musi
        /// filtrowac po tym polu.
        ///
        /// Nie rusza kontraktu kolumn [PN-DATA], wiec nie wymaga wlasnego numeru wersji -
        /// parser, ktory jej nie zna, po prostu ja pominie po prefiksie.
        /// </summary>
        public static void Load(string runId, string zrodlo, string profil, string profilZapisany,
                                int mapCount, int wpisow, int decyzji, int odrzuconych, int wersjaPamieci,
                                string mapy, string luki, string fakty,
                                int odrzuconychHistorii, int odrzuconychLukow, int odrzuconychFaktow)
        {
            string linia = LoadPrefix
                           + "runId=" + (string.IsNullOrEmpty(runId) ? "?" : runId)
                           + "; zrodlo=" + zrodlo
                           + "; profil=" + (string.IsNullOrEmpty(profil) ? "?" : profil)
                           + "; map=" + mapCount.ToString(CultureInfo.InvariantCulture)
                           + "; wpisow=" + wpisow.ToString(CultureInfo.InvariantCulture)
                           + "; decyzji=" + decyzji.ToString(CultureInfo.InvariantCulture)
                           + "; odrzuconych=" + odrzuconych.ToString(CultureInfo.InvariantCulture)
                           // S6: ta sama suma rozbita na ksiegi - osobne wezly Scribe mialy izolowac bledy
                           // kodekow, a jedna liczba nie mowila, ktora ksiega zawiodla.
                           + "; odrzuconychHistorii=" + odrzuconychHistorii.ToString(CultureInfo.InvariantCulture)
                           + "; odrzuconychLukow=" + odrzuconychLukow.ToString(CultureInfo.InvariantCulture)
                           + "; odrzuconychFaktow=" + odrzuconychFaktow.ToString(CultureInfo.InvariantCulture)
                           + "; wersjaPamieci=" + wersjaPamieci.ToString(CultureInfo.InvariantCulture)
                           + "; profilZapisany=" + (string.IsNullOrEmpty(profilZapisany) ? "?" : profilZapisany)
                           + "; narrator=" + CurrentStorytellerName()
                           + "; tick=" + CurrentTickText()
                           + "; dzien=" + CurrentDayText()
                           + "; mapy=" + (mapy ?? string.Empty)
                           // Krok 5: otwarte luki "uid:luk#nr:faza,..." - stan lukow w chwili
                           // wczytania; ta sama regula rozwidlenia co dla historii.
                           + "; luki=" + (luki ?? string.Empty)
                           // Krok 6: fakty "uid:klucz=wartosc@dzien/zycie,..." (+ "uid:kolejka@tick") -
                           // stan pamieci faktow w chwili wczytania, ta sama regula rozwidlenia.
                           + "; fakty=" + (fakty ?? string.Empty);

            WriteData(linia);

            // Ta sama tresc idzie do logu czytelnego, bo przy diagnozowaniu "czemu narrator
            // zapomnial" pierwszym miejscem, w ktore sie patrzy, jest Player.log, a nie plik danych.
            string opis;
            if (zrodlo == "nowaGra")
            {
                opis = "Nowa rozgrywka, runId=" + runId + ", profil=" + profil
                       + ". Pamiec narratora startuje pusta.";
            }
            else if (zrodlo == "zapisBezPamieci")
            {
                // Dwie przyczyny daja ten sam stan i log czytelny ma je obie wymienic. Silnik przy
                // nieudanej deserializacji komponentu (nieznana klasa, wyjatek w ExposeData) loguje
                // Error i dostawia swiezy komponent przez FillComponents - dokladnie jak przy zapisie,
                // w ktorym bloku nie bylo. Wykrycie tego po naszej stronie wymagaloby kruchego
                // podgladania surowego XML w konstruktorze, wiec rozstrzyga Player.log.
                opis = "Wczytano zapis BEZ pamieci narratora (runId=" + runId
                       + "). Normalne dla zapisu sprzed kroku 6 albo dla moda dolozonego do trwajacej "
                       + "rozgrywki - narracja zaczyna sie od tego momentu. JESLI zapis byl robiony z tym "
                       + "modem, to znaczy, ze pamiec sie NIE WCZYTALA: szukaj wyzej w Player.log bledu "
                       + "silnika o NarratorMemoryComponent (np. 'Could not find class' albo wyjatku "
                       + "w ExposeData).";
            }
            else
            {
                opis = "Wczytano pamiec narratora: runId=" + runId
                       + ", map=" + mapCount.ToString(CultureInfo.InvariantCulture)
                       + ", wpisow=" + wpisow.ToString(CultureInfo.InvariantCulture)
                       + ", decyzji=" + decyzji.ToString(CultureInfo.InvariantCulture)
                       + ", odrzuconych linii=" + odrzuconych.ToString(CultureInfo.InvariantCulture) + ".";
            }

            Decision(opis);
        }

        /// <summary>
        /// Znacznik recznej ingerencji w pamiec albo profil (akcje debugowe "PN: skasuj pamiec",
        /// "PN: wymus profil"). Bez niego decyzjaNr, histDecyzji i histWpisow spadalyby do zera
        /// pod tym samym runId bez zadnej linii w pliku danych, a zmiana profilu byla widoczna
        /// tylko jako zmiana wartosci kolumny - bez momentu i przyczyny. Poza kontraktem [PN-DATA].
        /// </summary>
        public static void Reset(string akcja, string szczegoly)
        {
            WriteData(ResetPrefix
                      + "runId=" + CurrentRunId()
                      + "; tick=" + CurrentTickText()
                      + "; dzien=" + CurrentDayText()
                      + "; akcja=" + (akcja ?? "?")
                      + (string.IsNullOrEmpty(szczegoly) ? string.Empty : "; " + szczegoly));
        }

        private const string ArcPrefix = "[PN-ARC] ";
        private const string ExecPrefix = "[PN-EXEC] ";
        private const string FactPrefix = "[PN-FACT] ";

        /// <summary>
        /// Jedno zdarzenie ksiegi faktow (krok 6): ustawienie po potwierdzonym wykonaniu, odrzucenie
        /// faktow zdarzenia niewykonanego albo deklaracja niepoprawna. Poza kontraktem [PN-DATA],
        /// bo fakty trafiaja do pamieci MIEDZY decyzjami (na poczatku nastepnego wywolania compa).
        ///
        /// tick = chwila ZASTOSOWANIA, tickZrodla/decyzjaZrodla = zdarzenie, ktore fakt zostawilo -
        /// analiza sprawdza z tego regule "fakt widoczny dopiero od nastepnej tury". Wygasanie nie ma
        /// linii: jest leniwe, wiec analiza liczy je z dnia i czasu zycia.
        /// </summary>
        public static void Fact(int mapId, int tick, Core.Blackboard.FactEvent e)
        {
            if (e == null)
            {
                return;
            }
            WriteData(FactPrefix
                      + "runId=" + CurrentRunId()
                      + "; tryb=" + (InExperiment ? "symulacja" : "gra")
                      + "; eksperyment=" + (InExperiment ? experimentId + "/" + experimentArm : string.Empty)
                      + "; tick=" + tick.ToString(CultureInfo.InvariantCulture)
                      + "; mapa=" + mapId.ToString(CultureInfo.InvariantCulture)
                      + "; zdarzenie=" + e.KindLabel()
                      + "; klucz=" + (string.IsNullOrEmpty(e.Key) ? "-" : e.Key)
                      + "; wartosc=" + (float.IsNaN(e.Value) ? string.Empty : e.Value.ToString("G9", CultureInfo.InvariantCulture))
                      + "; dzien=" + e.Day.ToString("G9", CultureInfo.InvariantCulture)
                      + "; zycie=" + e.LifespanDays.ToString("G9", CultureInfo.InvariantCulture)
                      + "; tickZrodla=" + e.SourceTick.ToString(CultureInfo.InvariantCulture)
                      + "; decyzjaZrodla=" + e.SourceDecision.ToString(CultureInfo.InvariantCulture)
                      + "; powod=" + (string.IsNullOrEmpty(e.Reason) ? "-" : e.Reason));
        }

        /// <summary>
        /// Jeden krok automatu luku (krok 5): otwarcie, przejscie, zamkniecie albo odrzucenie
        /// instancji. Poza kontraktem [PN-DATA] - wlasna linia, bo kroki lukow zachodza takze
        /// MIEDZY decyzjami (obserwacja co 1000 tickow). decyzjaNr = liczba decyzji mapy w chwili
        /// kroku; rozwidlenie po wczytaniu rozstrzyga sie po ticku (tick &gt; tick [PN-LOAD]).
        /// </summary>
        public static void Arc(int mapId, int decyzjaNr, Core.Arcs.ArcTransitionRecord r)
        {
            if (r == null)
            {
                return;
            }
            WriteData(ArcPrefix
                      + "runId=" + CurrentRunId()
                      + "; tryb=" + (InExperiment ? "symulacja" : "gra")
                      + "; eksperyment=" + (InExperiment ? experimentId + "/" + experimentArm : string.Empty)
                      + "; tick=" + r.Tick.ToString(CultureInfo.InvariantCulture)
                      + "; dzien=" + r.GameDay.ToString("0.000", CultureInfo.InvariantCulture)
                      + "; mapa=" + mapId.ToString(CultureInfo.InvariantCulture)
                      + "; decyzjaNr=" + decyzjaNr.ToString(CultureInfo.InvariantCulture)
                      + "; " + r.ToDataFragment());
        }

        /// <summary>
        /// Potwierdzenie wykonania zdarzenia wyemitowanego przez narratora (krok 5, decyzja autora
        /// nr 6): lastFireTicks[def] przed i po TryFire. Jedna linia na kazde zdarzenie - takze
        /// niewykonane, bo odsetek wykonan to wprost pomiar dlugu 7 ("historia zapisuje intencje").
        /// </summary>
        public static void Exec(int mapId, int decyzjaNr, string incydent, string klucz, string status,
                                int ostatniPrzed, int ostatniPo, string frakcja, string frakcjaZwiazana, int tick,
                                string frakcjaZrodlo, int tickDecyzji)
        {
            WriteData(ExecPrefix
                      + "runId=" + CurrentRunId()
                      + "; tryb=" + (InExperiment ? "symulacja" : "gra")
                      + "; eksperyment=" + (InExperiment ? experimentId + "/" + experimentArm : string.Empty)
                      + "; tick=" + tick.ToString(CultureInfo.InvariantCulture)
                      + "; mapa=" + mapId.ToString(CultureInfo.InvariantCulture)
                      + "; decyzjaNr=" + decyzjaNr.ToString(CultureInfo.InvariantCulture)
                      + "; incydent=" + (incydent ?? "-")
                      + "; klucz=" + (klucz ?? "-")
                      + "; status=" + (status ?? "-")
                      + "; ostatniPrzed=" + ostatniPrzed.ToString(CultureInfo.InvariantCulture)
                      + "; ostatniPo=" + ostatniPo.ToString(CultureInfo.InvariantCulture)
                      + "; frakcja=" + (string.IsNullOrEmpty(frakcja) ? "-" : frakcja)
                      + "; frakcjaZwiazana=" + (string.IsNullOrEmpty(frakcjaZwiazana) ? "-" : frakcjaZwiazana)
                      // parms = faktyczna frakcja z IncidentParms po TryExecute; emulacja = symulator
                      // (FactionBinding.EmulatedRaidFaction); "-" = brak frakcji.
                      + "; frakcjaZrodlo=" + (string.IsNullOrEmpty(frakcjaZrodlo) ? "-" : frakcjaZrodlo)
                      // S6: tick DECYZJI, ktorej dotyczy potwierdzenie. Na sciezce normalnej == tick; na
                      // spoznionej tick to chwila emisji (T+1000), a analiza laczy wykonanie z decyzja po tym polu.
                      + "; tickDecyzji=" + tickDecyzji.ToString(CultureInfo.InvariantCulture));
        }

        private static string CurrentDayText()
        {
            return Current.Game != null && Find.TickManager != null
                ? (Find.TickManager.TicksGame / 60000f).ToString("0.000", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static string CurrentStorytellerName()
        {
            if (Current.Game == null || Current.Game.storyteller == null || Current.Game.storyteller.def == null)
            {
                return "?";
            }
            return Current.Game.storyteller.def.defName;
        }

        /// <summary>
        /// Jedna linia maszynowa na jedna decyzje narratora - takze na decyzje o ciszy.
        ///
        /// Kolumny czynnikow ZDARZENIOWYCH dla wiersza PASS zostaja PUSTE, a nie zerowe.
        /// Zero jest wartoscia, pustka jest brakiem pomiaru: wpisanie tam 0.5 wstrzyknelo by
        /// do danych badawczych pomiar, ktorego nie bylo, i sciagnelo srednie w kierunku
        /// udzialu PASS-ow. W Pandas pusta kolumna to NaN i wypada z agregacji sama.
        /// (Realizuje to NarratorDecision.ToDataFragment; tutaj tylko o tym nie zapominamy.)
        /// </summary>
        public static void Data(int tick, int mapId, DecisionContext context, CandidateSet candidates,
                                NarratorDecision decision, int engineRefusals,
                                int preGateRefusals, int acceptorCalls,
                                TensionReading tension, CrisisReading crisis)
        {
            if (decision == null)
            {
                // Bez czesci decyzyjnej wiersz mialby inny zestaw kolumn niz wszystkie pozostale,
                // a wiersz o zmiennym ksztalcie jest gorszy niz brak wiersza - psuje cala ramke.
                Error("linia [PN-DATA] pominieta: brak obiektu decyzji");
                return;
            }

            CandidateSet zbior = candidates ?? new CandidateSet();
            var sb = new StringBuilder(768);

            // ---- preambula ----
            Append(sb, "wersjaLogu", DataFormatVersion.ToString(CultureInfo.InvariantCulture));
            Append(sb, "runId", CurrentRunId());
            // TRYB I EKSPERYMENT ZARAZ PO runId: runId zostaje prawdziwym identyfikatorem
            // rozgrywki (niesie pochodzenie danych i laczy sie z [PN-LOAD]), a o tym, czy wiersz
            // opisuje gre, czy symulacje, mowi osobna kolumna. Prefiks w runId laczylby dwa
            // znaczenia w jednej kolumnie - wzorzec, ktory projekt juz raz usuwal (seriaPass).
            Append(sb, "tryb", InExperiment ? "symulacja" : "gra");
            Append(sb, "eksperyment", InExperiment ? experimentId + "/" + experimentArm : string.Empty);
            Append(sb, "profil", CurrentProfileId());
            Append(sb, "tick", Int(tick));
            Append(sb, "dzien", Num(context == null ? 0f : context.GameDay));
            Append(sb, "decyzjaNr", Int(context == null ? 0 : context.DecisionIndex));
            // Identyfikator mapy: kazda kolonia prowadzi WLASNA pamiec (klucz Map.uniqueID),
            // wiec bez tej kolumny skok liczby wpisow przy przejsciu miedzy mapami wygladalby
            // w danych jak utrata pamieci. Od kroku 6 pamiec zyje w NarratorMemoryComponent
            // i przezywa zapis gry, ale rozdzial per mapa zostaje - i nadal trzeba go widziec.
            Append(sb, "mapa", Int(mapId));
            Append(sb, "histWpisow", Int(context == null || context.History == null ? 0 : context.History.Count));
            Append(sb, "histDecyzji", Int(context == null || context.History == null ? 0 : context.History.DecisionCount));
            Append(sb, "napiecie", Num(context == null ? 0f : context.Tension));
            // Czlony napiecia i ich wejscia. Puste (brak pomiaru), gdy odczytu nie podano.
            Append(sb, "napiecieNarr", tension == null ? string.Empty : Num(tension.Narrative));
            Append(sb, "napiecieSyt", tension == null ? string.Empty : Num(tension.Situational));
            WorldSnapshot swiat = context == null ? null : context.Snapshot;
            Append(sb, "powalonych", swiat == null ? string.Empty : Int(swiat.AcuteDownedCount));
            Append(sb, "kolonistowNaMapie", swiat == null ? string.Empty : Int(swiat.ColonistsOnMap));
            Append(sb, "zagrozenie", swiat == null ? string.Empty : swiat.Danger.ToString());
            Append(sb, "kryzys", crisis != null && crisis.Extreme ? "true" : "false");
            Append(sb, "intencja", context == null ? string.Empty : context.Intent.ToString());
            Append(sb, "docelowaMoc", Num(context == null ? 0f : context.TargetIntensity));
            // TRZY LICZBY OPISUJACE PRACE ODDANA SILNIKOWI GRY, i kazda odpowiada na inne pytanie.
            //
            //   odmowSilnika - ile razy gra odmowila w tej turze, LACZNIE
            //   odmowCzola   - ile z tego padlo PRZED brama, w prewerifikacji czola rankingu
            //   pytanDoGry   - ile razy w ogole zapytano akceptor (prewerifikacja plus rundy)
            //
            // Roznica odmowSilnika - odmowCzola to odmowy PO bramie, czyli te, ktore faktycznie
            // kosztowaly runde petli wyboru. Bez tego rozdzielenia nie da sie odroznic tury,
            // w ktorej prewerifikacja zadzialala (duzo odmow, jedna runda), od tury, w ktorej
            // narrator brnal przez ranking - a to sa przeciwne diagnozy.
            //
            // pytanDoGry jest miara KOSZTU naprawy: prewerifikacja kupuje trafniejsza brame za
            // dodatkowe wywolania CanFireNow, a ta kolumna mowi, ile ich naprawde bylo.
            Append(sb, "odmowSilnika", Int(engineRefusals));
            Append(sb, "odmowCzola", Int(preGateRefusals));
            Append(sb, "pytanDoGry", Int(acceptorCalls));

            // KROK 5 (v7). Kontekst decyzji z SNAPSHOTU tury (ten sam, na ktorym liczono warunki):
            // bogactwo dla storytellera, jego krotnosc normy dnia i bazowe punkty zagrozenia -
            // do rozdzialu o kontekstowosci (dlug 5, rozszerzenie P1).
            Append(sb, "bogactwo", swiat == null ? string.Empty : Num(swiat.ColonyWealth));
            Append(sb, "bogactwoWzgl", swiat == null ? string.Empty : Num(swiat.WealthRelative));
            Append(sb, "punkty", swiat == null ? string.Empty : Num(swiat.ThreatPoints));
            // Stan lukow: PUSTE, gdy warstwa lukow w tej turze nie istnieje (wylaczona albo ramie
            // "bez lukow"); 0/false, gdy istnieje, ale nic nie jest otwarte - pustka to brak
            // pomiaru, zero to pomiar.
            Core.Arcs.ArcFocus fokus = context == null ? null : context.ArcFocus;
            Append(sb, "lukiAktywne", fokus == null ? string.Empty : Int(fokus.Entries.Count));
            Append(sb, "lukFazy", fokus == null ? string.Empty : fokus.DataPhases());
            Append(sb, "frakcjaLuku", fokus == null ? string.Empty : fokus.BoundFaction());
            Append(sb, "lukStosowany", fokus == null ? string.Empty : (fokus.Applied ? "true" : "false"));
            Append(sb, "lukDopasowanych", fokus == null ? string.Empty : Int(fokus.Matched));
            // KROK 6 (v8): liczba OBOWIAZUJACYCH faktow w snapshocie decyzji - tych, ktore widzialy
            // warunki. Analiza odtwarza ja z linii [PN-FACT] (dzien ustawienia + czas zycia) i porownuje.
            Append(sb, "faktow", swiat == null ? string.Empty
                                              : Int(Core.Blackboard.NarratorBlackboard.FactCountIn(swiat.Facts)));

            // ---- generowanie kandydatow ----
            // Kolumny budowane tutaj, a NIE przez CandidateSet.DataLogFragment(), mimo ze tamta
            // metoda podaje te same liczby. Powod: tamta nazywa swoje pole "kandydatow", a takiej
            // samej nazwy uzywa czesc decyzyjna dla licznika OCENIONYCH w ostatniej rundzie. Te
            // dwie liczby rozjezdzaja sie, gdy silnik gry odmowi odpalenia kandydata (pula maleje
            // miedzy rundami), wiec sa to dwie rozne wielkosci. Jeden klucz wystepujacy w wierszu
            // dwa razy to w Pythonie cicha utrata jednej z nich - stad osobna nazwa "wygenerowanych".
            Append(sb, "wygenerowanych", Int(zbior.Candidates == null ? 0 : zbior.Candidates.Count));
            Append(sb, "budzet", Int(zbior.Budget));
            Append(sb, "akcji", Int(zbior.ActionCount));
            Append(sb, "limitNaAkcje", Int(zbior.PerActionQuota));
            // "przestrzen" jest obowiazkowe obok "wyczerpano": bez mianownika wpis wyczerpano=false
            // nie mowi, czy zbadano 95% czy 5% przestrzeni wariantow, a to jest wprost metryka
            // pokrycia do rozdzialu o ewaluacji.
            Append(sb, "przestrzen", Int(zbior.TotalVariants));
            Append(sb, "wyczerpano", VariantEnumerationStats.Flag(zbior.Exhausted));
            Append(sb, "ucieto", VariantEnumerationStats.Flag(zbior.Truncated));
            Append(sb, "budzetPrzekroczony", VariantEnumerationStats.Flag(zbior.BudgetExceeded));

            // ---- decyzja (rdzen) ----
            string czescDecyzyjna = decision.ToDataFragment();
            if (!string.IsNullOrEmpty(czescDecyzyjna))
            {
                sb.Append("; ").Append(czescDecyzyjna);
            }

            string linia = sb.ToString();
            VerifyFormatOnce(linia);
            WriteData(DataPrefix + linia);
        }

        /// <summary>
        /// Jednorazowa kontrola, czy faktycznie zbudowana linia ma dokladnie te kolumny i w tej
        /// kolejnosci, co deklaracja DataColumns.
        ///
        /// Nie jest to paranoja: trzecia grupa kolumn powstaje w rdzeniu (NarratorDecision), a
        /// deklaracja jest tutaj - kompilator nie ma jak ich zwiazac. Dolozenie kolumny w rdzeniu
        /// bez podbicia wersji tutaj jest dokladnie ta klasa CICHEJ awarii, ktora w kroku 1 dala
        /// pusty katalog klockow: wszystko dziala, log wyglada sensownie, a dane sa przesuniete.
        /// Koszt jest jednorazowy (jedna decyzja na sesje), wiec kontrola moze zostac w Release.
        /// </summary>
        private static void VerifyFormatOnce(string linia)
        {
            if (formatVerified)
            {
                return;
            }
            formatVerified = true;

            string[] pola = linia.Split(';');
            var faktyczne = new string[pola.Length];
            for (int i = 0; i < pola.Length; i++)
            {
                string pole = pola[i].Trim();
                int eq = pole.IndexOf('=');
                faktyczne[i] = eq < 0 ? pole : pole.Substring(0, eq);
            }

            bool zgodne = faktyczne.Length == DataColumns.Length;
            if (zgodne)
            {
                for (int i = 0; i < DataColumns.Length; i++)
                {
                    // Porownanie ORDYNALNE - nazwy kolumn sa identyfikatorami technicznymi,
                    // a porownanie kulturowe potrafi zalezec od ustawien systemu.
                    if (string.CompareOrdinal(faktyczne[i], DataColumns[i]) != 0)
                    {
                        zgodne = false;
                        break;
                    }
                }
            }

            if (zgodne)
            {
                Decision("Format linii [PN-DATA] zgodny z deklaracja: wersja "
                         + DataFormatVersion.ToString(CultureInfo.InvariantCulture)
                         + ", " + DataColumns.Length.ToString(CultureInfo.InvariantCulture) + " kolumn.");
                return;
            }

            Error("NIEZGODNOSC FORMATU [PN-DATA] - dane badawcze z tej sesji beda przesuniete. "
                  + "Zadeklarowano (" + DataColumns.Length.ToString(CultureInfo.InvariantCulture) + "): "
                  + string.Join(",", DataColumns)
                  + " | zbudowano (" + faktyczne.Length.ToString(CultureInfo.InvariantCulture) + "): "
                  + string.Join(",", faktyczne)
                  + " | napraw tablice PNLog.DataColumns i PODBIJ DataFormatVersion.");
        }

        /// <summary>
        /// Ten sam separator, ktorego uzywa NarratorDecision.ToDataFragment: pola rozdziela "; ",
        /// nazwe od wartosci "=". Parser to split(';') plus split('=') i nic wiecej.
        /// </summary>
        private static void Append(StringBuilder sb, string name, string value)
        {
            if (sb.Length > 0)
            {
                sb.Append("; ");
            }
            sb.Append(name).Append('=').Append(value ?? string.Empty);
        }

        private static string Int(int v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Liczba zmiennoprzecinkowa ZAWSZE przez InvariantCulture. Przy polskim locale
        /// separatorem dziesietnym jest przecinek, ktory w Pythonie wywroci float() - i to
        /// nie na jednym wierszu, tylko na calej serii rozgrywek naraz.
        /// </summary>
        private static string Num(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                return string.Empty;
            }
            return v.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }
}
