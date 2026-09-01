using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Pojedynczy czynnik scoringu utility AI: jedna os oceny kandydata (dopasowanie
    /// kontekstowe, swiezosc typu, kontrast dramaturgiczny, zgodnosc z intencja...).
    ///
    /// UtilityScorer trzyma DWIE ROZLACZNE tablice implementacji tego interfejsu -
    /// zdarzeniowa (wagi z ScoringWeights) i PASS-owa (wagi z PassScoringParams) -
    /// i nigdy ich nie miesza. Interfejs jest wspolny, przestrzenie nazw i kalibracje sa osobne.
    ///
    /// DLACZEGO PIERWSZYM ARGUMENTEM JEST ScoredCandidate, A NIE ComposedEvent:
    /// pseudo-kandydat PASS nie ma zlozonego wydarzenia (Event == null), wiec przy
    /// ComposedEvent w sygnaturze czynniki PASS bylyby NIEWYRAZALNE i trzeba by je obchodzic
    /// recznie sklecanym wpisem sladu. Czynniki zdarzeniowe siegaja po candidate.Event.*,
    /// czynniki PASS po candidate.IsPass i kontekscie - jeden kontrakt obsluguje oba swiaty.
    ///
    /// DLACZEGO SLAD IDZIE PRZEZ out-PARAMETR, A NIE OSOBNA METODA Explain():
    ///  - osobna metoda liczylaby wartosc DRUGI RAZ, wiec slad moglby sie rozjechac z wynikiem
    ///    (a to wlasnie slad jest danymi wejsciowymi czesci badawczej pracy),
    ///  - wariant z zapamietaniem sladu w polu (lastExplain) czyni czynnik STANOWYM: przy ocenie
    ///    wsadowej 84 kandydatow log przypisalby rozbicie nie temu kandydatowi, co trzeba.
    /// Jedno wywolanie zwraca wartosc I jej uzasadnienie - nie da sie ich rozdzielic.
    ///
    /// KONTRAKT IMPLEMENTACJI (obowiazuje kazdy czynnik):
    ///  - funkcja CZYSTA: zero stanu miedzy wywolaniami, zero IRandomSource, zero czasu
    ///    rzeczywistego. Determinizm calej warstwy decyzyjnej stoi na tym zalozeniu;
    ///  - wynik w [0,1], bez NaN i bez nieskonczonosci (UtilityScorer i tak przepuszcza go
    ///    przez ScoreMath.Sanitize01, ale to siatka bezpieczenstwa, nie licencja);
    ///  - nie mutuje ani candidate, ani context - ten sam kontekst ocenia caly ranking tury;
    ///  - explanation NIGDY nie jest null: przy braku danych zwroc krotkie zdanie, dlaczego
    ///    wartosc jest taka, a nie inna. Pusty slad = dziura w danych ewaluacyjnych.
    ///
    /// NIEZMIENNIK NAZW: Name czynnika == nazwa pola wagi == nazwa wezla XML,
    /// W OBREBIE JEDNEJ PRZESTRZENI (zdarzeniowej albo PASS-owej). Po tej nazwie
    /// UtilityScorer odnajduje wage i po niej nazywaja sie kolumny logu badawczego.
    /// </summary>
    public interface IScoringFactor
    {
        /// <summary>
        /// Stabilny identyfikator czynnika. Zmiana tej nazwy zmienia nazwe kolumny
        /// w danych badawczych i rozspaja czynnik z jego waga - traktowac jak zmiane formatu.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Ocenia kandydata w danym kontekscie i JEDNOCZESNIE zwraca czytelne uzasadnienie.
        /// </summary>
        /// <param name="candidate">Skorupa kandydata (Event, IsPass, SortKey) zbudowana przez UtilityScorer.</param>
        /// <param name="context">Zamrozony kontekst tury - ten sam obiekt dla calego rankingu.</param>
        /// <param name="explanation">Slad: dlaczego wyszla ta wartosc. Nigdy null.</param>
        /// <returns>Wartosc czynnika w [0,1].</returns>
        float Evaluate(ScoredCandidate candidate, DecisionContext context, out string explanation);
    }
}
