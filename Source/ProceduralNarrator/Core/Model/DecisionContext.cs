using System.Globalization;
using System.Text;

namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Komplet danych, na ktorych warstwa decyzyjna podejmuje JEDNA decyzje: zamrozony stan
    /// swiata, intencja narratora, pamiec zdarzen i dwa znaczniki czasu.
    ///
    /// Kontekst jest budowany RAZ na ture i przekazywany do wszystkich kandydatow oraz do
    /// wszystkich czynnikow. To nie jest optymalizacja, tylko warunek poprawnosci: gdyby
    /// kazdy kandydat pytal o stan swiata osobno, ranking porownywalby oceny liczone w
    /// nieznacznie roznych chwilach, a determinizm i odtwarzalnosc ewaluacji by padly.
    ///
    /// Wszystkie pola sa publiczne i wypelnia je warstwa integracji - Core niczego tu
    /// nie odczytuje z gry.
    /// </summary>
    public class DecisionContext
    {
        /// <summary>Zrzut stanu swiata, zamrozony na cala ture narratora.</summary>
        public WorldSnapshot Snapshot;

        /// <summary>
        /// Intencja wyznaczona przez krzywa dramaturgiczna. W kroku 3 zawsze Hold -
        /// ustawia ja jedno miejsce (BuildRecipe w warstwie integracji), a krok 4 podmieni
        /// stala na wyjscie krzywej. Czynniki zgodnosci z intencja sa juz wpiete z waga 0,
        /// zeby format logu badawczego nie zmienil sie miedzy krokiem 3 a 4.
        /// </summary>
        public Intent Intent = Intent.Hold;

        /// <summary>
        /// Pamiec zdarzen. NIGDY null - czynniki maja prawo czytac ja bez sprawdzania,
        /// a pusta historia jest w pelni poprawnym stanem (start kolonii, swiezo po wczytaniu).
        /// </summary>
        public EventHistory History = new EventHistory();

        /// <summary>
        /// Dzien gry jako FLOAT (w integracji: TicksGame / 60000f).
        ///
        /// Celowo nie WorldSnapshot.DaysPassed, ktore jest int-em: odstepy miedzy decyzjami
        /// narratora to ULAMKI dnia (interwal 1000 tickow to 1/60 doby), wiec przy int-cie
        /// gestosc zdarzen dla uzytecznosci PASS i wiek wpisow w rytmie mialyby rozdzielczosc
        /// grubsza niz mierzone zjawisko. Snapshot zachowuje swoj int do warunkow twardych.
        /// </summary>
        public float GameDay;

        /// <summary>
        /// Numer biezacej decyzji, liczony od zera, z PASS-ami wliczonymi.
        ///
        /// NIEZMIENNIK: DecisionIndex == History.DecisionCount w chwili budowy kontekstu.
        /// Zaden inny kod nie ma prawa go ustawiac. Sa to logicznie dwa liczniki tej samej
        /// wielkosci i ich rozjazd zafalszowalby wiek wpisow w czynniku swiezosci - bez
        /// zadnego objawu w logu, bo obie liczby z osobna wygladalyby sensownie.
        /// Metoda Create() jest jedynym miejscem, ktore ten niezmiennik realizuje.
        /// </summary>
        public int DecisionIndex;

        /// <summary>
        /// Jedyny poprawny sposob zbudowania kontekstu: numer decyzji bierze sie WYLACZNIE
        /// z licznika historii, wiec dwa zrodla tej liczby nie moga sie rozjechac.
        /// </summary>
        public static DecisionContext Create(WorldSnapshot snapshot, EventHistory history, float gameDay, Intent intent)
        {
            var context = new DecisionContext();
            context.Snapshot = snapshot;
            // Historia pusta zamiast null: Core nie rzuca wyjatkami w sciezce decyzyjnej,
            // bo wyjatek w MakeIntervalIncidents zabija cala ture narratora w grze.
            context.History = history ?? new EventHistory();
            context.GameDay = gameDay;
            context.Intent = intent;
            context.DecisionIndex = context.History.DecisionCount;
            return context;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("decyzja=").Append(DecisionIndex.ToString(CultureInfo.InvariantCulture))
              .Append(" dzien=").Append(GameDay.ToString("0.000", CultureInfo.InvariantCulture))
              .Append(" intencja=").Append(Intent);

            // Historia idzie do logu przy KAZDEJ decyzji (pole "wpisow" w Summary). To jedyny
            // sposob, zeby zauwazyc utrate pamieci narratora - np. przy przejsciu miedzy mapami
            // albo po wczytaniu zapisu, gdzie krok 3 z zalozenia startuje z pusta historia.
            sb.Append(" | ").Append(History == null ? "historia=BRAK" : History.Summary());
            sb.Append(" | ").Append(Snapshot == null ? "swiat=BRAK" : Snapshot.ToString());
            return sb.ToString();
        }
    }
}
