using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Composition
{
    /// <summary>
    /// Warstwa kompozycji (sekcja 5.7). Sklada konkretne wydarzenie z klockow
    /// zgodnie z przepisem, grafem kompatybilnosci i twardymi warunkami kontekstu.
    ///
    /// Sloty w kolejnosci narracyjnej:
    ///     Trigger (opcjonalny) -> Actor -> Action -> Target -> Modifier (opcjonalny)
    /// Kazdy kolejny klocek musi byc zgodny ze WSZYSTKIMI juz wybranymi.
    ///
    /// Dobor klockow nie patrzy na kontekst poza twardymi warunkami - kontekst wchodzi dopiero
    /// przy ocenie gotowego kandydata (ContextEvaluator), zgodnie z podzialem warstw
    /// z koncepcji: kompozycja sklada, warstwa decyzyjna ocenia.
    ///
    /// Klasa ma DWIE sciezki, celowo nierownowazne:
    ///  - TryCompose  - sklada JEDNO losowe wydarzenie (sciezka kroku 1 i 2, uzywana przez
    ///                  walidator offline i zachowana bez zmian semantycznych),
    ///  - EnumerateVariants - zwraca CALA przestrzen wariantow danej akcji albo jej jednostajna
    ///                  probke (sciezka kroku 3: utility AI potrzebuje zbioru kandydatow,
    ///                  a nie jednego wylosowanego).
    ///
    /// Consequence dochodzi w kroku 6, razem z blackboardem.
    /// </summary>
    public class EventComposer
    {
        private static readonly BlockType[] RequiredSlots = { BlockType.Actor, BlockType.Action, BlockType.Target };
        private static readonly BlockType[] OptionalSlots = { BlockType.Trigger, BlockType.Modifier };

        /// <summary>
        /// Kolejnosc, w jakiej ENUMERACJA schodzi po slotach - inna niz narracyjna i inna niz
        /// SlotOrder. Sloty WYMAGANE ida pierwsze, bo tylko one moga zabic cala galaz: pusty
        /// slot wymagany konczy poddrzewo, a pusty opcjonalny je tylko splyca. Ustawienie ich
        /// na poczatku daje najwczesniejsze mozliwe przyciecie, czyli najmniej odwiedzonych wezlow.
        ///
        /// Klocek akcji NIE jest tu wymieniony, bo enumeracja dostaje go z zewnatrz - to on
        /// jest korzeniem drzewa, a nie jednym z jego poziomow.
        ///
        /// Dlugosc tej tablicy jest JEDYNYM zrodlem liczby poziomow drzewa (uzywana wszedzie
        /// zamiast literalu 4). Krok 6 dolozy slot Consequence: wtedy wystarczy dopisac go tutaj
        /// i uzupelnic IsOptional oraz SignatureOrder - reszta przelotu jest od dlugosci zalezna.
        /// </summary>
        private static readonly BlockType[] EnumOrder =
        {
            BlockType.Actor, BlockType.Target, BlockType.Trigger, BlockType.Modifier
        };

        /// <summary>
        /// Kolejnosc slotow w SYGNATURZE - narracyjna, zeby klucz czytalo sie jak zdanie.
        /// Sygnatura jest budowana PETLA po tej tablicy, a nie recznym sklejaniem pieciu
        /// zmiennych: krok 6 dopisze szosty segment i ma to byc zmiana w jednym miejscu.
        /// </summary>
        private static readonly BlockType[] SignatureOrder =
        {
            BlockType.Trigger, BlockType.Actor, BlockType.Action, BlockType.Target, BlockType.Modifier
        };

        /// <summary>
        /// Czy slot opcjonalny (wyzwalacz, modyfikator) moze zostac pusty CELOWO, jako
        /// samodzielny wariant obok wariantow z wypelnionym slotem.
        ///
        /// NIE. Pusty slot opcjonalny powstaje wylacznie jako WYMUSZONY - gdy zbior klockow
        /// tego typu, zgodnych z grafem i przechodzacych twarde warunki, jest pusty. To jest
        /// dokladnie semantyka TryCompose potwierdzona w grze w kroku 2.
        ///
        /// Liczby na dzisiejszym katalogu (policzone recznie i zweryfikowane enumeracja):
        ///     false -> 84 kombinacje,  maksimum na akcje 16 (PN_Akcja_Zrzut)
        ///     true  -> 216 kombinacji, maksimum na akcje 36 (PN_Akcja_Zrzut)
        /// Przy 216 i budzecie 400 wychodzi K = 33 &lt; 36, wiec pierwszy przebieg juz TNIE
        /// najbogatsza akcje i decyzja przestaje byc w 100% enumeracyjna - traci sie wlasnosc,
        /// ze dzisiejszy ranking nie zuzywa ANI JEDNEGO losowania. Regresja migracji schematu
        /// (32 przed = 32 po) tez opiera sie na tej definicji "kombinacji".
        ///
        /// Kontrargument do rozwazenia w kroku 4, zapisany zeby nie zaginal: modyfikatory NIE sa
        /// kosmetyczne - PN_Mod_Noc ma intensity High (+1), PN_Mod_Slabo Low (-1), wiec "bez
        /// modyfikatora" jest mechanicznie odrebnym wariantem o innej wypadkowej sile. Dzis to
        /// nie boli, bo oba modyfikatory sa symetryczne, a poziom 0 jest osiagalny inaczej.
        ///
        /// Dlatego stala nazwana, a nie pole z XML: jedno zachowanie, jeden zestaw testow,
        /// jedna linia do przestawienia, gdy krzywa dramaturgiczna bedzie tego potrzebowac.
        /// </summary>
        private const bool AllowDeliberateEmptyOptional = false;

        /// <summary>
        /// Zawor bezpieczenstwa na katalog przyszlosci: gorne ograniczenie liczby odwiedzonych
        /// LISCI w jednym przelocie. Po jego przekroczeniu przelot jest przerywany, a wynik
        /// oznaczany flaga Truncated.
        ///
        /// Wartosc jest o trzy rzedy wielkosci wyzsza od dzisiejszego maksimum (16) CELOWO.
        /// Cap ustawiony zbyt nisko niepostrzezenie zamienilby jednostajne probkowanie
        /// w zwracanie kanonicznego prefiksu - czyli dokladnie to, czego wymaganie zabrania.
        /// To ma byc zawor, nigdy narzedzie sterowania budzetem; budzetem steruje limit `max`.
        /// Pole, a nie stala, wylacznie po to, zeby test mogl je obnizyc i sprawdzic przerwanie.
        /// </summary>
        public int TraversalCap = 20000;

        private readonly List<Block> catalog;
        private readonly CompatibilityGraph graph;

        /// <summary>Id klocka -> jego pozycja w katalogu kanonicznym. Klucz do macierzy zgodnosci.</summary>
        private readonly Dictionary<string, int> indexById;

        /// <summary>
        /// Macierz zgodnosci po indeksach katalogu, budowana RAZ. compat[i][j] == graph.Allows(...).
        /// Powod w CompatibilityGraph.BuildMatrix: klucze stringowe w petli goracej to setki
        /// tysiecy alokacji na jedna decyzje.
        /// </summary>
        private readonly bool[][] compat;

        public EventComposer(IEnumerable<Block> catalog, CompatibilityGraph graph)
        {
            this.graph = graph ?? new CompatibilityGraph();

            // Katalog przechodzi przez kanonizacje (deduplikacja + sortowanie ordynalne)
            // ZANIM powstanie cokolwiek, co sie do niego indeksuje.
            this.catalog = BuildCanonicalCatalog(catalog);

            indexById = new Dictionary<string, int>(this.catalog.Count, StringComparer.Ordinal);
            for (int i = 0; i < this.catalog.Count; i++)
            {
                indexById[CatalogKey(this.catalog[i])] = i;
            }

            compat = this.graph.BuildMatrix(this.catalog);
        }

        public int CatalogSize
        {
            get { return catalog.Count; }
        }

        /// <summary>
        /// Katalog kanoniczny: bez duplikatow Id i posortowany ORDYNALNIE rosnaco po Id.
        ///
        /// Deduplikacja (pierwszy wygrywa) broni przed duplikatem defName, ktory RimWorld
        /// blokuje, ale wlasny loader walidatora offline juz nie. Duplikat trafilby dwa razy
        /// do puli slotowej i wygenerowal dwa warianty o IDENTYCZNEJ sygnaturze - przechodzace
        /// wszystkie testy legalnosci grafu i psujace unikalnosc klucza kandydata.
        ///
        /// Sortowanie jest wazniejsze i ma skutek uboczny, ktory trzeba nazwac wprost.
        /// Dzis kolejnosc katalogu to kolejnosc ladowania Defow z DefDatabase, a ta zalezy od
        /// listy aktywnych modow. Bez sortowania to samo ziarno daje INNA kompozycje po dolozeniu
        /// obcego moda - cicha dziura w determinizmie, obecna juz dzis w TryCompose przez rng.Pick
        /// po nieuporzadkowanej liscie. Sortowanie ja zamyka, ale ZRYWA ciaglosc ziarna miedzy
        /// krokiem 2 a 3: dla danego ziarna TryCompose wylosuje teraz inny element niz wczesniej.
        /// To swiadoma zmiana zachowania, a nie przypadek. Liczby regresyjne (32 i 84) sie NIE
        /// zmieniaja, bo sa licznosciami zbiorow i od kolejnosci nie zaleza.
        ///
        /// List.Sort jest niestabilny, ale po deduplikacji klucze sa unikalne, wiec porzadek
        /// jest liniowy i wynik jednoznaczny.
        /// </summary>
        private static List<Block> BuildCanonicalCatalog(IEnumerable<Block> source)
        {
            var result = new List<Block>();
            if (source == null)
            {
                return result;
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Block block in source)
            {
                if (block == null)
                {
                    continue;
                }
                if (!seenIds.Add(CatalogKey(block)))
                {
                    continue;
                }
                result.Add(block);
            }

            result.Sort(CompareById);
            return result;
        }

        private static int CompareById(Block x, Block y)
        {
            // Ordynalnie, a nie kulturowo: porzadek ma byc identyczny na kazdej maszynie
            // i przy kazdych ustawieniach regionalnych, bo od niego zalezy determinizm.
            return string.CompareOrdinal(CatalogKey(x), CatalogKey(y));
        }

        /// <summary>Id klocka sprowadzone do postaci uzywalnej jako klucz (null nie jest kluczem).</summary>
        private static string CatalogKey(Block block)
        {
            if (block == null || block.Id == null)
            {
                return string.Empty;
            }
            return block.Id;
        }

        /// <summary>
        /// Sklada wydarzenie albo zwraca null, jesli graf i warunki nie dopuszczaja
        /// zadnej spojnej kombinacji. Snapshot moze byc null - wtedy twarde warunki
        /// nie filtruja (przydatne w testach samej kompozycji).
        /// </summary>
        public ComposedEvent TryCompose(EventRecipe recipe, IRandomSource rng, WorldSnapshot snapshot)
        {
            if (recipe == null || rng == null)
            {
                return null;
            }

            var chosen = new List<Block>();
            var trace = new StringBuilder();
            trace.Append(recipe).Append(" | ");

            // Klocek akcji wybieramy pierwszy, bo to on niesie ladunek wydarzenia
            // i najmocniej zawezia reszte kompozycji.
            Block action = PickCandidate(BlockType.Action, recipe.RequiredActionTag, chosen, rng, snapshot);
            if (action == null)
            {
                return null;
            }
            chosen.Add(action);
            trace.Append("akcja=").Append(action.Id);

            foreach (BlockType slot in RequiredSlots)
            {
                if (slot == BlockType.Action)
                {
                    continue;
                }
                Block block = PickCandidate(slot, null, chosen, rng, snapshot);
                if (block == null)
                {
                    // Brak dopuszczalnego klocka w wymaganym slocie = wydarzenie niespojne.
                    return null;
                }
                chosen.Add(block);
                trace.Append(", ").Append(slot).Append('=').Append(block.Id);
            }

            foreach (BlockType slot in OptionalSlots)
            {
                Block block = PickCandidate(slot, null, chosen, rng, snapshot);
                if (block != null)
                {
                    chosen.Add(block);
                    trace.Append(", ").Append(slot).Append('=').Append(block.Id);
                }
            }

            ComposedEvent composed = Assemble(chosen, action, trace.ToString());
            ContextEvaluator.Evaluate(composed, snapshot);
            return composed;
        }

        /// <summary>Losuje klocek danego typu: zgodny z tagiem, grafem i twardymi warunkami.</summary>
        private Block PickCandidate(BlockType type, string requiredTag, List<Block> chosen,
                                    IRandomSource rng, WorldSnapshot snapshot)
        {
            List<string> chosenIds = chosen.Select(b => b.Id).ToList();

            List<Block> candidates = catalog
                .Where(b => b.Type == type)
                .Where(b => b.HasTag(requiredTag))
                .Where(b => b.IsAvailable(snapshot))
                .Where(b => graph.AllowsWithAll(chosenIds, b.Id))
                .ToList();

            return candidates.Count == 0 ? null : rng.Pick(candidates);
        }

        /// <summary>
        /// Klocki akcji dostepne w tym kontekscie: pasujace do tagu z przepisu i przechodzace
        /// twarde warunki. Kolejnosc kanoniczna, odziedziczona po posortowanym katalogu.
        ///
        /// Swiadomie NIE sprawdzamy tutaj, czy akcja ma niepusta przestrzen wariantow. Wyszloby
        /// to dopiero z enumeracji (jako N_i = 0), a kosztowaloby caly przelot po drzewie - drugi
        /// raz. Niewykorzystany limit takiej akcji i tak wraca do puli w drugim przebiegu budzetu.
        /// </summary>
        public List<Block> AvailableActions(WorldSnapshot snapshot, EventRecipe recipe)
        {
            string requiredTag = recipe != null ? recipe.RequiredActionTag : null;

            var result = new List<Block>();
            for (int i = 0; i < catalog.Count; i++)
            {
                Block block = catalog[i];
                if (block.Type != BlockType.Action)
                {
                    continue;
                }
                if (!block.HasTag(requiredTag))
                {
                    continue;
                }
                if (!block.IsAvailable(snapshot))
                {
                    continue;
                }
                result.Add(block);
            }
            return result;
        }

        /// <summary>
        /// Zwraca warianty JEDNEJ akcji: cala przestrzen, gdy miesci sie w limicie `max`,
        /// albo jednostajna probke rozmiaru `max`, gdy nie.
        ///
        /// Probkowanie to RESERVOIR SAMPLING (algorytm R Vittera) po lisciach drzewa,
        /// z materializacja kandydata dopiero po przyjeciu do zbiornika. Wybor tej metody
        /// jest kluczowy i warto zapisac, co odrzucono:
        ///  - "pierwsze K" zwracaloby zawsze te same klocki (wymaganie tego zabrania),
        ///  - indeksowanie mieszanoradiksowe (i-ty element iloczynu) NIE DZIALA, bo drzewo jest
        ///    poszarpane: rozmiar puli modyfikatorow zalezy od wybranego wczesniej aktora,
        ///  - losowanie K razy z powtorzeniami daje duplikaty i marnuje budzet,
        ///  - pelna enumeracja z tasowaniem jest poprawna, ale materializuje N obiektow, czyli
        ///    placi pelny koszt (Assemble + Evaluate) za KAZDY wariant - a budzet jest budzetem
        ///    OCEN, wiec to lamie jego sens.
        /// Reservoir daje jednostajny podzbior bez powtorzen, jeden przelot, koszt drogi placony
        /// dokladnie Returned razy, a N_i wychodzi za darmo jako licznik odwiedzonych lisci.
        ///
        /// Wlasnosc, na ktorej stoi dzisiejsza odtwarzalnosc: rng.Next jest wolane WYLACZNIE dla
        /// lisci o numerze wiekszym od `max`. Przy N_i &lt;= max enumeracja jest pelna i zuzywa
        /// ZERO losowan, wiec wynik nie zalezy od ziarna.
        /// </summary>
        /// <param name="rng">
        /// Moze byc null - wtedy zbiornik zapelnia sie kanonicznym prefiksem i przestaje przyjmowac,
        /// bez ani jednego siegniecia po losowosc. Tryb dla walidatora offline, ktory liczy same
        /// rozmiary przestrzeni: VariantsSeen nadal jest dokladne.
        /// </param>
        public List<BuiltVariant> SelectVariants(Block action, WorldSnapshot snapshot, int max,
                                                 IRandomSource rng, out VariantEnumerationStats stats)
        {
            var result = new List<BuiltVariant>();
            stats = new VariantEnumerationStats
            {
                ActionId = action != null ? action.Id : null,
                Limit = max
            };

            if (action == null || action.Type != BlockType.Action)
            {
                // Nie ma korzenia drzewa, wiec nie ma przestrzeni do zbadania - i to jest
                // stan wyczerpany, a nie blad. Wolajacy dostaje pusta liste i idzie dalej.
                stats.Exhausted = true;
                stats.Trace = "nie-akcja";
                return result;
            }

            if (max <= 0)
            {
                // FALSE, a nie TRUE: nie zajrzelismy w przestrzen ani razu, wiec twierdzenie
                // o jej wyczerpaniu byloby falszem wpisanym wprost do danych ewaluacyjnych.
                stats.Exhausted = false;
                stats.Trace = "limit<=0";
                return result;
            }

            int actionIndex;
            if (!indexById.TryGetValue(CatalogKey(action), out actionIndex))
            {
                // Klocek akcji spoza katalogu nie ma wiersza w macierzy zgodnosci, wiec nie da
                // sie go poprawnie przyciac. Sytuacja mozliwa tylko w tescie podajacym klocek
                // z palca; Exhausted=false, bo znowu nic nie zbadano.
                stats.Exhausted = false;
                stats.Trace = "akcja spoza katalogu";
                return result;
            }

            var state = new EnumerationState(EnumOrder.Length, max, rng);
            BuildPools(state.Pools, actionIndex, snapshot);

            // Wczesne wyjscie: pusty slot WYMAGANY oznacza, ze akcja nie ma w tym kontekscie
            // ani jednego spojnego wariantu. Przestrzen jest naprawde pusta (Exhausted=true),
            // a rekurencja i tak by tego nie zmienila - tylko kosztowala.
            for (int level = 0; level < EnumOrder.Length; level++)
            {
                if (!IsOptional(level) && state.Pools[level].Count == 0)
                {
                    stats.Exhausted = true;
                    return result;
                }
            }

            Visit(0, state);

            // A6. Kanonizacja: sortujemy ZAWSZE, takze przy pelnej enumeracji. Dzieki temu
            // kolejnosc wyjscia nie zdradza, czy doszlo do probkowania, wiec rozstrzyganie
            // remisow w polityce wyboru i diffy logow miedzy rozgrywkami sa stabilne.
            // Sygnatury sa unikalne (dwie rozne krotki roznia sie co najmniej jednym Id),
            // wiec niestabilnosc List.Sort nie ma na czym zadzialac.
            var built = new List<BuiltVariant>(state.Reservoir.Count);
            for (int i = 0; i < state.Reservoir.Count; i++)
            {
                List<Block> chosen = ToChosenList(state.Reservoir[i], action);
                built.Add(new BuiltVariant(VariantSignature(chosen), chosen));
            }
            built.Sort(CompareBySignature);

            stats.VariantsSeen = state.Seen;
            stats.Returned = built.Count;
            stats.Truncated = state.Truncated;
            stats.Exhausted = !state.Truncated && built.Count == state.Seen;
            stats.RandomDraws = state.Draws;
            if (rng == null)
            {
                stats.Trace = "bezRng";
            }
            return built;
        }

        /// <summary>
        /// A7. JEDYNE miejsce, w ktorym placimy pelny koszt kandydata (Assemble + Evaluate).
        ///
        /// Rozdzielone od SelectVariants, bo rozdzial budzetu wymaga DWOCH przebiegow wyboru:
        /// akcja uciete w przebiegu 1 jest wybierana ponownie z wieksza pojemnoscia w przebiegu 2.
        /// Gdyby materializacja siedziala w srodku wyboru, taka akcja placilaby za oceny DWA RAZY
        /// (raz za odrzucony wybor z przebiegu 1, raz za koncowy), a candidateBudget bylby twardym
        /// limitem dla ZWRACANYCH kandydatow i tylko miekkim dla WYKONANYCH ocen - czyli nie tym,
        /// co deklaruje jego nazwa. Po rozdziale placimy dokladnie raz, za wybor koncowy.
        /// </summary>
        public List<ComposedEvent> MaterializeVariants(Block action, List<BuiltVariant> selection,
                                                       WorldSnapshot snapshot)
        {
            var result = new List<ComposedEvent>();
            if (action == null || selection == null)
            {
                return result;
            }

            string trace = "enumeracja akcja=" + action.Id;
            for (int i = 0; i < selection.Count; i++)
            {
                ComposedEvent composed = Assemble(selection[i].Chosen, action, trace);
                ContextEvaluator.Evaluate(composed, snapshot);
                result.Add(composed);
            }
            return result;
        }

        /// <summary>
        /// Wybor plus materializacja w jednym kroku - wygodne wszedzie tam, gdzie nie ma
        /// dwuprzebiegowego budzetu (walidator offline, testy kompozycji, wywolania z palca).
        /// CandidateGenerator NIE uzywa tej sciezki; on rozdziela oba etapy celowo.
        /// </summary>
        public List<ComposedEvent> EnumerateVariants(Block action, WorldSnapshot snapshot, int max,
                                                     IRandomSource rng, out VariantEnumerationStats stats)
        {
            List<BuiltVariant> selection = SelectVariants(action, snapshot, max, rng, out stats);
            return MaterializeVariants(action, selection, snapshot);
        }

        /// <summary>
        /// Pule slotowe: po jednej na poziom drzewa, wypelniane JEDNYM przelotem po katalogu.
        /// Filtr wzgledem samej akcji (compat[akcja][klocek]) to przyciecie poziomu zerowego -
        /// reszte zgodnosci sprawdza juz rekurencja, wobec wszystkich wybranych klockow.
        /// Pule dziedzicza kanoniczna kolejnosc po posortowanym katalogu i NIE sa sortowane ponownie.
        /// </summary>
        private void BuildPools(List<PooledBlock>[] pools, int actionIndex, WorldSnapshot snapshot)
        {
            bool[] actionRow = compat[actionIndex];
            for (int i = 0; i < catalog.Count; i++)
            {
                Block block = catalog[i];
                int level = SlotIndex(block.Type);
                if (level < 0)
                {
                    // Action (korzen) i Consequence (krok 6) nie sa poziomami tego drzewa.
                    continue;
                }
                if (!actionRow[i])
                {
                    continue;
                }
                if (!block.IsAvailable(snapshot))
                {
                    continue;
                }
                pools[level].Add(new PooledBlock(block, i));
            }
        }

        /// <summary>
        /// Rekurencyjny przelot po drzewie wariantow z PRZYCINANIEM: klocek wchodzi do galezi
        /// dopiero po sprawdzeniu zgodnosci ze wszystkimi juz wybranymi, a nie po zbudowaniu
        /// pelnego iloczynu i odsianiu na koncu.
        ///
        /// Ta roznica jest dzis NIEWIDOCZNA i to jest jej najwieksze ryzyko: wszystkie 52
        /// krawedzie grafu wychodza z klockow akcji, wiec przestrzen jest przypadkiem prostokatna
        /// i naiwny iloczyn dalby te same 84. Pierwsza krawedz miedzy klockami nie-akcji
        /// (aktor-modyfikator, wyzwalacz-cel) ujawni blad dopiero wtedy, gdy nikt juz nie bedzie
        /// patrzyl na ten kod - dlatego test ze sztuczna krawedzia Natura-Noc musi istniec od razu.
        /// </summary>
        private void Visit(int level, EnumerationState state)
        {
            if (state.Truncated)
            {
                return;
            }

            if (level == EnumOrder.Length)
            {
                // LISC: kompletna krotka slotow. Tutaj i tylko tutaj dziala zbiornik.
                state.Seen++;

                if (state.Reservoir.Count < state.Max)
                {
                    state.Reservoir.Add(CloneTuple(state.Current));
                }
                else if (state.Rng != null)
                {
                    // Algorytm R: Next(seen) PO inkrementacji seen. Uzycie starej wartosci albo
                    // Next(seen+1) psuje jednostajnosc w sposob niewidoczny na oko - proba dalej
                    // wyglada losowo i dalej jest deterministyczna, tylko pierwsze liscie sa
                    // systematycznie nadreprezentowane. Lapie to wylacznie test rozproszenia.
                    state.Draws++;
                    int j = state.Rng.Next(state.Seen);
                    if (j < state.Max)
                    {
                        state.Reservoir[j] = CloneTuple(state.Current);
                    }
                }

                if (state.Seen >= TraversalCap)
                {
                    state.Truncated = true;
                }
                return;
            }

            List<PooledBlock> pool = state.Pools[level];
            bool any = false;

            for (int p = 0; p < pool.Count; p++)
            {
                int candidateIndex = pool[p].Index;

                bool ok = true;
                for (int k = 0; k < level; k++)
                {
                    int chosenIndex = state.CurrentIdx[k];
                    // Indeksy juz wybranych trzymamy w rownoleglej tablicy int[], zeby ta petla
                    // - najgoretsza w calym kroku 3 - nie robila lookupu w slowniku po Id.
                    if (chosenIndex >= 0 && !compat[chosenIndex][candidateIndex])
                    {
                        ok = false;
                        break;
                    }
                }
                if (!ok)
                {
                    continue;
                }

                any = true;
                state.Current[level] = pool[p].Block;
                state.CurrentIdx[level] = candidateIndex;
                Visit(level + 1, state);

                if (state.Truncated)
                {
                    state.Current[level] = null;
                    state.CurrentIdx[level] = -1;
                    return;
                }
            }

            state.Current[level] = null;
            state.CurrentIdx[level] = -1;

            // Galaz z PUSTYM slotem. Wchodzimy w nia, gdy nic nie pasowalo (pusty slot wymuszony)
            // - i tylko wtedy, dopoki AllowDeliberateEmptyOptional pozostaje false. Przy pustym
            // slocie WYMAGANYM galaz jest martwa i nie robimy nic: brak aktora albo celu oznacza
            // wariant niespojny, a nie wariant skrocony.
            if (IsOptional(level) && (!any || AllowDeliberateEmptyOptional))
            {
                Visit(level + 1, state);
            }
        }

        /// <summary>
        /// Sygnatura wariantu - JEDYNY klucz kandydata (sortowanie, deduplikacja, remisy, log).
        /// Format: trigId|actorId|actionId|targetId|modId, klocek nieobecny jako "-".
        ///
        /// Metoda nie zaklada, ze lista jest posortowana - klocek kazdego slotu jest wyszukiwany
        /// po typie. Dzieki temu ten sam kod obsluguje krotke z enumeracji i liste z TryCompose,
        /// i nie da sie dostac dwoch roznych kluczy dla tego samego wydarzenia.
        /// </summary>
        public static string VariantSignature(IList<Block> orderedBlocks)
        {
            var sb = new StringBuilder();
            for (int s = 0; s < SignatureOrder.Length; s++)
            {
                if (s > 0)
                {
                    sb.Append('|');
                }
                sb.Append(SlotId(orderedBlocks, SignatureOrder[s]));
            }
            return sb.ToString();
        }

        private static string SlotId(IList<Block> blocks, BlockType type)
        {
            if (blocks != null)
            {
                for (int i = 0; i < blocks.Count; i++)
                {
                    Block block = blocks[i];
                    if (block != null && block.Type == type)
                    {
                        return string.IsNullOrEmpty(block.Id) ? "-" : block.Id;
                    }
                }
            }
            return "-";
        }

        /// <summary>
        /// Sklada gotowego kandydata z wybranych klockow. Porzadkuje je w kolejnosci NARRACYJNEJ
        /// (SlotOrder), ktora jest inna niz kolejnosc enumeracji - opis ma sie czytac jak zdanie,
        /// a nie jak drzewo przeszukiwania.
        ///
        /// Wypelnia tez ActionBlockId i Signature, i robi to dla OBU sciezek (TryCompose oraz
        /// EnumerateVariants), bo oba pola sa kluczami: pierwsze dla czynnika swiezosci,
        /// drugie dla calej reszty. Wypelnienie ich tylko na sciezce enumeracji dawaloby
        /// kandydatow z pustym kluczem w walidatorze offline i cicho zepsuty czynnik w grze.
        /// </summary>
        private static ComposedEvent Assemble(List<Block> chosen, Block action, string trace)
        {
            List<Block> ordered = chosen
                .OrderBy(b => SlotOrder(b.Type))
                .ToList();

            var description = new StringBuilder();
            foreach (Block block in ordered)
            {
                if (!string.IsNullOrEmpty(block.TextFragment))
                {
                    if (description.Length > 0)
                    {
                        description.Append(' ');
                    }
                    description.Append(block.TextFragment);
                }
            }

            IntensityLevel intensity = ContextEvaluator.AggregateIntensity(ordered);
            string signature = VariantSignature(ordered);

            return new ComposedEvent
            {
                Blocks = ordered,
                ActionPayload = action.Payload,
                ActionBlockId = action.Id,
                Signature = signature,
                // Charakter wydarzenia dziedziczymy po klocku akcji - to on decyduje,
                // czym zdarzenie JEST; pozostale sloty je doprecyzowuja.
                Theme = action.Theme,
                Valence = action.Valence,
                Scale = action.Scale,
                Intensity = intensity,
                Description = description.ToString(),
                Trace = trace + " -> intensywnosc=" + intensity + " | sig=" + signature
            };
        }

        /// <summary>Krotka slotow -> lista klockow kandydata (akcja plus niepuste sloty).</summary>
        private static List<Block> ToChosenList(Block[] tuple, Block action)
        {
            var chosen = new List<Block>(tuple.Length + 1);
            chosen.Add(action);
            for (int i = 0; i < tuple.Length; i++)
            {
                if (tuple[i] != null)
                {
                    chosen.Add(tuple[i]);
                }
            }
            return chosen;
        }

        /// <summary>
        /// Kopia krotki robiona WYLACZNIE przy przyjeciu do zbiornika, nigdy przy kazdym lisciu.
        /// Roznica to O(K + K*ln(N/K)) alokacji zamiast O(N) - przy N=6000 i K=4 okolo 1500 razy
        /// mniej smiecia na jedna decyzje narratora.
        /// </summary>
        private static Block[] CloneTuple(Block[] current)
        {
            var copy = new Block[current.Length];
            Array.Copy(current, copy, current.Length);
            return copy;
        }

        private static int CompareBySignature(BuiltVariant x, BuiltVariant y)
        {
            return string.CompareOrdinal(x.Signature, y.Signature);
        }

        /// <summary>Pozycja typu klocka w drzewie enumeracji; -1 dla typow, ktore nie sa poziomami.</summary>
        private static int SlotIndex(BlockType type)
        {
            for (int i = 0; i < EnumOrder.Length; i++)
            {
                if (EnumOrder[i] == type)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Czy slot na tym poziomie wolno zostawic pusty (wyzwalacz i modyfikator).</summary>
        private static bool IsOptional(int level)
        {
            BlockType type = EnumOrder[level];
            return type == BlockType.Trigger || type == BlockType.Modifier;
        }

        private static int SlotOrder(BlockType type)
        {
            switch (type)
            {
                case BlockType.Trigger: return 0;
                case BlockType.Actor: return 1;
                case BlockType.Action: return 2;
                case BlockType.Target: return 3;
                case BlockType.Modifier: return 4;
                default: return 5;
            }
        }

        /// <summary>Klocek z puli razem z jego indeksem w katalogu - zeby nie szukac go ponownie.</summary>
        private struct PooledBlock
        {
            public Block Block;
            public int Index;

            public PooledBlock(Block block, int index)
            {
                Block = block;
                Index = index;
            }
        }

        /// <summary>
        /// WYBRANY wariant przed materializacja: uporzadkowane klocki plus klucz sortowania.
        /// Publiczny, bo CandidateGenerator przenosi wybor miedzy przebiegami budzetu i dopiero
        /// na koncu placi za niego pelny koszt (patrz MaterializeVariants).
        /// </summary>
        public struct BuiltVariant
        {
            public readonly string Signature;
            public readonly List<Block> Chosen;

            public BuiltVariant(string signature, List<Block> chosen)
            {
                Signature = signature;
                Chosen = chosen;
            }
        }

        /// <summary>
        /// Caly stan jednego przelotu w jednym obiekcie. Nie pola klasy EventComposer, bo ten
        /// sam kompozytor obsluguje wiele akcji w jednej turze - stan w polach zamienilby
        /// rekurencje w bombe z opoznionym zaplonem przy pierwszej proble zrownoleglenia.
        /// </summary>
        private class EnumerationState
        {
            public readonly List<PooledBlock>[] Pools;
            public readonly Block[] Current;
            public readonly int[] CurrentIdx;
            public readonly List<Block[]> Reservoir;
            public readonly int Max;
            public readonly IRandomSource Rng;

            public int Seen;
            public int Draws;
            public bool Truncated;

            public EnumerationState(int slots, int max, IRandomSource rng)
            {
                Pools = new List<PooledBlock>[slots];
                for (int i = 0; i < slots; i++)
                {
                    Pools[i] = new List<PooledBlock>();
                }

                Current = new Block[slots];
                CurrentIdx = new int[slots];
                for (int i = 0; i < slots; i++)
                {
                    CurrentIdx[i] = -1;
                }

                // Pojemnosc poczatkowa klamrowana: `max` przychodzi posrednio z budzetu w XML,
                // a rezerwacja listy na absurdalna wartosc padlaby na alokacji, zanim ktokolwiek
                // zdazylby zauwazyc bledna konfiguracje. Zbiornik i tak rosnie sam.
                Reservoir = new List<Block[]>(max > 1024 ? 1024 : max);

                Max = max;
                Rng = rng;
            }
        }
    }
}
