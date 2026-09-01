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
        /// polityka ma byc funkcja samej puli. Warstwa integracji liczy predykat raz na ture
        /// i podaje go do Select jako flage.
        /// </summary>
        public static bool IsPassSuppressedByStreak(DecisionContext context, PassScoringParams passParams)
        {
            if (context == null || context.History == null || passParams == null)
            {
                return false;
            }
            if (passParams.maxStreak <= 0)
            {
                return false;
            }
            return context.History.ConsecutivePassCount >= passParams.maxStreak;
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
            passKandydat.SelectionProbability = 0f;

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

                    // Pola etapowe sa wlasnoscia rundy, wiec kazda runda zaczyna od czystego stanu.
                    k.Rejected = RejectionStage.None;
                    k.SelectionProbability = 0f;
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
            for (int i = 0; i < lista.Count; i++)
            {
                ScoredCandidate k = lista[i];
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
            // LICZBA POBRAN Z GENERATORA JEST STALA I ROWNA DWA na runde - takze wtedy, gdy
            // ktorys etap jest zdegenerowany i jego wynik zostaje zignorowany (patrz BurnDraw).
            // Powod jest ten sam, dla ktorego wersja jednoetapowa losowala zawsze raz: strumien
            // losowy ma zalezec WYLACZNIE od liczby rund. Gdyby zalezal od tresci puli, dwa
            // przebiegi ewaluacji rozniace sie jednym kandydatem rozjechalyby caly dalszy
            // strumien i przestaly byc porownywalne.
            int losowan = 0;
            bool straznikZadzialal = passSuppressedByStreak && pulaZdarzen.Count > 0;

            double pBramaPass;
            bool bramaDalaPass;

            if (pulaZdarzen.Count == 0)
            {
                // Nie ma z czym konkurowac. To cisza z BRAKU MATERIALU, nie z wyboru -
                // rozroznia je PassReason ustawiany w C10.
                pBramaPass = 1.0;
                bramaDalaPass = true;
                losowan += BurnDraw(rng);
                slad.Append("brama pominieta (pula zdarzen pusta); ");
            }
            else if (straznikZadzialal)
            {
                pBramaPass = 0.0;
                bramaDalaPass = false;
                losowan += BurnDraw(rng);
                slad.Append("PASS wygaszony przez straznika serii; ");
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
            }

            // ---- C6. ETAP B: WYBOR ZDARZENIA ----
            // Softmax po SAMYCH zdarzeniach z pasma. PASS-a juz tu nie ma - rozstrzygnal sie
            // w bramie - wiec licznosc puli wplywa wylacznie na to, KTORE zdarzenie padnie,
            // a nie na to, CZY jakiekolwiek padnie.
            //
            // Etap wykonuje sie takze wtedy, gdy brama wybrala cisze, i jego wynik jest wtedy
            // porzucany. To nie jest marnotrawstwo, tylko warunek stalej liczby pobran.
            double[] pZdarzen;
            int idxZdarzenia = -1;

            if (pulaZdarzen.Count == 0)
            {
                pZdarzen = new double[0];
                losowan += BurnDraw(rng);
            }
            else
            {
                var uzytecznosci = new double[pulaZdarzen.Count];
                for (int k = 0; k < pulaZdarzen.Count; k++)
                {
                    uzytecznosci[k] = pulaZdarzen[k].Utility;
                }

                double[] wagi = ScoreMath.SoftmaxWeights(uzytecznosci, parameters.softmaxTemperature);
                int[] progi = ScoreMath.CumulativeThresholds(wagi);
                pZdarzen = ScoreMath.ProbabilitiesFromThresholds(progi);

                idxZdarzenia = ScoreMath.PickByThresholds(progi, rng);
                losowan += rng == null ? 0 : 1;
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
            for (int k = 0; k < pulaZdarzen.Count; k++)
            {
                pulaZdarzen[k].SelectionProbability = (float)(pBramaZdarzenie * pZdarzen[k]);
            }
            passKandydat.SelectionProbability = (float)pBramaPass;

            decyzja.RandomDraws = losowan;

            ScoredCandidate zwyciezca = (bramaDalaPass || idxZdarzenia < 0)
                ? passKandydat
                : pulaZdarzen[idxZdarzenia];

            // ---- C9. Ranking: WSZYSCY ocenieni + PASS ----
            // Zawetowani ZOSTAJA, z zachowanym RawUtility. Bez nich znika mianownik metryk
            // z sekcji o ewaluacji: nie da sie policzyc, ile razy weto zadzialalo ani jak szeroka
            // byla stawka. Stad niezmiennik Ranking.Count == CountScored + 1.
            var ranking = new List<ScoredCandidate>(liczbaOcenionych + 1);
            ranking.AddRange(lista);
            ranking.Add(passKandydat);
            ranking.Sort(CompareCandidates);

            decyzja.Winner = zwyciezca;
            decyzja.Ranking = ranking;
            decyzja.PassCandidate = passKandydat;
            decyzja.BestUtility = best;
            decyzja.BandThreshold = progPasma;
            decyzja.PassUtility = passKandydat.Utility;
            decyzja.PassSuppressedByStreak = straznikZadzialal;
            decyzja.CountScored = liczbaOcenionych;
            decyzja.CountVetoed = zawetowanych;
            decyzja.CountBelowCutoff = ponizejProgu;
            decyzja.CountBelowBand = ponizejPasma;
            decyzja.CountInSoftmax = pulaZdarzen.Count;
            decyzja.GatePassProbability = (float)pBramaPass;

            // ---- C10. Powod PASS-a ----
            // Kolejnosc sprawdzania jest istotna: od przyczyny najbardziej zewnetrznej do najbardziej
            // wewnetrznej. Competitive to JEDYNY powod liczacy sie jako swiadome milczenie w metryce
            // udzialu PASS; pozostale opisuja brak materialu, a nie decyzje narratora.
            // AllRefusedByGame i RoundBudgetExhausted ustawia warstwa integracji, bo tylko ona wie,
            // czy pula opustoszala przez odmowy silnika, czy nigdy nic nie zawierala.
            if (zwyciezca.IsPass)
            {
                if (liczbaOcenionych == 0)
                {
                    decyzja.PassReason = PassReason.NoCandidates;
                }
                else if (zawetowanych == liczbaOcenionych)
                {
                    decyzja.PassReason = PassReason.AllVetoed;
                }
                else if (zawetowanych + ponizejProgu == liczbaOcenionych)
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
                .Append(" wSoftmaksie=").Append(pulaZdarzen.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" pBrama=").Append(pBramaPass.ToString("0.0000", CultureInfo.InvariantCulture))
                .Append(" best=").Append(best.ToString("0.000", CultureInfo.InvariantCulture))
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
        /// liczba pobran na runde byla STALA (dwa) takze wtedy, gdy ktorys z dwoch etapow jest
        /// zdegenerowany i nie ma czego losowac. Bez tego strumien losowy zalezalby od TRESCI puli,
        /// a nie tylko od liczby rund, i dwa przebiegi ewaluacji rozniace sie jednym kandydatem
        /// rozjechalyby sie nieodwracalnie - czyli przestalyby byc porownywalne.
        ///
        /// Przy rng rownym null (tryb argmax walidatora offline) nie ma czego zuzywac.
        /// </summary>
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
