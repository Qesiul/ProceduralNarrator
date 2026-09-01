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
    ///   (2) tresc katalogu: 12 klockow akcji, wiec 24 = dwa pelne obroty - bufor potrafi
    ///       udowodnic rotacje, a nie pozwala prehistorii zdominowac decyzji;
    ///   (3) czas gry: 24 decyzje razy mtbDays 2.5 = 60 dni, czyli dokladnie rok gry.
    ///
    /// PASS NIE ZAJMUJE SLOTU W BUFORZE. Decyzja o ciszy nie ma ani tematu, ani klocka akcji,
    /// ani osi walencji i skali - nie ma czego zapamietac. Gdyby PASS dostawal wpis, wnosilby
    /// do sredniej rytmu punkt, ktorego nikt nie zaobserwowal, i przy udziale PASS rzedu 6%
    /// marnowal co szesnasty slot okna. Jego slady sa SKALARAMI: DecisionCount przesuwa czas
    /// narracyjny (a wiec starzeje wszystkie wpisy), ConsecutivePassCount obsluguje straznika
    /// serii. Efekt emergentny: seria PASS-ow sama odswieza wszystkie zdarzenia i podnosi ich
    /// uzytecznosc wzgledem PASS-u, wiec uklad koryguje sie bez liczenia "swiezosci ciszy".
    ///
    /// DWIE KOLEJNOSCI, ZADNA NIE JEST ALIASEM DRUGIEJ - patrz komentarze KONTRAKT przy
    /// Entries i Recent(). To jest najczestszy blad integracyjny w tej warstwie: odwrocenie
    /// jednej z nich nie wywoluje zadnego bledu, tylko po cichu odwraca sens zaniku.
    ///
    /// OGRANICZENIE KROKU 3: historia NIE PRZEZYWA zapisu i wczytania gry, bo StorytellerComp
    /// jest odtwarzany z StorytellerDef. Po kazdym wczytaniu czynnik swiezosci wraca do wartosci
    /// neutralnej i rozgrzewa sie przez 4 decyzje (okolo 10 dni gry). Przy czestym zapisywaniu
    /// moze nigdy nie osiagnac pelnej mocy - a to psuje metryke powtarzalnosci z sekcji 12
    /// koncepcji. Naprawia to krok 6: GameComponent z ExposeData wola RestoreFromLines, a ta
    /// klasa nie zmienia sie ani o linie (metody trwalosci sa juz gotowe).
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
        private int consecutivePassCount;
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
            consecutivePassCount = 0;
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

        /// <summary>Ile decyzji PASS zapadlo z rzedu. Zeruje je kazda emisja. Dla straznika serii.</summary>
        public int ConsecutivePassCount
        {
            get { return consecutivePassCount; }
        }

        /// <summary>
        /// Dzien gry ostatniej decyzji, PASS-y wliczone. Wielkosc diagnostyczna do logu
        /// (odstep miedzy decyzjami); zaden wzor scoringu jej nie czyta - gestosc zdarzen
        /// liczy sie z GameDay poszczegolnych wpisow, nie z tej wartosci.
        /// Przed pierwsza decyzja rowna 0.
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
                AdvanceDecision(gameDay, false);
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

            AdvanceDecision(gameDay, false);
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
        /// </summary>
        public void RecordPass(float gameDay, int gameTick)
        {
            AdvanceDecision(gameDay, true);
        }

        /// <summary>Kasuje bufor i wszystkie liczniki. Uzywane przez RestoreFromLines i testy.</summary>
        public void Clear()
        {
            entries.Clear();
            decisionCount = 0;
            consecutivePassCount = 0;
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
        /// AUTONAPRAWA licznika decyzji: stan, w ktorym licznik jest mniejszy albo rowny
        /// najwiekszemu DecisionIndex we wpisach, jest wewnetrznie sprzeczny (wiek wyszedlby
        /// zerowy lub ujemny). Podnosimy licznik ponad najnowszy wpis, zamiast kasowac bufor:
        /// naprawa w te strone kosztuje jedna decyzje pozornego zuzycia, a kasowanie kosztuje
        /// cala pamiec narratora.
        /// </summary>
        public int RestoreFromLines(int decisionCount, int consecutivePassCount, IEnumerable<string> lines)
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

            while (entries.Count > capacity)
            {
                entries.RemoveAt(0);
            }

            this.decisionCount = decisionCount < 0 ? 0 : decisionCount;
            this.consecutivePassCount = consecutivePassCount < 0 ? 0 : consecutivePassCount;

            if (entries.Count > 0)
            {
                EventHistoryEntry newest = entries[entries.Count - 1];
                if (this.decisionCount <= newest.DecisionIndex)
                {
                    this.decisionCount = newest.DecisionIndex + 1;
                }
                // Przyblizenie: ostatnia decyzja mogla byc pozniejszym PASS-em, ktory nie
                // zostawil wpisu. Pole jest diagnostyczne, wiec blad rzedu jednego odstepu
                // miedzy decyzjami nie wplywa na zadna wielkosc scoringu.
                lastDecisionGameDay = newest.GameDay;
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

        /// <summary>
        /// Wspolne domkniecie kazdej decyzji: przesuniecie licznika, obsluga serii PASS-ow
        /// i znacznika czasu. Jedno miejsce, bo rozjazd tych trzech operacji miedzy sciezka
        /// emisji a sciezka ciszy jest bledem, ktorego nie widac w zadnym logu.
        /// </summary>
        private void AdvanceDecision(float gameDay, bool wasPass)
        {
            decisionCount++;
            consecutivePassCount = wasPass ? consecutivePassCount + 1 : 0;
            lastDecisionGameDay = gameDay;
        }
    }
}
