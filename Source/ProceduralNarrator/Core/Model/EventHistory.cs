using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Pamiec zdarzen narratora: krotki bufor ostatnich EMISJI plus trzy skalary opisujace
    /// przebieg decyzji. Czyta ja czynnik swiezosci typu, czynnik kontrastu dramaturgicznego
    /// i czynnik powsciagliwosci PASS-u.
    ///
    /// POJEMNOSC 24 nie jest liczba przyjeta na oko - wychodzi z trzech niezaleznych rachunkow,
    /// ktore daja ten sam wynik:
    ///   (1) horyzont wolniejszej osi zaniku: 0.5^((wiek-1)/5) spada ponizej 0.05 dla wieku
    ///       powyzej 1 + 5*log2(20) = 23.6, wiec starsze wpisy to koszt bez wplywu na wynik;
    ///   (2) tresc katalogu: przy 12 klockach akcji (katalog kroku 3) 24 = dwa pelne obroty -
    ///       bufor potrafi udowodnic rotacje, a nie pozwala prehistorii zdominowac decyzji.
    ///       Od dodania PN_Akcja_Amok klockow jest 13, wiec dwa obroty to 26 - argument (2)
    ///       stal sie przyblizony; (1) i (3) nie zaleza od katalogu i trzymaja pojemnosc;
    ///   (3) czas gry: 24 decyzje razy mtbDays 2.5 = 60 dni, czyli dokladnie rok gry.
    ///
    /// PASS NIE ZAJMUJE SLOTU W BUFORZE. Decyzja o ciszy nie ma ani tematu, ani klocka akcji,
    /// ani osi walencji i skali - nie ma czego zapamietac. Gdyby PASS dostawal wpis, wnosilby
    /// do sredniej rytmu punkt, ktorego nikt nie zaobserwowal, i przy udziale PASS rzedu 6%
    /// marnowal co szesnasty slot okna. Jego slady sa SKALARAMI: DecisionCount przesuwa czas
    /// narracyjny (a wiec starzeje wszystkie wpisy), DeliberateSilenceStreak obsluguje straznika
    /// serii. Efekt emergentny: seria PASS-ow sama odswieza wszystkie zdarzenia i podnosi ich
    /// uzytecznosc wzgledem PASS-u, wiec uklad koryguje sie bez liczenia "swiezosci ciszy".
    ///
    /// DWIE KOLEJNOSCI, ZADNA NIE JEST ALIASEM DRUGIEJ - patrz komentarze KONTRAKT przy
    /// Entries i Recent(). To jest najczestszy blad integracyjny w tej warstwie: odwrocenie
    /// jednej z nich nie wywoluje zadnego bledu, tylko po cichu odwraca sens zaniku.
    ///
    /// TRWALOSC (krok 6): historia przezywa zapis i wczytanie gry - wlascicielem jest
    /// NarratorMemoryComponent (GameComponent z ExposeData), ktory zapisuje ToPersistableLines
    /// oraz dwa skalary (DecisionCount, DeliberateSilenceStreak) i odbudowuje je przez
    /// RestoreFromLines. Do kroku 6 historia ginela przy kazdym wczytaniu, bo StorytellerComp
    /// jest odtwarzany z StorytellerDef. NIE jest utrwalane LastDecisionGameDay - patrz tam.
    ///
    /// OGRANICZENIE: historia widzi WYLACZNIE emisje naszego narratora. Zdarzenia z 17 compow
    /// Cassandry przejetych bez zmian (handlarze, choroby, questy) do niej nie trafiaja, wiec
    /// "rytm ostatnich zdarzen" jest rytmem naszych trzech podmienionych compow, a nie calej
    /// rozgrywki. Do zapisania w rozdziale o ewaluacji.
    /// </summary>
    public class EventHistory
    {
        /// <summary>Pojemnosc bufora. Wyprowadzenie liczby - w opisie klasy.</summary>
        public const int DefaultCapacity = 24;

        private readonly int capacity;

        // Bufor cykliczny zrobiony na List z eweikcja od przodu, a NIE na arytmetyce indeksow
        // po tablicy. Przy n = 24 koszt RemoveAt(0) jest bez znaczenia, a w zamian kolejnosc
        // "od najstarszego" jest poprawna z konstrukcji i trywialnie serializowalna. Wersja
        // z glowa i ogonem wymaga rozwijania bufora przy zapisie i jest klasycznym miejscem,
        // w ktorym historia wychodzi z zapisu odwrocona w czasie.
        private readonly List<EventHistoryEntry> entries;

        // Widok tylko do odczytu budowany RAZ. Bez niego kazde odwolanie do Entries albo
        // zwracaloby liste mutowalna (kontrakt na papierze), albo alokowalo nowa oslone -
        // a Entries jest czytane w petli po wszystkich kandydatach, dwa razy na kazdego
        // (wartosc czynnika i jego slad).
        private readonly ReadOnlyCollection<EventHistoryEntry> entriesView;

        private int decisionCount;
        private int deliberateSilenceStreak;
        private float lastDecisionGameDay;

        public EventHistory() : this(DefaultCapacity)
        {
        }

        public EventHistory(int capacity)
        {
            // Pojemnosc 0 dawalaby bufor, ktory natychmiast wyrzuca kazdy wpis - czyli czynnik
            // swiezosci zamrozony na wartosci neutralnej bez zadnego objawu. Jeden slot to
            // minimum, przy ktorym klasa nadal cokolwiek pamieta.
            this.capacity = Math.Max(1, capacity);
            entries = new List<EventHistoryEntry>(this.capacity);
            entriesView = new ReadOnlyCollection<EventHistoryEntry>(entries);
            decisionCount = 0;
            deliberateSilenceStreak = 0;
            lastDecisionGameDay = 0f;
        }

        public int Capacity
        {
            get { return capacity; }
        }

        /// <summary>Liczba wpisow w buforze - same EMISJE, bez decyzji PASS.</summary>
        public int Count
        {
            get { return entries.Count; }
        }

        /// <summary>
        /// Licznik WSZYSTKICH decyzji warstwy decyzyjnej, z PASS-ami wlacznie. To jest zegar,
        /// wzgledem ktorego starzeja sie wpisy.
        ///
        /// NIEZMIENNIK CALEJ WARSTWY: DecisionContext.DecisionIndex bierze wartosc stad
        /// w chwili budowy kontekstu i ZADNE inne miejsce go nie ustawia. Dwa niezalezne
        /// liczniki decyzji zafalszowalyby wieki wpisow bez najmniejszego objawu w logu.
        /// </summary>
        public int DecisionCount
        {
            get { return decisionCount; }
        }

        /// <summary>
        /// Ile razy Z RZEDU narrator SWIADOMIE zrezygnowal z dzialania od ostatniego wybranego
        /// zdarzenia. Wejscie straznika serii.
        ///
        /// LICZY WYLACZNIE CISZE SWIADOMA (PassReason.Competitive) i to jest naprawa piatego
        /// zarzutu. Poprzednia wersja nazywala sie ConsecutivePassCount i podnosila sie przy
        /// KAZDEJ turze bez zdarzenia, takze wtedy, gdy narrator chcial dzialac i nie mogl -
        /// bo silnik odmowil wszystkim kandydatom albo skonczyl sie budzet rund. Straznik serii
        /// istnieje po to, zeby narrator nie milczal w nieskonczonosc Z WYBORU; przeszkoda
        /// techniczna nie jest wyborem i nie ma prawa zuzywac limitu swiadomej ciszy.
        ///
        /// Skutek bledu byl niesymetryczny dokladnie tam, gdzie boli najbardziej: w koloniach,
        /// w ktorych gra duzo odmawia (brak stropu gorskiego, wczesna gra, uboga kolonia),
        /// straznik wygaszal PASS po serii ciszy, ktorej narrator wcale nie wybral - czyli
        /// zmuszal go do dzialania za cudze przeszkody.
        ///
        /// TRZY ZACHOWANIA, ROZLACZNE I ZAMIERZONE:
        ///     cisza swiadoma (Competitive POZA kryzysem skrajnym)  -> licznik ROSNIE
        ///     wybrane zdarzenie                                    -> licznik ZERUJE SIE
        ///     cisza techniczna ALBO cisza w kryzysie skrajnym      -> licznik BEZ ZMIANY
        /// O tym, ktora cisza jest swiadoma, rozstrzyga TurnResult.DeliberateSilence w rdzeniu.
        /// Cisza w kryzysie ma w danych powodPass=Competitive (brama ja wybrala), ale jest regula
        /// bezpieczenstwa, nie wyborem osobowosci - kryzys "pauzuje zegar" serii. Metryki
        /// swiadomego milczenia per profil trzeba wiec filtrowac po kolumnie kryzys=false.
        /// Trzecia linia jest istota: przeszkoda techniczna ani nie zuzywa limitu swiadomej
        /// ciszy, ani go nie odnawia. DecisionCount rosnie w KAZDYM z trzech przypadkow, wiec
        /// starzenie swiezosci pozostaje nietkniete.
        ///
        /// CompetitiveAfterRefusal liczy sie jako TECHNICZNA: to stan alarmowy oznaczajacy, ze
        /// zamrozenie bramy padlo, a cisza wygrala z pula juz okrojona przez silnik - czyli nie
        /// byla w pelni wyborem.
        /// </summary>
        public int DeliberateSilenceStreak
        {
            get { return deliberateSilenceStreak; }
        }

        /// <summary>
        /// Dzien gry ostatniej decyzji, PASS-y wliczone. Przed pierwsza decyzja rowna 0.
        ///
        /// POLE NIETRWALE I BEZ CZYTELNIKA - stan zweryfikowany przegladem etapu 4, nie zalozony.
        /// Zaden wzor scoringu, zaden warunek ani zadna kolumna logu go nie czyta (gestosc zdarzen
        /// liczy sie z GameDay poszczegolnych wpisow). Wczesniejszy komentarz obiecywal "wielkosc
        /// diagnostyczna do logu" - takiej linii nie ma. Po wczytaniu zapisu wartosc jest
        /// PRZYBLIZANA dniem najnowszego wpisu (patrz RestoreFromLines), wiec zaniza date
        /// o ogon PASS-ow po ostatnim zdarzeniu.
        ///
        /// Swiadomie NIE utrwalamy: odstep miedzy decyzjami da sie w calosci wyprowadzic z danych
        /// (roznica kolumny "dzien" kolejnych wierszy [PN-DATA] o tym samym runId i mapie - runId
        /// jest trwaly, wiec dziala to takze przez granice wczytania). Utrwalenie wymagaloby
        /// podbicia formatu zapisu dla pola bez konsumenta. Zrobic to razem z PIERWSZYM
        /// konsumentem, ktory potrzebuje tej wartosci w trakcie gry - jako string G9 (float przez
        /// Scribe_Values traci do 29 tickow i przy uszkodzonym wezle zwraca 0 zamiast braku),
        /// z wartoscia zastepcza "dzien najnowszego wpisu" dla starych zapisow.
        /// </summary>
        public float LastDecisionGameDay
        {
            get { return lastDecisionGameDay; }
        }

        /// <summary>
        /// KONTRAKT: kolejnosc od NAJSTARSZEGO (indeks 0) do NAJNOWSZEGO (indeks Count-1).
        ///
        /// Taka i tylko taka kolejnosc jest naturalna dla serializacji: ToPersistableLines
        /// zapisuje ja wprost, a odwrocenie przy zapisie odwrocilo by po wczytaniu sens zaniku.
        /// Czynniki, ktore chca wag malejacych od najnowszego, MUSZA uzyc Recent() - te dwie
        /// kolejnosci sa rozne i zadna nie jest aliasem drugiej.
        /// </summary>
        public IReadOnlyList<EventHistoryEntry> Entries
        {
            get { return entriesView; }
        }

        /// <summary>
        /// KONTRAKT KRYTYCZNY: zwraca NAJNOWSZY WPIS NA INDEKSIE 0, potem coraz starsze.
        /// Odwrotnie niz Entries.
        ///
        /// Ta metoda istnieje dla srednich wykladniczo wazonych, ktore mnoza wpis i-ty przez
        /// lambda^i. Gdyby zwracala kolejnosc Entries, wagi lezalyby od zlej strony: kod dziala,
        /// log wyglada sensownie, a wyniki ewaluacji sa smieciami (zmierzone na przypadku
        /// testowym: 0.7576 przy poprawnej kolejnosci wobec 0.5673 przy odwroconej). Dlatego
        /// obie kolejnosci sa osobnymi skladowymi i obie maja ten komentarz.
        ///
        /// Zwracana lista jest kopia - wolajacy moze ja trzymac i sortowac bez wplywu na bufor.
        /// </summary>
        public IReadOnlyList<EventHistoryEntry> Recent(int max)
        {
            if (max <= 0 || entries.Count == 0)
            {
                return new List<EventHistoryEntry>(0);
            }

            int take = max < entries.Count ? max : entries.Count;
            List<EventHistoryEntry> result = new List<EventHistoryEntry>(take);
            for (int i = 0; i < take; i++)
            {
                result.Add(entries[entries.Count - 1 - i]);
            }
            return result;
        }

        /// <summary>Najnowszy wpis albo null, gdy bufor jest pusty.</summary>
        public EventHistoryEntry Newest
        {
            get { return entries.Count == 0 ? null : entries[entries.Count - 1]; }
        }

        /// <summary>
        /// Wiek wpisu liczony w DECYZJACH, nie w dniach ani tickach. Zawsze co najmniej 1:
        /// wpis z decyzji bezposrednio poprzedniej ma wiek dokladnie 1, bo DecisionIndex bierze
        /// wartosc licznika PRZED inkrementacja.
        ///
        /// Klamra Math.Max(1, ...) jest bezpiecznikiem na niespojny stan po wczytaniu zapisu.
        /// Psuje sie W STRONE KARY: przy uszkodzeniu wszystko wyglada na swiezo uzyte, wiec
        /// swiezosci na calej liscie sa niskie i widac to w logu natychmiast. Awaria w druga
        /// strone (wszystko wyglada na swieze) bylaby niewidoczna - czynnik po cichu zwracalby
        /// 1.0 i przestal cokolwiek robic.
        ///
        /// Wpis rowny null to blad okablowania, a nie uszkodzenie licznika, wiec dostaje wiek
        /// maksymalny: nie ma prawa dokladac cisnienia do zadnej osi.
        /// </summary>
        public int AgeInDecisions(EventHistoryEntry entry)
        {
            if (entry == null)
            {
                return int.MaxValue;
            }
            return Math.Max(1, decisionCount - entry.DecisionIndex);
        }

        /// <summary>
        /// Notuje EMISJE zdarzenia i przesuwa licznik decyzji. Zwraca false, gdy wpis powstal
        /// bez klucza swiezosci - warstwa integracji ma wtedy zalogowac blad.
        ///
        /// Bledy sa raportowane GLOSNO, bo cala klasa awarii tej warstwy jest cicha: pusta
        /// historia daje czynnik swiezosci zamrozony na wartosci neutralnej, czyli wartosc
        /// w zakresie, zero wyjatkow i ranking, ktory sie liczy. To dokladnie ten sam ksztalt
        /// pulapki co "KATALOG KLOCKOW PUSTY" opisany w CLAUDE.md.
        ///
        /// Wpis z pustym ActionBlockId POWSTAJE (z kluczem "?"), bo osie Theme/Valence/Scale
        /// sa wciaz poprawne i potrzebne czynnikowi kontrastu - tracimy tylko os akcji.
        /// Natomiast composed rowny null nie daje wpisu w ogole: wpis z domyslnymi osiami
        /// wnosilby do rytmu punkt, ktorego nikt nie zaobserwowal. Liczniki przesuwaja sie
        /// w obu przypadkach, bo decyzja mimo wszystko zapadla i czas narracyjny plynie.
        /// </summary>
        public bool RecordEvent(ComposedEvent composed, float gameDay, int gameTick)
        {
            if (composed == null)
            {
                AdvanceDecision(gameDay, PassKind.Event);
                return false;
            }

            bool hasKey = !string.IsNullOrEmpty(composed.ActionBlockId);

            EventHistoryEntry entry = new EventHistoryEntry
            {
                // Licznik PRZED inkrementacja - dzieki temu wiek wpisu z poprzedniej decyzji
                // wynosi przy nastepnej ocenie dokladnie 1, a nie 0 (waga recencji rowna 1.0).
                DecisionIndex = decisionCount,
                GameDay = gameDay,
                GameTick = gameTick,
                ActionBlockId = hasKey ? composed.ActionBlockId : "?",
                ActionPayload = composed.ActionPayload,
                Theme = composed.Theme,
                Valence = composed.Valence,
                Scale = composed.Scale
            };

            entries.Add(entry);

            // Eweikcja FIFO od przodu: wypada NAJSTARSZY, czyli zawsze ten o najmniejszej wadze
            // recencji. Wiekow nie przelicza sie przy eweikcji, bo DecisionIndex jest absolutny.
            // Petla zamiast pojedynczego if-a obsluguje tez bufor odtworzony z zapisu, ktory
            // moze byc dluzszy niz biezaca pojemnosc.
            while (entries.Count > capacity)
            {
                entries.RemoveAt(0);
            }

            AdvanceDecision(gameDay, PassKind.Event);
            return hasKey;
        }

        /// <summary>
        /// Notuje decyzje o ciszy. Przesuwa liczniki i NIE dopisuje wpisu do bufora.
        ///
        /// gameTick jest w sygnaturze dla symetrii z RecordEvent i dla przyszlego logu momentu
        /// PASS-u; dzis nie ma go gdzie zapisac, bo PASS nie tworzy wpisu.
        ///
        /// WOLAC WYLACZNIE PO DECYZJI WARSTWY DECYZYJNEJ. Nieprzepuszczajaca bramka MTB
        /// to NIE jest decyzja i nie wolno jej tu notowac: przy interwale 1000 tickow licznik
        /// roslby o okolo 60 na dobe gry zamiast o 0.4, wiec przy polowicznosci 5 decyzji
        /// wszystko byloby w pelni swieze po dwoch godzinach gry, a czynnik swiezosci
        /// zdegenerowalby sie do stalej.
        ///
        /// deliberate rozstrzyga o serii swiadomej ciszy - patrz DeliberateSilenceStreak.
        /// Wolajacy podaje true WYLACZNIE dla PassReason.Competitive; kazda inna cisza jest
        /// przeszkoda, a nie wyborem, i zostawia licznik bez zmiany.
        /// </summary>
        public void RecordPass(float gameDay, int gameTick, bool deliberate)
        {
            AdvanceDecision(gameDay, deliberate ? PassKind.Deliberate : PassKind.Technical);
        }

        /// <summary>Kasuje bufor i wszystkie liczniki. Uzywane przez RestoreFromLines i testy.</summary>
        public void Clear()
        {
            entries.Clear();
            decisionCount = 0;
            deliberateSilenceStreak = 0;
            lastDecisionGameDay = 0f;
        }

        /// <summary>
        /// Zrzuca bufor do linii tekstu w kolejnosci OD NAJSTARSZEGO - dokladnie tej, ktorej
        /// oczekuje RestoreFromLines. Odwrocenie tutaj odwrocilo by po wczytaniu sens zaniku.
        ///
        /// Typem wyjsciowym jest List of string, a nie wlasny typ z interfejsem gry: Core nie
        /// zna API RimWorlda, a lista stringow zapisuje sie Scribe_Collections z LookMode.Value
        /// bez implementowania IExposable gdziekolwiek w rdzeniu. Cala migracja do kroku 6
        /// zamyka sie wtedy w warstwie integracji.
        /// </summary>
        public List<string> ToPersistableLines()
        {
            List<string> lines = new List<string>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                lines.Add(entries[i].Encode());
            }
            return lines;
        }

        /// <summary>
        /// Odtwarza stan z zapisu. Zwraca liczbe linii ODRZUCONYCH - warstwa integracji ma ja
        /// zalogowac, gdy jest wieksza od zera.
        ///
        /// Zaklada kolejnosc taka, jaka daje ToPersistableLines (od najstarszego). Linie, ktore
        /// nie parsuja sie po zmianie nazwy wartosci enuma albo po uszkodzeniu zapisu, sa
        /// pomijane pojedynczo - jedna zla linijka pamieci narratora nie moze przewrocic
        /// wczytywania calej rozgrywki.
        ///
        /// AUTONAPRAWY stanu wewnetrznie sprzecznego (zapis uszkodzony albo edytowany recznie -
        /// z kodu taki stan nie powstaje, bo RecordEvent nadaje DecisionIndex monotonicznie):
        ///   1. KOLEJNOSC. Wpisy sa porzadkowane rosnaco po DecisionIndex (stabilnie). Bez tego
        ///      Newest bralby ostatnia LINIE, a nie najnowsze zdarzenie, i wiek najnowszego wpisu
        ///      wychodzil z klamry zamiast z danych.
        ///   2. LICZNIK DECYZJI. Licznik mniejszy albo rowny NAJWIEKSZEMU DecisionIndex daje wiek
        ///      zerowy lub ujemny. Podnosimy go ponad najnowszy wpis, zamiast kasowac bufor:
        ///      naprawa w te strone kosztuje jedna decyzje pozornego zuzycia, a kasowanie - cala
        ///      pamiec narratora.
        ///   3. SERIA CISZY. Seria liczy cisze PO ostatnim zdarzeniu, wiec nie moze przekroczyc
        ///      liczby decyzji, ktore po nim zapadly (decisionCount - 1 - najnowszyIndeks). Gdy
        ///      najnowsze zdarzenie bylo ostatnia decyzja, seria musi byc zerowa - tak samo jak
        ///      po RecordEvent.
        /// Naprawy nie zwiekszaja liczby odrzuconych linii: zadna linia nie przepada.
        /// </summary>
        public int RestoreFromLines(int decisionCount, int deliberateSilenceStreak, IEnumerable<string> lines)
        {
            Clear();

            int dropped = 0;
            if (lines != null)
            {
                foreach (string line in lines)
                {
                    EventHistoryEntry entry;
                    if (EventHistoryEntry.TryDecode(line, out entry))
                    {
                        entries.Add(entry);
                    }
                    else
                    {
                        dropped++;
                    }
                }
            }

            // (1) Porzadek rosnacy po DecisionIndex, STABILNY - sortowanie przez wstawianie, bo
            // List.Sort nie gwarantuje stabilnosci, a bufor ma kilkadziesiat wpisow.
            for (int i = 1; i < entries.Count; i++)
            {
                EventHistoryEntry biezacy = entries[i];
                int j = i - 1;
                while (j >= 0 && entries[j].DecisionIndex > biezacy.DecisionIndex)
                {
                    entries[j + 1] = entries[j];
                    j--;
                }
                entries[j + 1] = biezacy;
            }

            while (entries.Count > capacity)
            {
                entries.RemoveAt(0);
            }

            this.decisionCount = decisionCount < 0 ? 0 : decisionCount;
            this.deliberateSilenceStreak = deliberateSilenceStreak < 0 ? 0 : deliberateSilenceStreak;

            if (entries.Count > 0)
            {
                // Po sortowaniu ostatni wpis ma NAJWIEKSZY DecisionIndex.
                EventHistoryEntry newest = entries[entries.Count - 1];
                if (this.decisionCount <= newest.DecisionIndex)
                {
                    this.decisionCount = newest.DecisionIndex + 1;
                }

                // (3) Seria nie dluzsza niz liczba decyzji po najnowszym zdarzeniu.
                int decyzjiPoZdarzeniu = this.decisionCount - 1 - newest.DecisionIndex;
                if (this.deliberateSilenceStreak > decyzjiPoZdarzeniu)
                {
                    this.deliberateSilenceStreak = decyzjiPoZdarzeniu;
                }
                // PRZYBLIZENIE: ostatnia decyzja mogla byc pozniejszym PASS-em, ktory nie
                // zostawil wpisu - pole nie jest utrwalane (patrz LastDecisionGameDay).
                // Nie ma czytelnika, wiec zaden wynik od tego nie zalezy; blad w przegladzie
                // zmierzony jako mediana 1.8 dnia, p95 8 dni.
                lastDecisionGameDay = newest.GameDay;
            }
            else if (this.deliberateSilenceStreak > this.decisionCount)
            {
                // Bez zdarzen seria nie moze byc dluzsza niz wszystkie decyzje.
                this.deliberateSilenceStreak = this.decisionCount;
            }

            return dropped;
        }

        /// <summary>
        /// Jedna linia stanu do logu czytelnego dla czlowieka.
        /// Format: "wpisow=8/24 decyzji=12 ostatnie=PN_Akcja_Napad(wiek 1)".
        ///
        /// Liczba wpisow jest tu po to, zeby najgrozniejsza cicha awaria tej warstwy - zapomniane
        /// wolanie RecordEvent - byla widoczna od pierwszej decyzji, a nie dopiero po zebraniu
        /// rozgrywek. Tym samym widac skok liczby wpisow do zera przy przejsciu miedzy mapami.
        /// </summary>
        public string Summary()
        {
            EventHistoryEntry newest = Newest;
            string last = newest == null
                ? "brak"
                : newest.ActionBlockId + "(wiek "
                  + AgeInDecisions(newest).ToString(CultureInfo.InvariantCulture) + ")";

            return "wpisow=" + entries.Count.ToString(CultureInfo.InvariantCulture)
                   + "/" + capacity.ToString(CultureInfo.InvariantCulture)
                   + " decyzji=" + decisionCount.ToString(CultureInfo.InvariantCulture)
                   + " ostatnie=" + last;
        }

        /// <summary>Jak zakonczyla sie tura - z punktu widzenia serii swiadomej ciszy.</summary>
        private enum PassKind
        {
            /// <summary>Tura zakonczona zdarzeniem. Zeruje serie.</summary>
            Event,

            /// <summary>Cisza wybrana przez brame (Competitive) poza kryzysem skrajnym. Podnosi serie.</summary>
            Deliberate,

            /// <summary>
            /// Cisza z przeszkody technicznej ALBO cisza w kryzysie skrajnym. Zostawia serie bez zmiany.
            /// </summary>
            Technical,
        }

        /// <summary>
        /// Wspolne domkniecie kazdej decyzji: przesuniecie licznika, obsluga serii swiadomej
        /// ciszy i znacznika czasu. Jedno miejsce, bo rozjazd tych trzech operacji miedzy
        /// sciezka emisji a sciezka ciszy jest bledem, ktorego nie widac w zadnym logu.
        ///
        /// DecisionCount rosnie przy KAZDYM rodzaju domkniecia - takze przy ciszy technicznej.
        /// To jest zamierzone i niezalezne od serii: licznik decyzji jest ZEGAREM starzenia
        /// swiezosci, a czas plynie tak samo niezaleznie od tego, czy narrator milczal z wyboru,
        /// czy z bezsily.
        /// </summary>
        private void AdvanceDecision(float gameDay, PassKind kind)
        {
            decisionCount++;
            if (kind == PassKind.Event)
            {
                deliberateSilenceStreak = 0;
            }
            else if (kind == PassKind.Deliberate)
            {
                deliberateSilenceStreak++;
            }
            // PassKind.Technical celowo nie rusza licznika - patrz DeliberateSilenceStreak.

            lastDecisionGameDay = gameDay;
        }
    }
}
