using System;
using System.Collections.Generic;
using System.Globalization;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Tension;
using ProceduralNarrator.Core.Util;
using ProceduralNarrator.Integration.Defs;
using Verse;

namespace ProceduralNarrator.Integration.Persistence
{
    /// <summary>
    /// TRWALY MAGAZYN STANU NARRATORA (krok 6, wycinek: sama trwalosc pamieci zdarzen).
    ///
    /// Dlaczego GameComponent, a nie pole w StorytellerComp_Generative: comp NIE PRZEZYWA
    /// wczytania. Zweryfikowane dekompilacja Assembly-CSharp 1.5.4063 - Storyteller.ExposeData
    /// w fazie ResolvingCrossRefs wola InitializeStorytellerComps(), a ta robi
    ///     Activator.CreateInstance(def.comps[i].compClass)
    /// czyli buduje KAZDY comp od zera z Defa. StorytellerComp nie implementuje IExposable
    /// i nie ma zadnego wlasnego mechanizmu trwalosci. Kazde pole instancyjne compa jest wiec
    /// kasowane przy kazdym wczytaniu ORAZ przy kazdej zmianie narratora w trakcie gry.
    ///
    /// Skutek braku tej klasy byl niewidoczny w logu i przez to grozny: po wczytaniu swiezosc
    /// wracala na maksimum dla wszystkiego, DaysSinceLastEvent gubil sie (wiec Cond_CalmPeriod
    /// przepuszczal "okres spokoju" tuz po napadzie), a gestosc PASS tracila historie. Dane
    /// z rozgrywki granej na raty byly NIEPELNE W SPOSOB NIEODROZNIALNY OD POPRAWNYCH.
    ///
    /// REJESTRACJA JEST AUTOMATYCZNA, BEZ ZADNEGO XML. Game.FillComponents() chodzi po
    /// typeof(GameComponent).AllSubclassesNonAbstract() i wola Activator.CreateInstance(typ, this).
    /// Stad wymagany ksztalt konstruktora - patrz nizej.
    /// </summary>
    public class NarratorMemoryComponent : GameComponent
    {
        /// <summary>
        /// Wersja formatu zapisu pamieci. PODBIC przy zmianie ksztaltu MapMemoryRecord albo
        /// kodowania linii w EventHistoryEntry.Encode(). Numer jest zapisywany i porownywany
        /// przy wczytaniu - rozjazd daje ostrzezenie, a nie ciche przemilczenie.
        /// </summary>
        private const int MemoryFormatVersion = 1;

        /// <summary>
        /// Pamiec zdarzen narratora, OSOBNA DLA KAZDEJ MAPY (klucz: Map.uniqueID).
        ///
        /// Dlaczego nie jedna wspolna: StorytellerComp istnieje JEDEN na cala gre, nie jeden
        /// na mape. Przy drugiej kolonii wspolny bufor psulby trzy rzeczy naraz, kazda po cichu:
        ///   swiezosc - zdarzenie na kolonii A "zuzywaloby" temat dla kolonii B, wiec narrator
        ///              unikalby na drugiej mapie tego, czego uzyl na pierwszej, bez powodu,
        ///   kontrast - rytm mieszalby dwa niezalezne ciagi, wiec "po serii katastrof" znaczyloby
        ///              katastrofy w zupelnie innym miejscu,
        ///   gestosc PASS - licznik decyzji roslby dwa razy szybciej, wiec narrator uznalby, ze
        ///              jest gesto, i zaczalby milczec na OBU mapach.
        /// Kazda kolonia prowadzi wlasna narracje, wiec kazda ma wlasna pamiec.
        ///
        /// Map.uniqueID, a NIE Map.Index: indeks jest pozycja na liscie map i przesuwa sie,
        /// gdy gracz porzuci kolonie, wiec pamiec przeskoczylaby wtedy na inna mape. uniqueID
        /// jest przydzielane raz w MapGenerator.GenerateMap z monotonicznego, serializowanego
        /// licznika UniqueIDsManager.nextMapID i NIGDY nie jest recyklingowane - wiec martwy
        /// wpis nie moze "ozyc" na nowej kolonii.
        /// </summary>
        private Dictionary<int, EventHistory> histories = new Dictionary<int, EventHistory>();

        /// <summary>
        /// Bufor serializacji - WYLACZNIE do rozmowy ze Scribe'em, nigdy do odczytu w trakcie gry.
        /// Zrodlem prawdy jest histories; ten slownik zyje tylko przez czas zapisu albo wczytania.
        /// </summary>
        private Dictionary<int, MapMemoryRecord> zapis = new Dictionary<int, MapMemoryRecord>();

        /// <summary>
        /// Identyfikator ROZGRYWKI (nie sesji gry). Nadawany raz, przy pierwszym FinalizeInit,
        /// i od tego momentu jedzie w zapisie.
        ///
        /// Po co: linia [PN-SESSION] w pliku danych oznacza URUCHOMIENIE PROCESU, a nie rozgrywke.
        /// Jedna rozgrywka grana na raty rozpada sie wiec w danych na N nieodroznialnych sesji
        /// i nie da sie jej zszyc w Pythonie. runId jest kolumna w kazdym wierszu [PN-DATA],
        /// wiec grupowanie po rozgrywce staje sie trywialne.
        ///
        /// NIE WCHODZI DO ZADNEGO ZIARNA ANI WZORU SCORINGU - jest wylacznie etykieta danych.
        /// Wymaganie odtwarzalnosci z sekcji 11 zostaje przez to nietkniete. Z tego samego powodu
        /// uzywamy Guid, a nie Verse.Rand: Rand ruszylby globalny generator gry.
        /// </summary>
        private string runId = string.Empty;

        /// <summary>
        /// OSOBOWOSC NARRATORA przydzielona tej rozgrywce. Losowana RAZ, w StartedNewGame(),
        /// i od tego momentu jedzie w zapisie.
        ///
        /// Dlaczego nie wybiera jej gracz: menu wyboru narratora jest dokladnie tym modelem,
        /// ktory ta praca krytykuje - Cassandra, Phoebe i Randy to trzy pozycje na liscie,
        /// a cala ich osobowosc to siedem liczb sterujacych tempem. Tutaj profil jest stanem
        /// WEWNETRZNYM systemu: gracz go nie wybiera i nie zna, wiec kolejna rozgrywka to nie
        /// tylko inne zdarzenia, ale inny narrator. To wprost obsluguje teze o grywalnosci
        /// powtornej z sekcji 1.
        ///
        /// Utrwalenie jest KONIECZNE, nie wygodne: bez niego wczytanie zapisu moglo by dac
        /// innego narratora niz przed zapisem, co lamie wymog odtwarzalnosci z sekcji 11.
        /// </summary>
        private string profileId = string.Empty;

        private int memoryVersion;

        /// <summary>Czy stan przyszedl z zapisu (true) czy to swieza gra (false). Tylko do logu.</summary>
        private bool wczytanoZZapisu;

        /// <summary>Liczba linii pamieci odrzuconych przy wczytaniu. Tylko do logu.</summary>
        private int odrzuconychLinii;

        private bool kanarekWypisany;

        /// <summary>
        /// KONSTRUKTOR MUSI PRZYJMOWAC Game. Game.FillComponents() wola
        /// Activator.CreateInstance(typ, this), wiec brak tego ctora konczy sie wpisem
        /// "Could not instantiate a GameComponent of type ..." w logu i komponent po prostu
        /// nie powstaje - a narrator dziala dalej, tylko bez trwalosci. Parametru nie
        /// przechowujemy: baza GameComponent, w odroznieniu od MapComponent, nie ma pola na gre,
        /// a Current.Game i tak jest dostepne wszedzie tam, gdzie go potrzebujemy.
        /// </summary>
        public NarratorMemoryComponent(Game game)
        {
        }

        public string RunId
        {
            get { return runId; }
        }

        public int MapCount
        {
            get { return histories == null ? 0 : histories.Count; }
        }

        /// <summary>defName przydzielonego profilu. Pusty tylko zanim padnie FinalizeInit.</summary>
        public string ProfileId
        {
            get { return profileId; }
        }

        /// <summary>
        /// Profil rdzenia dla tej rozgrywki. Rozwiazywany PRZY KAZDYM WYWOLANIU, a nie
        /// cache'owany: Def moze zostac przeladowany, a cache przezylby wyjscie do menu.
        /// Koszt to slownikowy GetNamedSilentFail raz na 1000 tickow, czyli zero.
        /// </summary>
        public NarratorProfile ActiveProfile()
        {
            return NarratorProfileCatalog.Resolve(profileId);
        }

        /// <summary>
        /// Podmienia profil w trakcie rozgrywki. WYLACZNIE dla akcji debugowej prowadzacej
        /// serie kontrolne - normalna rozgrywka nie zmienia profilu, bo osobowosc narratora
        /// nie jest ustawieniem.
        ///
        /// Zmiana profilu NIE kasuje pamieci zdarzen: to jest celowe, bo dzieki temu da sie
        /// porownac dwa profile na IDENTYCZNEJ historii, czyli izolowac badany czynnik.
        /// </summary>
        public void ForceProfile(string defName)
        {
            string poprzedni = profileId;
            profileId = defName ?? string.Empty;
            PNLog.Decision("PROFIL NARRATORA WYMUSZONY recznie (akcja debugowa): "
                           + (string.IsNullOrEmpty(poprzedni) ? "(brak)" : poprzedni)
                           + " -> " + (string.IsNullOrEmpty(profileId) ? "(brak)" : profileId)
                           + ". Pamiec zdarzen NIE zostala skasowana, wiec oba profile widza te sama historie.");
        }

        /// <summary>Widok do akcji debugowych i diagnostyki. Nie mutowac przez niego pamieci.</summary>
        public IEnumerable<KeyValuePair<int, EventHistory>> All
        {
            get { return histories ?? new Dictionary<int, EventHistory>(); }
        }

        /// <summary>
        /// Pamiec dla danej mapy; tworzona przy pierwszym pytaniu o nia.
        /// Przeniesione tu z StorytellerComp_Generative razem z uzasadnieniem rozdzialu per mapa.
        /// </summary>
        public EventHistory HistoryFor(int mapUniqueId)
        {
            if (histories == null)
            {
                histories = new Dictionary<int, EventHistory>();
            }

            EventHistory h;
            if (!histories.TryGetValue(mapUniqueId, out h))
            {
                h = new EventHistory();
                histories[mapUniqueId] = h;
                if (histories.Count > 1)
                {
                    PNLog.Decision("Nowa pamiec narratora dla mapy " + mapUniqueId.ToString(CultureInfo.InvariantCulture)
                                   + " (map z wlasna pamiecia: " + histories.Count.ToString(CultureInfo.InvariantCulture)
                                   + "). Kazda kolonia prowadzi osobna narracje.");
                }
            }
            return h;
        }

        /// <summary>
        /// Kasuje CALA pamiec narratora. Uzywane przez akcje debugowa.
        ///
        /// Po co to w ogole istnieje: waniliowy symulator DebugLogTestFutureIncidents przechodzi
        /// przez pelny nasz kod decyzyjny, wiec MUTUJE te pamiec - 100 dni symulacji zostawia
        /// kilkadziesiat fikcyjnych decyzji. Zanim pamiec byla trwala, zanieczyszczenie ginelo
        /// razem z procesem. Teraz zapisanie gry po eksperymencie utrwaliloby je na stale,
        /// wiec musi istniec sposob cofniecia tego inny niz "pamietaj, zeby nie zapisac".
        /// </summary>
        public void ClearAll()
        {
            int map = histories == null ? 0 : histories.Count;
            histories = new Dictionary<int, EventHistory>();
            PNLog.Decision("PAMIEC NARRATORA SKASOWANA recznie (akcja debugowa). Zwolniono map: "
                           + map.ToString(CultureInfo.InvariantCulture)
                           + ". Swiezosc, kontrast i gestosc PASS licza sie od zera.");
        }

        public override void ExposeData()
        {
            // KOLEJNOSC MA ZNACZENIE: bufor musi powstac PRZED Scribe_Collections.Look,
            // bo to jego zawartosc idzie na dysk.
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                BuildSaveBuffer();
            }

            Scribe_Values.Look(ref memoryVersion, "wersjaPamieci", 0, true);
            Scribe_Values.Look(ref runId, "runId", string.Empty, true);
            Scribe_Values.Look(ref profileId, "profil", string.Empty, true);

            // LISTY ROBOCZE SA TU NIEPOTRZEBNE i to nie jest niedopatrzenie.
            // Scribe_Collections.Look przesuwa budowanie slownika na faze ResolvingCrossRefs
            // TYLKO wtedy, gdy ktorykolwiek LookMode jest Reference; przy Value + Deep
            // BuildDictionary leci w tym samym przebiegu LoadingVars, w ktorym wypelniane sa
            // listy pomocnicze, wiec krotkie przeciazenie jest poprawne. Ten sam wzorzec ma
            // waniliowy StoryState.lastFireTicks (LookMode.Def + LookMode.Value).
            Scribe_Collections.Look(ref zapis, "pamiecMap", LookMode.Value, LookMode.Deep);

            // Rehydratacja idzie TUTAJ, a nie w PostLoadInit, mimo ze wanilia lubi tamta faze.
            // Powod: komponent DOLOZONY przez FillComponents() do zapisu, w ktorym go nie bylo
            // (czyli kazdy zapis sprzed tej zmiany), NIE przechodzi przez ScribeExtractor,
            // wiec NIGDY nie dostaje ani ResolvingCrossRefs, ani PostLoadInit. Odbudowa jest
            // przy tym czysto liczbowa (int -> EventHistory) i nie potrzebuje map, ktorych
            // w tej fazie i tak jeszcze nie ma.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                RestoreFromSaveBuffer();
            }
        }

        /// <summary>
        /// Przepisuje histories do bufora zapisu, POMIJAJAC mapy, ktorych juz nie ma.
        ///
        /// Sprzatanie sierot jest TYLKO tutaj, przy zapisie, i to jest istotne. W fazie
        /// LoadingVars ExposeData leci z Game.ExposeSmallComponents() PRZED
        /// Scribe_Collections.Look(ref maps, ...), wiec Find.Maps jest wtedy PUSTA - to samo
        /// sprzatanie wykonane tam skasowaloby CALA pamiec przy kazdym wczytaniu.
        /// </summary>
        private void BuildSaveBuffer()
        {
            zapis = new Dictionary<int, MapMemoryRecord>();
            memoryVersion = MemoryFormatVersion;

            if (histories == null)
            {
                histories = new Dictionary<int, EventHistory>();
                return;
            }

            List<Map> mapy = Current.Game != null ? Current.Game.Maps : null;

            // Sprzatamy WYLACZNIE wtedy, gdy naprawde widzimy jakies mapy. Pusta lista map
            // przy zapisie nie powinna sie zdarzyc, ale gdyby sie zdarzyla (zapis w nietypowym
            // momencie cyklu zycia), interpretacja "wszystkie mapy sa sierotami" kosztowalaby
            // cala pamiec narratora. Cena tej ostroznosci to co najwyzej kilka martwych wpisow.
            bool moznaSprzatac = mapy != null && mapy.Count > 0;
            int sierot = 0;

            foreach (KeyValuePair<int, EventHistory> para in histories)
            {
                if (para.Value == null)
                {
                    continue;
                }

                if (moznaSprzatac && para.Key >= 0 && !MapaIstnieje(mapy, para.Key))
                {
                    sierot++;
                    continue;
                }

                MapMemoryRecord rekord = new MapMemoryRecord();
                rekord.decisionCount = para.Value.DecisionCount;
                rekord.consecutivePassCount = para.Value.ConsecutivePassCount;
                rekord.lines = para.Value.ToPersistableLines();
                zapis[para.Key] = rekord;
            }

            if (sierot > 0)
            {
                PNLog.Decision("Zapis pamieci narratora: pominieto " + sierot.ToString(CultureInfo.InvariantCulture)
                               + " wpis(ow) map, ktorych juz nie ma (porzucone kolonie).");
            }
        }

        private void RestoreFromSaveBuffer()
        {
            histories = new Dictionary<int, EventHistory>();
            odrzuconychLinii = 0;
            wczytanoZZapisu = true;

            if (zapis == null)
            {
                zapis = new Dictionary<int, MapMemoryRecord>();
                return;
            }

            foreach (KeyValuePair<int, MapMemoryRecord> para in zapis)
            {
                if (para.Value == null)
                {
                    continue;
                }

                EventHistory h = new EventHistory();
                odrzuconychLinii += h.RestoreFromLines(para.Value.decisionCount,
                                                       para.Value.consecutivePassCount,
                                                       para.Value.lines);
                histories[para.Key] = h;
            }

            // Bufor przestaje byc potrzebny natychmiast po odbudowie. Trzymanie go dalej
            // groziloby tym, ze ktos kiedys odczyta z niego nieaktualny stan.
            zapis = new Dictionary<int, MapMemoryRecord>();
        }

        private static bool MapaIstnieje(List<Map> mapy, int uniqueId)
        {
            for (int i = 0; i < mapy.Count; i++)
            {
                if (mapy[i] != null && mapy[i].uniqueID == uniqueId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Wolane w OBU sciezkach - i przy nowej grze, i przy wczytaniu - wiec to jedyne miejsce,
        /// w ktorym straznik null i kanarek nie musza byc duplikowane.
        /// </summary>
        public override void FinalizeInit()
        {
            if (histories == null)
            {
                histories = new Dictionary<int, EventHistory>();
            }
            if (zapis == null)
            {
                zapis = new Dictionary<int, MapMemoryRecord>();
            }

            if (string.IsNullOrEmpty(runId))
            {
                runId = Guid.NewGuid().ToString("N").Substring(0, 8);
            }

            if (wczytanoZZapisu && memoryVersion != MemoryFormatVersion)
            {
                // NIE KASUJEMY pamieci przy rozjezdzie wersji. EventHistoryEntry.TryDecode
                // odrzuca niepasujace linie POJEDYNCZO i nigdy nie rzuca, wiec proba wczytania
                // jest bezpieczna, a cicha utrata calego bufora bylaby gorsza niz ostrzezenie.
                PNLog.Warn("Pamiec narratora pochodzi z zapisu w wersji formatu "
                           + memoryVersion.ToString(CultureInfo.InvariantCulture)
                           + ", a biezaca jest " + MemoryFormatVersion.ToString(CultureInfo.InvariantCulture)
                           + ". Wpisy nie do odczytania zostaly pominiete pojedynczo.");
            }

            memoryVersion = MemoryFormatVersion;
        }

        /// <summary>
        /// Kanarek idzie z tego hooka, a NIE z FinalizeInit, bo FinalizeInit leci w obu
        /// sciezkach naraz i nie da sie w nim odroznic nowej gry od wczytania. Oba hooki sa
        /// wolane PO FinalizeInit (GameComponentUtility.StartedNewGame / LoadedGame chodza po
        /// calej liscie komponentow, takze po tych dolozonych przez FillComponents), wiec
        /// straznik null i runId sa juz gotowe.
        /// </summary>
        public override void StartedNewGame()
        {
            PrzydzielProfil("nowa rozgrywka");
            WypiszKanarka("nowaGra");
        }

        public override void LoadedGame()
        {
            // Trzy stany, nie dwa - i to rozroznienie jest cala wartoscia tej linii:
            //   "zapis"           - nasz blok byl w zapisie i wczytal sie. Przypadek poprawny.
            //   "zapisBezPamieci" - zapis wczytany, ale bloku w nim nie bylo. To NIE jest awaria:
            //                       tak wyglada kazdy zapis sprzed tej zmiany oraz mod dolozony
            //                       do trwajacej rozgrywki. Komponent zostal wtedy dostawiony
            //                       przez FillComponents() i nie przeszedl przez ScribeExtractor.
            // Bez tego rozroznienia "trwalosc nie dziala" i "ten zapis jest starszy niz trwalosc"
            // wygladalyby w logu identycznie - czyli dokladnie ta dwuznacznosc, ktora kanarek
            // ma usuwac.
            // Zapis sprzed kroku 4 albo mod dolozony do trwajacej rozgrywki nie ma profilu.
            // Przydzielamy go wtedy awaryjnie, zamiast zostawiac narratora bez osobowosci -
            // ale odnotowujemy w logu, bo to znaczy, ze rozgrywka zaczela sie bez krzywej
            // dramaturgicznej i jej wczesniejsze decyzje nie sa porownywalne z pozniejszymi.
            if (string.IsNullOrEmpty(profileId))
            {
                PrzydzielProfil("wczytany zapis bez profilu");
            }

            WypiszKanarka(wczytanoZZapisu ? "zapis" : "zapisBezPamieci");
        }

        /// <summary>
        /// Losuje profil deterministycznie z runId.
        ///
        /// ZIARNO Z runId, A NIE Z Verse.Rand. Waniliowy generator jest wspoldzielony z cala
        /// gra, wiec pobranie z niego jednej liczby przesunelo by KAZDY pozniejszy losowy wynik
        /// w tej sesji - od generacji mapy po zachowania pionkow. Wyprowadzenie ziarna z runId
        /// daje przy okazji to, czego wymaga sekcja 11: ta sama rozgrywka zawsze dostaje ten
        /// sam profil, takze po wczytaniu zapisu sprzed przydzialu.
        /// </summary>
        private void PrzydzielProfil(string powod)
        {
            int ziarno = SeededRandom.Avalanche(runId == null ? 0 : runId.GetHashCode());
            NarratorProfileDef wybrany = NarratorProfileCatalog.PickWeighted(ziarno);

            if (wybrany == null)
            {
                profileId = string.Empty;
                PNLog.Error("KATALOG PROFILI NARRATORA PUSTY (albo wszystkie wagi zerowe) - "
                            + "narrator uzyje profilu awaryjnego z wartosciami domyslnymi z kodu. "
                            + "Sprawdz, czy Defs/Storytellers/Profiles_Core.xml uzywa PELNEJ nazwy typu "
                            + "<ProceduralNarrator.Integration.Defs.NarratorProfileDef>.");
                return;
            }

            profileId = wybrany.defName;
            PNLog.Decision("Profil narratora przydzielony (" + powod + "): " + profileId
                           + " \"" + wybrany.label + "\". Katalog: " + NarratorProfileCatalog.DescribeCatalog());
        }

        /// <summary>
        /// Kanarek wczytania. Bez niego nie da sie z logu odroznic "pamiec wczytala sie poprawnie"
        /// od "pamiec byla pusta i narrator zaczal od zera" - a to jest dokladnie ta awaria,
        /// ktorej ta klasa ma zapobiegac. Idzie do OBU strumieni: czytelnego, zeby bylo widac
        /// w Player.log, i maszynowego, bo jest zarazem GRANICA WCZYTANIA dla skryptu w Pythonie
        /// (wiersze [PN-DATA] po tej linii naleza do tej samej rozgrywki, mimo nowej sesji).
        /// </summary>
        private void WypiszKanarka(string zrodlo)
        {
            if (kanarekWypisany)
            {
                return;
            }
            kanarekWypisany = true;

            int wpisow = 0;
            int decyzji = 0;
            foreach (KeyValuePair<int, EventHistory> para in histories)
            {
                if (para.Value == null)
                {
                    continue;
                }
                wpisow += para.Value.Count;
                decyzji += para.Value.DecisionCount;
            }

            PNLog.Load(runId, zrodlo, profileId, histories.Count, wpisow, decyzji,
                       odrzuconychLinii, memoryVersion);

            if (odrzuconychLinii > 0)
            {
                PNLog.Warn("Przy wczytaniu pamieci narratora odrzucono "
                           + odrzuconychLinii.ToString(CultureInfo.InvariantCulture)
                           + " linii nie do sparsowania. Reszta pamieci zostala zachowana.");
            }
        }
    }
}
