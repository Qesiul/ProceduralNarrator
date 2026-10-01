using System;
using System.Collections.Generic;
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
    /// Logowanie narratora. [PN] - log czytelny (wolno go przeformatowac). [PN-DATA] - jeden wiersz na decyzje,
    /// stala lista i kolejnosc kolumn (DataColumns, jedno zrodlo prawdy), InvariantCulture - kontrakt z analiza.
    /// Pozostale linie ([PN-LOAD], [PN-EXEC], [PN-FIRED]...) sa poza kontraktem kolumn: pola klucz=wartosc.
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
        /// Wersja formatu [PN-DATA] - pierwsza kolumna kazdego wiersza. PODBIC przy KAZDEJ zmianie DataColumns
        /// (dolozenie, usuniecie, przestawienie kolumny albo zmiana jej znaczenia), zeby analiza nie zmieszala serii.
        /// Historia (szczegoly: dziennik, CLAUDE.md 2.7): v2 brama dwuetapowa; v3 runId; v4 profil i napiecie;
        /// v5 pRunda, liczniki odmow i pytan; v6 tryb, eksperyment, czlony napiecia, kryzys; v7 kontekst i luki;
        /// v8 faktow i konsekwencja (klucz 6 segmentow); v9 styl gracza; v10 brama Anomaly i lustro;
        /// v11 (krok 9, etap L) lustroOdcina (nazwy akcji odcietych lustrem, K1-b) i czasMs (czas decyzji, L-2).
        /// </summary>
        public const int DataFormatVersion = 11;

        /// <summary>
        /// Kolumny [PN-DATA] w obowiazujacej kolejnosci. Grupa decyzyjna powstaje w rdzeniu (NarratorDecision);
        /// rozjazd z ta lista wychwytuje VerifyFormatOnce przy pierwszym wierszu i check_columns.py offline.
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
            // --- krok 7 (v9): styl gracza w chwili decyzji ---
            "stylDni", "stylAktywny", "stylWalka", "stylGospodarka", "stylEkspansja", "stylReaktywnosc",
            "stylMocne", "stylEtykieta", "stylKierunek",
            // --- krok 9 (v10): brama Anomaly w tej turze ---
            "anomaliaSzansa", "anomaliaTura",

            // --- generowanie kandydatow (CandidateSet) ---
            "wygenerowanych", "budzet", "akcji", "limitNaAkcje", "przestrzen",
            "wyczerpano", "ucieto", "budzetPrzekroczony",
            // --- krok 9 (v10): lustro sprawdzen gry; (v11, etap L) jego lista i czas decyzji ---
            "zablokowanychSilnik", "lustroOdcina", "czasMs",

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
            "konsekwencja",
            // --- krok 7 (v9): styl gracza u zwyciezcy ---
            "stylWartosc", "premiaStylu", "stylWPasmie"
        };

        private static bool formatVerified;

        // Eksperyment ("PN: test przyszlych incydentow"): wiersze [PN-DATA] ida do pliku danych z tryb=symulacja,
        // log czytelny do PN_symulacje.log (Verse.Log wylacza sie po 1000 komunikatach procesu), Warn/Error tez do Verse.Log.

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

        // Dane badawcze ida do WLASNEGO pliku obok Player.log: Verse.Log wylacza sie calkowicie po 1000 komunikatach
        // procesu (Log.Notify_MessageReceivedThreadedInternal), a wczytanie zapisu licznika nie zeruje. Warn i Error
        // sa lustrzane w pliku danych ([PN-WARN] / [PN-ERR]).

        private const string DataFileName = "PN_decyzje.log";
        private static StreamWriter dataWriter;
        private static bool dataSinkBroken;

        /// <summary>Pelna sciezka pliku z danymi badawczymi - wypisywana na starcie, zeby dalo sie ja znalezc.</summary>
        public static string DataFilePath
        {
            get { return Path.Combine(GenFilePaths.SaveDataFolderPath, DataFileName); }
        }

        /// <summary>
        /// Jedna linia do pliku danych: otwarcie leniwe, dopisywanie (seria to wiele uruchomien, granica [PN-SESSION]),
        /// AutoFlush (proces bywa zabijany). Blad zapisu - jedno glosne ostrzezenie i koniec prob.
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
                    // UTF8Encoding(false): Encoding.UTF8 dopisuje BOM przy tworzeniu pliku i psuje pierwsza linie w Pythonie.
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
                                int odrzuconychHistorii, int odrzuconychLukow, int odrzuconychFaktow,
                                string styl, int odrzuconychStylu)
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
                           // Krok 7: ksiega stylu gracza (osobny wezel "stylGracza"); wliczona w "odrzuconych".
                           + "; odrzuconychStylu=" + odrzuconychStylu.ToString(CultureInfo.InvariantCulture)
                           + "; wersjaPamieci=" + wersjaPamieci.ToString(CultureInfo.InvariantCulture)
                           + "; profilZapisany=" + (string.IsNullOrEmpty(profilZapisany) ? "?" : profilZapisany)
                           + "; narrator=" + CurrentStorytellerName()
                           // Krok 8 (przeglad S10): warunki gry, bez ktorych rozgrywek nie da sie porownac -
                           // trudnosc, skala zagrozen, duze zagrozenia, zagrozenia intro i scenariusz.
                           + CurrentGameConditionsText()
                           + "; tick=" + CurrentTickText()
                           + "; dzien=" + CurrentDayText()
                           + "; mapy=" + (mapy ?? string.Empty)
                           // Krok 5: otwarte luki "uid:luk#nr:faza,..." - stan lukow w chwili
                           // wczytania; ta sama regula rozwidlenia co dla historii.
                           + "; luki=" + (luki ?? string.Empty)
                           // Krok 6: fakty "uid:klucz=wartosc@dzien/zycie,..." (+ "uid:kolejka@tick") -
                           // stan pamieci faktow w chwili wczytania, ta sama regula rozwidlenia.
                           + "; fakty=" + (fakty ?? string.Empty)
                           // Krok 7: stan stylu "dni:..,dzien:..,aktywny:..,mocne:..,etykieta:.." - poczatek
                           // ciaglosci linii [PN-GRACZ] po rozwidleniu.
                           + "; styl=" + (styl ?? string.Empty);

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

        private const string PlayerPrefix = "[PN-GRACZ] ";

        /// <summary>
        /// Jedna linia na ZAMKNIETA dobe obserwacji stylu gracza (krok 7) - TYLKO do pliku danych, bez
        /// Verse.Log (limit 1000 komunikatow). Pisze ja obserwator w kazdej grze, takze pod innym
        /// narratorem (pole narrator=). Poza kontraktem [PN-DATA].
        ///
        /// POLA: dzien = zamknieta doba; dni = dni w kolejce po jej dopisaniu; styl* = cechy na wspolnej
        /// skali (puste = nieznana, takze w rozgrzewce); c* = profil wzgledny; mocne, etykieta, druga,
        /// margines - puste w rozgrzewce; epizody/oferty/schwytani - liczebnosci w oknie; x*/z* - pomiary
        /// (puste = brak danych w oknie); n*/d* - surowy licznik i mianownik ZAMKNIETEJ doby (liczby
        /// calkowite z ksiegi). Analiza przelicza z nich mocne strony i etykiete niezaleznie.
        /// </summary>
        public static void Player(Core.PlayerModel.StyleDaySample dzien, Core.PlayerModel.StyleReading r)
        {
            if (dzien == null || r == null)
            {
                return;
            }
            var sb = new StringBuilder(1200);
            sb.Append(PlayerPrefix)
              .Append("runId=").Append(CurrentRunId())
              .Append("; tryb=").Append(InExperiment ? "symulacja" : "gra")
              .Append("; narrator=").Append(CurrentStorytellerName())
              .Append("; tick=").Append(CurrentTickText())
              .Append("; dzien=").Append(dzien.Day.ToString(CultureInfo.InvariantCulture))
              .Append("; dni=").Append(r.Days.ToString(CultureInfo.InvariantCulture))
              .Append("; aktywny=").Append(r.Active ? "true" : "false");
            for (int d = 0; d < Core.PlayerModel.StyleDimensions.Count; d++)
            {
                sb.Append("; styl").Append(Core.PlayerModel.StyleDimensions.Name((Core.PlayerModel.StyleDimension)d)).Append('=')
                  .Append(r.Known[d] ? r.Z[d].ToString("0.000", CultureInfo.InvariantCulture) : string.Empty);
            }
            for (int d = 0; d < Core.PlayerModel.StyleDimensions.Count; d++)
            {
                sb.Append("; c").Append(Core.PlayerModel.StyleDimensions.Name((Core.PlayerModel.StyleDimension)d)).Append('=')
                  .Append(r.Known[d] ? r.C[d].ToString("0.000", CultureInfo.InvariantCulture) : string.Empty);
            }
            sb.Append("; mocne=").Append(r.Active ? r.StrongData() : string.Empty)
              .Append("; etykieta=").Append(r.Active ? r.Label ?? string.Empty : string.Empty)
              .Append("; druga=").Append(r.Active ? r.SecondLabel ?? string.Empty : string.Empty)
              .Append("; margines=").Append(r.Active && !string.IsNullOrEmpty(r.SecondLabel)
                                            ? r.Margin.ToString("0.0000", CultureInfo.InvariantCulture) : string.Empty)
              .Append("; epizody=").Append(r.EpisodesInWindow.ToString(CultureInfo.InvariantCulture))
              .Append("; oferty=").Append(r.OffersInWindow.ToString(CultureInfo.InvariantCulture))
              .Append("; schwytani=").Append(r.CapturesInWindow.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < Core.PlayerModel.StyleSignals.Count; i++)
            {
                string n = Core.PlayerModel.StyleSignals.Name((Core.PlayerModel.StyleSignal)i);
                sb.Append("; x").Append(n).Append('=')
                  .Append(r.SignalKnown[i] ? r.SignalX[i].ToString("0.######", CultureInfo.InvariantCulture) : string.Empty)
                  .Append("; z").Append(n).Append('=')
                  .Append(r.SignalKnown[i] ? r.SignalZ[i].ToString("0.000", CultureInfo.InvariantCulture) : string.Empty)
                  .Append("; n").Append(n).Append('=').Append(dzien.Num[i].ToString(CultureInfo.InvariantCulture))
                  .Append("; d").Append(n).Append('=').Append(dzien.Den[i].ToString(CultureInfo.InvariantCulture));
            }
            WriteData(sb.ToString());
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
            Exec(mapId, decyzjaNr, incydent, klucz, status, ostatniPrzed, ostatniPo, frakcja, frakcjaZwiazana, tick,
                 frakcjaZrodlo, tickDecyzji, "-", 0, "-", null);
        }

        /// <summary>
        /// Jak wyzej, plus LIST GRACZA (krok 8, decyzje K8-4 i K8-6):
        ///   list=         dopisany | odroczony | brak | wylaczony | niewykonane | symulacja | pozno | pustyOpis | blad;
        ///   nowychListow= ile listow przybylo miedzy migawka a wznowieniem iteratora;
        ///   warianty=     slad TextComposer ("klocek:wariant" po przecinku; "-" = tekst nie liczony);
        ///   tekstListu=   zlozony opis (jedna linia) - OSTATNIE pole, do konca linii, bo tekst moze
        ///                 zawierac srednik. Pusty, gdy tekstu nie liczono.
        /// </summary>
        public static void Exec(int mapId, int decyzjaNr, string incydent, string klucz, string status,
                                int ostatniPrzed, int ostatniPo, string frakcja, string frakcjaZwiazana, int tick,
                                string frakcjaZrodlo, int tickDecyzji, string list, int nowychListow,
                                string warianty, string tekstListu)
        {
            Exec(mapId, decyzjaNr, incydent, klucz, status, ostatniPrzed, ostatniPo, frakcja, frakcjaZwiazana, tick,
                 frakcjaZrodlo, tickDecyzji, list, nowychListow, warianty, tekstListu, 0);
        }

        /// <summary>Jak wyzej; wymuszone = 0 albo numer akcji debugowej "PN: wymus akcje" (etap L, L-3) - bez wiersza [PN-DATA].</summary>
        public static void Exec(int mapId, int decyzjaNr, string incydent, string klucz, string status,
                                int ostatniPrzed, int ostatniPo, string frakcja, string frakcjaZwiazana, int tick,
                                string frakcjaZrodlo, int tickDecyzji, string list, int nowychListow,
                                string warianty, string tekstListu, int wymuszone)
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
                      + "; tickDecyzji=" + tickDecyzji.ToString(CultureInfo.InvariantCulture)
                      + "; list=" + (string.IsNullOrEmpty(list) ? "-" : list)
                      + "; nowychListow=" + nowychListow.ToString(CultureInfo.InvariantCulture)
                      + "; warianty=" + (string.IsNullOrEmpty(warianty) ? "-" : JednaLinia(warianty))
                      + "; wymuszone=" + wymuszone.ToString(CultureInfo.InvariantCulture)
                      + "; tekstListu=" + JednaLinia(tekstListu));
        }

        private const string FiredPrefix = "[PN-FIRED] ";

        /// <summary>Pola linii [PN-FIRED]; -1 / null = brak pomiaru (puste pole).</summary>
        internal sealed class FiredFields
        {
            public int Tick;
            public string Cel;
            public int Mapa = -1;
            public bool Dom;
            public string Incydent;
            public string Kategoria;
            public bool Nasz;
            public string Kontekst;
            public int Kolonisci = -1;
            public int NaMapie = -1;
            public int Powaleni = -1;
            public int Zagrozenie = -1;
            public float Bogactwo = -1f;
            public float BogactwoWzgl = -1f;
            public float Punkty = -1f;
            public int Opoznienie;
            public int Pora = -1;
            public int Noc = -1;
            public int BogactwoWiek = -1;
            public string Frakcja;
            public string FrakcjaDef;
            public List<string> Etykiety;
            public bool ListyWspolne;
            public int Wymuszone;
        }

        /// <summary>
        /// Odpalenie incydentu - KAZDEGO narratora, w KAZDEJ grze (krok 8, K8-2; etap L kroku 9). Pola:
        ///   narrator=, tick=/dzien= (chwila odpalenia), cel= (map:uid | world | caravan:id), mapa=, dom=, incydent=, kategoria=;
        ///   pn= 1 = zdarzenie naszego compa (takze "PN: wymus akcje");
        ///   kontekst= przed (tick 999 mod 1000) | po (chwila wykrycia) | - - dotyczy WSZYSTKICH pol kontekstu:
        ///   kolonisci=, kolonisciNaMapie=, powaleni=, zagrozenie=, pora= 0-3, noc= 0/1 (jasnosc &lt;= 0,3),
        ///   bogactwo=, bogactwoWzgl= z pol gry bez przeliczania, bogactwoWiek= ticki od ich liczenia, punkty= tylko bez
        ///   przeliczania; opoznienie= ticki od odpalenia do wykrycia;
        ///   frakcja=/frakcjaDef= tylko napady (StoryState.lastRaidFaction); wymuszone= 0 albo numer akcji "PN: wymus akcje";
        ///   listow=, listyWspolne= (kilka odpalen w ticku);
        ///   listy= etykiety nowych listow z ticku, " | " miedzy nimi - OSTATNIE pole, do konca linii.
        /// </summary>
        internal static void Fired(FiredFields z)
        {
            int listow = z.Etykiety == null ? 0 : z.Etykiety.Count;
            var etykiety = new List<string>(listow);
            for (int i = 0; i < listow; i++)
            {
                etykiety.Add(JednaLinia(z.Etykiety[i]));
            }
            WriteData(FiredPrefix
                      + "runId=" + CurrentRunId()
                      + "; narrator=" + CurrentStorytellerName()
                      + "; tick=" + z.Tick.ToString(CultureInfo.InvariantCulture)
                      + "; dzien=" + (z.Tick / 60000f).ToString("0.000", CultureInfo.InvariantCulture)
                      + "; cel=" + (z.Cel ?? "-")
                      + "; mapa=" + z.Mapa.ToString(CultureInfo.InvariantCulture)
                      + "; dom=" + (z.Dom ? "true" : "false")
                      + "; incydent=" + (z.Incydent ?? "-")
                      + "; kategoria=" + (z.Kategoria ?? "-")
                      + "; pn=" + (z.Nasz ? "1" : "0")
                      + "; kontekst=" + (z.Kontekst ?? "-")
                      + "; kolonisci=" + Liczba(z.Kolonisci)
                      + "; kolonisciNaMapie=" + Liczba(z.NaMapie)
                      + "; powaleni=" + Liczba(z.Powaleni)
                      + "; zagrozenie=" + Liczba(z.Zagrozenie)
                      + "; bogactwo=" + (z.Bogactwo < 0f ? string.Empty : z.Bogactwo.ToString("0", CultureInfo.InvariantCulture))
                      + "; bogactwoWzgl=" + (z.BogactwoWzgl < 0f ? string.Empty : z.BogactwoWzgl.ToString("0.000", CultureInfo.InvariantCulture))
                      + "; punkty=" + (z.Punkty < 0f ? string.Empty : z.Punkty.ToString("0.0", CultureInfo.InvariantCulture))
                      + "; opoznienie=" + z.Opoznienie.ToString(CultureInfo.InvariantCulture)
                      + "; pora=" + Liczba(z.Pora)
                      + "; noc=" + Liczba(z.Noc)
                      + "; bogactwoWiek=" + Liczba(z.BogactwoWiek)
                      + "; frakcja=" + (string.IsNullOrEmpty(z.Frakcja) ? "-" : z.Frakcja)
                      + "; frakcjaDef=" + (string.IsNullOrEmpty(z.FrakcjaDef) ? "-" : z.FrakcjaDef)
                      + "; wymuszone=" + z.Wymuszone.ToString(CultureInfo.InvariantCulture)
                      + "; listow=" + listow.ToString(CultureInfo.InvariantCulture)
                      + "; listyWspolne=" + (z.ListyWspolne ? "tak" : "nie")
                      + "; listy=" + string.Join(" | ", etykiety.ToArray()));
        }

        private const string ListPrefix = "[PN-LIST] ";

        /// <summary>
        /// Nowy list gracza - KAZDY, kazdego narratora (poprawka metodologii etapu L: widoczne formy porownywane jedna zasada
        /// normalizacji dla wszystkich). incydenty= odpalenia wykryte w tym ticku ("-" = list bez odpalenia: pozniejszy,
        /// zadanie); wymuszone= 0 albo numer akcji "PN: wymus akcje"; typ= LetterDef; frakcja= nazwa relatedFaction; pionki= imiona pionkow
        /// wskazanych przez list (pelne, krotkie, imie, nazwisko; " | " miedzy nimi) - do znacznikow w analizie; tytul= bez
        /// srednikow; tresc= pelny tekst bez znacznikow formatowania - OSTATNIE pole, do konca linii.
        /// </summary>
        internal static void List(int tick, int mapa, string incydenty, int wymuszone, string typ, string frakcja,
                                  List<string> pionki, string tytul, string tresc)
        {
            var imiona = new List<string>();
            if (pionki != null)
            {
                for (int i = 0; i < pionki.Count; i++)
                {
                    imiona.Add(BezSrednika(pionki[i]));
                }
            }
            WriteData(ListPrefix
                      + "runId=" + CurrentRunId()
                      + "; narrator=" + CurrentStorytellerName()
                      + "; tick=" + tick.ToString(CultureInfo.InvariantCulture)
                      + "; dzien=" + (tick / 60000f).ToString("0.000", CultureInfo.InvariantCulture)
                      + "; mapa=" + mapa.ToString(CultureInfo.InvariantCulture)
                      + "; incydenty=" + (string.IsNullOrEmpty(incydenty) ? "-" : incydenty)
                      + "; wymuszone=" + wymuszone.ToString(CultureInfo.InvariantCulture)
                      + "; typ=" + (typ ?? "?")
                      + "; frakcja=" + (string.IsNullOrEmpty(frakcja) ? "-" : BezSrednika(frakcja))
                      + "; pionki=" + string.Join(" | ", imiona.ToArray())
                      + "; tytul=" + BezSrednika(tytul)
                      + "; tresc=" + JednaLinia(tresc));
        }

        /// <summary>Pole w srodku linii: jedna linia i bez srednikow (separator pol).</summary>
        private static string BezSrednika(string s)
        {
            return JednaLinia(s).Replace(';', ',');
        }

        private const string DayPrefix = "[PN-DZIEN] ";
        private const string ColonistPrefix = "[PN-KOLONISTA] ";
        private const string PerfPrefix = "[PN-PERF] ";
        private const string EvalPrefix = "[PN-EVAL] ";

        /// <summary>Pola linii [PN-DZIEN]; -1 / NaN / null = brak pomiaru (puste pole).</summary>
        internal sealed class DayFields
        {
            public int Dzien;
            public int Mapa;
            public int Kolonisci = -1;
            public int NaMapie = -1;
            public int Powaleni = -1;
            public int Zagrozenie = -1;
            public float Bogactwo = -1f;
            public int BogactwoWiek = -1;
            public float BogactwoWzgl = -1f;
            public float Punkty = -1f;
            public int Pora = -1;
            public float Temperatura = float.NaN;
            public string Warunki;
            public int Monolit = -1;
            public string MonolitDef;
            public float Zywnosc = -1f;
            public float Nastroj = float.NaN;
            public float Adaptacja = float.NaN;
            public int Napadow = -1;
            public int ThreatBig = -1;
            public int Poleglych = -1;
        }

        /// <summary>
        /// Stan mapy domowej na koniec doby (krok 9, etap L, decyzja L-1) - kazdy narrator, pierwszy tick nastepnej doby.
        /// dzien= doba zamknieta (tick div 60000 - 1); warunki= aktywne warunki gry mapy i swiata ("-" = zadne);
        /// monolit= poziom (puste bez Anomaly); zywnosc= suma wartosci odzywczej jedzenia dla ludzi; nastroj= sredni nastroj
        /// wolnych kolonistow na mapie; adaptacja= dni adaptacji narratora gry; napadow/threatBig/poleglych = StatsRecord.
        /// </summary>
        internal static void Day(DayFields d)
        {
            WriteData(DayPrefix
                      + "runId=" + CurrentRunId()
                      + "; narrator=" + CurrentStorytellerName()
                      + "; tick=" + CurrentTickText()
                      + "; dzien=" + d.Dzien.ToString(CultureInfo.InvariantCulture)
                      + "; mapa=" + d.Mapa.ToString(CultureInfo.InvariantCulture)
                      + "; kolonisci=" + Liczba(d.Kolonisci)
                      + "; kolonisciNaMapie=" + Liczba(d.NaMapie)
                      + "; powaleni=" + Liczba(d.Powaleni)
                      + "; zagrozenie=" + Liczba(d.Zagrozenie)
                      + "; bogactwo=" + (d.Bogactwo < 0f ? string.Empty : d.Bogactwo.ToString("0", CultureInfo.InvariantCulture))
                      + "; bogactwoWiek=" + Liczba(d.BogactwoWiek)
                      + "; bogactwoWzgl=" + (d.BogactwoWzgl < 0f ? string.Empty : d.BogactwoWzgl.ToString("0.000", CultureInfo.InvariantCulture))
                      + "; punkty=" + (d.Punkty < 0f ? string.Empty : d.Punkty.ToString("0.0", CultureInfo.InvariantCulture))
                      + "; pora=" + Liczba(d.Pora)
                      + "; temperatura=" + Num1(d.Temperatura)
                      + "; warunki=" + (string.IsNullOrEmpty(d.Warunki) ? "-" : d.Warunki)
                      + "; monolit=" + Liczba(d.Monolit)
                      + "; monolitDef=" + (d.MonolitDef ?? string.Empty)
                      + "; zywnosc=" + (d.Zywnosc < 0f ? string.Empty : Num1(d.Zywnosc))
                      + "; nastroj=" + Num(d.Nastroj)
                      + "; adaptacja=" + Num(d.Adaptacja)
                      + "; napadow=" + Liczba(d.Napadow)
                      + "; threatBig=" + Liczba(d.ThreatBig)
                      + "; poleglych=" + Liczba(d.Poleglych));
        }

        /// <summary>
        /// Zmiana skladu wolnych kolonistow (etap L, L-5): start (kotwica: pionek=-1, liczebnosc) | dolaczyl | zginal |
        /// porwany | uwieziony | zdziczal | odszedl | inne. mapa = ostatnia znana (-1 = karawana, kapsula).
        /// </summary>
        internal static void Colonist(int tick, Core.Evaluation.RosterEvent e)
        {
            WriteData(ColonistPrefix
                      + "runId=" + CurrentRunId()
                      + "; narrator=" + CurrentStorytellerName()
                      + "; tick=" + tick.ToString(CultureInfo.InvariantCulture)
                      + "; dzien=" + (tick / 60000f).ToString("0.000", CultureInfo.InvariantCulture)
                      + "; zdarzenie=" + (e.Kind ?? "?")
                      + "; pionek=" + e.Id.ToString(CultureInfo.InvariantCulture)
                      + "; mapa=" + e.Map.ToString(CultureInfo.InvariantCulture)
                      + "; liczebnosc=" + e.CountAfter.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Stan mapy domowej dla regul RN (etap L, PLAN_EWALUACJI.md 5): start | zmiana (ktorykolwiek z kryzys/zagrozenie/pusta)
        /// | koniec (mapa przestala byc domem, pola stanu puste). Stan obowiazuje od linii do nastepnej linii tej mapy.
        /// </summary>
        internal static void State(int tick, int mapa, string zdarzenie, int naMapie, int powaleni, Core.Evaluation.RuleState s)
        {
            WriteData(StatePrefix + StateHead(tick, mapa, zdarzenie)
                      + "; kolonisciNaMapie=" + naMapie.ToString(CultureInfo.InvariantCulture)
                      + "; powaleni=" + powaleni.ToString(CultureInfo.InvariantCulture)
                      + "; zagrozenie=" + (s.Threat ? "1" : "0")
                      + "; kryzys=" + (s.Crisis ? "1" : "0")
                      + "; pusta=" + (s.Empty ? "1" : "0"));
        }

        internal static void StateEnd(int tick, int mapa)
        {
            WriteData(StatePrefix + StateHead(tick, mapa, Core.Evaluation.RuleStateTracker.End)
                      + "; kolonisciNaMapie=; powaleni=; zagrozenie=; kryzys=; pusta=");
        }

        private const string StatePrefix = "[PN-STAN] ";

        private static string StateHead(int tick, int mapa, string zdarzenie)
        {
            return "runId=" + CurrentRunId()
                   + "; narrator=" + CurrentStorytellerName()
                   + "; tick=" + tick.ToString(CultureInfo.InvariantCulture)
                   + "; dzien=" + (tick / 60000f).ToString("0.000", CultureInfo.InvariantCulture)
                   + "; mapa=" + mapa.ToString(CultureInfo.InvariantCulture)
                   + "; zdarzenie=" + zdarzenie;
        }

        /// <summary>Koszt czasu za zamknieta dobe (etap L, L-2): fragment pol sklada PerfMonitor.</summary>
        internal static void Perf(int dzien, string pola)
        {
            WriteData(PerfPrefix
                      + "runId=" + CurrentRunId()
                      + "; narrator=" + CurrentStorytellerName()
                      + "; tick=" + CurrentTickText()
                      + "; dzien=" + dzien.ToString(CultureInfo.InvariantCulture)
                      + (pola ?? string.Empty));
        }

        /// <summary>
        /// Start gry ewaluacyjnej (etap L, L-4): nowy runId od tej linii; pola mapy/luki/fakty/styl = stan SPRZED
        /// wyczyszczenia pamieci i stylu. Analiza traktuje linie jako poczatek nowej rozgrywki.
        /// </summary>
        internal static void Eval(string runIdPoprzedni, string runIdNowy, string etykieta, string profil, bool profilWymuszony,
                                  string mapy, string luki, string fakty, string styl)
        {
            WriteData(EvalPrefix
                      + "runIdPoprzedni=" + (string.IsNullOrEmpty(runIdPoprzedni) ? "?" : runIdPoprzedni)
                      + "; runId=" + runIdNowy
                      + "; etykieta=" + etykieta
                      + "; narrator=" + CurrentStorytellerName()
                      + "; profil=" + (string.IsNullOrEmpty(profil) ? "-" : profil)
                      + "; profilWymuszony=" + (profilWymuszony ? "tak" : "nie")
                      + "; tick=" + CurrentTickText()
                      + "; dzien=" + CurrentDayText()
                      + CurrentGameConditionsText()
                      + "; mapy=" + (mapy ?? string.Empty)
                      + "; luki=" + (luki ?? string.Empty)
                      + "; fakty=" + (fakty ?? string.Empty)
                      + "; styl=" + (styl ?? string.Empty));
        }

        private static string Num1(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v) ? string.Empty : v.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private const string CachePrefix = "[PN-CACHE] ";

        /// <summary>
        /// KOLIZJA CACHE'U CanFireNow (krok 8, dlug 8): przed naszym pierwszym pytaniem o incydent w turze
        /// w cache'u gry byl juz werdykt Z TEGO TICKU od innego pytajacego. Izolacja i tak liczy werdykt dla
        /// naszych parametrow i przywraca cudzy po naszej turze - linia jest POMIAREM, jak czesto bez izolacji
        /// narrator dostawalby cudzy werdykt (rozny=true: dostalby inny niz wlasny).
        /// </summary>
        public static void Cache(int tick, int mapa, string incydent, bool werdyktGry, bool nasz)
        {
            WriteData(CachePrefix
                      + "runId=" + CurrentRunId()
                      + "; tryb=" + (InExperiment ? "symulacja" : "gra")
                      + "; eksperyment=" + (InExperiment ? experimentId + "/" + experimentArm : string.Empty)
                      + "; tick=" + tick.ToString(CultureInfo.InvariantCulture)
                      + "; mapa=" + mapa.ToString(CultureInfo.InvariantCulture)
                      + "; incydent=" + (incydent ?? "-")
                      + "; werdyktGry=" + (werdyktGry ? "true" : "false")
                      + "; nasz=" + (nasz ? "true" : "false")
                      + "; rozny=" + (werdyktGry != nasz ? "true" : "false"));
        }

        /// <summary>Liczba albo puste pole dla braku pomiaru (-1).</summary>
        private static string Liczba(int v)
        {
            return v < 0 ? string.Empty : v.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Tekst w jednej linii pliku danych: kazdy znak sterujacy (takze U+0085) oraz separatory U+2028/U+2029
        /// zamienione na spacje - parser w Pythonie (str.splitlines) cial by na nich linie (przeglad S10).
        /// Nazwa frakcji pochodzi z gry, wiec o jej znakach nie decydujemy.
        /// </summary>
        private static string JednaLinia(string tekst)
        {
            if (string.IsNullOrEmpty(tekst))
            {
                return string.Empty;
            }
            var sb = new StringBuilder(tekst.Length);
            for (int i = 0; i < tekst.Length; i++)
            {
                char c = tekst[i];
                sb.Append(char.IsControl(c) || c == '\u2028' || c == '\u2029' ? ' ' : c);
            }
            return sb.ToString();
        }

        private static string CurrentDayText()
        {
            return Current.Game != null && Find.TickManager != null
                ? (Find.TickManager.TicksGame / 60000f).ToString("0.000", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        /// <summary>
        /// Warunki gry do [PN-LOAD] (krok 8, przeglad S10): trudnosc, skala zagrozen, duze zagrozenia,
        /// zagrozenia intro i scenariusz. Ewaluacja porownuje narratorow tylko przy tych samych warunkach.
        /// </summary>
        private static string CurrentGameConditionsText()
        {
            // Pelna nazwa: w przestrzeni ProceduralNarrator.Integration "Storyteller" to nasza podprzestrzen.
            RimWorld.Storyteller st = Current.Game == null ? null : Current.Game.storyteller;
            string wynik;
            if (st == null || st.difficulty == null)
            {
                wynik = "; trudnosc=?; skalaZagrozen=; duzeZagrozenia=; zagrozeniaIntro=";
            }
            else
            {
                wynik = "; trudnosc=" + (st.difficultyDef == null ? "?" : st.difficultyDef.defName)
                        + "; skalaZagrozen=" + st.difficulty.threatScale.ToString("0.###", CultureInfo.InvariantCulture)
                        + "; duzeZagrozenia=" + (st.difficulty.allowBigThreats ? "tak" : "nie")
                        + "; zagrozeniaIntro=" + (st.difficulty.allowIntroThreats ? "tak" : "nie");
            }
            string scen = Current.Game == null || Find.Scenario == null ? "?" : (Find.Scenario.name ?? "?");
            return wynik + "; scenariusz=" + JednaLinia(scen).Replace(';', ',').Replace('=', '-');
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
                                TensionReading tension, CrisisReading crisis, float czasMs)
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
            // KROK 7 (v9): styl gracza w chwili decyzji. Wartosci i reguly pustych pol liczy rdzen
            // (StyleFocus.Preamble, TEST 14n): wszystko puste, gdy warstwy stylu nie ma (ramie S, styl
            // wylaczony, bezpiecznik); w rozgrzewce puste cechy, mocne strony i etykieta, kierunek jest.
            Core.PlayerModel.StyleFocus styl = context == null ? null : context.StyleFocus;
            Core.PlayerModel.StyleFocus.PreambleColumns ks = styl == null
                ? new Core.PlayerModel.StyleFocus.PreambleColumns()
                : styl.Preamble();
            Append(sb, "stylDni", ks.Dni);
            Append(sb, "stylAktywny", ks.Aktywny);
            Append(sb, "stylWalka", ks.Z[0]);
            Append(sb, "stylGospodarka", ks.Z[1]);
            Append(sb, "stylEkspansja", ks.Z[2]);
            Append(sb, "stylReaktywnosc", ks.Z[3]);
            Append(sb, "stylMocne", ks.Mocne);
            Append(sb, "stylEtykieta", ks.Etykieta);
            Append(sb, "stylKierunek", ks.Kierunek);
            // KROK 9 (v10): brama Anomaly. Szansa PUSTA bez DLC (gra jej wtedy nie liczy - brak pomiaru, nie zero);
            // strona to symbol enuma (Regular/Anomaly), bez DLC zawsze Regular (AnomalyGate.Draw).
            Append(sb, "anomaliaSzansa", swiat == null || !swiat.AnomalyActive ? string.Empty : Num(swiat.AnomalyIncidentChance));
            Append(sb, "anomaliaTura", zbior.AnomalySide.ToString());

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
            // KROK 9 (v10): akcje, ktore przeszly tag i wlasne warunki, a odpadly przez lustro sprawdzen gry. PUSTE,
            // gdy lustro w tej turze nie dzialalo (bezpiecznik) - wtedy zero znaczyloby "nic nie zablokowano".
            Append(sb, "zablokowanychSilnik", swiat != null && swiat.EngineMirrorActive ? Int(zbior.EngineBlocked) : string.Empty);
            // v11 (etap L): pelna lista odcietych lustrem ("-" = nic, puste = lustro nie dzialalo) i czas decyzji w ms
            // (od wejscia w decyzje po bramce MTB do tego wiersza; puste = brak pomiaru).
            Append(sb, "lustroOdcina", Core.Composition.EngineMirror.DataColumn(swiat));
            Append(sb, "czasMs", czasMs < 0f || float.IsNaN(czasMs) ? string.Empty
                                                             : czasMs.ToString("0.###", CultureInfo.InvariantCulture));

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
