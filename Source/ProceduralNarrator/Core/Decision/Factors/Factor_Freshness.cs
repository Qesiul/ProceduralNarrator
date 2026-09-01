using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Pokretla czynnika swiezosci. Dwa NIEZALEZNE zestawy parametrow na dwie osie
    /// (temat i konkretny klocek akcji) plus rampa rozgrzewki.
    ///
    /// ROZDZIELENIE POKRETEL JEST CELOWE: polowicznosc (halfLife) rzadzi tym, JAK DLUGO
    /// pamietamy uzycie, ostrosc (sharpness) tym, JAK MOCNO boli powtorka, a waga osi tym,
    /// JAKI UDZIAL ma dana os w wyniku. To trzy rozne pytania i sklejenie ich w jedno
    /// pokretlo uniemozliwilo by strojenie (np. "pamietaj dlugo, ale karz lagodnie").
    ///
    /// Klasa nie jest dzis czytana z XML - w kroku 3 czynnik powstaje na wartosciach
    /// domyslnych. Pola sa jednak publiczne i z inicjalizatorami, wiec przeniesienie ich
    /// do StorytellerCompProperties bedzie dopisaniem jednego wezla, bez zmian w logice.
    /// </summary>
    public class FreshnessSettings
    {
        /// <summary>
        /// Polowiczny zanik cisnienia na osi TEMATU, liczony w DECYZJACH (~7.5 dnia gry
        /// przy mtbDays = 2.5).
        ///
        /// H_temat &lt; H_akcja CELOWO. W katalogu jest 6 uzywanych tematow, ale 12 klockow
        /// akcji, przy czym Social, Natural i Economic maja po trzy akcje. Gdyby temat
        /// zanikal wolniej niz akcja, narrator bylby systematycznie spychany do tematow
        /// jednoakcyjnych (Raid, Military, Supernatural) - czyli dokladnie do klockow
        /// z najostrzejszymi warunkami twardymi i najczestszymi odmowami CanFireNow.
        /// </summary>
        public float themeHalfLifeDecisions = 3f;

        /// <summary>Polowiczny zanik cisnienia na osi KLOCKA AKCJI (~12.5 dnia gry).</summary>
        public float actionHalfLifeDecisions = 5f;

        /// <summary>Ostrosc kary na osi tematu: jedna swieza powtorka daje exp(-0.60) = 0.549.</summary>
        public float themeSharpness = 0.60f;

        /// <summary>
        /// Ostrosc kary na osi akcji: jedna swieza powtorka daje exp(-1.20) = 0.301.
        /// Rowne DWUKROTNOSCI ostrosci tematu - powtorzenie konkretnego incydentu boli
        /// dokladnie dwa razy mocniej niz powtorzenie samego tematu.
        /// </summary>
        public float actionSharpness = 1.20f;

        /// <summary>Udzial osi tematu w wyniku.</summary>
        public float themeAxisWeight = 0.40f;

        /// <summary>
        /// Udzial osi akcji w wyniku - WIEKSZY niz osi tematu, bo gracz rozpoznaje konkretne
        /// zdarzenie ("znowu szal zwierzat"), a nie kategorie. To tez cel dosypki katalogu
        /// z kroku 2: wiele akcji na temat istnieje po to, by narrator zmienial AKCJE
        /// wewnatrz tematu, wiec ta wlasnie os musi dominowac.
        /// </summary>
        public float actionAxisWeight = 0.60f;

        /// <summary>
        /// Ile EMISJI musi byc w buforze, zeby czynnik mial pelny glos. Ponizej tej liczby
        /// wynik jest mieszany liniowo z wartoscia neutralna: przy jednym punkcie danych
        /// narrator nie ma prawa twierdzic, ze cokolwiek jest zuzyte. Ta sama rampa lagodzi
        /// powrot po wczytaniu zapisu (historia startuje pusta - patrz EventHistory).
        /// </summary>
        public int warmupDecisions = 4;

        /// <summary>Umowna wartosc "brak informacji", wspolna dla calej warstwy decyzyjnej.</summary>
        public float neutralValue = 0.5f;

        /// <summary>Nowy zestaw wartosci domyslnych. Istnieje dla czytelnosci wywolan w testach.</summary>
        public static FreshnessSettings Default()
        {
            return new FreshnessSettings();
        }
    }

    /// <summary>
    /// Pelne rozbicie jednego pomiaru swiezosci. Slad scoringu jest w tym projekcie
    /// WYMAGANY, a nie opcjonalny (sekcja 8 CLAUDE.md) - to sa dane wejsciowe czesci
    /// badawczej, wiec kazda wielkosc posrednia musi dac sie odczytac osobno, a nie tylko
    /// jako gotowa liczba koncowa.
    /// </summary>
    public class FreshnessResult
    {
        /// <summary>Wartosc koncowa 0..1 - to ja zwraca czynnik.</summary>
        public float Value;

        /// <summary>Wartosc PRZED mieszaniem z neutralem (przed rampa rozgrzewki).</summary>
        public float RawValue;

        /// <summary>exp(-k_temat * cisnienie tematu).</summary>
        public float ThemeFreshness;

        /// <summary>exp(-k_akcja * cisnienie akcji).</summary>
        public float ActionFreshness;

        /// <summary>Suma wag recencji wpisow o TYM SAMYM temacie.</summary>
        public float ThemePressure;

        /// <summary>Suma wag recencji wpisow o TYM SAMYM klocku akcji.</summary>
        public float ActionPressure;

        /// <summary>Udzial glosu czynnika: 0 przy pustej historii, 1 po rozgrzewce.</summary>
        public float Confidence;

        /// <summary>Ile wpisow pasowalo na osi tematu (bez wag - do diagnostyki).</summary>
        public int ThemeMatches;

        /// <summary>Ile wpisow pasowalo na osi akcji (bez wag - do diagnostyki).</summary>
        public int ActionMatches;

        /// <summary>Slad w jednej linii, gotowy do wpisania w rozbicie scoringu.</summary>
        public string Trace;
    }

    /// <summary>
    /// Czynnik SWIEZOSCI TYPU: jak bardzo dany kandydat jest "zuzyty" w swietle ostatnich
    /// decyzji narratora. To on odpowiada za metryki powtarzalnosci i roznorodnosci typow
    /// z sekcji 12 koncepcji.
    ///
    /// DWIE OSIE, BO GRACZ PATRZY NA DWIE RZECZY NARAZ: na kategorie zdarzenia (temat)
    /// i na konkretne zdarzenie (klocek akcji). Os akcji wazy wiecej, bo to ona odpowiada
    /// odczuciu "znowu to samo".
    ///
    /// ZANIK LICZONY W DECYZJACH, NIE W DNIACH ANI TICKACH. Dwie decyzje wstecz to dwie
    /// decyzje wstecz niezaleznie od tego, czy dzielilo je pol dnia, czy piec dni - a wiec
    /// czynnik nie rozjedzie sie, gdy krok 4 zacznie zmieniac tempo narratora. Konsekwencja,
    /// ktora trzeba pamietac: seria decyzji PASS przesuwa czas narracyjny (DecisionCount
    /// rosnie takze przy ciszy), wiec sama z siebie odswieza wszystkie zdarzenia.
    ///
    /// ZLOZENIE OSI TO SREDNIA WAZONA, NIE ILOCZYN I NIE MINIMUM. Powtorzenie akcji z
    /// definicji powtarza jej temat, wiec iloczyn karal by dwa razy za to samo (0.549 * 0.301
    /// = 0.165, czyli de facto weto), a minimum gubi informacje i nie stopniuje. Decyzja
    /// projektowa mowi wprost: swiezosc NIE MA PRAWA WETA. Wynika to tu ze wzoru, a nie
    /// z klamry - wartosc nalezy do przedzialu (0,1] i nigdy nie osiaga zera (absolutne
    /// minimum osiagalne w grze to 0.0221 przy 24 identycznych zdarzeniach z rzedu).
    ///
    /// CZYNNIK JEST BEZSTANOWY. Compute jest funkcja czysta i nie ma tu zadnego cache'a
    /// ani zapamietanego sladu - 84 kandydatow razy 24 wpisy to okolo 2000 elementarnych
    /// operacji na decyzje, a bezstanowosc wyklucza cala klase bledow determinizmu i jest
    /// warta wiecej niz ta mikrooptymalizacja.
    /// </summary>
    public class Factor_Freshness : IScoringFactor
    {
        /// <summary>
        /// Nazwa wyprowadzona z nazwy pola wagi, nie wpisana literalem - zmiana nazwy pola
        /// przenosi sie tu sama, a literal cicho zostalby przy starej nazwie, czynnik
        /// dostalby wage 0 i przestal cokolwiek robic (awaria bez zadnego objawu w logu).
        /// </summary>
        public const string FactorName = nameof(ScoringWeights.freshness);

        private readonly FreshnessSettings settings;

        public Factor_Freshness()
            : this(null)
        {
        }

        public Factor_Freshness(FreshnessSettings settings)
        {
            // Ustawienia rowne null zastepujemy domyslnymi zamiast rzucac wyjatkiem:
            // wyjatek w sciezce decyzyjnej zabija cala ture narratora w grze.
            this.settings = settings ?? FreshnessSettings.Default();
        }

        /// <summary>Ustawienia efektywnie uzyte przez ten czynnik - do logu startowego.</summary>
        public FreshnessSettings Settings
        {
            get { return settings; }
        }

        public string Name
        {
            get { return FactorName; }
        }

        public float Evaluate(ScoredCandidate candidate, DecisionContext context, out string explanation)
        {
            if (candidate == null)
            {
                explanation = "brak kandydata - wartosc neutralna "
                              + settings.neutralValue.ToString("0.00", CultureInfo.InvariantCulture);
                return settings.neutralValue;
            }

            if (candidate.IsPass || candidate.Event == null)
            {
                // MARTWA ASERCJA OBRONNA: PASS ma wlasna, rozlaczna tablice czynnikow.
                // Gdyby jednak tu trafil - cisza nie ma ani tematu, ani klocka akcji, wiec
                // obie osie sa dla niej niezdefiniowane. Swiadomie NIE liczymy "swiezosci
                // ciszy" ("ostatnio pasowalismy, wiec PASS jest zuzyty"): to jest decyzja
                // o TEMPIE, a tempo nalezy do krzywej dramaturgicznej (krok 4) i do wlasnego
                // wzoru uzytecznosci PASS-u.
                explanation = "PASS - swiezosc nie dotyczy (wartosc neutralna "
                              + settings.neutralValue.ToString("0.00", CultureInfo.InvariantCulture) + ")";
                return settings.neutralValue;
            }

            EventHistory history = context == null ? null : context.History;
            FreshnessResult result = Compute(candidate.Event.Theme, candidate.Event.ActionBlockId, history, settings);
            explanation = result.Trace;
            return result.Value;
        }

        /// <summary>
        /// Rdzen czynnika: liczy swiezosc pary (temat, klocek akcji) wobec pamieci narratora.
        /// Funkcja CZYSTA - nie dotyka zadnego stanu i nie modyfikuje historii.
        ///
        /// Wystawiona jako publiczna i statyczna, bo tego samego rachunku uzywa walidator
        /// offline (odtworzenie tablicy wartosci referencyjnych) i testy jednostkowe -
        /// bez budowania sztucznego ScoredCandidate i DecisionContext.
        /// </summary>
        public static FreshnessResult Compute(Theme theme, string actionId, EventHistory history, FreshnessSettings settings)
        {
            if (settings == null)
            {
                settings = FreshnessSettings.Default();
            }

            var res = new FreshnessResult();
            float neutral = settings.neutralValue;

            if (history == null)
            {
                // Brak pamieci to nie to samo co pamiec pusta (ta liczy sie normalnie i tez
                // wychodzi na neutral przez confidence = 0), ale wynik musi byc ten sam,
                // zeby awaria okablowania nie zmienila poziomu uzytecznosci wszystkich zdarzen.
                res.Value = neutral;
                res.RawValue = 1f;
                res.ThemeFreshness = 1f;
                res.ActionFreshness = 1f;
                res.Trace = "swiezosc=" + neutral.ToString("0.00", CultureInfo.InvariantCulture)
                            + " [brak historii - czynnik nie zabiera glosu]";
                return res;
            }

            // Ostrosci i wagi osi klamrujemy od dolu do zera. Wartosc ujemna nie jest tu
            // "mocniejszym strojeniem", tylko ODWROCENIEM sensu czynnika: ujemne k daje
            // exp(dodatnie) > 1, czyli premie za powtarzanie tego samego zdarzenia.
            float kTheme = NonNegative(settings.themeSharpness);
            float kAction = NonNegative(settings.actionSharpness);
            float wTheme = NonNegative(settings.themeAxisWeight);
            float wAction = NonNegative(settings.actionAxisWeight);

            double themePressure = 0.0;
            double actionPressure = 0.0;
            int themeMatches = 0;
            int actionMatches = 0;

            bool hasActionKey = !string.IsNullOrEmpty(actionId);
            IReadOnlyList<EventHistoryEntry> entries = history.Entries;

            for (int i = 0; i < entries.Count; i++)
            {
                EventHistoryEntry e = entries[i];
                if (e == null)
                {
                    continue;
                }

                // Wiek pochodzi WYLACZNIE z historii (AgeInDecisions), zeby istnialo jedno
                // miejsce, w ktorym mieszka bezpiecznik Math.Max(1, ...) na niespojny stan
                // po wczytaniu zapisu.
                int age = history.AgeInDecisions(e);

                if (e.Theme == theme)
                {
                    themePressure += RecencyWeight(age, settings.themeHalfLifeDecisions);
                    themeMatches++;
                }

                // Porownanie ORDYNALNE: ActionBlockId to identyfikator techniczny (defName
                // klocka), a porownanie kulturowe potrafi zalezec od ustawien systemu -
                // to byla by dziura w determinizmie warstwy decyzyjnej.
                if (hasActionKey && string.CompareOrdinal(e.ActionBlockId, actionId) == 0)
                {
                    actionPressure += RecencyWeight(age, settings.actionHalfLifeDecisions);
                    actionMatches++;
                }
            }

            float themeFresh = (float)Math.Exp(-kTheme * themePressure);
            float actionFresh = (float)Math.Exp(-kAction * actionPressure);

            float sumW = wTheme + wAction;
            float raw;
            string weightsNote = null;
            if (sumW <= 0f)
            {
                // Obie osie wylaczone - czynnik nie ma czym mierzyc zuzycia. Wynik neutralny,
                // ale GLOSNO oznaczony w sladzie: cicho zwrocona jedynka wygladalaby jak
                // "wszystko swieze" i nikt by nie zauwazyl, ze czynnik jest martwy.
                raw = neutral;
                weightsNote = "OBIE WAGI OSI ZEROWE - czynnik nie mierzy niczego";
            }
            else
            {
                raw = (wTheme * themeFresh + wAction * actionFresh) / sumW;
            }

            // Rozgrzewka: przy chudej historii wynik jest sciagany do neutralu. Pusta historia
            // daje confidence = 0, czyli DOKLADNIE wartosc neutralna - i to nie jest przypadek
            // szczegolny obsluzony if-em, tylko granica tego samego wzoru.
            float confidence = settings.warmupDecisions <= 0
                ? 1f
                : Math.Min(1f, history.Count / (float)settings.warmupDecisions);
            confidence = Curves.Clamp01(confidence);

            // Klamra na koncu jest formalna (matematycznie wynik i tak nalezy do (0,1]),
            // ale zbija tez NaN, ktore mogloby przyjsc z patologicznej konfiguracji.
            float value = Curves.Clamp01(neutral + confidence * (raw - neutral));

            res.Value = value;
            res.RawValue = raw;
            res.ThemeFreshness = themeFresh;
            res.ActionFreshness = actionFresh;
            res.ThemePressure = (float)themePressure;
            res.ActionPressure = (float)actionPressure;
            res.Confidence = confidence;
            res.ThemeMatches = themeMatches;
            res.ActionMatches = actionMatches;
            res.Trace = BuildTrace(res, theme, actionId, history, wTheme, wAction, weightsNote);
            return res;
        }

        /// <summary>
        /// Waga recencji wpisu: 0.5^((wiek - 1) / H). Wiek 1 (decyzja bezposrednio poprzednia)
        /// daje dokladnie 1.0, wiec "swieza powtorka" ma zawsze pelne cisnienie niezaleznie
        /// od polowicznosci.
        ///
        /// Delegat do Curves.HalfLifeDecay, a nie wlasny Math.Pow: zanik polowiczny jest
        /// w projekcie JEDNA funkcja (uzywa go tez uzytecznosc PASS-u), wiec obie warstwy
        /// musza rozumiec ten sam parametr tak samo - lacznie z konwencja dla H &lt;= 0
        /// (zanik natychmiastowy: pelna waga dla wieku 1, zero dla starszych).
        /// </summary>
        private static double RecencyWeight(int ageInDecisions, float halfLifeDecisions)
        {
            // Przesuniecie o 1 zamienia "wiek w decyzjach" na "ile decyzji minelo od uzycia",
            // czyli na delte, ktorej oczekuje zanik polowiczny.
            return Curves.HalfLifeDecay(ageInDecisions - 1f, halfLifeDecisions);
        }

        private static float NonNegative(float v)
        {
            // NaN i nieskonczonosc traktujemy jak zero, a nie klamrujemy: kazde porownanie
            // z NaN jest falszywe, wiec "v < 0 ? 0 : v" przepuscilby go nietknietego.
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f)
            {
                return 0f;
            }
            return v;
        }

        /// <summary>
        /// Slad w jednej linii, wszystkie liczby przez InvariantCulture. Format jest czescia
        /// kontraktu danych badawczych: polski separator dziesietny rozbilby parsowanie
        /// w Pythonie w kroku 8.
        /// </summary>
        private static string BuildTrace(FreshnessResult res, Theme theme, string actionId,
                                         EventHistory history, float wTheme, float wAction, string weightsNote)
        {
            var sb = new StringBuilder();
            sb.Append("swiezosc=").Append(res.Value.ToString("0.00", CultureInfo.InvariantCulture));
            sb.Append(" [temat=").Append(theme)
              .Append(" p=").Append(res.ThemePressure.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" n=").Append(res.ThemeMatches.ToString(CultureInfo.InvariantCulture))
              .Append(" -> ").Append(res.ThemeFreshness.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" (w").Append(wTheme.ToString("0.00", CultureInfo.InvariantCulture)).Append(')');

            sb.Append("; akcja=").Append(string.IsNullOrEmpty(actionId) ? "?" : actionId)
              .Append(" p=").Append(res.ActionPressure.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" n=").Append(res.ActionMatches.ToString(CultureInfo.InvariantCulture))
              .Append(" -> ").Append(res.ActionFreshness.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" (w").Append(wAction.ToString("0.00", CultureInfo.InvariantCulture)).Append(')');

            sb.Append("; raw=").Append(res.RawValue.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" conf=").Append(res.Confidence.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" hist=").Append(history.Count.ToString(CultureInfo.InvariantCulture))
              .Append('/').Append(history.Capacity.ToString(CultureInfo.InvariantCulture))
              .Append(" dec=").Append(history.DecisionCount.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrEmpty(weightsNote))
            {
                sb.Append("; ").Append(weightsNote);
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
