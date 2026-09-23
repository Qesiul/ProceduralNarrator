using System.Collections.Generic;

namespace ProceduralNarrator.Core.Arcs
{
    /// <summary>
    /// Faza luku - stan automatu skonczonego (koncepcja 5.4). Pola camelCase = wezly XML.
    ///
    /// SEMANTYKA "BIEZACEJ FAZY". Luk otwiera sie W MOMENCIE rozpoznania zdarzenia zasiewu, wiec
    /// Zasiew nigdy nie jest faza oczekujaca - jest spelniony samym otwarciem. Biezaca faza
    /// instancji to zawsze faza, na ktorej zdarzenie luk dopiero CZEKA. Jej spelnienie przesuwa
    /// luk do nastepnej, a spelnienie Rozwiazania zamyka luk jako "rozwiazany".
    /// </summary>
    public class ArcPhase
    {
        /// <summary>Identyfikator fazy w luku (ASCII, np. "Eskalacja"). Zapisywany w pamieci gry.</summary>
        public string id;

        public ArcPhaseKind kind = ArcPhaseKind.Seed;

        /// <summary>Alternatywy oczekiwanego typu - faze przesuwa zdarzenie pasujace do KTOREJKOLWIEK.</summary>
        public List<ArcExpectation> expectations = new List<ArcExpectation>();

        /// <summary>
        /// "Dojrzalosc": ile dni po wejsciu w te faze musi minac, zanim zdarzenie moze ja przesunac
        /// (i zanim faza zacznie sterowac wyborem). Chroni przed dwoma ciosami tego samego watku
        /// dzien po dniu.
        /// </summary>
        public float minDaysAfterPrevious;

        /// <summary>
        /// Limit czasu fazy w dniach gry (decyzja autora nr 12). Eskalacja/Kulminacja po limicie
        /// przechodza do Rozwiazania, Rozwiazanie po limicie zamyka luk jako "wygaszony". Czekanie
        /// na zgodna intencje NIE zatrzymuje tego zegara.
        /// </summary>
        public float maxDays = 20f;

        /// <summary>
        /// Rozpoznanie zdarzenia tej fazy WIAZE jego frakcje z lukiem (dzis: Zasiew Wendety).
        /// Otwarcie luku z ta flaga wymaga znanej frakcji wykonanego zdarzenia.
        /// </summary>
        public bool bindsFaction;

        /// <summary>Przejscia sterowane strazniakami (reakcja na gracza, decyzja autora nr 5).</summary>
        public List<ArcTransition> transitions = new List<ArcTransition>();

        /// <summary>
        /// Komunikat w grze po rozpoznaniu zdarzenia TEJ fazy (decyzja autora nr 8). Moze zawierac
        /// {FRAKCJA} tylko wtedy, gdy frakcja jest juz zwiazana (pilnuje ArcCatalog.Validate).
        /// Zasada prawdy tekstu: stwierdza wylacznie fakty gwarantowane warunkiem.
        /// </summary>
        public string message;

        /// <summary>defName waniliowego MessageTypeDef; pusty = NeutralEvent.</summary>
        public string messageType;
    }

    /// <summary>
    /// Przejscie sterowane strazniakami: gdy WSZYSCY straznicy zachodza, luk skreca do fazy
    /// `target` albo zamyka sie z wynikiem `close`. Dokladnie jedno z nich (ArcCatalog.Validate).
    /// Przejscia na poziomie calego luku moga tylko zamykac.
    /// </summary>
    public class ArcTransition
    {
        public List<ArcGuard> guards = new List<ArcGuard>();

        /// <summary>Id fazy docelowej - wylacznie POZNIEJSZEJ niz biezaca (automat idzie naprzod).</summary>
        public string target;

        /// <summary>Wynik zamkniecia, np. "pojednanie". ASCII, bez ';' i '|'.</summary>
        public string close;

        /// <summary>Komunikat w grze po przejsciu (ta sama zasada prawdy tekstu).</summary>
        public string message;

        public string messageType;

        public bool Closes
        {
            get { return !string.IsNullOrEmpty(close); }
        }

        public string DescribeGuards()
        {
            if (guards == null || guards.Count == 0)
            {
                return "(brak)";
            }
            var opisy = new string[guards.Count];
            for (int i = 0; i < guards.Count; i++)
            {
                opisy[i] = guards[i] == null ? "?" : guards[i].Describe();
            }
            return string.Join("+", opisy);
        }
    }
}
