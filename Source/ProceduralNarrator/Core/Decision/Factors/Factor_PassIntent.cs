using System.Globalization;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Czynnik zgodnosci PASS-a z intencja narratora: intencja "oddech" (Breathe) premiuje cisze,
    /// intencja "eskaluj" (Escalate) ja wyklucza, "utrzymaj" (Hold) jest neutralna.
    ///
    /// W KROKU 3 WAGA TEGO CZYNNIKA WYNOSI 0, A CZYNNIK MIMO TO JEST LICZONY I TRAFIA DO SLADU.
    /// Powod jest formatowy, nie merytoryczny: linia badawcza [PN-DATA] ma miec IDENTYCZNY zestaw
    /// i identyczna kolejnosc kolumn w kroku 3 i w kroku 4, zeby skrypty agregujace w Pythonie
    /// nie wymagaly przepisania po wpieciu krzywej dramaturgicznej. Waga 0 jest w normalizacji
    /// dokladnie neutralna (wnosi 0 do licznika i 0 do mianownika), wiec nie rozciencza wyniku -
    /// czynnik mozna wpiac juz dzis bez zadnego wplywu na decyzje. Dokladnie tak samo traktowany
    /// jest Factor_IntentAlignment po stronie zdarzen.
    ///
    /// UWAGA O PRZESTRZENIACH NAZW WAG: nazwa "intentAlignment" wystepuje ZAROWNO w ScoringWeights
    /// (czynniki zdarzeniowe), JAK I w PassScoringParams (czynniki PASS). To jest celowe i poprawne,
    /// bo obie przestrzenie sa rozlaczne i walidowane osobno. Nie wolno ich "dla porzadku"
    /// zunifikowac - zepnie to ze soba dwie niezalezne kalibracje.
    /// </summary>
    public class Factor_PassIntent : IScoringFactor
    {
        /// <summary>Nazwa czynnika == nazwa wagi w PassScoringParams == nazwa wezla XML.</summary>
        public const string FactorName = PassScoringParams.IntentAlignmentFactorName;

        private readonly PassScoringParams p;

        public Factor_PassIntent(PassScoringParams parameters)
        {
            p = parameters ?? PassScoringParams.Defaults();
        }

        public string Name
        {
            get { return FactorName; }
        }

        public float Evaluate(ScoredCandidate candidate, DecisionContext context, out string explanation)
        {
            if (candidate != null && !candidate.IsPass)
            {
                explanation = "BLAD: czynnik PASS na kandydacie-zdarzeniu";
                return 0f;
            }

            Intent intencja = context == null ? Intent.Hold : context.Intent;
            float fit = FitFor(intencja);

            explanation = "intencja=" + intencja + " -> dopasowanie "
                          + fit.ToString("0.00", CultureInfo.InvariantCulture)
                          + "; waga " + p.For(FactorName).ToString("0.00", CultureInfo.InvariantCulture)
                          + " (0 do kroku 4)";
            return fit;
        }

        /// <summary>
        /// Tablica dopasowania ciszy do intencji. Swiadomie tablica, a nie wzor:
        /// intencji sa cztery i kazda ma odrebne, jawne uzasadnienie narracyjne.
        ///   Escalate -> 0.0  cisza jest sprzeczna z eskalacja
        ///   Hold     -> 0.5  utrzymanie tempa nie faworyzuje ani ciszy, ani zdarzenia
        ///   Breathe  -> 1.0  oddech to wprost prosba o cisze
        ///   Pass     -> 1.0  intencja PASS wskazuje cisze wprost
        /// </summary>
        public static float FitFor(Intent intent)
        {
            switch (intent)
            {
                case Intent.Escalate:
                    return 0f;
                case Intent.Breathe:
                    return 1f;
                case Intent.Pass:
                    return 1f;
                case Intent.Hold:
                    return 0.5f;
                default:
                    // Nowa wartosc enuma dodana w kroku 4 bez aktualizacji tablicy: neutralnie 0.5,
                    // zeby nie faworyzowac ani ciszy, ani zdarzenia, dopoki ktos tego nie uzupelni.
                    return 0.5f;
            }
        }
    }
}
