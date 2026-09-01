using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Warstwa decyzyjna: zamienia zlozone wydarzenie na ocenionego kandydata z pelnym sladem
    /// rozbicia wyniku na czynniki, oraz buduje pseudo-kandydata PASS.
    ///
    /// DWIE ROZLACZNE TABLICE CZYNNIKOW, JEDNA METODA AGREGACJI.
    /// Tablica ZDARZENIOWA jest wazona przez ScoringWeights, tablica PASS przez PassScoringParams.
    /// Tablice nie sa nigdy mieszane, bo czynniki zdarzeniowe czytaja candidate.Event.* (dla PASS
    /// bylby to null), a czynniki PASS opisuja wielkosc tury, nie kandydata. Wspolna jest wylacznie
    /// metoda agregacji Aggregate(), ktora normalizuje przez SUME WAG swojej tablicy - dzieki temu
    /// oba swiaty daja wynik w [0,1] i oba maja slad w tym samym formacie.
    ///
    /// KONSEKWENCJA DO ZAPISANIA W PRACY: U_pass i U_zdarzenia sa znormalizowane po ROZNYCH
    /// zestawach wag, wiec ich zestawienie w softmaksie jest KONWENCJA kalibrowana empirycznie,
    /// a nie wielkoscia wyprowadzona z jednego modelu. Kalibracje wykonuje test udzialu PASS.
    ///
    /// KOLEJNOSC REJESTRACJI CZYNNIKOW JEST CZESCIA KONTRAKTU DANYCH: ustala kolejnosc kolumn
    /// w logu badawczym i musi byc stala miedzy rozgrywkami.
    ///   zdarzenia: [contextFit, freshness, dramaticContrast, intentAlignment]
    ///   PASS:      [restraint, baseline, intentAlignment]   (patrz BuildPassFactors)
    ///
    /// ILE RAZY SIE TO WOLA: ScoreAll i ScorePass dokladnie RAZ na ture narratora. Kolejne rundy
    /// petli wyboru powtarzaja wylacznie SelectionPolicy.Select na juz ocenionej liscie. Ponowne
    /// wolanie ScoreAll przeliczyloby czynniki osiem razy i nadpisalo Factors, gubiac slad rundy,
    /// ktora faktycznie zakonczyla decyzje.
    /// </summary>
    public class UtilityScorer
    {
        /// <summary>
        /// Nazwa czynnika, na ktorym stoi weto. Wyprowadzona przez nameof z POLA WAGI, zeby
        /// niezmiennik "nazwa czynnika == nazwa pola wagi == nazwa wezla XML" byl pilnowany przez
        /// kompilator: zmiana nazwy pola bez zmiany nazwy czynnika zerwie build zamiast po cichu
        /// wylaczyc gwarancje spojnosci.
        /// </summary>
        public const string ContextFitFactorName = nameof(ScoringWeights.contextFit);

        /// <summary>Domyslny prog weta. Ponizej tej wartosci contextFit kandydat jest odrzucany bezwarunkowo.</summary>
        public const float DefaultVetoContextFitBelow = 0.15f;

        /// <summary>Klucz sortowania pseudo-kandydata PASS. Jeden, staly, nigdy nie kolidujacy z sygnatura kandydata.</summary>
        public const string PassSortKey = "PASS";

        /// <summary>
        /// Klucz zastepczy dla kandydata pustego. U+FFFF jest ostatnim punktem kodowym plaszczyzny
        /// podstawowej, wiec przy porownaniu ordynalnym takie wpisy sortuja sie zawsze na koniec
        /// i nie mieszaja sie z prawdziwymi sygnaturami.
        /// </summary>
        public const string NullSortKey = "\uFFFF_NULL";

        private readonly IScoringFactor[] eventFactors;
        private readonly ScoringWeights eventWeights;
        private readonly IScoringFactor[] passFactors;
        private readonly PassScoringParams passParams;
        private readonly float vetoContextFitBelow;

        // Delegaty wag tworzone RAZ w konstruktorze. Gdyby powstawaly przy kazdym wywolaniu
        // Aggregate, kazda tura alokowalaby 84 domkniecia - niepotrzebna presja na GC w petli,
        // ktora i tak jest najgoretszym miejscem warstwy decyzyjnej.
        private readonly Func<string, float> eventWeightLookup;
        private readonly Func<string, float> passWeightLookup;

        private float lastPassDensity;

        public UtilityScorer(IReadOnlyList<IScoringFactor> eventFactors, ScoringWeights weights,
                             IReadOnlyList<IScoringFactor> passFactors, PassScoringParams passParams,
                             float vetoContextFitBelow)
        {
            // Wszystkie zrodla moga przyjsc z XML, wiec null oznacza nieudana deserializacje.
            // Podstawiamy domysly i nie rzucamy - wyjatek w MakeIntervalIncidents zabija ture gry,
            // a od wykrycia zlej konfiguracji jest Validate() logowane na starcie.
            this.eventFactors = ToArray(eventFactors);
            this.eventWeights = weights ?? new ScoringWeights();
            this.passFactors = ToArray(passFactors);
            this.passParams = passParams ?? PassScoringParams.Defaults();

            this.vetoContextFitBelow = (float.IsNaN(vetoContextFitBelow) || float.IsInfinity(vetoContextFitBelow)
                                        || vetoContextFitBelow < 0f)
                ? 0f
                : vetoContextFitBelow;

            eventWeightLookup = this.eventWeights.For;
            passWeightLookup = this.passParams.For;
        }

        public IReadOnlyList<IScoringFactor> EventFactors
        {
            get { return eventFactors; }
        }

        public IReadOnlyList<IScoringFactor> PassFactors
        {
            get { return passFactors; }
        }

        public float VetoContextFitBelow
        {
            get { return vetoContextFitBelow; }
        }

        /// <summary>
        /// Gestosc ostatnich zdarzen policzona przy ostatnim ScorePass. Jest to wielkosc TURY,
        /// wspolna dla wszystkich kandydatow, a nie cecha kandydata - dlatego nie miesci sie
        /// w ScoredCandidate i wedruje osobno do NarratorDecision.RecentDensity.
        /// Warstwa integracji ma po decyzji wykonac: decision.AttachTurnContext(scorer.LastPassDensity, ...).
        /// </summary>
        public float LastPassDensity
        {
            get { return lastPassDensity; }
        }

        /// <summary>
        /// Jedyne miejsce ustalajace SKLAD I KOLEJNOSC tablicy czynnikow PASS.
        /// Kolejnosc [restraint, baseline, intentAlignment] jest czescia formatu danych badawczych;
        /// jej zmiana zmienia kolejnosc kolumn w logu i uniewaznia wczesniej zebrane serie.
        /// Odpowiednika dla czynnikow zdarzeniowych nie ma tutaj celowo - tamte klasy powstaja
        /// w osobnej warstwie i sklada je warstwa integracji, w kolejnosci
        /// [contextFit, freshness, dramaticContrast, intentAlignment].
        /// </summary>
        public static IScoringFactor[] BuildPassFactors(PassScoringParams parameters)
        {
            PassScoringParams p = parameters ?? PassScoringParams.Defaults();
            return new IScoringFactor[]
            {
                new Factor_PassRestraint(p),
                new Factor_PassBaseline(p),
                new Factor_PassIntent(p)
            };
        }

        /// <summary>
        /// Ocena pojedynczego kandydata-zdarzenia. Nigdy nie rzuca wyjatku: narrator ma przezyc
        /// kazda ture, a bledne wejscie ma zostawic slad, a nie polozyc gre.
        /// </summary>
        public ScoredCandidate Score(ComposedEvent candidate, DecisionContext context)
        {
            if (candidate == null || context == null)
            {
                var pusty = new ScoredCandidate();
                pusty.Event = candidate;
                pusty.IsPass = false;
                pusty.SortKey = NullSortKey;
                pusty.RawUtility = 0f;
                pusty.Utility = 0f;
                pusty.Vetoed = true;
                pusty.VetoReason = candidate == null ? "kandydat pusty" : "kontekst decyzji pusty";
                pusty.Factors = new List<FactorScore>();
                return pusty;
            }

            var c = new ScoredCandidate();
            c.Event = candidate;
            c.IsPass = false;

            // Klucz liczony RAZ i przed czynnikami, bo uzywa go i sortowanie, i log, i rozstrzyganie
            // remisow. Jedno zrodlo prawdy: sygnatura kompozycji, nie payload (dwa rozne klocki akcji
            // moga wskazywac ten sam IncidentDef, wiec payload nie musi byc roznowartosciowy).
            c.SortKey = SortKeyOf(candidate);

            float surowyContextFit;
            Aggregate(c, eventFactors, eventWeightLookup, context, ContextFitFactorName, out surowyContextFit);

            // WETO - WYLACZNIE contextFit i WYLACZNIE na wartosci SUROWEJ.
            //
            // Predykat czyta wartosc czynnika PRZED przemnozeniem przez wage, wiec jest od wagi
            // NIEZALEZNY: ustawienie w XML contextFit=0 wylacza wplyw dopasowania na wynik, ale
            // NIE ZNOSI weta. Gdyby weto liczyc na wkladzie (v*w) albo na wyniku po normalizacji,
            // wyzerowanie wagi byloby cicha furtka omijajaca gwarancje spojnosci.
            //
            // Aplikowane jest natomiast PO policzeniu pelnego sladu. Gdyby weto przerywalo liczenie,
            // slad bylby niekompletny - a kandydat zawetowany jest najciekawszym materialem do
            // rozdzialu o ewaluacji, bo pokazuje, czego narrator NIE zrobil i dlaczego.
            //
            // surowyContextFit == -1 oznacza "czynnika nie zarejestrowano" - wtedy nie ma na czym
            // wetowac i Validate() zglasza to jako blad konfiguracji.
            if (surowyContextFit >= 0f && !ScoreMath.AtLeast(surowyContextFit, vetoContextFitBelow))
            {
                c.Vetoed = true;
                c.VetoReason = ContextFitFactorName + " "
                               + surowyContextFit.ToString("0.00", CultureInfo.InvariantCulture)
                               + " < " + vetoContextFitBelow.ToString("0.00", CultureInfo.InvariantCulture)
                               + " (bez weta " + c.RawUtility.ToString("0.00", CultureInfo.InvariantCulture) + ")";
                c.Utility = 0f;
            }
            else
            {
                c.Vetoed = false;
                c.VetoReason = null;
                c.Utility = c.RawUtility;
            }

            // Etapy odrzucenia ustawia dopiero SelectionPolicy - scorer nie zna polityki wyboru.
            c.Rejected = RejectionStage.None;
            return c;
        }

        /// <summary>
        /// Ocena calej stawki. Kandydaci pusci sa POMIJANI (a nie oceniani jako zawetowani),
        /// zeby nie zawyzali licznika weta w danych ewaluacyjnych - null w liscie to blad
        /// generatora, nie zjawisko narracyjne.
        /// </summary>
        public List<ScoredCandidate> ScoreAll(IReadOnlyList<ComposedEvent> candidates, DecisionContext context)
        {
            var wynik = new List<ScoredCandidate>(candidates == null ? 0 : candidates.Count);
            if (candidates == null)
            {
                return wynik;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                ComposedEvent kandydat = candidates[i];
                if (kandydat == null)
                {
                    continue;
                }
                wynik.Add(Score(kandydat, context));
            }
            return wynik;
        }

        /// <summary>
        /// Buduje pseudo-kandydata PASS i liczy jego uzytecznosc z WLASNEJ tablicy czynnikow.
        ///
        /// PASS NIE PODLEGA WETU - i nie jest to zwolnienie, tylko brak przedmiotu weta: weto jest
        /// zdefiniowane na contextFit, a cisza nie ma dopasowania kontekstowego.
        ///
        /// Sufit U_pass wynosi 1.0 przy weightIntentAlignment == 0, wiec PASS moze przebic kazdego
        /// kandydata. Regula strojenia tego zjawiska (podnosic densitySaturation, nie ruszac
        /// weightBaseline) jest opisana w Factor_PassRestraint.
        /// </summary>
        public ScoredCandidate ScorePass(DecisionContext context)
        {
            DecisionContext ctx = context ?? new DecisionContext();

            var c = new ScoredCandidate();
            c.Event = null;
            c.IsPass = true;
            c.SortKey = PassSortKey;
            c.Vetoed = false;
            c.VetoReason = null;

            float nieuzywane;
            Aggregate(c, passFactors, passWeightLookup, ctx, null, out nieuzywane);
            c.Utility = c.RawUtility;
            c.Rejected = RejectionStage.None;

            // Gestosc liczymy tu drugi raz (czynnik policzyl ja u siebie), bo alternatywa byloby
            // zapamietanie jej w polu czynnika, czyli zlamanie kontraktu "czynnik jest funkcja czysta".
            // Koszt to najwyzej 24 potegowania raz na ture - cena warta utrzymania bezstanowosci.
            lastPassDensity = Factor_PassRestraint.Density(ctx.History, ctx.GameDay, passParams.halfLifeDays);

            return c;
        }

        /// <summary>
        /// Walidacja startowa konfiguracji. Uruchamiana OSOBNO dla kazdej z dwoch tablic czynnikow,
        /// przeciwko JEJ WLASNEMU zrodlu wag: tablica zdarzeniowa przeciwko ScoringWeights, tablica
        /// PASS przeciwko PassScoringParams. Wspolna walidacja wywalilaby blad na poprawnej
        /// konfiguracji, bo czynniki PASS nie maja pol w ScoringWeights.
        ///
        /// Sprawdzamy dokladnie te klasy bledow, ktore inaczej sa CICHE: literowka w nazwie czynnika
        /// (waga 0, martwy czynnik, zero komunikatow), waga ujemna (lamie zakres [0,1] i odwraca
        /// pasmo near-best), suma wag zero (narrator zawsze PASS), brak czynnika contextFit przy
        /// aktywnym wecie (gwarancja spojnosci wylaczona po cichu).
        /// </summary>
        public bool Validate(out string problem)
        {
            var problemy = new List<string>();

            // ---- tablica ZDARZENIOWA vs ScoringWeights ----
            if (eventFactors.Length == 0)
            {
                problemy.Add("tablica czynnikow zdarzeniowych jest pusta - kazdy kandydat dostanie wynik 0 i narrator bedzie zawsze PASS");
            }

            CheckDuplicates(eventFactors, "zdarzeniowej", problemy);

            for (int i = 0; i < eventFactors.Length; i++)
            {
                IScoringFactor f = eventFactors[i];
                if (f == null)
                {
                    problemy.Add("pusty wpis w tablicy czynnikow zdarzeniowych na pozycji " + i.ToString(CultureInfo.InvariantCulture));
                    continue;
                }
                if (string.IsNullOrEmpty(f.Name) || !eventWeights.HasWeightFor(f.Name))
                {
                    problemy.Add("czynnik zdarzeniowy \"" + (f.Name ?? "?") + "\" nie ma odpowiadajacego pola wagi w <weights> - jego waga bedzie zawsze 0");
                }
            }

            string ujemna;
            if (eventWeights.AnyNegativeInXml(out ujemna))
            {
                problemy.Add("waga ujemna w <weights>: " + ujemna + " - zostanie scieta do 0, ale poprawcie XML (ujemna waga odwraca pasmo near-best)");
            }

            if (eventWeights.Total() <= ScoreMath.Epsilon)
            {
                problemy.Add("suma wag czynnikow zdarzeniowych wynosi 0 - kazdy kandydat dostanie wynik 0, wszyscy padna na progu jakosci i narrator bedzie zawsze PASS");
            }

            if (vetoContextFitBelow > 0f && !HasFactorNamed(eventFactors, ContextFitFactorName))
            {
                problemy.Add("weto ustawione na " + vetoContextFitBelow.ToString("0.00", CultureInfo.InvariantCulture)
                             + ", ale nie zarejestrowano czynnika \"" + ContextFitFactorName + "\" - gwarancja spojnosci jest WYLACZONA");
            }

            // ---- tablica PASS vs PassScoringParams ----
            // Nazwa "intentAlignment" wystepuje w OBU przestrzeniach nazw i to jest poprawne:
            // przestrzenie sa rozlaczne i kalibrowane niezaleznie. Nie unifikowac.
            if (passFactors.Length == 0)
            {
                problemy.Add("tablica czynnikow PASS jest pusta - U_pass bedzie stale 0, a PASS zostanie wylacznie sciezka awaryjna");
            }

            CheckDuplicates(passFactors, "PASS", problemy);

            for (int i = 0; i < passFactors.Length; i++)
            {
                IScoringFactor f = passFactors[i];
                if (f == null)
                {
                    problemy.Add("pusty wpis w tablicy czynnikow PASS na pozycji " + i.ToString(CultureInfo.InvariantCulture));
                    continue;
                }
                if (string.IsNullOrEmpty(f.Name) || !passParams.HasWeightFor(f.Name))
                {
                    problemy.Add("czynnik PASS \"" + (f.Name ?? "?") + "\" nie ma odpowiadajacego pola wagi w <pass> - jego waga bedzie zawsze 0");
                }
            }

            // Suma wag PASS rowna 0 NIE jest tu bledem: to dopuszczalna konfiguracja "PASS wylacznie
            // jako sciezka awaryjna", ktora PassScoringParams.Sanitize odnotowuje osobno jako uwage.
            // Blad startowy na poprawnej konfiguracji zmusilby do wylaczenia walidacji.

            if (problemy.Count == 0)
            {
                problem = null;
                return true;
            }

            problem = string.Join("; ", problemy.ToArray());
            return false;
        }

        /// <summary>
        /// Efektywna konfiguracja scoringu jednym stringiem. Logowana na starcie - to jedyny dowod,
        /// ze wezly &lt;weights&gt; i &lt;pass&gt; w XML w ogole sie zdeserializowaly. Gdy blok XML
        /// nie zostanie rozpoznany, pola zostaja na inicjalizatorach C# i awaria jest CICHA:
        /// narrator dziala pozornie poprawnie, tylko kalibracja z XML nie ma zadnego wplywu.
        /// Ta sama klasa pulapki, ktora w kroku 1 dala pusty katalog klockow.
        /// </summary>
        public string DescribeConfiguration()
        {
            var sb = new StringBuilder();

            sb.Append("czynniki zdarzen: ");
            AppendFactorList(sb, eventFactors, eventWeightLookup);

            sb.Append("; weto ").Append(ContextFitFactorName).Append('<')
              .Append(vetoContextFitBelow.ToString("0.00", CultureInfo.InvariantCulture));

            sb.Append("; czynniki PASS: ");
            AppendFactorList(sb, passFactors, passWeightLookup);

            sb.Append("; U_pass podloga=").Append(passParams.UtilityFloor.ToString("0.000", CultureInfo.InvariantCulture))
              .Append(" sufit=").Append(passParams.UtilityCeiling.ToString("0.000", CultureInfo.InvariantCulture));

            return sb.ToString();
        }

        /// <summary>
        /// WSPOLNA metoda agregacji obu tablic. Rozne sa wylacznie: lista czynnikow i zrodlo wag.
        ///
        /// NORMALIZACJA PRZEZ SUME WAG: RawUtility = suma(v_i * w_i) / suma(w_i). Przy v_i w [0,1]
        /// i w_i &gt;= 0 wynik jest kombinacja wypukla, wiec z konstrukcji lezy w [0,1]. Czynnik
        /// o wadze 0 wnosi 0 do licznika i 0 do mianownika, czyli jest DOKLADNIE NEUTRALNY, a nie
        /// rozcienczajacy - dlatego intentAlignment mozna wpiac juz w kroku 3 z waga 0.
        ///
        /// trackedFactorName pozwala wyciagnac SUROWA wartosc jednego czynnika (contextFit) na
        /// potrzeby weta, bez drugiego przebiegu po tablicy.
        /// </summary>
        private static void Aggregate(ScoredCandidate target, IScoringFactor[] factors,
                                      Func<string, float> weightOf, DecisionContext context,
                                      string trackedFactorName, out float trackedValue)
        {
            trackedValue = -1f;

            int n = factors.Length;
            target.Factors = new List<FactorScore>(n);

            if (n == 0)
            {
                target.RawUtility = 0f;
                target.WeightsDegenerate = true;
                return;
            }

            var nazwy = new string[n];
            var wartosci = new float[n];
            var wagi = new float[n];
            var opisy = new string[n];
            var bledne = new bool[n];

            double licznik = 0.0;
            double sumaWag = 0.0;

            for (int i = 0; i < n; i++)
            {
                IScoringFactor f = factors[i];
                string nazwa = (f == null || string.IsNullOrEmpty(f.Name)) ? "?" : f.Name;

                string opis;
                float surowa;
                if (f == null)
                {
                    opis = "BLAD: pusty czynnik w tablicy";
                    surowa = 0f;
                }
                else
                {
                    surowa = f.Evaluate(target, context, out opis);
                }

                // Sanitize01 jawnie lapie NaN i nieskonczonosc. Naiwny clamp oparty na porownaniach
                // przepuscilby NaN (kazde porownanie z NaN jest falszywe), a dalej exp(NaN) polozylby
                // cala kwantyzacje softmaksu i losowanie nie znalazloby kubelka.
                bool blad;
                float v = ScoreMath.Sanitize01(surowa, out blad);
                if (blad)
                {
                    opis = (opis ?? string.Empty) + " [WARTOSC NIEPOPRAWNA -> 0]";
                    target.HadInvalidFactor = true;
                }

                float w = weightOf == null ? 0f : weightOf(nazwa);
                if (float.IsNaN(w) || float.IsInfinity(w) || w < 0f)
                {
                    w = 0f;
                }

                nazwy[i] = nazwa;
                wartosci[i] = v;
                wagi[i] = w;
                opisy[i] = opis ?? string.Empty;
                bledne[i] = blad;

                licznik += (double)v * w;
                sumaWag += w;

                if (trackedFactorName != null && trackedValue < 0f
                    && string.CompareOrdinal(nazwa, trackedFactorName) == 0)
                {
                    trackedValue = v;
                }
            }

            bool zdegenerowane = !(sumaWag > ScoreMath.Epsilon);
            target.WeightsDegenerate = zdegenerowane;
            target.RawUtility = zdegenerowane ? 0f : (float)(licznik / sumaWag);

            // FactorScore sam liczy Share = Value*Weight/sumaWag, wiec niezmiennik
            // "suma Share == RawUtility" wychodzi z jednej definicji, a nie z dwoch zgodnych.
            float mianownik = (float)sumaWag;
            for (int i = 0; i < n; i++)
            {
                target.Factors.Add(new FactorScore(nazwy[i], wartosci[i], wagi[i], mianownik, opisy[i], bledne[i]));
            }
        }

        private static string SortKeyOf(ComposedEvent candidate)
        {
            if (!string.IsNullOrEmpty(candidate.Signature))
            {
                return candidate.Signature;
            }

            // Sygnatura pusta oznacza kandydata zbudowanego z pominieciem Assemble. Klucz zastepczy
            // jest oznaczony, zeby bylo widac w logu, ze remisy rozstrzygaly sie na gorszym kluczu.
            return "?BRAK-SYGNATURY|" + (candidate.ActionPayload ?? "?");
        }

        private static IScoringFactor[] ToArray(IReadOnlyList<IScoringFactor> source)
        {
            if (source == null || source.Count == 0)
            {
                return new IScoringFactor[0];
            }

            var tablica = new IScoringFactor[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                tablica[i] = source[i];
            }
            return tablica;
        }

        private static bool HasFactorNamed(IScoringFactor[] factors, string name)
        {
            for (int i = 0; i < factors.Length; i++)
            {
                IScoringFactor f = factors[i];
                if (f != null && string.CompareOrdinal(f.Name ?? string.Empty, name) == 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void CheckDuplicates(IScoringFactor[] factors, string ktoraTablica, List<string> problemy)
        {
            // Zdublowana nazwa oznacza, ze ta sama waga zostanie policzona dwa razy i po cichu
            // podwoi swoj udzial w wyniku - bez zadnego objawu poza przekrzywionym rankingiem.
            for (int i = 0; i < factors.Length; i++)
            {
                if (factors[i] == null || string.IsNullOrEmpty(factors[i].Name))
                {
                    continue;
                }
                for (int j = i + 1; j < factors.Length; j++)
                {
                    if (factors[j] == null || string.IsNullOrEmpty(factors[j].Name))
                    {
                        continue;
                    }
                    if (string.CompareOrdinal(factors[i].Name, factors[j].Name) == 0)
                    {
                        problemy.Add("zdublowana nazwa czynnika \"" + factors[i].Name + "\" w tablicy " + ktoraTablica
                                     + " - jego waga zostanie policzona dwukrotnie");
                    }
                }
            }
        }

        private static void AppendFactorList(StringBuilder sb, IScoringFactor[] factors, Func<string, float> weightOf)
        {
            if (factors.Length == 0)
            {
                sb.Append("BRAK");
                return;
            }

            for (int i = 0; i < factors.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                string nazwa = (factors[i] == null || string.IsNullOrEmpty(factors[i].Name)) ? "?" : factors[i].Name;
                float w = weightOf == null ? 0f : weightOf(nazwa);
                sb.Append(nazwa).Append('(').Append(w.ToString("0.0##", CultureInfo.InvariantCulture)).Append(')');
            }
        }
    }
}
