using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Parametry polityki wyboru. camelCase, bo RimWorld mapuje wezly XML na nazwy pol 1:1.
    ///
    /// POLA passUtility TU NIE MA I MIEC NIE MOZE. Uzytecznosc ciszy wyznacza WYLACZNIE
    /// UtilityScorer.ScorePass z wlasnej tablicy czynnikow i wlasnego bloku wag &lt;pass&gt;.
    /// Druga, niezalezna stala w tym miejscu bylaby gwarantowanym cichym rozjazdem: dwie liczby
    /// opisujace to samo, z ktorych tylko jedna faktycznie dziala.
    /// </summary>
    public class SelectionParameters
    {
        /// <summary>
        /// Prog BEZWZGLEDNY. Kandydat slabszy niz to nie wchodzi do losowania nigdy, niezaleznie
        /// od tego, jak slaba jest reszta stawki. To jest jedyna gwarancja "narrator nigdy nie
        /// odpali bzdury" - pasmo near-best jej nie daje, bo jest wzgledne.
        /// </summary>
        public float qualityCutoff = 0.35f;

        /// <summary>
        /// Szerokosc pasma near-best: do losowania wchodza kandydaci w promieniu 25% od najlepszego.
        /// </summary>
        public float nearBestFraction = 0.75f;

        /// <summary>
        /// Temperatura softmaksu. 0.1 jest OSTRE: roznica 0.1 uzytecznosci to czynnik e = 2.72,
        /// roznica 0.3 to czynnik 20. Dobor tej wartosci decyduje o tym, czy narrator jest
        /// przewidywalny (male T) czy rozstrzelony (duze T), i jest glownym zrodlem czulosci
        /// udzialu PASS na liczbe kandydatow w pasmie.
        /// </summary>
        public float softmaxTemperature = 0.1f;

        /// <summary>
        /// Temperatura BRAMY "czy w ogole dzialac" - dwuelementowego softmaksu miedzy
        /// najlepszym dostepnym zdarzeniem a cisza. ODDZIELNA od softmaxTemperature, bo obie
        /// liczby odpowiadaja na rozne pytania i nie ma powodu, zeby dzielily wartosc.
        ///
        /// Powod rozdzialu jest glebszy niz wygoda strojenia: uzytecznosc zdarzenia i uzytecznosc
        /// PASS-a sa normalizowane po ROZLACZNYCH zestawach wag (ScoringWeights kontra
        /// PassScoringParams), wiec ich porownanie jest KONWENCJA kalibrowana empirycznie,
        /// a nie porownaniem wielkosci tej samej natury. Etap B porownuje natomiast zdarzenia
        /// miedzy soba, czyli wielkosci wspolmierne z konstrukcji. Wiazanie tych dwoch
        /// niepewnosci jedna liczba oznaczaloby, ze strojenie udzialu ciszy zmienia przy okazji
        /// ostrosc wyboru miedzy zdarzeniami.
        ///
        /// Wartosc domyslna rowna softmaxTemperature jest CELOWA i tymczasowa: nie mamy jeszcze
        /// danych, ktore uzasadnialyby inna, a wpisanie tu innej liczby "na oko" udawaloby
        /// kalibracje, ktorej nie przeprowadzono. To jest glowne pokretlo udzialu PASS obok
        /// PassScoringParams.densitySaturation - patrz komentarz w Storyteller_Generative.xml.
        /// </summary>
        public float gateTemperature = 0.1f;

        public bool Validate(out string problem)
        {
            var problemy = new List<string>();

            if (float.IsNaN(qualityCutoff) || qualityCutoff < 0f || qualityCutoff > 1f)
            {
                problemy.Add("qualityCutoff poza [0,1]: " + Fmt(qualityCutoff));
            }
            if (float.IsNaN(nearBestFraction) || nearBestFraction <= 0f || nearBestFraction > 1f)
            {
                problemy.Add("nearBestFraction poza (0,1]: " + Fmt(nearBestFraction));
            }
            if (float.IsNaN(softmaxTemperature) || softmaxTemperature <= 0f)
            {
                problemy.Add("softmaxTemperature musi byc dodatnia, jest " + Fmt(softmaxTemperature)
                             + " - polityka zdegeneruje sie do trybu argmax");
            }
            if (float.IsNaN(gateTemperature) || gateTemperature <= 0f)
            {
                problemy.Add("gateTemperature musi byc dodatnia, jest " + Fmt(gateTemperature)
                             + " - brama zdegeneruje sie do trybu argmax, czyli PASS wygra tylko"
                             + " wtedy, gdy przebije NAJLEPSZE zdarzenie");
            }

            if (problemy.Count == 0)
            {
                problem = null;
                return true;
            }

            problem = string.Join("; ", problemy.ToArray());
            return false;
        }

        public string Describe()
        {
            return "qualityCutoff=" + Fmt(qualityCutoff)
                   + " nearBestFraction=" + Fmt(nearBestFraction)
                   + " softmaxTemperature=" + Fmt(softmaxTemperature)
                   + " gateTemperature=" + Fmt(gateTemperature);
        }

        public override string ToString()
        {
            return Describe();
        }

        private static string Fmt(float v)
        {
            return v.ToString("0.0##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Polityka wyboru: z ocenionej stawki plus pseudo-kandydata PASS wyznacza jedna decyzje.
    ///
    /// FILTRY, W TEJ KOLEJNOSCI: weto -> prog bezwzgledny -> pasmo wzgledne.
    /// Nastepnie DWUETAPOWA decyzja: BRAMA "czy w ogole dzialac" -> WYBOR "co konkretnie".
    ///
    /// DLACZEGO PROG PRZED PASMEM: zbior ocalalych jest w obu kolejnosciach TEN SAM (jesli
    /// ktokolwiek przechodzi prog, to przechodzi go takze globalne maksimum, wiec best liczony po
    /// progu rowna sie best liczonemu przed nim; a gdy nikt nie przechodzi, oba warianty daja zbior
    /// pusty). Rozni sie natomiast LOG i INTERPRETACJA: kazdy odrzucony dostaje dokladnie jeden
    /// powod, liczniki sa rozlaczne, a po progu zachodzi best &gt;= 0.35, wiec pasmo nigdy nie
    /// degeneruje sie do "0.75 * smiec = nieco wiekszy smiec".
    ///
    /// PASS JEST ZWOLNIONY Z OBU PROGOW. Nie przechodzi ani przez weto (nie ma contextFit, wiec
    /// nie ma przedmiotu weta), ani przez prog jakosci, ani przez pasmo. Konkuruje WYLACZNIE
    /// w bramie, przeciwko najlepszemu zdarzeniu, ktore te progi przeszlo.
    ///
    /// PASS NIE WCHODZI DO BestUtility i NIE ZWEZA PASMA. Gdyby wchodzil, wysoka uzytecznosc ciszy
    /// wypychalaby realnych kandydatow z puli - czyli PASS tlumilby wydarzenia DWOMA mechanizmami
    /// naraz i zdobylby weto tylnymi drzwiami, wbrew decyzji "weto ma wylacznie contextFit".
    /// Dodatkowo wplyw parametrow PASS na liczbe wydarzen przestalby byc rozdzielny od
    /// nearBestFraction, czyli ewaluacja stalaby sie niemozliwa do zinterpretowania.
    ///
    /// DLACZEGO BRAMA, A NIE JEDNA WSPOLNA PULA (zmiana wobec pierwotnej wersji kroku 3).
    /// Wczesniej PASS wchodzil do TEJ SAMEJ puli softmaksu co zdarzenia, wiec jego udzial byl
    /// rozcienczany ich licznoscia: przy 15 kandydatach cisza miala kilkakrotnie wieksza szanse
    /// niz przy 37, mimo IDENTYCZNEJ sytuacji w kolonii. Udzial swiadomego milczenia zalezal
    /// zatem od ROZMIARU KATALOGU KLOCKOW, a nie od stanu rozgrywki - dosypanie klockow po cichu
    /// czynilo narratora mniej sklonnym do ciszy. Zmierzone na danych z gry (8 decyzji): udzial
    /// PASS 0.25%, czyli jeden PASS na okolo 405 decyzji (~1000 dni gry), przy PELNEJ sprawnosci
    /// samego mechanizmu gestosci (passWynik rosl 0.250 -> 0.489 wraz z zageszczeniem zdarzen).
    /// Koncepcja wymaga natomiast, by PASS byl pelnoprawna decyzja sterujaca tempem.
    ///
    /// Rozdzielenie "czy dzialac" od "co zrobic" to standardowy wzorzec utility AI
    /// (Mark &amp; Dill: decision score kontra option score). Brama widzi DOKLADNIE DWIE opcje,
    /// wiec jej wynik zalezy wylacznie od tego, ile warta jest cisza wobec NAJLEPSZEGO dostepnego
    /// zdarzenia - i jest z konstrukcji niewrazliwa na licznosc puli.
    ///
    /// SELECT JEST FUNKCJA CZYSTA OD PULI I NIE MUTUJE LISTY WEJSCIOWEJ. Warstwa integracji podaje
    /// te sama liste w kolejnych rundach (po odmowie silnika usuwa z niej zwyciezce), wiec sortowanie
    /// w miejscu albo usuwanie z oryginalu dawaloby blad zalezny od numeru rundy.
    /// </summary>
    /// <summary>
    /// Rozstrzygniecie BRAMY "czy w ogole dzialac". Zapada RAZ NA TURE i jest przekazywane
    /// niezmienione do kolejnych rund petli wyboru.
    ///
    /// DLACZEGO RAZ NA TURE, A NIE RAZ NA RUNDE. Petla rund istnieje wylacznie po to, by
    /// naprawic odmowe silnika (CanFireNow) - jest mechanizmem NAPRAWCZYM, nie kolejna decyzja
    /// narracyjna. Gdyby brama losowala sie w kazdej rundzie, to po kazdej odmowie zwyciezca
    /// znikalby z puli, BestUtility spadaloby, uzytecznosc ciszy zostawalaby staia - i brama
    /// dostawalaby kolejne, coraz korzystniejsze dla PASS losowanie. Faktyczne P(cisza) w turze
    /// wynosiloby wtedy 1 - iloczyn(1 - p_i), czyli wielkosc rosnaca z LICZBA ODMOW SILNIKA.
    /// A liczba odmow jest funkcja tego, ile klockow ma warunki twarde slabsze niz wymagania ich
    /// IncidentWorkera - czyli zaleznosc udzialu ciszy od ZAWARTOSCI KATALOGU wracalaby pietro
    /// wyzej, dokladnie ta, ktora brama zostala wprowadzona, zeby usunac.
    ///
    /// Skutek uboczny i pozadany: gdy brama powiedziala "dzialaj", zadna pozniejsza cisza nie
    /// moze byc juz policzona jako SWIADOME MILCZENIE - jest porazka silnika. Patrz PassReason.
    /// </summary>
    public class GateOutcome
    {
        /// <summary>Brama wybrala cisze.</summary>
        public bool ChoseSilence;

        /// <summary>
        /// P(brama wybiera cisze) w tej turze - kolumna pBrama. To NIE jest zrealizowany udzial
        /// ciszy: tura, w ktorej brama powiedziala "dzialaj", moze skonczyc sie cisza techniczna
        /// (odmowy, budzet, niedostepne werdykty). Udzial ciszy liczy sie z decyzja=PASS
        /// z podzialem na powodPass; srednia pBrama - po turach bez ciszy technicznej.
        /// </summary>
        public float PassProbability;

        /// <summary>Straznik serii wygasil PASS (brama nie losowala).</summary>
        public bool SuppressedByStreak;

        /// <summary>Pula zdarzen byla pusta, wiec brama nie miala z czym konkurowac.</summary>
        public bool Degenerate;
    }

    /// <summary>
    /// Wielkosci opisujace CALA TURE, zamrazane na PELNEJ puli w rundzie pierwszej.
    ///
    /// POWOD ISTNIENIA. Warstwa integracji po odmowie silnika FIZYCZNIE USUWA kandydata z puli
    /// (inaczej Select, bedac funkcja czysta od puli, zwracalby w kolejnej rundzie tego samego
    /// zwyciezce). Bez zamrozenia kazda kolejna runda liczylaby swoje statystyki z coraz
    /// mniejszego zbioru, a do logu trafialaby runda OSTATNIA - czyli kolumny kandydatow,
    /// zawetowanych, odrzuconeCutoff, odrzuconePasmo, wSoftmaksie, best i pasmo opisywalyby
    /// mniejszy problem decyzyjny niz ten, ktory narrator faktycznie rozwiazywal.
    ///
    /// To jest obciazenie SKORELOWANE Z KONTEKSTEM (kurczy sie tam, gdzie gra duzo odmawia),
    /// czyli ta sama klasa bledu, przed ktora broni PassReason.CompetitiveAfterRefusal.
    /// Mianownik metryk z rozdzialu o ewaluacji musi opisywac TURE, nie ostatnia runde.
    /// </summary>
    public class TurnStats
    {
        public int CountScored;
        public int CountVetoed;
        public int CountBelowCutoff;
        public int CountBelowBand;
        public int CountInSoftmax;
        public float BestUtility;
        public float BandThreshold;

        /// <summary>
        /// Ilu kandydatow W PASMIE rundy pierwszej mialo wartosc lukowa 1 (krok 5); -1, gdy luk
        /// sie w tej turze wstrzymal (kolumna lukWPasmie pusta). Metryka ograniczenia fazy 0:
        /// dopasowany wariant akcji o innej mocy niz czolo bywa w turze odlozony.
        /// </summary>
        public int ArcMatchedInBand = -1;

        /// <summary>
        /// Ranking z PELNEJ puli tury wraz z PASS. Kandydaci odrzuceni pozniej przez silnik
        /// zostaja w nim jako te same referencje, wiec ich RejectionStage.EngineRefused i pelne
        /// rozbicie na czynniki NIE GINIE po usunieciu z puli roboczej.
        /// </summary>
        public List<ScoredCandidate> Ranking;
    }

    public class SelectionPolicy
    {
        private readonly SelectionParameters parameters;

        public SelectionPolicy(SelectionParameters parameters)
        {
            this.parameters = parameters ?? new SelectionParameters();
        }

        public SelectionParameters Parameters
        {
            get { return parameters; }
        }

        /// <summary>
        /// Straznik serii PASS-ow: ubezpieczenie przed zla konfiguracja XML, a nie normalny tryb
        /// pracy (przy domyslnych parametrach nie powinien nigdy zadzialac).
        ///
        /// Liczony jest TUTAJ, a nie w Select, bo Select celowo nie widzi DecisionContext -
        /// polityka ma byc funkcja samej puli. TurnRunner liczy predykat raz na ture i podaje go
        /// do Select jako flage.
        /// </summary>
        public static bool IsPassSuppressedByStreak(DecisionContext context, PassScoringParams passParams)
        {
            return IsStreakAtLimit(context, passParams) && !context.ExtremeCrisis;
        }

        /// <summary>
        /// Czy seria swiadomej ciszy doszla do limitu - NIEZALEZNIE od tego, czy straznik zadziala.
        /// Jedno zrodlo predykatu dla obu pytan ponizej, zeby "straznik stlumil PASS" i "straznik
        /// zostal zawieszony" nie mogly rozjechac sie w definicji limitu.
        /// </summary>
        public static bool IsStreakAtLimit(DecisionContext context, PassScoringParams passParams)
        {
            if (context == null || context.History == null || passParams == null)
            {
                return false;
            }
            if (passParams.maxStreak <= 0)
            {
                return false;
            }
            return context.History.DeliberateSilenceStreak >= passParams.maxStreak;
        }

        /// <summary>
        /// Czy straznik serii ZOSTAL ZAWIESZONY przez kryzys skrajny - czyli limit osiagniety,
        /// ale narrator nie jest zmuszany do dzialania (decyzja autora po przegladzie etapu 4).
        ///
        /// Straznik istnieje po to, zeby narrator nie milczal w nieskonczonosc Z WYBORU. W kryzysie
        /// skrajnym cisza jest regula bezpieczenstwa, nie wyborem osobowosci, a zmuszanie narratora
        /// do wydarzenia w chwili, gdy polowa kolonii lezy, dzialaloby dokladnie wbrew intencji
        /// Breathe. Zawieszenie dotyczy WYLACZNIE kryzysu; poza nim straznik dziala jak przedtem.
        /// Osobne pole w danych (straznikZawieszony), bo bez niego tura z zawieszonym straznikiem
        /// wygladalaby identycznie jak tura ponizej limitu.
        /// </summary>
        public static bool IsStreakWaivedByCrisis(DecisionContext context, PassScoringParams passParams)
        {
            return IsStreakAtLimit(context, passParams) && context.ExtremeCrisis;
        }

        public NarratorDecision Select(IReadOnlyList<ScoredCandidate> candidates, ScoredCandidate pass, IRandomSource rng)
        {
            return Select(candidates, pass, rng, false);
        }

        /// <param name="passSuppressedByStreak">
        /// Wynik IsPassSuppressedByStreak. Dziala WYLACZNIE przy niepustej puli zdarzen: sciezka
        /// awaryjna ma pierwszenstwo nad straznikiem, bo straznik ma ograniczac milczenie Z WYBORU,
        /// a nie zmuszac narratora do odpalenia wydarzenia, ktorego nie ma.
        /// </param>
        public NarratorDecision Select(IReadOnlyList<ScoredCandidate> candidates, ScoredCandidate pass,
                                       IRandomSource rng, bool passSuppressedByStreak)
        {
            return Select(candidates, pass, rng, passSuppressedByStreak, null, null);
        }

        /// <param name="frozenGate">
        /// Rozstrzygniecie bramy z RUNDY PIERWSZEJ tej tury, albo null w rundzie pierwszej.
        /// Podane - brama NIE losuje ponownie (zuzycie rng spada z 3 na 2 w tej rundzie).
        /// </param>
        /// <param name="frozenStats">
        /// Statystyki PELNEJ puli z rundy pierwszej, albo null w rundzie pierwszej. Podane -
        /// liczniki i ranking w zwroconej decyzji opisuja TURE, a nie okrojona pule biezacej rundy.
        /// UWAGA: PassReason liczy sie mimo to z wartosci ZYWYCH, bo odpowiada na pytanie
        /// "dlaczego TERAZ nie ma czego odpalic", a nie "jak szeroka byla stawka na poczatku tury".
        /// </param>
        public NarratorDecision Select(IReadOnlyList<ScoredCandidate> candidates, ScoredCandidate pass,
                                       IRandomSource rng, bool passSuppressedByStreak,
                                       GateOutcome frozenGate, TurnStats frozenStats)
        {
            var decyzja = new NarratorDecision();
            var slad = new StringBuilder();

            // ---- C0. Straz wejscia ----
            ScoredCandidate passKandydat = pass;
            if (passKandydat == null)
            {
                // PASS musi istniec ZAWSZE, inaczej przy pustej puli nie ma czego zwrocic.
                // Awaryjny egzemplarz ma uzytecznosc 0 i PUSTY slad: pusty, bo zadnego pomiaru nie
                // bylo, a wpisanie tu wartosci udawaloby dane, ktorych nie zebrano. Uzytecznosc 0
                // jest bezpieczna - przy pustej puli PASS i tak wygra sciezka awaryjna, a przy
                // niepustej praktycznie nigdy nie wygra, wiec blad okablowania widac w logu,
                // zamiast byc po cichu skompensowanym.
                passKandydat = EmergencyPass();
                slad.Append("PASS AWARYJNY (scorer nie podal pseudo-kandydata); ");
            }
            passKandydat.Rejected = RejectionStage.None;
            if (frozenStats == null)
            {
                // Prawdopodobienstwa naleza do TURY, nie do rundy (patrz C7). W rundach dalszych
                // zerowanie skasowaloby zamrozona wartosc PASS-a i suma p po rankingu przestalaby
                // wynosic 1 - dokladnie ten defekt, ktory C7 naprawia po stronie zdarzen.
                passKandydat.SelectionProbability = 0f;
            }

            // ---- C1. Kopia i sortowanie deterministyczne ----
            // Sortowanie po (Utility malejaco, SortKey rosnaco ordynalnie) sprawia, ze wynik NIE
            // zalezy od kolejnosci wejscia: generator kandydatow moze zmienic kolejnosc, a decyzja
            // pozostanie ta sama. Porownanie ordynalne, nie kulturowe - string.Compare wrazliwy na
            // locale daje inna kolejnosc na innej maszynie i lamie determinizm mimo tego samego ziarna.
            var lista = new List<ScoredCandidate>(candidates == null ? 0 : candidates.Count);
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    ScoredCandidate k = candidates[i];
                    if (k == null)
                    {
                        continue;
                    }
                    if (k.IsPass)
                    {
                        // PASS w liscie kandydatow realnych to blad okablowania: wszedlby do
                        // BestUtility i zdobyl weto tylnymi drzwiami. Odfiltrowujemy i odnotowujemy.
                        slad.Append("UWAGA: PASS w liscie kandydatow realnych - pominiety; ");
                        continue;
                    }

                    // Pola etapowe sa wlasnoscia RUNDY i kazda runda zaczyna od czystego stanu -
                    // z DWOMA wyjatkami, ktore sa wlasnoscia TURY i musza przezyc kasowanie:
                    //
                    //  (1) EngineRefusedThisTurn. Odmowa silnika dotyczy calej akcji i obowiazuje
                    //      do konca tury. Kandydat nia oznaczony zostaje w liscie (wiec liczy sie
                    //      do mianownika i widac go w rankingu z pelnym sladem czynnikow), ale nie
                    //      wchodzi do zadnej puli wyboru - patrz C2.
                    //  (2) SelectionProbability w rundach dalszych - patrz C7.
                    k.Rejected = k.EngineRefusedThisTurn
                        ? RejectionStage.EngineRefused
                        : (k.UnverifiableThisTurn ? RejectionStage.Unverifiable : RejectionStage.None);
                    if (frozenStats == null)
                    {
                        k.SelectionProbability = 0f;
                    }
                    lista.Add(k);
                }
            }

            int liczbaOcenionych = lista.Count;
            lista.Sort(CompareCandidates);

            // ---- C2. ETAP 0: weto ----
            // Etap jest formalnie zbedny (zawetowany ma Utility 0, wiec i tak nie przeszedlby progu),
            // ale rozdziela w danych "bez sensu tutaj" od "za slaby". To sa dwa rozne zjawiska
            // narracyjne i zlanie ich w jeden licznik zafalszowaloby metryki z rozdzialu o ewaluacji.
            var poWecie = new List<ScoredCandidate>(lista.Count);
            int zawetowanych = 0;
            int odrzuconychPrzezSilnik = 0;
            int odlozonych = 0;
            for (int i = 0; i < lista.Count; i++)
            {
                ScoredCandidate k = lista[i];

                // Odmowa silnika WYPRZEDZA weto i prog jakosci. Kandydat, ktoremu gra juz w tej
                // turze odmowila, nie jest "bez sensu tutaj" ani "za slaby" - jest NIEDOSTEPNY.
                // Wliczenie go do ktoregokolwiek z tamtych dwoch licznikow zafalszowaloby metryki
                // weta i progu, i to niesymetrycznie: najmocniej tam, gdzie gra duzo odmawia.
                if (k.EngineRefusedThisTurn)
                {
                    odrzuconychPrzezSilnik++;
                    continue;
                }

                // ODLOZONY TO NIE ODRZUCONY - osobny licznik, bo diagnozuje co innego.
                // Zlanie obu w jeden kazaloby zaostrzac warunki twarde tam, gdzie problemem jest
                // cache silnika, a nie katalog.
                if (k.UnverifiableThisTurn)
                {
                    odlozonych++;
                    continue;
                }

                if (k.Vetoed)
                {
                    k.Rejected = RejectionStage.Veto;
                    zawetowanych++;
                }
                else
                {
                    poWecie.Add(k);
                }
            }

            // ---- C3. ETAP 1: prog jakosci (BEZWZGLEDNY) ----
            var poProgu = new List<ScoredCandidate>(poWecie.Count);
            int ponizejProgu = 0;
            for (int i = 0; i < poWecie.Count; i++)
            {
                ScoredCandidate k = poWecie[i];
                if (ScoreMath.AtLeast(k.Utility, parameters.qualityCutoff))
                {
                    poProgu.Add(k);
                }
                else
                {
                    k.Rejected = RejectionStage.QualityCutoff;
                    ponizejProgu++;
                }
            }

            // ---- C4. ETAP 2: pasmo near-best (WZGLEDNE) ----
            // Maksimum liczymy jawna petla po kandydatach REALNYCH. Po sortowaniu wystarczylby
            // element zerowy, ale jawna petla nie zaklada niczego o stabilnosci sortowania
            // i od razu widac, ze PASS w tym maksimum NIE uczestniczy.
            float best = 0f;
            for (int i = 0; i < poProgu.Count; i++)
            {
                if (i == 0 || poProgu[i].Utility > best)
                {
                    best = poProgu[i].Utility;
                }
            }

            float progPasma = parameters.nearBestFraction * best;

            var pulaZdarzen = new List<ScoredCandidate>(poProgu.Count);
            int ponizejPasma = 0;
            for (int i = 0; i < poProgu.Count; i++)
            {
                ScoredCandidate k = poProgu[i];
                if (ScoreMath.AtLeast(k.Utility, progPasma))
                {
                    pulaZdarzen.Add(k);
                }
                else
                {
                    k.Rejected = RejectionStage.NearBestBand;
                    ponizejPasma++;
                }
            }

            // ---- C5. ETAP A: BRAMA "czy w ogole dzialac" ----
            //
            // Brama porownuje uzytecznosc ciszy z BestUtility, czyli z najlepszym zdarzeniem,
            // ktore przeszlo weto, prog i pasmo. Referencja jest MAKSIMUM, a nie jakakolwiek
            // agregacja calej puli (np. log-sum-exp): kazda wielkosc rosnaca z licznoscia
            // przywrocilaby dokladnie te zaleznosc udzialu PASS od rozmiaru katalogu, ktora
            // ten etap usuwa.
            //
            // LICZBA POBRAN Z GENERATORA JEST STALA: trzy w rundzie pierwszej (brama, akcja,
            // wariant), dwie w kazdej kolejnej (brama zamrozona) - takze wtedy, gdy ktorys etap
            // jest zdegenerowany i jego wynik zostaje zignorowany (patrz BurnDraw).
            // Powod jest ten sam, dla ktorego wersja jednoetapowa losowala zawsze raz: strumien
            // losowy ma zalezec WYLACZNIE od liczby rund. Gdyby zalezal od tresci puli, dwa
            // przebiegi ewaluacji rozniace sie jednym kandydatem rozjechalyby caly dalszy
            // strumien i przestaly byc porownywalne.
            int losowan = 0;
            bool straznikZadzialal = passSuppressedByStreak && pulaZdarzen.Count > 0;

            double pBramaPass;
            bool bramaDalaPass;
            GateOutcome brama;

            if (frozenGate != null)
            {
                // RUNDA DALSZA. Brama zapadla juz w rundzie pierwszej i NIE jest losowana ponownie -
                // to jest cala istota poprawki. Ta runda zuzyje wiec tylko JEDNO pobranie (etap B).
                brama = frozenGate;
                pBramaPass = frozenGate.PassProbability;
                bramaDalaPass = frozenGate.ChoseSilence;
                slad.Append("brama z rundy 1 (nie losowana ponownie); ");
            }
            else if (pulaZdarzen.Count == 0)
            {
                // Nie ma z czym konkurowac. To cisza z BRAKU MATERIALU, nie z wyboru -
                // rozroznia je PassReason ustawiany w C10.
                pBramaPass = 1.0;
                bramaDalaPass = true;
                losowan += BurnDraw(rng);
                slad.Append("brama pominieta (pula zdarzen pusta); ");
                brama = new GateOutcome { ChoseSilence = true, PassProbability = 1f, Degenerate = true };
            }
            else if (straznikZadzialal)
            {
                pBramaPass = 0.0;
                bramaDalaPass = false;
                losowan += BurnDraw(rng);
                slad.Append("PASS wygaszony przez straznika serii; ");
                brama = new GateOutcome { ChoseSilence = false, PassProbability = 0f, SuppressedByStreak = true };
            }
            else
            {
                // Kolejnosc {zdarzenie, PASS} jest ISTOTNA przy temperaturze dazacej do zera:
                // SoftmaxWeights klade cala mase na PIERWSZE maksimum, wiec dokladny REMIS
                // rozstrzyga sie na korzysc dzialania. Konwencja arbitralna, ale musi byc
                // deterministyczna i zapisana, bo inaczej wynik zalezalby od kolejnosci pol.
                var uBramy = new double[] { best, passKandydat.Utility };
                double[] wagiBramy = ScoreMath.SoftmaxWeights(uBramy, parameters.gateTemperature);
                int[] progiBramy = ScoreMath.CumulativeThresholds(wagiBramy);
                double[] pBramy = ScoreMath.ProbabilitiesFromThresholds(progiBramy);

                pBramaPass = pBramy[1];
                bramaDalaPass = ScoreMath.PickByThresholds(progiBramy, rng) == 1;
                losowan += rng == null ? 0 : 1;
                brama = new GateOutcome
                {
                    ChoseSilence = bramaDalaPass,
                    PassProbability = (float)pBramaPass,
                };
            }

            // ---- C6. ETAP B: WYBOR ZDARZENIA ----
            // Softmax po SAMYCH zdarzeniach z pasma. PASS-a juz tu nie ma - rozstrzygnal sie
            // w bramie - wiec licznosc puli wplywa wylacznie na to, KTORE zdarzenie padnie,
            // a nie na to, CZY jakiekolwiek padnie.
            //
            // Etap wykonuje sie ZAWSZE - takze wtedy, gdy brama wybrala cisze albo gdy pula jest
            // pusta (BurnDraw) - i jego wynik jest wtedy porzucany. To nie jest marnotrawstwo,
            // tylko warunek przewidywalnego zuzycia losowosci:
            //     runda pierwsza  = 1 (brama) + 1 (wybor) = 2 pobrania
            //     runda kolejna   = 0 (brama zamrozona) + 1 (wybor) = 1 pobranie
            //     cala tura       = 1 + liczba rund
            // Zuzycie zalezy WYLACZNIE od liczby rund, nigdy od tresci ani rozmiaru puli.
            // WYBOR JEST DWUSTOPNIOWY: najpierw AKCJA, potem jej WARIANT.
            //
            // Wersja jednostopniowa losowala z plaskiej listy kandydatow, przez co akcja majaca
            // w pasmie K opraw narracyjnych dostawala K-krotnie wieksza mase niz akcja o jednej.
            // Wyrazone w uzytecznosci: plaski softmax dawal jej ukryta premie T*ln(K), czyli 0.2079
            // przy T = 0.1 i K = 8 - okolo PIEC RAZY wiecej niz zmierzony rozstep ocen w czole
            // rankingu (0.036-0.039). Liczba wariantow jest cecha KATALOGU, wiec przy tamtej
            // polityce dosypanie opraw do jednej akcji po cichu czynilo ja czestsza, bez zadnej
            // zmiany w sytuacji kolonii ani w scoringu. To ta sama rodzina bledu co piaty dlug
            // (udzial PASS dzielony przez licznosc puli) i naprawa jest ta sama: rozdzielic
            // pytania dotyczace roznych przestrzeni.
            //
            // OBA podetapy losuja ZAWSZE, takze gdy sa zdegenerowane (BurnDraw), z tego samego
            // powodu co brama: zuzycie losowosci ma zalezec WYLACZNIE od liczby rund, nigdy od
            // tresci ani rozmiaru puli. Bilans na ture wynosi odtad
            //     runda pierwsza = 1 (brama) + 1 (akcja) + 1 (wariant) = 3
            //     runda kolejna  = 0 (brama zamrozona) + 1 + 1          = 2
            //     cala tura      = 1 + 2 * liczba rund
            // PREMIA LUKOWA (krok 5, decyzja autora R4-1) - liczona DOPIERO TUTAJ, po bramie, progu
            // i pasmie, ktore czytaja Utility v6. Luk nie zmienia wiec decyzji "dzialac czy milczec"
            // ani zbioru dopuszczonych - przechyla tylko wybor MIEDZY nimi (etapy B1 i B2).
            // Wielkosc WYPROWADZONA z pasma, nie strojona: best - progPasma = (1 - nearBestFraction)
            // * best, wiec pasujacy kandydat z dna pasma remisuje z liderem. Nie zalezy od wag
            // profilu, wiec dziala jednakowo w kazdej osobowosci (decyzja nr 11). Premia jest
            // wlasnoscia RUNDY: zerowana dla calej listy, nadawana tylko w puli pasma.
            float premiaLuku = best - progPasma;
            bool lukStosowany = false;
            int lukowychWPasmie = 0;
            for (int i = 0; i < lista.Count; i++)
            {
                lista[i].ArcBonus = 0f;
                if (lista[i].ArcValue >= 0f)
                {
                    lukStosowany = true;
                }
            }
            for (int i = 0; i < pulaZdarzen.Count; i++)
            {
                ScoredCandidate k = pulaZdarzen[i];
                if (k.ArcValue > 0f)
                {
                    k.ArcBonus = premiaLuku * k.ArcValue;
                    lukowychWPasmie++;
                }
            }

            List<ActionGroup> grupy = ActionWeighting.Group(pulaZdarzen);
            double[] pZdarzen = new double[pulaZdarzen.Count];
            int idxZdarzenia = -1;
            int idxAkcji = -1;

            if (grupy.Count == 0)
            {
                losowan += BurnDraw(rng);
                losowan += BurnDraw(rng);
            }
            else
            {
                // ---- B1. Wybor AKCJI po jej ocenie = MAKSIMUM wariantu w pasmie ----
                var uAkcji = new double[grupy.Count];
                for (int g = 0; g < grupy.Count; g++)
                {
                    uAkcji[g] = grupy[g].Score;
                }

                double[] wagiAkcji = ScoreMath.SoftmaxWeights(uAkcji, parameters.softmaxTemperature);
                int[] progiAkcji = ScoreMath.CumulativeThresholds(wagiAkcji);
                double[] pAkcji = ScoreMath.ProbabilitiesFromThresholds(progiAkcji);
                for (int g = 0; g < grupy.Count; g++)
                {
                    grupy[g].Probability = pAkcji[g];
                }

                idxAkcji = ScoreMath.PickByThresholds(progiAkcji, rng);
                losowan += rng == null ? 0 : 1;
                if (idxAkcji < 0 || idxAkcji >= grupy.Count)
                {
                    idxAkcji = 0;
                }

                // ---- B2. Wybor WARIANTU wewnatrz wybranej akcji ----
                // Ta sama temperatura co w B1 i jest to decyzja, nie przeoczenie: obie wielkosci
                // sa uzytecznosciami z TEJ SAMEJ przestrzeni wag, wiec porownuja sie wprost.
                // Osobna temperatura bylaby parametrem bez wyprowadzenia - a projekt odrzucil juz
                // raz liczbe "na oko" udajaca kalibracje (patrz gateTemperature).
                List<ScoredCandidate> warianty = grupy[idxAkcji].Variants;
                int idxWariantu;
                if (warianty.Count == 1)
                {
                    // Zdegenerowany, ale pobranie i tak sie odbywa - patrz uwaga o BurnDraw wyzej.
                    idxWariantu = 0;
                    losowan += BurnDraw(rng);
                }
                else
                {
                    var uWariantow = new double[warianty.Count];
                    for (int v = 0; v < warianty.Count; v++)
                    {
                        uWariantow[v] = warianty[v].SelectionScore;
                    }

                    double[] wagiW = ScoreMath.SoftmaxWeights(uWariantow, parameters.softmaxTemperature);
                    int[] progiW = ScoreMath.CumulativeThresholds(wagiW);
                    idxWariantu = ScoreMath.PickByThresholds(progiW, rng);
                    losowan += rng == null ? 0 : 1;
                    if (idxWariantu < 0 || idxWariantu >= warianty.Count)
                    {
                        idxWariantu = 0;
                    }
                }

                // Rozklad LACZNY po kandydatach: p(kandydat) = p(jego akcja) * p(on | ta akcja).
                // Liczymy go dla CALEJ puli, nie tylko dla wybranej grupy - kolumna "p" ma byc
                // prawdopodobienstwem tego konkretnego wyniku, a niezmiennik "suma p po rankingu
                // rowna sie 1" ma sie domykac bez wzgledu na to, co padlo.
                for (int g = 0; g < grupy.Count; g++)
                {
                    ActionGroup grupa = grupy[g];
                    double[] pW;
                    if (grupa.Variants.Count == 1)
                    {
                        pW = new double[] { 1.0 };
                    }
                    else
                    {
                        var u = new double[grupa.Variants.Count];
                        for (int v = 0; v < grupa.Variants.Count; v++)
                        {
                            u[v] = grupa.Variants[v].SelectionScore;
                        }
                        pW = ScoreMath.ProbabilitiesFromThresholds(
                                 ScoreMath.CumulativeThresholds(
                                     ScoreMath.SoftmaxWeights(u, parameters.softmaxTemperature)));
                    }

                    for (int v = 0; v < grupa.Variants.Count; v++)
                    {
                        int poz = pulaZdarzen.IndexOf(grupa.Variants[v]);
                        if (poz >= 0)
                        {
                            pZdarzen[poz] = grupa.Probability * pW[v];
                        }
                        if (g == idxAkcji && v == idxWariantu)
                        {
                            idxZdarzenia = poz;
                        }
                    }
                }

                if (idxZdarzenia < 0 || idxZdarzenia >= pulaZdarzen.Count)
                {
                    idxZdarzenia = 0;
                }
            }

            // ---- C7. Prawdopodobienstwa BEZWARUNKOWE ----
            // p(zdarzenie k) = p(brama wybrala dzialanie) * p(k | dzialanie), p(PASS) = p(brama).
            // Suma po puli zdarzen i PASS wynosi 1, wiec kolumna "p" w danych badawczych nadal
            // jest prawdopodobienstwem TEGO KONKRETNEGO wyniku - dokladnie jak w wersji
            // jednoetapowej - i skrypty agregujace z kroku 8 nie wymagaja przeliczania.
            double pBramaZdarzenie = 1.0 - pBramaPass;

            // ZAPISUJEMY JE TYLKO W RUNDZIE PIERWSZEJ - i to jest naprawa czwartego zarzutu.
            //
            // Rozklad prawdopodobienstwa jest wlasnoscia TURY, tak samo jak liczniki, best, pasmo
            // i ranking zamrozone przy szostym dlugu. Wczesniej byl jedynym pominietym: po odmowie
            // silnika kandydat wypadly zachowywal p z rundy poprzedniej, a pozostali dostawali p
            // z rundy biezacej - wiec jeden Ranking mieszal dwa rozklady i suma p rosla powyzej 1
            // (zmierzone: 1.15 przy jednej odmowie, 1.94 przy czterech, 2.73 gdy silnik odrzucil
            // wszystko). Caly wiersz [PN-DATA] opisywal ture, a ta jedna kolumna - ostatnia runde.
            //
            // Rozklad rundy biezacej NIE GINIE: zwyciezca niesie go w WinnerRoundProbability,
            // czyli w osobnej kolumnie o jawnie innym znaczeniu. Dwie liczby, dwa pytania:
            //     p       - jakie szanse mial ten wynik w rozkladzie TURY (sumuje sie do 1)
            //     pRunda  - jakie szanse mial w rundzie, ktora go FAKTYCZNIE wybrala
            // Roznia sie tylko wtedy, gdy rund bylo wiecej niz jedna, i wtedy obie sa wymowne.
            if (frozenStats == null)
            {
                for (int k = 0; k < pulaZdarzen.Count; k++)
                {
                    pulaZdarzen[k].SelectionProbability = (float)(pBramaZdarzenie * pZdarzen[k]);
                }
                passKandydat.SelectionProbability = (float)pBramaPass;
            }

            decyzja.RandomDraws = losowan;

            // Pusta pula wygrywa PASS NIEZALEZNIE od tego, co powiedziala brama: gdy silnik
            // odmowil wszystkiemu, narrator nie ma czego odpalic, choc chcial dzialac.
            // PassReason odrozni to od ciszy z wyboru (patrz C10 i warstwa integracji).
            ScoredCandidate zwyciezca = (bramaDalaPass || idxZdarzenia < 0)
                ? passKandydat
                : pulaZdarzen[idxZdarzenia];

            // Prawdopodobienstwo zwyciezcy w LOSOWANIU, KTORE GO WYBRALO - warunkowe, czyli
            // BEZ czynnika bramy. To jest poprawka wczesniejszej wersji, ktora mnozyla przez
            // (1 - pBramaPass) takze w rundach naprawczych.
            //
            // DLACZEGO BEZ BRAMY. Brama rozstrzyga sie RAZ NA TURE. W rundzie drugiej i dalszych
            // jej wynik jest juz faktem ("dzialaj"), wiec mnozenie przez jego prawdopodobienstwo
            // opisywaloby losowanie, ktore w tej rundzie w ogole sie nie odbylo. Zmierzone na
            // przypadku testowym: zapisywano 0.274917, podczas gdy warunkowa szansa biezacego
            // wyboru wynosila 0.549834 - czyli dokladnie dwukrotnosc, bo pBramaZdarzenie = 0.5.
            //
            // Wklad bramy nie ginie: ma wlasna kolumne pBrama. Niesienie go drugi raz tutaj
            // znaczyloby, ze jedna wielkosc jest w wierszu dwa razy, w dodatku tylko czasem.
            //
            // PUSTE, GDY ZWYCIEZYLA CISZA. Wtedy wyniku nie wyprodukowalo losowanie WYBORU (etap B
            // zostal spalony na pusto), tylko brama - a ta ma juz swoja kolumne. Pusta wartosc
            // jest uczciwsza niz przepisanie tam pBramy: w Pandas wypada z agregacji sama,
            // zamiast zasilac srednia liczba, ktora nie opisuje tego losowania.
            decyzja.WinnerRoundProbability = (bramaDalaPass || idxZdarzenia < 0)
                ? (float?)null
                : (float?)pZdarzen[idxZdarzenia];

            // ---- C9. Ranking: WSZYSCY ocenieni + PASS ----
            // Zawetowani ZOSTAJA, z zachowanym RawUtility. Bez nich znika mianownik metryk
            // z sekcji o ewaluacji: nie da sie policzyc, ile razy weto zadzialalo ani jak szeroka
            // byla stawka. Stad niezmiennik Ranking.Count == CountScored + 1.
            var ranking = new List<ScoredCandidate>(liczbaOcenionych + 1);
            ranking.AddRange(lista);
            ranking.Add(passKandydat);
            ranking.Sort(CompareCandidates);

            // Statystyki TURY. W rundzie pierwszej powstaja z pelnej puli i sa od tej pory
            // niezmienne; w rundach dalszych bierzemy je gotowe, bo pula robocza jest juz
            // okrojona o kandydatow odrzuconych przez silnik.
            TurnStats tura = frozenStats ?? new TurnStats
            {
                CountScored = liczbaOcenionych,
                CountVetoed = zawetowanych,
                CountBelowCutoff = ponizejProgu,
                CountBelowBand = ponizejPasma,
                CountInSoftmax = pulaZdarzen.Count,
                BestUtility = best,
                BandThreshold = progPasma,
                Ranking = ranking,
                ArcMatchedInBand = lukStosowany ? lukowychWPasmie : -1,
            };

            decyzja.Winner = zwyciezca;
            decyzja.Ranking = tura.Ranking;
            decyzja.PassCandidate = passKandydat;
            decyzja.BestUtility = tura.BestUtility;
            decyzja.BandThreshold = tura.BandThreshold;
            decyzja.PassUtility = passKandydat.Utility;
            decyzja.PassSuppressedByStreak = brama.SuppressedByStreak;
            decyzja.CountScored = tura.CountScored;
            decyzja.CountVetoed = tura.CountVetoed;
            decyzja.CountBelowCutoff = tura.CountBelowCutoff;
            decyzja.CountBelowBand = tura.CountBelowBand;
            decyzja.CountInSoftmax = tura.CountInSoftmax;
            decyzja.GatePassProbability = brama.PassProbability;
            decyzja.Gate = brama;
            decyzja.TurnStats = tura;
            decyzja.ArcMatchedInBand = tura.ArcMatchedInBand;

            // ---- C10. Powod PASS-a ----
            // Kolejnosc sprawdzania jest istotna: od przyczyny najbardziej zewnetrznej do najbardziej
            // wewnetrznej. Competitive to JEDYNY powod liczacy sie jako swiadome milczenie w metryce
            // udzialu PASS; pozostale opisuja brak materialu, a nie decyzje narratora.
            // AllRefusedByGame i RoundBudgetExhausted ustawia warstwa integracji, bo tylko ona wie,
            // czy pula opustoszala przez odmowy silnika, czy nigdy nic nie zawierala.
            // UNIWERSUM TEGO ROZSTRZYGNIECIA to kandydaci DOSTEPNI, czyli ci, ktorym silnik
            // jeszcze nie odmowil. Bez odjecia odmow powod ciszy z opustoszalej puli wychodzilby
            // "Competitive" (bo weto i prog nie odrzucily przeciez nikogo), czyli cisza z bezsily
            // liczylaby sie jako swiadome milczenie - dokladnie ta klasa obciazenia, ktora
            // PassReason ma rozdzielac.
            int liczbaDostepnych = liczbaOcenionych - odrzuconychPrzezSilnik - odlozonych;

            if (zwyciezca.IsPass)
            {
                if (liczbaDostepnych <= 0)
                {
                    decyzja.PassReason = PassReason.NoCandidates;
                }
                else if (zawetowanych == liczbaDostepnych)
                {
                    decyzja.PassReason = PassReason.AllVetoed;
                }
                else if (zawetowanych + ponizejProgu == liczbaDostepnych)
                {
                    decyzja.PassReason = PassReason.BelowCutoff;
                }
                else
                {
                    decyzja.PassReason = PassReason.Competitive;
                }
            }
            else
            {
                decyzja.PassReason = PassReason.None;
            }

            slad.Append("kandydatow=").Append(liczbaOcenionych.ToString(CultureInfo.InvariantCulture))
                .Append(" zawetowanych=").Append(zawetowanych.ToString(CultureInfo.InvariantCulture))
                .Append(" ponizejProgu=").Append(ponizejProgu.ToString(CultureInfo.InvariantCulture))
                .Append(" ponizejPasma=").Append(ponizejPasma.ToString(CultureInfo.InvariantCulture))
                .Append(" wPasmieTury=").Append(tura.CountInSoftmax.ToString(CultureInfo.InvariantCulture))
                .Append(" wPasmieRundy=").Append(pulaZdarzen.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" pBrama=").Append(pBramaPass.ToString("0.0000", CultureInfo.InvariantCulture))
                .Append(" bestRundy=").Append(best.ToString("0.000", CultureInfo.InvariantCulture))
                .Append(" bestTury=").Append(tura.BestUtility.ToString("0.000", CultureInfo.InvariantCulture))
                .Append(" odrzSilnik=").Append(odrzuconychPrzezSilnik.ToString(CultureInfo.InvariantCulture))
                .Append(" odlozonych=").Append(odlozonych.ToString(CultureInfo.InvariantCulture))
                .Append(" akcjiWPasmie=").Append(grupy.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" lukowychWPasmie=").Append(lukStosowany ? lukowychWPasmie.ToString(CultureInfo.InvariantCulture) : "-")
                .Append(" premiaLuku=").Append(premiaLuku.ToString("0.000", CultureInfo.InvariantCulture))
                .Append(" progPasma=").Append(progPasma.ToString("0.000", CultureInfo.InvariantCulture))
                .Append(" T=").Append(parameters.softmaxTemperature.ToString("0.0##", CultureInfo.InvariantCulture))
                .Append(" Tbramy=").Append(parameters.gateTemperature.ToString("0.0##", CultureInfo.InvariantCulture))
                .Append(" losowan=").Append(decyzja.RandomDraws.ToString(CultureInfo.InvariantCulture))
                .Append(" zwyciezca=").Append(zwyciezca.Label)
                .Append(" p=").Append(zwyciezca.SelectionProbability.ToString("0.000", CultureInfo.InvariantCulture));

            if (!decyzja.RankingIsComplete)
            {
                // Niezmiennik zlamany oznacza, ze ktos obcial ranking - a wtedy metryki ewaluacji
                // traca mianownik. Ma to krzyczec w logu, a nie znikac.
                slad.Append(" [BLAD: ranking niekompletny]");
            }

            decyzja.PolicyTrace = slad.ToString();
            return decyzja;
        }

        /// <summary>
        /// Porzadek kandydatow: uzytecznosc malejaco, a przy remisie klucz rosnaco, porownaniem
        /// ORDYNALNYM. Klucze sa unikalne w turze (kazda kombinacja klockow rozni sie co najmniej
        /// jednym identyfikatorem), wiec porzadek jest calkowity i sortowanie - mimo ze List.Sort
        /// jest niestabilne - daje wynik powtarzalny miedzy uruchomieniami i miedzy maszynami.
        /// </summary>
        private static int CompareCandidates(ScoredCandidate a, ScoredCandidate b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }
            if (a == null)
            {
                return 1;
            }
            if (b == null)
            {
                return -1;
            }

            if (a.Utility > b.Utility)
            {
                return -1;
            }
            if (a.Utility < b.Utility)
            {
                return 1;
            }

            return string.CompareOrdinal(a.SortKey ?? string.Empty, b.SortKey ?? string.Empty);
        }

        /// <summary>
        /// Zuzywa jedno pobranie z generatora i NIC z nim nie robi. Istnieje wylacznie po to, zeby
        /// liczba pobran na runde byla STALA (3 w rundzie pierwszej, 2 w kolejnych) takze wtedy,
        /// gdy ktorys z etapow jest zdegenerowany i nie ma czego losowac. Bez tego strumien losowy zalezalby od TRESCI puli,
        /// a nie tylko od liczby rund, i dwa przebiegi ewaluacji rozniace sie jednym kandydatem
        /// rozjechalyby sie nieodwracalnie - czyli przestalyby byc porownywalne.
        ///
        /// Przy rng rownym null (tryb argmax walidatora offline) nie ma czego zuzywac.
        /// </summary>
        /// <summary>
        /// Najlepszy kandydat, ktory W TEJ CHWILI moglby byc referencja bramy: nie odrzucony przez
        /// silnik, nie zawetowany i nie ponizej progu jakosci. Zwraca null, gdy takiego nie ma.
        ///
        /// PO CO TO ISTNIEJE. Brama porownuje uzytecznosc ciszy z najlepszym ZDARZENIEM, wiec
        /// "najlepsze zdarzenie" musi byc zdarzeniem, ktore gra faktycznie dopuszcza. Bez tego
        /// kandydat strukturalnie niemozliwy do odpalenia podnosil referencje bramy i po cichu
        /// ZANIZAL sklonnosc narratora do milczenia - a potem i tak wypadal przy CanFireNow.
        /// Cisza przegrywala z opcja, ktora nie istniala.
        ///
        /// METODA JEST CZYSTA - niczego nie odrzuca ani nie oznacza. Weryfikacje wykonuje
        /// TurnRunner, bo tylko on ma akceptor, czyli jedyne wejscie do wiedzy o silniku gry.
        ///
        /// Deterministyczna: pierwszy element po tym samym porzadku (Utility malejaco, SortKey
        /// ordynalnie rosnaco), ktorego uzywa Select. Petla, a nie sortowanie kopii - pula bywa
        /// przegladana kilka razy w turze, a porzadek i tak jest jednoznaczny.
        /// </summary>
        public ScoredCandidate TopEligible(IReadOnlyList<ScoredCandidate> pool)
        {
            ScoredCandidate najlepszy = null;
            if (pool == null)
            {
                return null;
            }

            for (int i = 0; i < pool.Count; i++)
            {
                ScoredCandidate k = pool[i];
                if (k == null || k.IsPass || k.UnavailableThisTurn || k.Vetoed)
                {
                    continue;
                }
                if (!ScoreMath.AtLeast(k.Utility, parameters.qualityCutoff))
                {
                    continue;
                }
                if (najlepszy == null || CompareCandidates(k, najlepszy) < 0)
                {
                    najlepszy = k;
                }
            }

            return najlepszy;
        }

        private static int BurnDraw(IRandomSource rng)
        {
            if (rng == null)
            {
                return 0;
            }
            rng.Next(ScoreMath.ProbabilityScale);
            return 1;
        }

        private static ScoredCandidate EmergencyPass()
        {
            var c = new ScoredCandidate();
            c.Event = null;
            c.IsPass = true;
            c.SortKey = UtilityScorer.PassSortKey;
            c.RawUtility = 0f;
            c.Utility = 0f;
            c.Vetoed = false;
            c.VetoReason = null;
            c.Factors = new List<FactorScore>();
            return c;
        }
    }
}
