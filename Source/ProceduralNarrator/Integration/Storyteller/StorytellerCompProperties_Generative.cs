using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Tension;
using RimWorld;

namespace ProceduralNarrator.Integration.Storyteller
{
    /// <summary>
    /// Wlasciwosci komponentu narratora, konfigurowane z XML w StorytellerDef.
    /// compClass spina definicje XML z klasa wykonawcza.
    ///
    /// Od kroku 3 ta klasa jest JEDYNYM wejsciem kalibracji warstwy decyzyjnej: budzet ocen,
    /// trzy progi polityki wyboru, prog weta oraz dwa ROZLACZNE bloki wag - &lt;weights&gt; dla
    /// czynnikow zdarzeniowych i &lt;pass&gt; dla czynnikow ciszy. Rozlaczne, bo obie przestrzenie
    /// nazw sa kalibrowane niezaleznie i nazwa "intentAlignment" wystepuje w obu CELOWO.
    ///
    /// PULAPKA, KTORA TE POLA WPROWADZAJA - i jej FAKTYCZNY zasieg, sprawdzony dekompilacja.
    /// Literowka w nazwie wezla NIE jest cicha awaria: Verse.DirectXmlToObject loguje wtedy
    /// "XML error: ... doesn't correspond to any field in type", a zly Class= daje
    /// "Could not find type named". Te dwa przypadki pokrywa wiec silnik.
    /// Niepokryty zostaje jeden: caly blok &lt;li&gt; nie bierze udzialu w deserializacji. Wtedy
    /// wszystkie pola zostaja na inicjalizatorach C#, a te sa dzis CO DO JEDNEGO rowne wartosciom
    /// z XML - wiec samo wypisanie ich przez DescribeEffective() NICZEGO by nie dowiodlo.
    /// Dowodza tego dopiero trzy rzeczy razem: (1) waniliowe bledy DirectXmlToObject powyzej,
    /// (2) licznik compow w PNStartup.AuditDecisionConfig, (3) NIEPUSTY configStamp - jedyne pole
    /// bez sensownej wartosci domyslnej, wiec mogace pochodzic wylacznie z pliku XML.
    /// </summary>
    public class StorytellerCompProperties_Generative : StorytellerCompProperties
    {
        /// <summary>
        /// Domyslny budzet tury. NIE jest to zabezpieczenie poprawnosci - petla konczy sie sama,
        /// bo kazda odmowa silnika oznacza CALA akcje (zakres payloadu) jako niewykonalna w tej
        /// turze, a akcji jest skonczenie wiele. Jest to budzet KOSZTU i ogranicza DWIE rzeczy
        /// naraz: liczbe rund wyboru ORAZ laczna liczbe pytan do gry w turze, wspolna dla fazy 0
        /// i petli (niezmiennik pytanDoGry &lt;= maxSelectionRounds) - CanFireNow bywa drogie
        /// (Infestation przeszukuje mape). 8 to odpowiednik dawnego MaxCompositionAttempts = 6
        /// z zapasem.
        /// </summary>
        public const int DefaultMaxSelectionRounds = 8;

        /// <summary>Sredni czas miedzy wydarzeniami (mean time between), w dniach gry.</summary>
        // Inicjalizator MUSI byc rowny wartosci z XML (2.5), a nie dowolny.
        // Powod: brak albo literowka w wezle <mtbDays> to cicha awaria - pole zostaje na
        // inicjalizatorze i narrator dziala dalej, tylko w innym tempie. Przy 1.2 odpalalby
        // okolo dwa razy czesciej, a cala kalibracja PASS jest wyprowadzona z tempa
        // (halfLifeDays = 2 * mtbDays, progi gestosci z wartosci oczekiwanej), wiec po cichu
        // przestalaby odpowiadac rzeczywistosci. Zrownanie inicjalizatora z XML sprawia, ze
        // taka awaria nie zmienia zachowania, a i tak zostanie zauwazona - PNStartup wypisuje
        // wartosci EFEKTYWNE.
        public float mtbDays = 2.5f;

        /// <summary>Tag wymagany od klocka akcji. null = dowolny.</summary>
        public string requiredActionTag;

        /// <summary>
        /// B: budzet OCEN na jedna decyzje. Nie mylic z rozmiarem przestrzeni wariantow -
        /// to gorna granica liczby wywolan Assemble + ContextEvaluator.Evaluate, czyli jedyny
        /// czlon kosztu, ktory rosnie z katalogiem. Przy dzisiejszych 12 akcjach i przestrzeni 84
        /// budzet 400 obejmuje CALA przestrzen, wiec decyzja jest w 100% enumeracyjna i nie zuzywa
        /// ani jednego losowania przy generowaniu kandydatow.
        /// </summary>
        public int candidateBudget = CandidateGenerator.DefaultBudget;

        /// <summary>
        /// Prog BEZWZGLEDNY jakosci. Jedyna gwarancja "narrator nigdy nie odpali bzdury" -
        /// pasmo near-best jej nie daje, bo jest wzgledne i opada wraz z jakoscia stawki
        /// w kolejnych rundach. NIE obnizac przy strojeniu.
        /// </summary>
        public float qualityCutoff = 0.35f;

        /// <summary>Szerokosc pasma near-best: do losowania wchodza kandydaci w promieniu 25% od najlepszego.</summary>
        public float nearBestFraction = 0.75f;

        /// <summary>
        /// Temperatura softmaksu. 0.1 jest OSTRE: roznica 0.1 uzytecznosci to czynnik e = 2.72,
        /// roznica 0.3 to czynnik 20. Ta liczba decyduje, czy narrator jest przewidywalny (male T)
        /// czy rozstrzelony (duze T).
        /// </summary>
        public float softmaxTemperature = 0.1f;

        /// <summary>
        /// Temperatura BRAMY "czy w ogole dzialac" - dwuelementowego softmaksu miedzy najlepszym
        /// dostepnym zdarzeniem a cisza. Oddzielna od softmaxTemperature, bo obie liczby odpowiadaja
        /// na rozne pytania: brama porownuje wielkosci z ROZLACZNYCH przestrzeni wag (konwencja
        /// kalibrowana empirycznie), etap B porownuje zdarzenia miedzy soba (wielkosci wspolmierne).
        /// Wartosc domyslna rowna softmaxTemperature jest tymczasowa - brak danych, by uzasadnic inna.
        /// </summary>
        public float gateTemperature = 0.1f;

        /// <summary>
        /// KANAREK KONFIGURACJI - jedyne pole bez sensownej wartosci domyslnej.
        ///
        /// Kod inicjalizuje je PUSTYM napisem, wiec cokolwiek pojawi sie w logu startowym musi
        /// pochodzic z XML-a. To zamyka jedyna luke, ktorej nie pokrywa silnik: waniliowy
        /// DirectXmlToObject sam zglasza "doesn't correspond to any field in type" przy literowce
        /// w nazwie wezla i "Could not find type named" przy zlym Class=, ale nie ma jak zglosic
        /// sytuacji, w ktorej caly blok &lt;li&gt; nie wzialby udzialu w deserializacji.
        ///
        /// SPROSTOWANIE wobec pierwotnego uzasadnienia: nie jest prawda, ze wszystkie pola sa rowne
        /// inicjalizatorom C#. Odziedziczone minDaysPassed ma domyslnie 0f, a XML wysyla 5, wiec
        /// jest DRUGIM, niezaleznym kanarkiem. configStamp zostaje mimo to, bo jego wartosc nie jest
        /// parametrem kalibracji: minDaysPassed moze kiedys wrocic do zera i wtedy tamten kanarek
        /// milczy, a ten nie. Kanarek ma byc odporny na przyszle strojenie, nie tylko na dzisiejsze.
        /// </summary>
        public string configStamp = string.Empty;

        /// <summary>
        /// Prog weta spojnosci. Kandydat o dopasowaniu kontekstowym ponizej tej wartosci jest
        /// odrzucany bezwarunkowo, NIEZALEZNIE od wagi contextFit - wyzerowanie wagi w XML nie
        /// jest furtka omijajaca gwarancje spojnosci.
        /// </summary>
        public float vetoContextFitBelow = UtilityScorer.DefaultVetoContextFitBelow;

        /// <summary>Budzet rund petli wyboru. Patrz DefaultMaxSelectionRounds.</summary>
        public int maxSelectionRounds = DefaultMaxSelectionRounds;

        /// <summary>
        /// Wagi czynnikow ZDARZENIOWYCH. Nazwy pol sa jednoczesnie nazwami czynnikow i nazwami
        /// wezlow XML - to inwariant, nie zbieg okolicznosci.
        /// </summary>
        public ScoringWeights weights = new ScoringWeights();

        /// <summary>
        /// Parametry i wagi pseudo-kandydata PASS. ROZLACZNA przestrzen nazw wzgledem weights;
        /// nie unifikowac, bo obie kalibracje sa niezalezne.
        /// </summary>
        public PassScoringParams pass = new PassScoringParams();

        /// <summary>
        /// Strojenie czynnika kontrastu dramaturgicznego. Wystawione do XML w kroku 4.
        ///
        /// Powod jest konkretny: pola reliefGain i strikeGain byly zaszytymi stalymi (1.00
        /// i 0.75), ktore realizowaly preferencje "po serii ciosow lepsza jest ulga" - czyli
        /// dokladnie to, co od kroku 4 robi krzywa dramaturgiczna przez Intent.Breathe.
        /// Ukryta stala nie ma swojego wiersza w sladzie decyzji, wiec podwojne liczenie
        /// tej preferencji byloby niewidoczne w danych. Po wystawieniu do XML asymetria jest
        /// decyzja kalibracyjna widoczna w logu startowym, a nie wlasnoscia kodu.
        ///
        /// Ten sam obiekt dostaje TensionModel, zeby napiecie i kontrast mialy wspolne okno,
        /// wagi kolejnosci i mapowanie osi. Od polerowania etapu 4 napiecie doklada do tego
        /// zanik czasowy KAZDEGO wpisu (Factor_DramaticContrast.ComputeAgedLoad), a kontrast
        /// liczy rytm bez zaniku - rozjazd swiadomy, opisany przy konstruktorze TensionModel.
        /// </summary>
        public ContrastTuning contrast = ContrastTuning.Default();

        /// <summary>
        /// Predykat KRYZYSU SKRAJNEGO (Core/Tension/CrisisDetector) - WSPOLNA MASZYNERIA, nie profil.
        ///
        /// Decyzja autora po przegladzie etapu 4: gdy co najmniej polowa kolonistow obecnych na
        /// mapie lezy powalona ostro, KAZDY profil przechodzi na Breathe z moca co najwyzej 0,
        /// a straznik serii ciszy zostaje zawieszony. Regula bezpieczenstwa ma byc jednakowa dla
        /// wszystkich osobowosci - inaczej porownanie profili mieszaloby osobowosc z tym,
        /// czy regula w ogole dziala.
        /// </summary>
        public CrisisParams crisis = CrisisParams.Default();

        /// <summary>
        /// Warstwa LUKOW narracyjnych (krok 5) - WSPOLNA maszyneria, nie profil: limit lukow
        /// rownoczesnych i wylacznik do serii kontrolnych "bez lukow". Katalog lukow zyje osobno,
        /// w Defs/Arcs (NarrativeArcDef).
        /// </summary>
        public ArcParams arcs = ArcParams.Default();

        /// <summary>
        /// Czy zlozony opis narracyjny ma zastapic waniliowy list w grze.
        ///
        /// DOMYSLNIE WYLACZONE I TO JEST DECYZJA, NIE ZANIECHANIE. Powod jest zmierzony:
        /// customLetterText honoruje tylko bazowy IncidentWorker.SendIncidentLetter (takze przez
        /// SendStandardLetter), a przez niego przechodzi 9 z naszych 13 incydentow. Pozostale
        /// cztery ignoruja pole: MeteoriteImpact i RansomDemand buduja list samodzielnie,
        /// a WandererJoin i RefugeePodCrash (IncidentWorker_GiveQuest) oddaja list zadaniu.
        /// Wlaczenie przelacznika daje wiec rozgrywke, w ktorej CZESC zdarzen ma opis zlozony
        /// z klockow, a czesc waniliowy - niespojnosc widoczna dla gracza i trudna do obronienia
        /// w rozdziale o ewaluacji. (Wczesniejsze "7 z 12" pochodzilo z pomiaru obalonego
        /// przegladem - zgadnietych typow lisciowych bez przejscia po dziedziczeniu.)
        ///
        /// Mechanizm jest gotowy i przetestowany; brakuje decyzji, czy niespojnosc 9/13 jest
        /// akceptowalna, czy najpierw domknac pozostala czworke (dwie z nich wymagaja wejscia
        /// w warstwe questow, nie w incydent).
        ///
        /// Katalog jest po stronie TEKSTU juz bezpieczny: regula "fakt w tekscie = warunek
        /// twardy" zostala przeprowadzona, a fragmenty, ktorych nie dalo sie zabezpieczyc
        /// (polozenie, koncowa skala), zostaly przepisane tak, by nic nie stwierdzaly.
        /// </summary>
        public bool useComposedLetter = false;

        public StorytellerCompProperties_Generative()
        {
            compClass = typeof(StorytellerComp_Generative);
        }

        /// <summary>
        /// Trzy progi polityki wyboru przepakowane do typu rdzenia. Osobny typ, a nie
        /// przekazywanie calych Props, bo Core nie ma prawa znac API gry.
        /// Uzytecznosci PASS tu NIE MA celowo: liczy ja wylacznie blok &lt;pass&gt; przez
        /// wlasne czynniki, a druga stala opisujaca to samo bylaby gwarantowanym cichym rozjazdem.
        /// </summary>
        public SelectionParameters ToSelectionParameters()
        {
            return new SelectionParameters
            {
                qualityCutoff = qualityCutoff,
                nearBestFraction = nearBestFraction,
                softmaxTemperature = softmaxTemperature,
                gateTemperature = gateTemperature
            };
        }

        /// <summary>
        /// Klamruje wartosci spoza dziedziny i zwraca opis poprawek albo null, gdy nic nie trzeba
        /// bylo ruszac. Wywolywane RAZ na starcie przez PNStartup, nie w petli decyzyjnej.
        ///
        /// Zasada: konfiguracja z XML nie ma prawa wywrocic tury narratora, ale kazda poprawka
        /// musi zostac ZAPISANA w logu. Cicha korekta jest gorsza od zlej wartosci, bo gracz
        /// (i autor pracy) widzi w XML jedna liczbe, a system uzywa innej.
        /// </summary>
        public string Sanitize()
        {
            var poprawki = new List<string>();

            if (candidateBudget <= 0)
            {
                poprawki.Add("candidateBudget " + Int(candidateBudget) + " -> " + Int(CandidateGenerator.DefaultBudget));
                candidateBudget = CandidateGenerator.DefaultBudget;
            }

            if (maxSelectionRounds < 1)
            {
                // Zero rund oznaczaloby, ze polityka wyboru nie uruchamia sie ANI RAZU, czyli
                // trwaly PASS bez zadnego komunikatu - z zewnatrz objaw identyczny jak pusty
                // katalog klockow. Minimum to jedna runda.
                poprawki.Add("maxSelectionRounds " + Int(maxSelectionRounds) + " -> " + Int(DefaultMaxSelectionRounds));
                maxSelectionRounds = DefaultMaxSelectionRounds;
            }

            qualityCutoff = Klamruj01(qualityCutoff, 0.35f, "qualityCutoff", poprawki);

            if (!Skonczona(nearBestFraction) || nearBestFraction <= 0f || nearBestFraction > 1f)
            {
                // Zero albo wartosc ujemna wpuszczaja do losowania cala stawke, wartosc powyzej 1
                // wyklucza nawet lidera. Oba przypadki niszcza sens pasma, wiec wracamy do domyslnej.
                poprawki.Add("nearBestFraction " + Num(nearBestFraction) + " poza (0,1] -> 0.75");
                nearBestFraction = 0.75f;
            }

            if (!Skonczona(softmaxTemperature) || softmaxTemperature <= 0f)
            {
                // T <= 0 to dzielenie przez zero w wykladniku softmaksu. Zamiast wylaczac losowanie
                // po cichu wracamy do domyslnej ostrej temperatury; tryb argmax nadal da sie
                // uzyskac swiadomie, ustawiajac T ponizej 1e-4 (obsluguje to ScoreMath).
                poprawki.Add("softmaxTemperature " + Num(softmaxTemperature) + " musi byc dodatnia -> 0.1");
                softmaxTemperature = 0.1f;
            }

            if (!Skonczona(gateTemperature) || gateTemperature <= 0f)
            {
                // Ta sama zasada co wyzej. Zdegenerowana brama oznaczalaby, ze cisza wygrywa
                // wylacznie wtedy, gdy przebije NAJLEPSZE zdarzenie - czyli praktycznie nigdy.
                poprawki.Add("gateTemperature " + Num(gateTemperature) + " musi byc dodatnia -> 0.1");
                gateTemperature = 0.1f;
            }

            vetoContextFitBelow = Klamruj01(vetoContextFitBelow, UtilityScorer.DefaultVetoContextFitBelow,
                                            "vetoContextFitBelow", poprawki);

            if (weights == null)
            {
                poprawki.Add("brak bloku <weights> -> wagi domyslne");
                weights = new ScoringWeights();
            }

            string ujemna;
            if (weights.AnyNegativeInXml(out ujemna))
            {
                // For() i tak scina ujemna wage do zera, ale nie wolno tego przemilczec: ujemna waga
                // wypycha uzytecznosc poza [0,1], a przy ujemnym maksimum warunek u >= 0.75 * best
                // przepuszcza kandydatow GORSZYCH od najlepszego, czyli pasmo near-best sie odwraca.
                poprawki.Add("waga ujemna w <weights>: " + ujemna + " (zostanie scieta do 0 - popraw XML)");
            }

            if (weights.Total() <= ScoreMath.Epsilon)
            {
                // Rdzen jest tu szczery i zwrocilby zera; warstwa integracji chroni gracza, bo
                // przy sumie wag 0 kazdy kandydat pada na progu jakosci i narrator milczy ZAWSZE,
                // a mod z zewnatrz wyglada na dzialajacy.
                poprawki.Add("suma wag <weights> = 0 - narrator bylby zawsze PASS -> przywrocono wagi domyslne");
                weights = new ScoringWeights();
            }

            if (pass == null)
            {
                poprawki.Add("brak bloku <pass> -> parametry PASS domyslne");
                pass = PassScoringParams.Defaults();
            }

            string poprawkiPass = pass.Sanitize();
            if (!string.IsNullOrEmpty(poprawkiPass))
            {
                poprawki.Add("<pass>: " + poprawkiPass);
            }

            if (crisis == null)
            {
                poprawki.Add("brak bloku <crisis> -> parametry kryzysu domyslne");
                crisis = CrisisParams.Default();
            }

            string poprawkiKryzysu = crisis.Sanitize();
            if (!string.IsNullOrEmpty(poprawkiKryzysu))
            {
                poprawki.Add("<crisis>: " + poprawkiKryzysu);
            }

            if (arcs == null)
            {
                poprawki.Add("brak bloku <arcs> -> parametry lukow domyslne");
                arcs = ArcParams.Default();
            }

            string poprawkiLukow = arcs.Sanitize();
            if (!string.IsNullOrEmpty(poprawkiLukow))
            {
                poprawki.Add("<arcs>: " + poprawkiLukow);
            }

            return poprawki.Count == 0 ? null : string.Join("; ", poprawki.ToArray());
        }

        /// <summary>
        /// Jedna linia z EFEKTYWNYMI wartosciami wszystkich parametrow decyzyjnych, logowana
        /// na starcie. To jedyny dowod, ze wezly konfiguracyjne z XML w ogole sie zdeserializowaly -
        /// przy nierozpoznanym wezle log pokaze wartosci domyslne zamiast tych z pliku.
        /// </summary>
        public string DescribeEffective()
        {
            var sb = new StringBuilder(320);
            // configStamp NAJPIERW - to jedyna pozycja w tej linii, ktora dowodzi, ze blok XML
            // w ogole wzial udzial w deserializacji. Reszta wartosci jest dzis rowna
            // inicjalizatorom C#, wiec sama z siebie niczego nie rozstrzyga.
            sb.Append("configStamp=").Append(string.IsNullOrEmpty(configStamp) ? "(PUSTY!)" : configStamp)
              .Append(" minDaysPassed=").Append(Num(minDaysPassed))
              .Append(' ');
            sb.Append("mtbDays=").Append(Num(mtbDays))
              .Append(" tagAkcji=").Append(string.IsNullOrEmpty(requiredActionTag) ? "dowolny" : requiredActionTag)
              .Append(" candidateBudget=").Append(Int(candidateBudget))
              .Append(" maxSelectionRounds=").Append(Int(maxSelectionRounds))
              .Append(" | ").Append(ToSelectionParameters().Describe())
              .Append(" vetoContextFitBelow=").Append(Num(vetoContextFitBelow))
              // ScoringWeights.Describe() dokleja sume SAM. Wczesniej bylo tu drugie
              // "(suma=...)", wiec linia kanarka drukowala te liczbe dwa razy - a jest to
              // linia, ktora idzie do pracy jako dowod zadzialania konfiguracji.
              // "wagi awaryjne" to od drugiego przegladu etapu 4 wagi PROFILU AWARYJNEGO
              // (NarratorProfile.Fallback) - wczesniej etykieta obiecywala to, czego sciezka
              // awaryjna nie robila (brala inicjalizatory C#).
              .Append(" | wagi awaryjne: ").Append(weights == null ? "BRAK" : weights.Describe())
              .Append(" | ").Append(pass == null ? "BRAK BLOKU <pass>" : pass.ToString())
              // Blok <contrast> byl jedynym parametrem decyzyjnym pominietym w tej linii.
              // NIE jest to kanarek w sensie configStamp - literowke w nazwie wezla
              // zagniezdzonego zglasza sam DirectXmlToObject ("doesn't correspond to any field
              // in type"). Ma jednak znaczenie inne: od kroku 4 te dwie liczby sa PARAMETREM
              // KALIBRACJI osobowosci (zrownanie wzmocnien przenioslo preferencje ulgi
              // z ukrytej stalej do intencji), wiec musza byc widoczne w logu razem z reszta
              // kalibracji, a nie tylko w kodzie.
              .Append(" | kontrast: reliefGain=").Append(Num(contrast == null ? 0f : contrast.reliefGain))
              .Append(" strikeGain=").Append(Num(contrast == null ? 0f : contrast.strikeGain))
              .Append(" | zlozonyList=").Append(useComposedLetter ? "tak" : "nie")
              .Append(" | ").Append(crisis == null ? "BRAK BLOKU <crisis>" : crisis.ToString())
              .Append(" | ").Append(arcs == null ? "BRAK BLOKU <arcs>" : arcs.ToString());
            return sb.ToString();
        }

        private static float Klamruj01(float wartosc, float domyslna, string nazwa, List<string> poprawki)
        {
            if (!Skonczona(wartosc))
            {
                poprawki.Add(nazwa + " nieliczbowe -> " + Num(domyslna));
                return domyslna;
            }
            if (wartosc < 0f)
            {
                poprawki.Add(nazwa + " " + Num(wartosc) + " -> 0");
                return 0f;
            }
            if (wartosc > 1f)
            {
                poprawki.Add(nazwa + " " + Num(wartosc) + " -> 1");
                return 1f;
            }
            return wartosc;
        }

        private static bool Skonczona(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        private static string Num(float v)
        {
            return v.ToString("0.0##", CultureInfo.InvariantCulture);
        }

        private static string Int(int v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }
    }
}
