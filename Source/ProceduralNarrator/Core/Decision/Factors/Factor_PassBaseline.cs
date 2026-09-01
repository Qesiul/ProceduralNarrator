using System.Globalization;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Czynnik bazowy PASS: bazowa sklonnosc narratora do ciszy, wyrazona jako STALA 1.0.
    ///
    /// DLACZEGO WARTOSC JEST STALA, A REGULOWANA JEST WAGA:
    /// w modelu utylitarnym liczy sie wylacznie iloczyn wartosci i wagi, wiec konfigurowanie obu
    /// naraz jest nadmiarowe i daje dwa pokretla na to samo. Regulujemy WAGE. Przy domyslnej sumie
    /// wag rownej 1.0 (0.75 + 0.25 + 0) waga baseline czyta sie wprost jako PODLOGA uzytecznosci
    /// PASS: U_pass_min = w_baseline / sumW = 0.25. To jest "bazowa sklonnosc do ciszy" z decyzji
    /// projektowych, wyrazona jedna liczba w XML.
    ///
    /// KONTRARGUMENT DO ZAPISANIA W PRACY (bo czytelnik go postawi):
    /// staly wyraz w scoringu latwo odczytac jako sztuczke podbijajaca wynik PASS-a. Odpowiedz:
    /// to jest standardowy wyraz bazowy modelu utylitarnego, jest w sladzie JAWNY (wartosc 1.00,
    /// waga i udzial widoczne w kazdej linii logu), ma dokladnie jedno regulowane pokretlo,
    /// a przy sumie wag 1.0 to pokretlo ma bezposrednia interpretacje jako podloga uzytecznosci.
    /// </summary>
    public class Factor_PassBaseline : IScoringFactor
    {
        /// <summary>Nazwa czynnika == nazwa wagi w PassScoringParams == nazwa wezla XML.</summary>
        public const string FactorName = PassScoringParams.BaselineFactorName;

        /// <summary>
        /// Wartosc stala czynnika. Nie jest parametrem i nie ma byc: parametrem jest waga.
        /// </summary>
        public const float ConstantValue = 1f;

        private readonly PassScoringParams p;

        public Factor_PassBaseline(PassScoringParams parameters)
        {
            p = parameters ?? PassScoringParams.Defaults();
        }

        public string Name
        {
            get { return FactorName; }
        }

        public float Evaluate(ScoredCandidate candidate, DecisionContext context, out string explanation)
        {
            // Ten sam straznik kontraktu co w Factor_PassRestraint - czynniki obu tablic sa rozlaczne
            // i pomylka w okablowaniu ma byc widoczna w sladzie, a nie cicho przepuszczona.
            if (candidate != null && !candidate.IsPass)
            {
                explanation = "BLAD: czynnik PASS na kandydacie-zdarzeniu";
                return 0f;
            }

            // Podloge liczymy do wyjasnienia, a nie do wyniku: pokazuje ona, ile PASS jest wart
            // przy zerowej gestosci, czyli jaka jest jego uzytecznosc "z samego istnienia".
            explanation = "stala sklonnosc do ciszy; podloga U_pass = w_baseline/sumaWag = "
                          + p.UtilityFloor.ToString("0.000", CultureInfo.InvariantCulture);
            return ConstantValue;
        }
    }
}
