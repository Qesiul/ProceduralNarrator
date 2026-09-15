using System;
using System.Globalization;
using System.IO;
using System.Text;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
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
        public const int DataFormatVersion = 4;

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
            "wersjaLogu", "runId", "profil", "tick", "dzien", "decyzjaNr", "mapa",
            "histWpisow", "histDecyzji",
            "napiecie", "intencja", "docelowaMoc", "odmowSilnika",

            // --- generowanie kandydatow (CandidateSet) ---
            "wygenerowanych", "budzet", "akcji", "limitNaAkcje", "przestrzen",
            "wyczerpano", "ucieto", "budzetPrzekroczony",

            // --- decyzja: scoring i polityka wyboru (NarratorDecision.ToDataFragment) ---
            "decyzja", "wybor", "klucz", "wynik", "p", "best", "pasmo",
            "kandydatow", "zawetowanych", "odrzuconeCutoff", "odrzuconePasmo",
            "wSoftmaksie", "losowan",
            "powodPass", "passWynik", "pBrama", "passStlumiony", "gestosc", "seriaPass",
            "contextFit", "freshness", "dramaticContrast", "intentAlignment",
            "passRestraint", "passBaseline", "passIntent"
        };

        private static bool formatVerified;

        // =====================================================================================
        //  UJSCIE DANYCH BADAWCZYCH - WLASNY PLIK, NIE Player.log
        // =====================================================================================
        //  Verse.Log ma twardy limit: Log.StopLoggingAtMessageCount = 1000 (zweryfikowane
        //  w Assembly-CSharp 1.5.4063). Po tysiacu wiadomosci RimWorld PRZESTAJE LOGOWAC
        //  CALKOWICIE - bez bledu, bez ostrzezenia. Player.log jest przy tym wspoldzielony ze
        //  WSZYSTKIMI modami, a niektore (np. PickUpAndHaul) zaczynaja od wlasnego logspamu.
        //
        //  Przy szesciu liniach [PN] na decyzje limit wyczerpuje sie po okolo 150 decyzjach,
        //  czyli po niecalym roku gry. Dokladnie wtedy, gdy dane zaczynaja byc ciekawe, seria
        //  urywa sie po cichu, a wykres w pracy pokazuje "koniec danych" tam, gdzie skonczyl
        //  sie budzet logu, a nie rozgrywka. To jest cicha awaria w danych badawczych, czyli
        //  najgorszy rodzaj - nie wywala gry i nie zostawia sladu.
        //
        //  Dlatego strumien MASZYNOWY idzie do wlasnego pliku obok Player.log, a Verse.Log
        //  dostaje wylacznie linie CZYTELNE dla czlowieka, ktorych utrata po limicie nic nie psuje.
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
                    // z doklejonym ﻿, wiec startswith('[PN-SESSION]') nie trafialo.
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
            Log.Message(Prefix + message);
        }

        public static void Warn(string message)
        {
            Log.Warning(Prefix + message);
        }

        public static void Error(string message)
        {
            Log.Error(Prefix + message);
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
        /// defName profilu przydzielonego tej rozgrywce. Czytany za kazdym razem z tego samego
        /// powodu co runId: PNLog zyje tak dlugo jak proces, a rozgrywka moze sie zmienic.
        /// </summary>
        private static string CurrentProfileId()
        {
            if (Current.Game == null)
            {
                return string.Empty;
            }

            NarratorMemoryComponent pamiec = Current.Game.GetComponent<NarratorMemoryComponent>();
            return pamiec == null ? string.Empty : pamiec.ProfileId;
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
        ///      [PN-DATA] po tej linii naleza do tej samej rozgrywki co przed zapisem, mimo ze
        ///      dzieli je nowa linia [PN-SESSION].
        ///
        /// Nie rusza kontraktu kolumn [PN-DATA], wiec nie wymaga wlasnego numeru wersji -
        /// parser, ktory jej nie zna, po prostu ja pominie po prefiksie.
        /// </summary>
        public static void Load(string runId, string zrodlo, string profil, int mapCount, int wpisow,
                                int decyzji, int odrzuconych, int wersjaPamieci)
        {
            string linia = LoadPrefix
                           + "runId=" + (string.IsNullOrEmpty(runId) ? "?" : runId)
                           + "; zrodlo=" + zrodlo
                           + "; profil=" + (string.IsNullOrEmpty(profil) ? "?" : profil)
                           + "; map=" + mapCount.ToString(CultureInfo.InvariantCulture)
                           + "; wpisow=" + wpisow.ToString(CultureInfo.InvariantCulture)
                           + "; decyzji=" + decyzji.ToString(CultureInfo.InvariantCulture)
                           + "; odrzuconych=" + odrzuconych.ToString(CultureInfo.InvariantCulture)
                           + "; wersjaPamieci=" + wersjaPamieci.ToString(CultureInfo.InvariantCulture);

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
                opis = "Wczytano zapis BEZ pamieci narratora (runId=" + runId
                       + "). To normalne dla zapisu sprzed kroku 6 albo dla moda dolozonego do "
                       + "trwajacej rozgrywki - narracja zaczyna sie od tego momentu.";
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
        /// Jedna linia maszynowa na jedna decyzje narratora - takze na decyzje o ciszy.
        ///
        /// Kolumny czynnikow ZDARZENIOWYCH dla wiersza PASS zostaja PUSTE, a nie zerowe.
        /// Zero jest wartoscia, pustka jest brakiem pomiaru: wpisanie tam 0.5 wstrzyknelo by
        /// do danych badawczych pomiar, ktorego nie bylo, i sciagnelo srednie w kierunku
        /// udzialu PASS-ow. W Pandas pusta kolumna to NaN i wypada z agregacji sama.
        /// (Realizuje to NarratorDecision.ToDataFragment; tutaj tylko o tym nie zapominamy.)
        /// </summary>
        public static void Data(int tick, int mapId, DecisionContext context, CandidateSet candidates,
                                NarratorDecision decision, int engineRefusals)
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
            Append(sb, "intencja", context == null ? string.Empty : context.Intent.ToString());
            Append(sb, "docelowaMoc", Num(context == null ? 0f : context.TargetIntensity));
            Append(sb, "odmowSilnika", Int(engineRefusals));

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
