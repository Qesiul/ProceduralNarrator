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
        /// Intencja wyznaczona przez krzywa dramaturgiczna (IntentSelector), ewentualnie
        /// nadpisana regula kryzysu skrajnego (IntentSelector.ApplyCrisis). Domyslne Hold
        /// dotyczy wylacznie kontekstow budowanych bez warstwy planowania (testy kompozycji).
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
        /// Docelowa intensywnosc wyznaczona przez krzywa dramaturgiczna, na skali
        /// IntensityLevel (-2 = VeryLow, 0 = Normal, +2 = VeryHigh).
        ///
        /// Drugie - obok intencji - wyjscie warstwy planowania wymagane przez sekcje 5.5
        /// koncepcji. Czyta je Factor_IntentAlignment jako skladnik "zgodnosci mocy".
        /// Domyslne 0 znaczy Normal, wiec kontekst zbudowany bez krzywej (walidator, test)
        /// zachowuje sie neutralnie zamiast preferowac skrajnosci.
        /// </summary>
        public float TargetIntensity;

        /// <summary>
        /// Napiecie policzone przez krzywa dramaturgiczna, [0,1]. Nie wchodzi do zadnego
        /// wzoru scoringu - intencja i docelowa moc sa juz jego pochodnymi. Jest tu WYLACZNIE
        /// po to, zeby trafic do kolumny logu badawczego: bez surowego napiecia nie da sie
        /// z danych odtworzyc, dlaczego narrator wybral akurat te intencje.
        /// </summary>
        public float Tension;

        /// <summary>
        /// Czy w tej turze zachodzi KRYZYS SKRAJNY (CrisisDetector). Czyta go straznik serii
        /// ciszy: w kryzysie narrator nie jest zmuszany do dzialania po serii PASS-ow, a cisza
        /// wybrana w kryzysie nie zuzywa limitu swiadomego milczenia (patrz TurnResult).
        ///
        /// Pole w kontekscie, a nie parametr polityki, bo SelectionPolicy celowo nie widzi stanu
        /// swiata ani profilu - predykat liczy sie RAZ, wyzej, a tu trafia jego wynik.
        /// Domyslne false: kontekst zbudowany bez warstwy planowania zachowuje sie jak przedtem.
        /// </summary>
        public bool ExtremeCrisis;

        /// <summary>
        /// Jedyny poprawny sposob zbudowania kontekstu: numer decyzji bierze sie WYLACZNIE
        /// z licznika historii, wiec dwa zrodla tej liczby nie moga sie rozjechac.
        ///
        /// Przeciazenie czteroargumentowe zostawia napiecie i docelowa moc na wartosciach
        /// neutralnych. Jest zachowane CELOWO, a nie z lenistwa: uzywaja go testy kompozycji
        /// i te sekcje walidatora, ktore badaja scoring w oderwaniu od krzywej dramaturgicznej.
        /// Dopisanie tam dwoch zer nic by nie wyjasnilo, a zacieralo by fakt, ze tamte testy
        /// SWIADOMIE nie maja warstwy planowania.
        /// </summary>
        public static DecisionContext Create(WorldSnapshot snapshot, EventHistory history, float gameDay, Intent intent)
        {
            return Create(snapshot, history, gameDay, intent, 0f, 0f);
        }

        public static DecisionContext Create(WorldSnapshot snapshot, EventHistory history, float gameDay,
                                             Intent intent, float targetIntensity, float tension)
        {
            return Create(snapshot, history, gameDay, intent, targetIntensity, tension, false);
        }

        public static DecisionContext Create(WorldSnapshot snapshot, EventHistory history, float gameDay,
                                             Intent intent, float targetIntensity, float tension,
                                             bool extremeCrisis)
        {
            var context = new DecisionContext();
            context.Snapshot = snapshot;
            // Historia pusta zamiast null: Core nie rzuca wyjatkami w sciezce decyzyjnej,
            // bo wyjatek w MakeIntervalIncidents zabija cala ture narratora w grze.
            context.History = history ?? new EventHistory();
            context.GameDay = gameDay;
            context.Intent = intent;
            context.TargetIntensity = targetIntensity;
            context.Tension = tension;
            context.ExtremeCrisis = extremeCrisis;
            context.DecisionIndex = context.History.DecisionCount;
            return context;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("decyzja=").Append(DecisionIndex.ToString(CultureInfo.InvariantCulture))
              .Append(" dzien=").Append(GameDay.ToString("0.000", CultureInfo.InvariantCulture))
              .Append(" intencja=").Append(Intent)
              .Append(" napiecie=").Append(Tension.ToString("0.000", CultureInfo.InvariantCulture))
              .Append(" docelowaMoc=").Append(TargetIntensity.ToString("0.00", CultureInfo.InvariantCulture));
            if (ExtremeCrisis)
            {
                sb.Append(" KRYZYS_SKRAJNY");
            }

            // Historia idzie do logu przy KAZDEJ decyzji (pole "wpisow" w Summary). To jedyny
            // sposob, zeby zauwazyc utrate pamieci narratora - np. przy przejsciu miedzy mapami
            // albo po wczytaniu zapisu, gdzie krok 3 z zalozenia startuje z pusta historia.
            sb.Append(" | ").Append(History == null ? "historia=BRAK" : History.Summary());
            sb.Append(" | ").Append(Snapshot == null ? "swiat=BRAK" : Snapshot.ToString());
            return sb.ToString();
        }
    }
}
