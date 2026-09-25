using System.Collections.Generic;
using System.Globalization;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Pytanie zadawane warstwie integracji: "czy ten kandydat moze teraz wypalic?".
    ///
    /// To jest JEDYNY punkt, w ktorym przebieg tury dotyka gry - i dlatego jest delegatem,
    /// a nie wywolaniem. Rdzen nie wie, ze pod spodem jest IncidentDef, IncidentParms ani
    /// CanFireNow; wie tylko, czy kandydat zostal przyjety i - gdy nie - dlaczego.
    ///
    /// Akceptor, ktory zwraca true, ma PRAWO zapamietac po swojej stronie, co wlasnie
    /// przygotowal (incydent i jego parametry). Rdzen tego nie transportuje, bo nie mialby
    /// jak tego opisac bez wciagania typow gry do Core.
    ///
    /// PARAMETR verifyWithEngine ISTNIEJE Z POWODU CACHE'U SILNIKA, nie dla wygody.
    /// Zdekompilowane IncidentWorker.CanFireNow konczy sie tak:
    ///     if (lastCheckCanRunTick == Find.TickManager.TicksGame) return lastCanRunResult;
    ///     lastCheckCanRunTick = Find.TickManager.TicksGame;
    ///     lastCanRunResult = CanFireNowSub(parms);
    /// Pola sa INSTANCYJNE na workerze, a worker jest JEDEN na IncidentDef. Bramki zalezne od
    /// parametrow (min/maxThreatPoints) leza PRZED tym blokiem i wykonuja sie za kazdym razem,
    /// ale CanFireNowSub - czyli caly wlasciwy test wykonalnosci - liczy sie w danym ticku
    /// DOKLADNIE RAZ. Narrator dziala w jednym ticku przez cala ture, wiec drugie pytanie
    /// o ten sam incydent zwraca odpowiedz policzona dla PARAMETROW INNEGO WARIANTU.
    ///
    /// Nie jest to problem teoretyczny: zmierzone na naszym katalogu, CanFireNowSub czyta
    /// parms.points w 3 z 13 payloadow, ale WYNIK od punktow zalezy realnie w JEDNYM -
    /// ManhunterPack (TryFindAggressiveAnimalKind(points)). WandererJoin i RefugeePodCrash
    /// przekazuja punkty do questScriptDef.CanRun, lecz QuestNode_Root_WandererJoin.TestRunInt
    /// zwraca bezwarunkowe true (RefugeePodCrash dziedziczy te metode). Jeden payload wystarczy:
    /// dla niego odpowiedz NAPRAWDE zalezy od wariantu, a cache i tak poda odpowiedz sprzed.
    ///
    /// Stad kontrakt: verifyWithEngine == false znaczy "przygotuj parametry, ale NIE pytaj".
    /// Rdzen podaje false wtedy, gdy o te akcje juz w tej turze pytano - bo ponowne pytanie
    /// nie przyniosloby nowej informacji, tylko starA odpowiedz w swiezym opakowaniu.
    /// </summary>
    /// <summary>Trzy mozliwe odpowiedzi warstwy integracji na pytanie o kandydata.</summary>
    public enum AcceptorVerdict
    {
        /// <summary>Kandydat przyjety - parametry przygotowane, gra go dopuszcza.</summary>
        Accepted,

        /// <summary>Gra odmowila. Powod w rejectionReason.</summary>
        RefusedByGame,

        /// <summary>
        /// ODPOWIEDZI NIE DA SIE UZYSKAC w tym ticku - i to jest wartosc, ktorej brak byl
        /// krytyczna dziura.
        ///
        /// Silnik buforuje CanFireNowSub na parze (IncidentDef, tick) - BEZ celu i BEZ parametrow.
        /// Jesli o ten payload pytano juz w tym ticku przy innym celu albo innych parametrach,
        /// kolejne pytanie zwroci TAMTEN werdykt. Integracja rozpoznaje taka sytuacje i zamiast
        /// podac odpowiedz cudza, przyznaje, ze odpowiedzi nie ma.
        ///
        /// NIE JEST TO ODMOWA GRY: nie liczy sie do odmow silnika ani do budzetu pytan, bo
        /// zadne pytanie nie padlo. Rdzen odklada takiego kandydata do nastepnej tury.
        /// </summary>
        Unanswerable,
    }

    public delegate AcceptorVerdict CandidateAcceptor(ScoredCandidate candidate, bool verifyWithEngine,
                                                      out string rejectionReason);

    /// <summary>
    /// Odcisk PARAMETROW WYKONANIA kandydata: dwa kandydaty o tym samym kluczu trafiaja do gry
    /// z IncidentParms nieodroznialnymi z punktu widzenia CanFireNow.
    ///
    /// PO CO. Potwierdzenie od silnika nalezy do PARAMETROW, nie do klocka akcji ani do calej
    /// kompozycji. Klucz pozwala rdzeniowi wiedziec, ktore warianty moze objac jednym pytaniem
    /// (te o identycznych parametrach - roznia sie wylacznie opisem), a ktore wymagaja pytania
    /// osobnego, ktorego cache silnika w tej turze nie przepusci.
    ///
    /// DLACZEGO TO DELEGAT, A NIE POLE W Core. O tym, co trafia do IncidentParms, wie wylacznie
    /// warstwa integracji - dzis sa to punkty zagrozenia (z intensywnosci) i tekst listu. Gdyby
    /// rdzen sam budowal ten klucz, musialby znac IncidentParms; gdyby zgadywal, rozjechalby sie
    /// po cichu przy pierwszym nowym polu. Implementacja mieszka obok IncidentParmsBuilder.Apply
    /// i ma sie zmieniac RAZEM z nia.
    /// </summary>
    public delegate string ExecutionKeySelector(ScoredCandidate candidate);

    /// <summary>
    /// Odcisk ZAKRESU CACHE'U SILNIKA: dwa kandydaty o tym samym kluczu zakresu trafiaja
    /// w gre w TEN SAM IncidentDef, wiec dziela jeden wpis cache'u CanFireNowSub.
    ///
    /// DLACZEGO TO NIE JEST ActionBlockId. Cache silnika jest kluczowany IncidentDefem, a nie
    /// naszym klockiem akcji. Dwa rozne klocki akcji moga wskazywac na ten sam payload - dzis
    /// w katalogu nie wystepuje to ani razu, ale nic tego nie zabrania, a skutkiem byloby
    /// przebicie OBU warstw obrony naraz: rdzen uznalby je za rozne akcje i zapytal o obie,
    /// a druga odpowiedz przyszlaby zbuforowana.
    ///
    /// Zakres jest WEZSZY od klucza wykonania: sam payload, bez intensywnosci. Klucz wykonania
    /// mowi "czy potwierdzenie sie przenosi", zakres mowi "czy pytanie ma w ogole sens".
    /// </summary>
    public delegate string ScopeKeySelector(ScoredCandidate candidate);

    /// <summary>
    /// Wynik JEDNEJ tury narratora: decyzja ze sladem plus wszystko, czego warstwa integracji
    /// potrzebuje do zalogowania i domkniecia petli.
    /// </summary>
    /// <summary>Czym skonczyla sie faza 0 - i dlaczego to rozroznienie jest konieczne.</summary>
    public enum PreVerificationOutcome
    {
        /// <summary>
        /// Nie bylo czego weryfikowac: pula pusta albo nikt nie przeszedl weta i progu jakosci.
        /// Brama zobaczy to sama i rozstrzygnie cisze z braku materialu.
        /// </summary>
        NoEligibleCandidates,

        /// <summary>
        /// Czolo POTWIERDZONE przez silnik. Referencja bramy jest zdarzeniem, ktore gra dopuszcza -
        /// czyli faza 0 zrobila to, po co istnieje.
        /// </summary>
        Verified,

        /// <summary>
        /// Budzet pytan wyczerpany, ZANIM ktorekolwiek czolo zostalo potwierdzone.
        ///
        /// TO JEST WYNIK TECHNICZNY i musi byc jako taki oznaczony. Brama dostaje wtedy
        /// referencje NIESPRAWDZONA - dokladnie ten stan, ktory faza 0 mial usunac - wiec cisza,
        /// ktora z niej wyniknie, nie jest wyborem narratora, tylko skutkiem niedokonczonej
        /// weryfikacji. Zaliczenie jej do swiadomego milczenia zawyzaloby metryke NIESYMETRYCZNIE,
        /// najmocniej tam, gdzie gra duzo odmawia - czyli ten sam rodzaj obciazenia, ktory
        /// PassReason ma rozdzielac.
        /// </summary>
        BudgetExhausted,
    }

    public class TurnResult
    {
        /// <summary>Decyzja tury - nigdy null. Przy braku zwyciezcy jest to decyzja PASS.</summary>
        public NarratorDecision Decision;

        /// <summary>Powody odmow silnika, w kolejnosci wystapienia. Puste, gdy gra nic nie odmowila.</summary>
        public List<string> Refusals = new List<string>();

        /// <summary>
        /// Stany "to nie powinno sie zdarzyc", ktore rdzen wykryl, ale ktorych nie moze zalogowac
        /// sam - Core nie zna Verse.Log. Warstwa integracji ma je wypisac jako BLEDY, nie jako
        /// informacje: kazdy z nich oznacza usterke okablowania albo padniete zamrozenie bramy.
        /// </summary>
        public List<string> Warnings = new List<string>();

        /// <summary>Ile rund petli wyboru zuzyto.</summary>
        public int Rounds;

        /// <summary>
        /// Ile odmow silnika padlo PRZED brama, czyli w prewerifikacji czola (faza 0).
        /// Prefiks listy Refusals - pierwsze tyle pozycji pochodzi wlasnie stamtad.
        ///
        /// ROZDZIELENIE JEST KONIECZNE, NIE KOSMETYCZNE. PassReason odrozniania cisze swiadoma
        /// od ciszy z bezsily po tym, czy pula zostala okrojona przez silnik. Odmowy fazy 0
        /// zachodza jednak ZANIM brama cokolwiek rozstrzygnie i sa dokladnie po to, zeby brama
        /// zobaczyla czolo MOZLIWE DO ODPALENIA. Cisza wybrana potem jest wiec bardziej swiadoma
        /// niz przedtem, a nie mniej - liczenie tych odmow do eskalacji powodu odwracaloby sens
        /// naprawy i zapalalo falszywy alarm CompetitiveAfterRefusal w kazdej takiej turze.
        /// </summary>
        public int PreGateRefusals;

        /// <summary>
        /// Ile razy w tej turze zapytano SILNIK o wykonalnosc (faza 0 plus rundy wyboru).
        /// Nie liczy przygotowan bez pytania - patrz CandidateAcceptor.verifyWithEngine.
        /// Niezmiennik: nigdy wiecej niz budzet tury, czyli maxSelectionRounds.
        /// </summary>
        public int AcceptorCalls;

        /// <summary>
        /// Ile wariantow odlozono do nastepnej tury ZANIM zapadla decyzja - bo ida do gry z INNYMI
        /// parametrami niz potwierdzone czolo (cache silnika nie pozwala ich w tej turze sprawdzic)
        /// albo bo werdykt o ich payloadzie byl w tym ticku niedostepny.
        ///
        /// Liczy WYLACZNIE odlozenia, ktore mogly zmienic wybor. Do polerowania etapu 4 licznik
        /// dostawal takze odlozenia robione PO przyjeciu zwyciezcy w petli rund - te nie wplywaly
        /// na nic, bo tura juz sie konczyla, a zawyzaly kolumne (zmierzone w danych v5 z waniliowego symulatora debugowego, 33 wiersze: 36 ze 115
        /// odlozen) i przestawialy rodzenstwo zwyciezcy w rankingu na "odlozone", choc ono w wyborze
        /// normalnie uczestniczylo.
        /// </summary>
        public int DeferredVariants;

        /// <summary>
        /// Ile razy w tej turze silnik NIE MOGL dac swiezej odpowiedzi, bo o dany payload pytano
        /// juz w tym ticku (inna mapa, inne parametry). Wartosc wieksza od zera znaczy, ze tura
        /// dzielila tick z inna tura narratora - w grze zachodzi to przy wiecej niz jednej
        /// kolonii, bo Storyteller.MakeIncidentsForInterval iteruje po WSZYSTKICH celach.
        /// </summary>
        public int UnanswerableScopes;

        /// <summary>Jak zakonczyla sie faza 0 - patrz PreVerificationOutcome.</summary>
        public PreVerificationOutcome PreVerification = PreVerificationOutcome.NoEligibleCandidates;

        /// <summary>
        /// Czy potwierdzenie od silnika zostalo WSPOLDZIELONE z innym wariantem tej samej akcji -
        /// wariantem o IDENTYCZNYCH parametrach wykonania, rozniacym sie wylacznie opisem.
        ///
        /// NIE JEST TO JUZ RYZYKO, tylko informacja o oszczedzonym pytaniu. Wczesniejsza wersja
        /// wspoldzielila potwierdzenie z KAZDYM rodzenstwem, takze o innych punktach zagrozenia,
        /// i ta flaga byla wtedy ostrzezeniem o mozliwym niewykonaniu zdarzenia. Odkad warianty
        /// o innych parametrach sa odkladane (patrz ScoredCandidate.UnverifiableThisTurn),
        /// wspoldzielenie zachodzi wylacznie miedzy kandydatami, ktorych IncidentParms sa
        /// nieodroznialne dla CanFireNow.
        ///
        /// Kolumna ZOSTAJE jako kanarek: jesli ktos doda do IncidentParmsBuilder.Apply nowe pole,
        /// nie dopisujac go do klucza wykonania, wspoldzielenie znowu zacznie obejmowac warianty
        /// realnie rozne - a udzial tej kolumny jest jedynym miejscem, gdzie to widac przed
        /// niepowodzeniem TryExecute.
        /// </summary>
        public bool FeasibilityInferred;

        /// <summary>Czy akceptor przyjal ktoregokolwiek kandydata.</summary>
        public bool Accepted;

        /// <summary>
        /// Czy cisza tej tury jest ciszA SWIADOMA, czyli czy ma podniesc licznik serii
        /// (EventHistory.RecordPass(deliberate)). Rozstrzygane TUTAJ, w rdzeniu, a nie w warstwie
        /// integracji - bo to jest regula ksiegowania, a walidator widzi tylko Core.
        ///
        /// Prawda wylacznie dla PassReason.Competitive POZA kryzysem skrajnym. Cisza w kryzysie
        /// jest regula bezpieczenstwa, nie wyborem osobowosci, wiec traktujemy ja jak przeszkode
        /// techniczna: nie zuzywa limitu swiadomego milczenia ani go nie odnawia. Kryzys "pauzuje
        /// zegar" serii - po jego koncu straznik widzi te sama serie co przed nim.
        /// </summary>
        public bool DeliberateSilence;
    }

    /// <summary>
    /// PRZEBIEG JEDNEJ TURY NARRATORA - petla rund wyboru z obsluga odmow silnika.
    ///
    /// DLACZEGO TO ZYJE W Core/, A NIE W WARSTWIE INTEGRACJI. Do kroku 4 wlacznie ta petla
    /// siedziala w StorytellerComp_Generative, czyli w jedynej warstwie, ktorej walidator
    /// offline nie kompiluje. Skutek byl zapisany wprost w CLAUDE.md przy TEST 5d: test
    /// ODWZOROWYWAL te petle zamiast ja wykonywac, wiec dowodzil, ze polityka UMOZLIWIA
    /// poprawne prowadzenie tury - nie ze integracja tak ja prowadzi. Odwzorowanie jest przy
    /// tym testem samego siebie: gdyby petla w grze rozjechala sie z kopia w tescie, obie
    /// wersje nadal bylyby zielone.
    ///
    /// Cena tamtego ukladu byla policzalna. Dwa bledy z rundy polerowania kroku 1 - walidacja
    /// scorera przed jego konstrukcja i szybkie wyjscie sprawdzajace niepelny zestaw pol -
    /// lezaly dokladnie w tej warstwie i nie widzial ich ani kompilator, ani walidator.
    ///
    /// Po przeniesieniu w Integration zostaja TRZY cienkie adaptery: zbudowanie snapshotu,
    /// przygotowanie parametrow incydentu (delegat nizej) i logowanie. Reszta jest testowalna
    /// bez uruchamiania gry.
    ///
    /// KLASA JEST BEZSTANOWA MIEDZY TURAMI. Caly stan tury zyje w zmiennych lokalnych Run(),
    /// wiec dwie tury nie moga sie przez nia zobaczyc - to ten sam kontrakt, ktory maja
    /// czynniki scoringu (IScoringFactor: funkcja czysta).
    /// </summary>
    public class TurnRunner
    {
        private readonly SelectionPolicy policy;
        private readonly PassScoringParams passParams;
        private readonly int maxRounds;

        public TurnRunner(SelectionPolicy policy, PassScoringParams passParams, int maxRounds)
        {
            this.policy = policy;
            this.passParams = passParams ?? PassScoringParams.Defaults();
            this.maxRounds = maxRounds > 0 ? maxRounds : 1;
        }

        public int MaxRounds
        {
            get { return maxRounds; }
        }

        /// <summary>
        /// Prowadzi cala ture: rundy wyboru, odmowy silnika, zamrozenie bramy i statystyk.
        ///
        /// DOWOD ZAKONCZENIA: kazda runda albo konczy petle (kandydat przyjety lub PASS), albo
        /// oznacza jako odrzucona przez silnik CALA AKCJE zwyciezcy, czyli co najmniej jednego
        /// kandydata, ktory wczesniej oznaczony nie byl. Liczba akcji jest skonczona, a Select
        /// na puli samych oznaczonych zwraca PASS, wiec petla zatrzymalaby sie i bez licznika.
        /// maxRounds jest budzetem KOSZTU (akceptor bywa drogi - CanFireNow przeszukuje mape),
        /// nie zabezpieczeniem poprawnosci.
        ///
        /// UWAGA: metoda MUTUJE przekazanych kandydatow - ustawia na nich EngineRefusedThisTurn.
        /// Samej LISTY nie skraca i to jest zmiana wzgledem wersji sprzed prewerifikacji: dzieki
        /// temu kandydat odrzucony przez silnik nadal liczy sie do mianownika i widac go
        /// w rankingu z pelnym sladem czynnikow, takze gdy odmowa padla przed brama.
        /// Kopia LISTY po stronie wolajacego nie jest wiec potrzebna (lista nie jest skracana),
        /// a przed mutacja flag i tak nie chroni - flagi siedza na wspoldzielonych obiektach
        /// ScoredCandidate. Ich zdjeciem na poczatku tury zarzadza ta metoda.
        /// </summary>
        public TurnResult Run(DecisionContext context, List<ScoredCandidate> pool,
                              ScoredCandidate pass, IRandomSource rng,
                              float passDensity, CandidateAcceptor accept,
                              ExecutionKeySelector executionKey, ScopeKeySelector scopeKey)
        {
            var result = new TurnResult();

            // ZDJECIE LEPKICH FLAG Z POPRZEDNIEJ TURY - pierwsza rzecz, jaka robi tura.
            //
            // EngineRefusedThisTurn zyje przez cala ture, wiec ktos musi je kasowac. Wlascicielem
            // jest TurnRunner, a nie wolajacy, i to jest decyzja: w grze kazda tura dostaje swiezo
            // ocenionych kandydatow, wiec kontrakt "wolajacy ma wyczyscic" bylby spelniany
            // przypadkiem i pekl by przy pierwszym wolajacym, ktory pule recyklinguje.
            //
            // Tak wlasnie stalo sie w walidatorze offline, gdzie ta sama lista kandydatow idzie
            // przez kilka symulowanych tur: flagi z tury poprzedniej unieruchamialy pule i P(cisza)
            // szlo z 11.92% na 100%. Awaria byla przy tym CICHA w tym sensie, ze wygladala jak
            // poprawne zachowanie bramy - narrator "slusznie" milczal, bo nie mial czego odpalic.
            for (int i = 0; pool != null && i < pool.Count; i++)
            {
                if (pool[i] != null)
                {
                    pool[i].EngineRefusedThisTurn = false;
                    pool[i].UnverifiableThisTurn = false;
                }
            }

            // Straznik serii liczony RAZ na ture: polityka wyboru celowo nie widzi kontekstu
            // decyzji, bo ma byc funkcja czysta od puli.
            bool straznikSerii = SelectionPolicy.IsPassSuppressedByStreak(context, passParams);

            // Ten sam limit, ale zawieszony przez kryzys skrajny. Liczony osobno tylko po to, zeby
            // trafil do danych - na przebieg tury wplywa wylacznie przez straznikSerii == false.
            bool straznikZawieszony = SelectionPolicy.IsStreakWaivedByCrisis(context, passParams);

            NarratorDecision decyzja = null;

            // STAN TURY, ustalany w rundzie pierwszej i niezmienny do konca tury.
            //
            // brama            - rozstrzygniecie "czy w ogole dzialac". Petla rund jest mechanizmem
            //                    NAPRAWCZYM po odmowie silnika, a nie ciagiem kolejnych decyzji
            //                    narracyjnych, wiec nie wolno jej losowac bramy raz za razem.
            // statystykiTury   - liczniki i ranking z PELNEJ puli. Zamrozenie zostaje mimo tego,
            //                    ze OdrzucAkcje juz nie skraca listy (tylko oznacza kandydatow):
            //                    liczniki weta, progu i pasma nadal liczy sie w KAZDEJ rundzie
            //                    z puli pomniejszonej o oznaczonych, wiec bez zamrozenia
            //                    mianownik metryk malalby z kazda odmowa - obciazenie
            //                    skorelowane z kontekstem, nie losowe.
            // losowaniaTury    - faktyczne zuzycie rng, sumowane po rundach. NIE liczymy go wzorem:
            //                    zrodlem prawdy jest to, co zaraportowal Select.
            GateOutcome brama = null;
            TurnStats statystykiTury = null;
            int losowaniaTury = 0;

            // Kandydat, o ktorego FAKTYCZNIE zapytano silnik i ktory dostal odpowiedz twierdzaca.
            // Null, dopoki zadnego nie potwierdzono. Trzymamy KANDYDATA, a nie samo id akcji,
            // bo odpowiedz silnika nalezy do wariantu - patrz CandidateAcceptor.
            ScoredCandidate potwierdzony = null;
            string potwierdzonyKlucz = null;

            // ================================================================================
            //  FAZA 0 - PREWERIFIKACJA CZOLA, PRZED BRAMA
            // ================================================================================
            //
            // PROBLEM. Brama porownuje uzytecznosc ciszy z BestUtility, czyli z najlepszym
            // zdarzeniem w pasmie. Dopoki czolo bylo brane bez pytania silnika, kandydat
            // strukturalnie niemozliwy do odpalenia podnosil te referencje i po cichu ZANIZAL
            // sklonnosc narratora do milczenia - a potem i tak wypadal przy CanFireNow.
            // Cisza przegrywala wiec z opcja, ktorej nie bylo, i to systematycznie: zmierzone
            // 44 odmowy emanatora na 88 rund w jednym przebiegu, wszystkie z tego samego powodu.
            //
            // NAPRAWA. Zanim brama cokolwiek rozstrzygnie, pytamy akceptor o kolejne CZOLA
            // i usuwamy z gry cale akcje, ktorym silnik odmawia. Gdy petla sie konczy, referencja
            // bramy jest zdarzeniem, ktore gra naprawde dopuszcza - albo nie ma zadnego i brama
            // jest zdegenerowana, co Select rozpozna sam.
            //
            // KOSZT. Pytamy o KAZDA AKCJE NAJWYZEJ RAZ NA TURE - i nie jest to oszczednosc,
            // tylko wymog poprawnosci: drugie pytanie o ten sam incydent w tym samym ticku
            // zwraca zbuforowany wynik CanFireNowSub policzony dla parametrow innego wariantu
            // (pelne wyprowadzenie przy CandidateAcceptor). Efektem ubocznym jest to, ze faza 0
            // kosztuje zwykle 1-2 pytania zamiast tylu, ile wariantow ma odrzucona akcja.
            //
            // BUDZET JEST WSPOLNY DLA CALEJ TURY i wynosi maxRounds. Wczesniejsza wersja dawala
            // fazie 0 osobny licznik rowny maxRounds, wiec tura mogla po cichu wykonac do 2*maxRounds
            // wywolan CanFireNow - limit, ktorego nikt nie zadeklarowal i ktorego nie bylo widac
            // w zadnej kolumnie. Teraz obowiazuje jeden, sprawdzalny niezmiennik:
            // pytanDoGry <= maxSelectionRounds.
            //
            // FAZA 0 NIE ZUZYWA LOSOWOSCI. Jest w calosci deterministyczna, wiec strumien rng
            // nadal zalezy wylacznie od liczby rund.
            while (true)
            {
                ScoredCandidate czolo = policy.TopEligible(pool);
                if (czolo == null)
                {
                    result.PreVerification = PreVerificationOutcome.NoEligibleCandidates;
                    break;
                }

                if (result.AcceptorCalls >= maxRounds)
                {
                    // BUDZET WYCZERPANY BEZ POTWIERDZENIA. Brama dostanie referencje
                    // NIESPRAWDZONA, wiec cala tura jest odtad technicznie podejrzana - patrz
                    // PreVerificationOutcome.BudgetExhausted i PrzypiszPowodCiszy.
                    result.PreVerification = PreVerificationOutcome.BudgetExhausted;
                    result.Warnings.Add("Budzet pytan do gry (" + maxRounds.ToString(CultureInfo.InvariantCulture)
                                        + ") wyczerpany w prewerifikacji, zanim ktorekolwiek czolo "
                                        + "zostalo potwierdzone. Brama dziala na referencji "
                                        + "niesprawdzonej; cisza z tej tury liczy sie jako techniczna. "
                                        + "Jesli to sie powtarza, podnies maxSelectionRounds albo "
                                        + "zaostrz warunki twarde.");
                    break;
                }

                result.AcceptorCalls++;

                string powodCzola;
                AcceptorVerdict werdyktCzola = accept(czolo, true, out powodCzola);

                if (werdyktCzola == AcceptorVerdict.Unanswerable)
                {
                    // Silnik nie moze dac swiezej odpowiedzi o tym payloadzie w tym ticku.
                    // Nie jest to odmowa - nie liczymy jej do odmow ani nie zuzywamy pytania.
                    result.AcceptorCalls--;
                    result.DeferredVariants += OdlozZakres(pool, czolo, scopeKey);
                    result.UnanswerableScopes++;
                    continue;
                }

                if (werdyktCzola == AcceptorVerdict.Accepted)
                {
                    // Czolo potwierdzone. Odpowiedz silnika nalezy do PARAMETROW WYKONANIA,
                    // nie do klocka akcji - wiec zapamietujemy kandydata RAZEM z jego kluczem.
                    potwierdzony = czolo;
                    potwierdzonyKlucz = Klucz(executionKey, czolo);
                    result.PreVerification = PreVerificationOutcome.Verified;

                    // Rodzenstwo o INNYCH parametrach odkladamy do nastepnej tury. Nie da sie go
                    // sprawdzic (cache silnika odda werdykt policzony dla klucza potwierdzonego),
                    // a odpalenie go na kredyt konczy sie zdarzeniem, ktore moze sie nie wykonac -
                    // i ktore mimo to trafi do historii narratora, przesuwajac swiezosc i rytm.
                    // Rodzenstwo o parametrach IDENTYCZNYCH zostaje i nadal konkuruje: rozni sie
                    // samym opisem, a IncidentParms ma co do wartosci takie same.
                    result.DeferredVariants += OdlozNiesprawdzalne(pool, czolo, potwierdzonyKlucz,
                                                                   executionKey, scopeKey);
                    break;
                }

                OdrzucAkcje(pool, czolo, result, scopeKey,
                            string.IsNullOrEmpty(powodCzola) ? "odmowa bez podanego powodu" : powodCzola);
            }

            // Odmowy sprzed bramy sa prefiksem listy Refusals - patrz komentarz przy polu.
            result.PreGateRefusals = result.Refusals.Count;

            while (result.Rounds < maxRounds)
            {
                result.Rounds++;

                // Pasmo near-best przelicza sie w KAZDEJ rundzie od nowa. To jest cala roznica
                // miedzy "powtorz wybor" a "zejdz po rankingu": po usunieciu lidera BestUtility
                // spada do wyniku drugiego, wiec BandThreshold opada razem z nim i wpuszcza
                // kandydatow, ktorzy wczesniej byli poza pasmem. Zamrozone pasmo losowaloby
                // z przedzialu zaczepionego o opcje, ktorej gra wlasnie odmowila.
                // Prog BEZWZGLEDNY (qualityCutoff) nie przelicza sie nigdy i to on, a nie pasmo,
                // jest gwarancja jakosci w kolejnych rundach.
                // UWAGA (przeglad S8 kroku 7): od prewerifikacji czola lider jest sprawdzony w fazie 0 i zostaje dostepny
                // we wszystkich rundach, wiec w praktyce best i pasmo sa stale przez cala ture; przeliczanie zostaje jako
                // bezpiecznik (galaz bledu okablowania z linia [PN-ERR]).
                decyzja = policy.Select(pool, pass, rng, straznikSerii, brama, statystykiTury);
                decyzja.AttachTurnContext(passDensity, context.History.DeliberateSilenceStreak);
                losowaniaTury += decyzja.RandomDraws;

                if (brama == null)
                {
                    // Runda pierwsza jest jedyna, ktora rozstrzyga brame i widzi pelna pule.
                    brama = decyzja.Gate;
                    statystykiTury = decyzja.TurnStats;
                }

                if (decyzja.IsPass)
                {
                    PrzypiszPowodCiszy(decyzja, result);
                    break;
                }

                ScoredCandidate zwyciezca = decyzja.Winner;
                ComposedEvent zdarzenie = zwyciezca == null ? null : zwyciezca.Event;

                if (zdarzenie == null)
                {
                    result.Warnings.Add("Zwyciezca rundy nie ma zlozonego zdarzenia - blad "
                                        + "okablowania warstwy decyzyjnej. Kandydat wypada z puli.");
                    if (zwyciezca != null)
                    {
                        OdrzucAkcje(pool, zwyciezca, result, scopeKey,
                                    "?: kandydat bez zlozonego zdarzenia");
                    }
                    continue;
                }

                // CZY O TE AKCJE JUZ PYTANO W TEJ TURZE?
                // Jesli tak i odpowiedz byla twierdzaca, drugie pytanie jest ZABRONIONE, a nie
                // tylko zbedne: silnik zwrocilby zbuforowany wynik CanFireNowSub policzony dla
                // parametrow potwierdzonego wariantu i podal go jako swiezy werdykt o tym.
                // Prosimy wiec akceptor o samo PRZYGOTOWANIE parametrow.
                bool tozsamy = potwierdzony != null && ReferenceEquals(potwierdzony, zwyciezca);

                // RODZENSTWO WYKONAWCZE: ta sama akcja ORAZ ten sam klucz parametrow. Drugi
                // warunek jest istota naprawy - bez niego wspoldzielilismy potwierdzenie takze
                // z wariantem, ktory idzie do gry z innymi punktami, czyli dokladnie z tym,
                // ktorego wykonalnosc moze byc inna.
                //
                // Wariant o innym kluczu nie ma prawa tu dotrzec: zostal odlozony zaraz po
                // potwierdzeniu czola. Straznik nizej sprawdza to mimo wszystko, bo cicha zmiana
                // w kluczu albo w IncidentParmsBuilder.Apply nie dalaby zadnego innego objawu.
                bool rodzenstwo = !tozsamy && potwierdzony != null
                                  && TenSamZakres(scopeKey, potwierdzony, zwyciezca)
                                  && string.CompareOrdinal(Klucz(executionKey, zwyciezca),
                                                           potwierdzonyKlucz) == 0;

                if (!tozsamy && !rodzenstwo && potwierdzony != null
                    && TenSamZakres(scopeKey, potwierdzony, zwyciezca))
                {
                    // TO NIE POWINNO SIE ZDARZYC. Oznacza, ze odkladanie rodzenstwa przestalo
                    // dzialac albo klucz parametrow rozjechal sie z tym, co robi Apply.
                    result.Warnings.Add("Zwyciezca " + zwyciezca.Label + " nalezy do potwierdzonej "
                                        + "akcji, ale ma INNY klucz parametrow wykonania ("
                                        + (Klucz(executionKey, zwyciezca) ?? "?") + " wobec "
                                        + (potwierdzonyKlucz ?? "?") + "). Odkladanie wariantow "
                                        + "o roznych parametrach nie zadzialalo - kandydat zostaje "
                                        + "odlozony tutaj, zamiast byc odpalonym na kredyt.");
                    zwyciezca.UnverifiableThisTurn = true;
                    zwyciezca.Rejected = RejectionStage.Unverifiable;
                    continue;
                }

                string powod;
                if (tozsamy || rodzenstwo)
                {
                    if (accept(zwyciezca, false, out powod) != AcceptorVerdict.Accepted)
                    {
                        // Przygotowanie bez pytania nie ma prawa zawiesc - to sama arytmetyka
                        // parametrow. Jesli zawiodlo, jest to usterka okablowania, a nie odmowa
                        // gry, wiec nie liczymy jej do odmow silnika.
                        result.Warnings.Add("Przygotowanie parametrow bez pytania do gry zwrocilo "
                                            + "odmowe (" + (powod ?? "bez powodu") + ") dla kandydata "
                                            + zwyciezca.Label + ". To blad okablowania warstwy "
                                            + "integracji, nie decyzja silnika.");
                        // NIE przez OdrzucAkcje: to nie jest odmowa gry, wiec nie moze zasilac
                        // kolumny odmowSilnika ani sugerowac, ze katalog produkuje kandydatow
                        // skazanych na odmowe. Odkladamy caly zakres i idziemy dalej.
                        result.DeferredVariants += OdlozZakres(pool, zwyciezca, scopeKey);
                        continue;
                    }

                    result.Accepted = true;
                    result.FeasibilityInferred = rodzenstwo;
                    break;
                }

                if (result.AcceptorCalls >= maxRounds)
                {
                    // Budzet pytan tury wyczerpany. Nie zgadujemy wykonalnosci nieznanej akcji -
                    // konczymy ture, a klasyfikacja ciszy nizej rozpozna to jako wynik techniczny.
                    result.Warnings.Add("Budzet pytan do gry wyczerpany w petli wyboru; zwyciezca "
                                        + zwyciezca.Label + " nie zostal sprawdzony.");
                    break;
                }

                result.AcceptorCalls++;
                AcceptorVerdict werdykt = accept(zwyciezca, true, out powod);

                if (werdykt == AcceptorVerdict.Unanswerable)
                {
                    result.AcceptorCalls--;
                    result.DeferredVariants += OdlozZakres(pool, zwyciezca, scopeKey);
                    result.UnanswerableScopes++;
                    continue;
                }

                if (werdykt == AcceptorVerdict.Accepted)
                {
                    // BEZ ODKLADANIA RODZENSTWA - i to jest naprawa, nie przeoczenie.
                    //
                    // W fazie 0 odkladanie ma sens: potwierdzone czolo dopiero stanie do wyboru,
                    // a rodzenstwo o innych parametrach nie moze z nim konkurowac na kredyt.
                    // Tutaj wybor JUZ ZAPADL - ta galaz konczy ture. Odkladanie po fakcie nie
                    // zmienialo niczego w decyzji, a (1) zawyzalo kolumne "odlozonych" (w danych
                    // v5 z waniliowego symulatora debugowego 36 ze 115 odlozen pochodzilo stad) i (2) przestawialo rodzenstwo
                    // zwyciezcy w rankingu na RejectionStage.Unverifiable, choc ono w wyborze
                    // normalnie uczestniczylo. Slad decyzji klamal o przebiegu tury.
                    potwierdzony = zwyciezca;
                    potwierdzonyKlucz = Klucz(executionKey, zwyciezca);
                    result.Accepted = true;
                    break;
                }

                OdrzucAkcje(pool, zwyciezca, result, scopeKey,
                            string.IsNullOrEmpty(powod) ? "odmowa bez podanego powodu" : powod);
            }

            if (decyzja == null)
            {
                // Nieosiagalne przy maxRounds >= 1 (konstruktor to wymusza), ale pusta decyzja
                // wywrocilaby log i linie danych, wiec budujemy zastepcza zamiast zwracac null.
                decyzja = AwaryjnaDecyzjaPass(pass,
                    "budzet rund <= 0 - polityka nie zostala uruchomiona ani razu");
            }
            else if (!result.Accepted && !decyzja.IsPass)
            {
                // Petla skonczyla sie bez przyjetego kandydata i bez decyzji o ciszy. Powodow
                // moga byc DWA i myla sie dokladnie na granicy, wiec rozstrzyga o tym stan puli,
                // a nie sam fakt wyjscia z petli.
                //
                // KTORY TO PRZYPADEK, ROZSTRZYGA pool.Count - i to jest naprawa bledu.
                // Poprzednia wersja przypisywala RoundBudgetExhausted bezwarunkowo, wiec gdy
                // liczba kandydatow byla ROWNA budzetowi rund i wszyscy zostali odrzuceni
                // (zmierzone: 8 kandydatow, 8 odmow, maxRounds 8), pula byla juz pusta, a mimo to
                // cisza raportowala "za ciasny budzet". Kolejna runda nie zmienilaby niczego -
                // nie bylo juz kogo wybierac.
                //
                // Pomylka nie jest kosmetyczna, bo te dwie wartosci diagnozuja PRZECIWNE rzeczy
                // i prowadza do przeciwnych napraw:
                //   RoundBudgetExhausted -> budzet rund jest za ciasny, podnies maxSelectionRounds
                //   AllRefusedByGame     -> warunki twarde sa luzniejsze niz wymagania workerow,
                //                           czyli katalog produkuje kandydatow skazanych na odmowe
                // Przypisanie pierwszej tam, gdzie zachodzi druga, kaze stroic nie to pokretlo -
                // a objaw (narrator milczy mimo pelnej puli kandydatow) wyglada identycznie.
                decyzja.Winner = decyzja.PassCandidate ?? pass;

                if (!MaDostepnych(pool))
                {
                    // NIEOSIAGALNE PO PRZEBUDOWIE - i to jest wniosek, nie przypuszczenie.
                    // Zeby pula opustoszala w petli, silnik musi odmowic wszystkim; zeby petla
                    // w ogole pytala, faza 0 musi wczesniej kogos POTWIERDZIC; a potwierdzony
                    // kandydat nigdy nie jest odrzucany (trafia w galaz "tozsamy" i konczy ture).
                    // Gdy z kolei faza 0 nikogo nie potwierdzila, zuzyla caly budzet pytan i petla
                    // nie ma czym odmawiac. Opustoszenie puli objawia sie wiec WEWNATRZ petli:
                    // Select zwraca wtedy PASS/NoCandidates, a PrzypiszPowodCiszy mapuje to na
                    // AllRefusedByGame - i tamta sciezka jest zywa oraz pokryta testem.
                    //
                    // Galaz zostaje jako WYZWALACZ ALARMOWY, tak samo jak CompetitiveAfterRefusal:
                    // jej wykonanie znaczy, ze warunki wyjscia z petli sie zmienily i rozumowanie
                    // wyzej przestalo obowiazywac.
                    result.Warnings.Add("Post-petlowa galaz AllRefusedByGame wykonala sie mimo "
                                        + "przebudowy fazy 0 - warunki wyjscia z petli zmienily sie "
                                        + "i klasyfikacja ciszy wymaga ponownego wyprowadzenia.");
                    decyzja.PassReason = PassReason.AllRefusedByGame;
                    decyzja.PolicyTrace = (decyzja.PolicyTrace ?? string.Empty)
                                          + " | pula wyczerpana odmowami silnika ("
                                          + result.Refusals.Count.ToString(CultureInfo.InvariantCulture)
                                          + " odmow, w tym przed brama "
                                          + result.PreGateRefusals.ToString(CultureInfo.InvariantCulture)
                                          + ")";
                    // pRunda ZOSTAJE PUSTE. Brama NIE wybrala ciszy - tura spadla do PASS-u
                    // mechanicznie, po wyczerpaniu puli. Wpisanie tu prawdopodobienstwa bramy
                    // sugerowaloby losowanie, ktore tego wyniku nie wyprodukowalo.
                    decyzja.WinnerRoundProbability = null;
                }
                else if (result.UnanswerableScopes > 0 && result.AcceptorCalls < maxRounds)
                {
                    // RUNDY ZJEDZONE PRZEZ NIEDOSTEPNE WERDYKTY, a nie przez budzet pytan.
                    //
                    // Werdykt Unanswerable zwraca pytanie (AcceptorCalls--), ale RUNDA juz sie
                    // odbyla - Rounds++ stoi na poczatku petli, a losowania wyboru padly. Tura
                    // moze wiec skonczyc rundy, majac jeszcze budzet pytan i dostepnych kandydatow.
                    // Etykieta RoundBudgetExhausted kazalaby wtedy podniesc maxSelectionRounds,
                    // a przyczyna jest wspoldzielony tick (druga kolonia pytala o te same payloady).
                    // Cofniecie licznika rund w galezi Unanswerable byloby naprawa "u zrodla", ale
                    // rozjechaloby niezmiennik losowan (1 + 2 * rundy) - rundy naprawde losowaly.
                    //
                    // Znalezione fuzzem w przegladzie: 43 takie tury na 200 000 przy 30% payloadow
                    // zajetych przez inna mape. W grze rzadkie i tylko przy wielu koloniach, ale
                    // obie etykiety sa cisza techniczna o PRZECIWNYCH naprawach.
                    decyzja.PassReason = PassReason.VerdictUnavailable;
                    decyzja.PolicyTrace = (decyzja.PolicyTrace ?? string.Empty)
                                          + " | rundy wyboru (" + maxRounds.ToString(CultureInfo.InvariantCulture)
                                          + ") zuzyte na niedostepne werdykty ("
                                          + result.UnanswerableScopes.ToString(CultureInfo.InvariantCulture)
                                          + "), budzet pytan niewyczerpany ("
                                          + result.AcceptorCalls.ToString(CultureInfo.InvariantCulture)
                                          + "), dostepnych kandydatow zostalo "
                                          + LiczbaDostepnych(pool).ToString(CultureInfo.InvariantCulture);
                    decyzja.WinnerRoundProbability = null;
                }
                else
                {
                    // Budzet wyczerpany, a pula WCIAZ ma kandydatow. Trzeci rodzaj ciszy,
                    // rozny i od decyzji o milczeniu, i od wyczerpania puli odmowami.
                    decyzja.PassReason = PassReason.RoundBudgetExhausted;
                    decyzja.PolicyTrace = (decyzja.PolicyTrace ?? string.Empty)
                                          + " | wyczerpano budzet rund wyboru ("
                                          + maxRounds.ToString(CultureInfo.InvariantCulture)
                                          + "), dostepnych kandydatow zostalo "
                                          + LiczbaDostepnych(pool).ToString(CultureInfo.InvariantCulture);
                    // Zwyciezca zmienil sie na PASS, wiec jego prawdopodobienstwo tez musi sie
                    // zmienic. Bez tego kolumna pRunda opisywalaby kandydata, ktory tej tury
                    // NIE wygral - a jest to jedyna kolumna niosaca rozklad rundy koncowej.
                    // Jak wyzej: to nie jest cisza wylosowana, tylko cisza z braku budzetu.
                    decyzja.WinnerRoundProbability = null;
                }
            }

            // Laczne zuzycie losowosci w CALEJ turze, zsumowane z tego, co zaraportowaly kolejne
            // wywolania Select - a nie policzone wzorem. Rozklad po wprowadzeniu wyboru
            // dwustopniowego: runda pierwsza 3 pobrania (brama + akcja + wariant), kazda kolejna
            // 2 (brama jest zamrozona), czyli lacznie 1 + 2 * liczba rund. Faza 0 nie losuje.
            // Wzor trzymamy w komentarzu, bo zrodlem prawdy ma byc pomiar: gdyby ktos zmienil
            // liczbe etapow, suma nadal bedzie zgodna, a zaszyty wzor po cichu falszowalby
            // kolumne badawcza - dokladnie tak, jak robilo to kiedys `= rundy`, ktore zanizalo
            // zuzycie dwukrotnie.
            decyzja.RandomDraws = losowaniaTury;

            decyzja.StreakWaivedByCrisis = straznikZawieszony;
            if (straznikZawieszony)
            {
                decyzja.PolicyTrace = (decyzja.PolicyTrace ?? string.Empty)
                                      + " | straznik serii ZAWIESZONY: kryzys skrajny (seria "
                                      + context.History.DeliberateSilenceStreak.ToString(CultureInfo.InvariantCulture)
                                      + " >= limit " + passParams.maxStreak.ToString(CultureInfo.InvariantCulture) + ")";
            }

            result.DeliberateSilence = decyzja.IsPass
                                       && decyzja.PassReason == PassReason.Competitive
                                       && !(context != null && context.ExtremeCrisis);

            result.Decision = decyzja;
            return result;
        }

        /// <summary>
        /// Rozroznia TRZY rodzaje ciszy. Bez tego metryka swiadomego milczenia liczylaby tez tury,
        /// w ktorych narrator chcial dzialac i nie mogl - a obciazenie byloby skorelowane
        /// z kontekstem, bo liczba odmow zalezy od tego, ile klockow ma warunki slabsze niz
        /// wymagania ich incydentow.
        /// </summary>
        private static void PrzypiszPowodCiszy(NarratorDecision decyzja, TurnResult result)
        {
            // DWA MAPOWANIA, DWA ROZNE UNIWERSA ODMOW - i mylenie ich bylo pulapka, w ktora
            // wpadla pierwsza wersja prewerifikacji czola.
            //
            //  (1) "pula opustoszala przez silnik" liczy WSZYSTKIE odmowy, takze te sprzed bramy.
            //      Cisza z pustej puli jest porazka gry niezaleznie od tego, w ktorej fazie pula
            //      opustoszala - a NoCandidates znaczy "warstwa kompozycji nic nie zwrocila",
            //      czyli cos zupelnie innego i prowadzacego do innej naprawy.
            //
            //  (2) "brama zawiodla" liczy WYLACZNIE odmowy PO bramie. Odmowy fazy 0 padaja, zanim
            //      brama cokolwiek rozstrzygnie, i sluza wlasnie temu, zeby zobaczyla czolo mozliwe
            //      do odpalenia - cisza wybrana po nich jest bardziej swiadoma, nie mniej.
            //      Liczenie ich tutaj zapalaloby ALARM o padnietym zamrozeniu bramy w kazdej turze,
            //      w ktorej prewerifikacja cokolwiek odsiala, czyli w wiekszosci tur.
            // (0) BUDZET PYTAN WYCZERPANY W FAZIE 0 - wynik TECHNICZNY, niezaleznie od odmow.
            //
            // Sprawdzane PRZED wszystkim innym, bo dotyczy samej wiarygodnosci bramy: referencja,
            // z ktora porownala cisze, nie zostala potwierdzona przez silnik. Cisza z takiej tury
            // nie jest wyborem narratora i nie moze zasilac metryki swiadomego milczenia ani
            // licznika serii. Diagnoza jest ta sama co przy wyczerpaniu rund - budzet za ciasny -
            // wiec i powod jest ten sam; faze rozroznia w danych kolumna odmowCzola.
            if (result.PreVerification == PreVerificationOutcome.BudgetExhausted)
            {
                decyzja.PassReason = PassReason.RoundBudgetExhausted;
                decyzja.PolicyTrace = (decyzja.PolicyTrace ?? string.Empty)
                                      + " | budzet pytan do gry wyczerpany w prewerifikacji - "
                                      + "referencja bramy NIESPRAWDZONA, cisza liczona jako techniczna";
                return;
            }

            int odmowyWszystkie = result.Refusals.Count;
            if (odmowyWszystkie == 0 && result.UnanswerableScopes == 0)
            {
                // Pula nigdy nie skurczyla sie przez silnik - powod zostaje taki, jaki ustalila
                // polityka (Competitive, AllVetoed, BelowCutoff, NoCandidates).
                return;
            }

            int odmowyPoBramie = result.Refusals.Count - result.PreGateRefusals;

            if (decyzja.PassReason == PassReason.NoCandidates)
            {
                // Pula opustoszala przez silnik - ale WAZNE, w ktory sposob. Gdy ani razu nie
                // padla odmowa, a puste miejsca zrobily odlozenia, diagnoza jest inna: nie
                // "katalog produkuje kandydatow skazanych na odmowe", tylko "narrator nie mogl
                // dostac werdyktu, bo dzieli tick z inna tura".
                decyzja.PassReason = odmowyWszystkie == 0 && result.UnanswerableScopes > 0
                    ? PassReason.VerdictUnavailable
                    : PassReason.AllRefusedByGame;
            }
            else if (decyzja.PassReason == PassReason.Competitive && odmowyPoBramie > 0)
            {
                // NIEOSIAGALNE od czasu przeniesienia bramy na poziom TURY, i to jest wlasnie zysk
                // z tamtej zmiany: brama zapada w rundzie pierwszej, PRZED jakakolwiek odmowa.
                // Jesli powiedziala "dzialaj", kazda pozniejsza cisza bierze sie z opustoszalej
                // puli, czyli ma powod NoCandidates (mapowany wyzej). Galaz zostaje jako
                // WYZWALACZ ALARMOWY: jej wykonanie oznacza, ze zamrozenie bramy przestalo
                // dzialac i metryka swiadomego milczenia znowu jest zawyzana tam, gdzie gra
                // duzo odmawia.
                decyzja.PassReason = PassReason.CompetitiveAfterRefusal;
                result.Warnings.Add("PassReason.CompetitiveAfterRefusal wystapil mimo bramy "
                                    + "rozstrzyganej raz na ture - zamrozenie bramy nie dziala. "
                                    + "Metryka swiadomego milczenia jest od tej tury obciazona.");
            }
        }

        /// <summary>
        /// Odrzucenie przez silnik gry, w zakresie CALEJ AKCJI odrzuconego kandydata.
        ///
        /// DLACZEGO CALA AKCJA, A NIE JEDEN KANDYDAT - i dlaczego pierwsze uzasadnienie bylo ZLE.
        ///
        /// Pisalem tu wczesniej, ze "wszystkie oprawy tego samego incydentu dostana te sama
        /// odpowiedz, bo jedynymi bramkami zaleznymi od parametrow sa min/maxThreatPoints".
        /// To NIEPRAWDA i obala to dekompilacja: CanFireNowSub czyta parms.points w 3 z 13 naszych
        /// payloadow, a w jednym - ManhunterPack (TryFindAggressiveAnimalKind(points)) - wynik
        /// realnie od nich zalezy (w WandererJoin i RefugeePodCrash TestRunInt zwraca true
        /// bezwarunkowo). Wykonalnosc NAPRAWDE bywa wiec rozna miedzy wariantami tej samej akcji.
        ///
        /// Zakres akcyjny zostaje, ale uzasadnia go co innego - CACHE SILNIKA. CanFireNow zapisuje
        /// wynik CanFireNowSub w polach instancyjnych workera na biezacy tick, a worker jest jeden
        /// na IncidentDef. Po pierwszej odmowie siostrzany wariant dostalby wiec w tej samej turze
        /// odpowiedz policzona dla NIE SWOICH parametrow. Nie mozemy sie dowiedziec, czy jest
        /// wykonalny; mozemy jedynie zapytac i dostac stara odpowiedz w swiezym opakowaniu.
        /// Wobec tego odkladamy cala akcje do nastepnej tury - jest to kierunek OSTROZNY
        /// (odrzucamy byc moze wykonalny wariant), a nie ryzykowny.
        ///
        /// Efekt uboczny jest ten sam co przedtem i nadal pozadany: koniec z osmioma pytaniami
        /// o ten sam incydent. Zmierzone na przebiegu przed naprawa: 64% rund petli wyboru
        /// zmarnowanych, przy czym 44 z 56 odmow dotyczylo jednego incydentu.
        ///
        /// DLACZEGO OZNACZENIE, A NIE USUNIECIE Z LISTY. Wczesniejsza wersja usuwala kandydata
        /// fizycznie, bo Select jest funkcja czysta od puli i przy samym oznaczeniu zwracalby
        /// w kolejnej rundzie tego samego zwyciezce. Select respektuje dzis EngineRefusedThisTurn,
        /// wiec postep petli jest zachowany, a oznaczony kandydat ZOSTAJE w mianowniku i w
        /// rankingu - co przy odmowach sprzed bramy (faza 0) jest jedynym sposobem, zeby wiersz
        /// [PN-DATA] opisywal ture, a nie to, co z niej zostalo.
        /// </summary>
        private static void OdrzucAkcje(List<ScoredCandidate> pool, ScoredCandidate kandydat,
                                        TurnResult result, ScopeKeySelector scopeKey, string powod)
        {
            kandydat.Rejected = RejectionStage.EngineRefused;
            kandydat.EngineRefusedThisTurn = true;
            result.Refusals.Add(powod);

            // ZAKRES IDZIE PO PAYLOADZIE, nie po klocku akcji - bo cache silnika jest kluczowany
            // IncidentDefem. Dwa rozne klocki akcji wskazujace ten sam payload dzielia jeden wpis
            // cache'u; rozroznianie ich tutaj kazaloby narratorowi zapytac o drugi i dostac
            // odpowiedz zbuforowana. Dzis katalog nie ma takiego duplikatu, ale nic go nie zabrania.
            string zakres = Zakres(scopeKey, kandydat);
            if (string.IsNullOrEmpty(zakres) || pool == null)
            {
                return;
            }

            for (int i = 0; i < pool.Count; i++)
            {
                ScoredCandidate inny = pool[i];
                if (inny == null || inny.EngineRefusedThisTurn)
                {
                    continue;
                }
                if (string.CompareOrdinal(Zakres(scopeKey, inny), zakres) == 0)
                {
                    inny.Rejected = RejectionStage.EngineRefused;
                    inny.EngineRefusedThisTurn = true;
                }
            }
        }

        /// <summary>
        /// Odklada do nastepnej tury CALY zakres cache'u (wszystkich kandydatow o tym samym
        /// payloadzie), bo silnik nie moze o nim w tym ticku powiedziec nic swiezego.
        /// Zwraca liczbe odlozonych.
        /// </summary>
        private static int OdlozZakres(List<ScoredCandidate> pool, ScoredCandidate kandydat,
                                       ScopeKeySelector scopeKey)
        {
            if (pool == null || kandydat == null)
            {
                return 0;
            }

            string zakres = Zakres(scopeKey, kandydat);
            int odlozonych = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                ScoredCandidate inny = pool[i];
                if (inny == null || inny.UnavailableThisTurn || inny.Vetoed)
                {
                    continue;
                }
                if (string.CompareOrdinal(Zakres(scopeKey, inny), zakres) != 0)
                {
                    continue;
                }
                inny.UnverifiableThisTurn = true;
                inny.Rejected = RejectionStage.Unverifiable;
                odlozonych++;
            }
            return odlozonych;
        }

        /// <summary>
        /// Klucz parametrow wykonania, odporny na brak delegata i na null.
        /// Brak delegata degraduje do klucza per KANDYDAT (SortKey), czyli do zachowania
        /// najostrozniejszego: zaden wariant nie jest wtedy uznany za wykonawczo rownowazny
        /// z innym, wiec nic nie zostaje odpalone na kredyt.
        /// </summary>
        private static string Klucz(ExecutionKeySelector selector, ScoredCandidate kandydat)
        {
            if (kandydat == null)
            {
                return null;
            }
            if (selector == null)
            {
                return "?" + (kandydat.SortKey ?? kandydat.Label);
            }
            string k = selector(kandydat);
            return string.IsNullOrEmpty(k) ? ("?" + (kandydat.SortKey ?? kandydat.Label)) : k;
        }

        /// <summary>
        /// Odklada do nastepnej tury wszystkie warianty potwierdzonej akcji, ktore ida do gry
        /// z INNYMI parametrami wykonania. Zwraca ich liczbe.
        ///
        /// Oznacza, a nie usuwa - tak samo jak przy odmowie silnika - zeby mianownik i ranking
        /// nadal opisywaly cala ture.
        /// </summary>
        private static int OdlozNiesprawdzalne(List<ScoredCandidate> pool, ScoredCandidate potwierdzony,
                                               string kluczPotwierdzony, ExecutionKeySelector selector,
                                               ScopeKeySelector scopeKey)
        {
            if (pool == null || potwierdzony == null)
            {
                return 0;
            }

            int odlozonych = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                ScoredCandidate inny = pool[i];
                if (inny == null || ReferenceEquals(inny, potwierdzony) || inny.UnavailableThisTurn)
                {
                    continue;
                }

                // ZAWETOWANYCH NIE ODKLADAMY. Weto jest wlasnoscia kandydata i zachodzi niezaleznie
                // od tego, czy gra by go dopuscila; odlozenie go zabieraloby go licznikowi weta,
                // czyli psuloby metryke, ktora z wykonalnoscia nie ma nic wspolnego.
                if (inny.Vetoed)
                {
                    continue;
                }

                if (!TenSamZakres(scopeKey, potwierdzony, inny))
                {
                    continue;
                }
                if (string.CompareOrdinal(Klucz(selector, inny), kluczPotwierdzony) == 0)
                {
                    // Wykonawczo rownowazny - zostaje. Rozni sie samym opisem.
                    continue;
                }

                inny.UnverifiableThisTurn = true;
                inny.Rejected = RejectionStage.Unverifiable;
                odlozonych++;
            }
            return odlozonych;
        }

        /// <summary>
        /// Czy dwaj kandydaci pochodza z tego samego klocka akcji, czyli czy pytanie o jednego
        /// trafiloby w gre w ten sam IncidentDef (a wiec w ten sam cache CanFireNowSub).
        /// Kandydat bez identyfikatora akcji nie jest rodzenstwem NIKOGO - degradacja idzie
        /// w strone ostrozna, czyli w strone zadania pytania.
        /// </summary>
        private static bool TaSamaAkcja(ScoredCandidate a, ScoredCandidate b)
        {
            if (a == null || b == null || a.Event == null || b.Event == null)
            {
                return false;
            }
            string ida = a.Event.ActionBlockId;
            if (string.IsNullOrEmpty(ida))
            {
                return false;
            }
            return string.CompareOrdinal(ida, b.Event.ActionBlockId) == 0;
        }

        /// <summary>
        /// Klucz zakresu cache'u, odporny na brak delegata. Brak degraduje do klocka akcji,
        /// a przy jego braku do pojedynczego kandydata - czyli zawsze w strone OSTROZNA
        /// (wiecej pytan, mniej wspoldzielenia), nigdy w strone kredytu.
        /// </summary>
        private static string Zakres(ScopeKeySelector selector, ScoredCandidate kandydat)
        {
            if (kandydat == null)
            {
                return null;
            }
            if (selector != null)
            {
                string z = selector(kandydat);
                if (!string.IsNullOrEmpty(z))
                {
                    return z;
                }
            }
            if (kandydat.Event != null && !string.IsNullOrEmpty(kandydat.Event.ActionBlockId))
            {
                return "blok:" + kandydat.Event.ActionBlockId;
            }
            return "kand:" + (kandydat.SortKey ?? kandydat.Label);
        }

        /// <summary>Czy dwaj kandydaci dziela jeden wpis cache'u CanFireNowSub w silniku.</summary>
        private static bool TenSamZakres(ScopeKeySelector selector, ScoredCandidate a, ScoredCandidate b)
        {
            string za = Zakres(selector, a);
            return !string.IsNullOrEmpty(za) && string.CompareOrdinal(za, Zakres(selector, b)) == 0;
        }

        /// <summary>Czy w puli zostal choc jeden kandydat, ktoremu silnik jeszcze nie odmowil.</summary>
        private static bool MaDostepnych(List<ScoredCandidate> pool)
        {
            if (pool == null)
            {
                return false;
            }
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null && !pool[i].UnavailableThisTurn)
                {
                    return true;
                }
            }
            return false;
        }

        private static int LiczbaDostepnych(List<ScoredCandidate> pool)
        {
            int n = 0;
            if (pool == null)
            {
                return 0;
            }
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null && !pool[i].UnavailableThisTurn)
                {
                    n++;
                }
            }
            return n;
        }

        private static NarratorDecision AwaryjnaDecyzjaPass(ScoredCandidate pass, string slad)
        {
            var decyzja = new NarratorDecision();
            decyzja.Winner = pass;
            decyzja.PassCandidate = pass;
            decyzja.Ranking = new List<ScoredCandidate>();
            if (pass != null)
            {
                decyzja.Ranking.Add(pass);
                decyzja.PassUtility = pass.Utility;
            }
            decyzja.PassReason = PassReason.RoundBudgetExhausted;
            decyzja.PolicyTrace = slad;
            return decyzja;
        }
    }
}
