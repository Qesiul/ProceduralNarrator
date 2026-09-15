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
    /// </summary>
    public delegate bool CandidateAcceptor(ScoredCandidate candidate, out string rejectionReason);

    /// <summary>
    /// Wynik JEDNEJ tury narratora: decyzja ze sladem plus wszystko, czego warstwa integracji
    /// potrzebuje do zalogowania i domkniecia petli.
    /// </summary>
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

        /// <summary>Czy akceptor przyjal ktoregokolwiek kandydata.</summary>
        public bool Accepted;
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
        /// USUWA dokladnie jednego kandydata z puli. Pula jest skonczona, a Select na pustej puli
        /// zwraca PASS, wiec petla zatrzymalaby sie i bez licznika. maxRounds jest budzetem
        /// KOSZTU (akceptor bywa drogi - CanFireNow przeszukuje mape), nie zabezpieczeniem
        /// poprawnosci.
        ///
        /// UWAGA: metoda MUTUJE przekazana pule (usuwa odrzuconych). Wolajacy ma podac KOPIE
        /// robocza, a nie liste zwrocona przez ScoreAll - tamta musi zostac nietknieta na
        /// potrzeby logu pelnego rankingu.
        /// </summary>
        public TurnResult Run(DecisionContext context, List<ScoredCandidate> pool,
                              ScoredCandidate pass, IRandomSource rng,
                              float passDensity, CandidateAcceptor accept)
        {
            var result = new TurnResult();

            // Straznik serii liczony RAZ na ture: polityka wyboru celowo nie widzi kontekstu
            // decyzji, bo ma byc funkcja czysta od puli.
            bool straznikSerii = SelectionPolicy.IsPassSuppressedByStreak(context, passParams);

            NarratorDecision decyzja = null;

            // STAN TURY, ustalany w rundzie pierwszej i niezmienny do konca tury.
            //
            // brama            - rozstrzygniecie "czy w ogole dzialac". Petla rund jest mechanizmem
            //                    NAPRAWCZYM po odmowie silnika, a nie ciagiem kolejnych decyzji
            //                    narracyjnych, wiec nie wolno jej losowac bramy raz za razem.
            // statystykiTury   - liczniki i ranking z PELNEJ puli. Odrzuc() kurczy pule robocza,
            //                    wiec bez zamrozenia mianownik metryk malalby z kazda odmowa,
            //                    a to obciazenie skorelowane z kontekstem.
            // losowaniaTury    - faktyczne zuzycie rng, sumowane po rundach. NIE liczymy go wzorem:
            //                    zrodlem prawdy jest to, co zaraportowal Select.
            GateOutcome brama = null;
            TurnStats statystykiTury = null;
            int losowaniaTury = 0;

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
                decyzja = policy.Select(pool, pass, rng, straznikSerii, brama, statystykiTury);
                decyzja.AttachTurnContext(passDensity, context.History.ConsecutivePassCount);
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
                        Odrzuc(pool, zwyciezca, result, "?: kandydat bez zlozonego zdarzenia");
                    }
                    continue;
                }

                string powod;
                if (accept(zwyciezca, out powod))
                {
                    result.Accepted = true;
                    break;
                }

                Odrzuc(pool, zwyciezca, result,
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

                if (pool.Count == 0)
                {
                    decyzja.PassReason = PassReason.AllRefusedByGame;
                    decyzja.PolicyTrace = (decyzja.PolicyTrace ?? string.Empty)
                                          + " | pula wyczerpana odmowami silnika w ostatniej rundzie ("
                                          + result.Refusals.Count.ToString(CultureInfo.InvariantCulture)
                                          + " odmow)";
                }
                else
                {
                    // Budzet rund wyczerpany, a pula WCIAZ ma kandydatow. Trzeci rodzaj ciszy,
                    // rozny i od decyzji o milczeniu, i od wyczerpania puli odmowami.
                    decyzja.PassReason = PassReason.RoundBudgetExhausted;
                    decyzja.PolicyTrace = (decyzja.PolicyTrace ?? string.Empty)
                                          + " | wyczerpano budzet rund wyboru ("
                                          + maxRounds.ToString(CultureInfo.InvariantCulture)
                                          + "), w puli zostalo "
                                          + pool.Count.ToString(CultureInfo.InvariantCulture);
                }
            }

            // Laczne zuzycie losowosci w CALEJ turze, zsumowane z tego, co zaraportowaly kolejne
            // wywolania Select - a nie policzone wzorem. Po przeniesieniu bramy na poziom tury
            // rozklad jest taki: runda pierwsza 2 pobrania (brama + wybor), kazda kolejna 1
            // (sam wybor), czyli lacznie 1 + liczba rund. Wzor trzymamy w komentarzu, bo zrodlem
            // prawdy ma byc pomiar: gdyby ktos zmienil liczbe etapow, suma nadal bedzie zgodna,
            // a zaszyty wzor po cichu falszowalby kolumne badawcza - dokladnie tak, jak robilo to
            // kiedys `= rundy`, ktore zanizalo zuzycie dwukrotnie.
            decyzja.RandomDraws = losowaniaTury;

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
            if (result.Refusals.Count == 0)
            {
                // Pula nigdy nie skurczyla sie przez silnik - powod zostaje taki, jaki ustalila
                // polityka (Competitive, AllVetoed, BelowCutoff, NoCandidates).
                return;
            }

            if (decyzja.PassReason == PassReason.NoCandidates)
            {
                // Pula opustoszala WYLACZNIE przez odmowy silnika. To porazka gry, nie decyzja
                // narracyjna.
                decyzja.PassReason = PassReason.AllRefusedByGame;
            }
            else if (decyzja.PassReason == PassReason.Competitive)
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
        /// Odrzucenie kandydata przez silnik gry. USUWA go z puli, a nie tylko oznacza flaga:
        /// Select jest funkcja czysta od puli, wiec przy samym oznaczeniu zwracalby w kazdej
        /// rundzie tego samego zwyciezce az do wyczerpania budzetu - a bez budzetu w nieskonczonosc.
        /// </summary>
        private static void Odrzuc(List<ScoredCandidate> pool, ScoredCandidate kandydat,
                                   TurnResult result, string powod)
        {
            kandydat.Rejected = RejectionStage.EngineRefused;
            pool.Remove(kandydat);
            result.Refusals.Add(powod);
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
