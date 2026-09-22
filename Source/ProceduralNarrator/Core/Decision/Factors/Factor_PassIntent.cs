using System.Globalization;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Czynnik zgodnosci PASS-a z intencja narratora: intencja "oddech" (Breathe) premiuje cisze,
    /// intencja "eskaluj" (Escalate) ja wyklucza, "utrzymaj" (Hold) jest neutralna.
    ///
    /// STAN OD KROKU 4: waga 0.25 w &lt;pass&gt; (suma wag PASS = 1.0), wartosci FitFor: Escalate 0.0,
    /// Hold 0.5, Breathe 1.0 - a Breathe wymuszone regula kryzysu skrajnego daje tu takze 1.0.
    /// To jest KANAL, ktorym krzywa dramaturgiczna steruje tempem: intencja podnosi albo obniza
    /// uzytecznosc ciszy, a brama porownuje ja z najlepszym zdarzeniem. Pelny wzor:
    ///     U_pass = (wR * r + wB * 1 + wI * FitFor(intencja)) / (wR + wB + wI)
    ///
    /// HISTORIA (krok 3): waga wynosila 0, a czynnik mimo to byl liczony i trafial do sladu, zeby
    /// linia [PN-DATA] miala ten sam zestaw kolumn w kroku 3 i 4. Waga 0 jest w normalizacji
    /// dokladnie neutralna (0 do licznika i 0 do mianownika), wiec wpiecie nie zmienialo decyzji.
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
                          + "; waga " + p.For(FactorName).ToString("0.00", CultureInfo.InvariantCulture);
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
