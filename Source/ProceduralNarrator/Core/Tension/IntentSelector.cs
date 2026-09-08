using System.Globalization;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Tension
{
    /// <summary>
    /// Wyjscie warstwy planowania na jedna ture: intencja plus docelowa intensywnosc.
    /// Sekcja 5.5 koncepcji wymaga OBU - "intencja narratora oraz docelowy poziom
    /// intensywnosci wydarzenia".
    /// </summary>
    public class IntentDecision
    {
        public Intent Intent;

        /// <summary>Docelowa intensywnosc na skali IntensityLevel, [-intensitySpan, +intensitySpan].</summary>
        public float TargetIntensity;

        /// <summary>Napiecie, z ktorego to wyniklo. Do logu i do sladu.</summary>
        public float Tension;

        public string Trace;
    }

    /// <summary>
    /// Zamienia napiecie na ZAMIAR. Dwa progi dziela [0,1] na trzy przedzialy, a intensywnosc
    /// docelowa jest interpolowana z napiecia CIAGLE - dzieki czemu narrator nie skacze miedzy
    /// trzema poziomami mocy, tylko przesuwa preferencje plynnie.
    ///
    /// DLACZEGO Intent.Pass NIE JEST TU NIGDY ZWRACANY - to jest decyzja, nie przeoczenie.
    ///
    /// Po pierwsze, bylby zbedny. Factor_PassIntent.FitFor daje Breathe -> 1.0 i Pass -> 1.0,
    /// czyli DOKLADNIE TE SAMA wartosc: Breathe juz maksymalnie sprzyja ciszy. Osobna intencja
    /// nie dodalaby ani jednego bitu informacji do bramy.
    ///
    /// Po drugie, i wazniejsze, bylby szkodliwy. Factor_PassRestraint ma udokumentowane UJEMNE
    /// sprzezenie zwrotne: gestosc zdarzen zanika w czasie, wiec kazda tura ciszy OSLABIA
    /// uzytecznosc PASS i narrator sam wraca do dzialania. Intencja Pass wyprowadzona z napiecia
    /// albo z gestosci odwrocilaby ten znak na DODATNI - cisza obnizalaby napiecie wolniej, niz
    /// podnosilaby sklonnosc do ciszy, i narrator zamilklby na stale. Wartosc zostaje w enumie
    /// jako zarezerwowana; pilnuje tego asercja w walidatorze.
    /// </summary>
    public static class IntentSelector
    {
        public static IntentDecision Select(float tension, TensionParams parameters)
        {
            TensionParams p = parameters ?? TensionParams.Default();
            var decision = new IntentDecision();
            decision.Tension = tension;

            if (tension < p.calmBelow)
            {
                decision.Intent = Intent.Escalate;
            }
            else if (tension > p.tenseAbove)
            {
                decision.Intent = Intent.Breathe;
            }
            else
            {
                decision.Intent = Intent.Hold;
            }

            // Interpolacja LINIOWA, celowo niezalezna od progow: napiecie 0 -> +span,
            // 0.5 -> 0 (Normal), 1 -> -span. Gdyby moc byla wyprowadzana z INTENCJI, a nie
            // z napiecia, mielibysmy trzy poziomy schodkowe i skok mocy dokladnie w punkcie
            // przejscia progu - czyli najbardziej widoczna dla gracza nieciaglosc w calym
            // systemie. Ciagla interpolacja daje ten sam kierunek bez schodka.
            decision.TargetIntensity = p.intensitySpan * (1f - 2f * Curves.Clamp01(tension));

            decision.Trace = "intencja=" + decision.Intent
                             + " napiecie=" + tension.ToString("0.000", CultureInfo.InvariantCulture)
                             + " progi=[" + p.calmBelow.ToString("0.00", CultureInfo.InvariantCulture)
                             + ", " + p.tenseAbove.ToString("0.00", CultureInfo.InvariantCulture) + "]"
                             + " docelowaMoc=" + decision.TargetIntensity.ToString("0.00", CultureInfo.InvariantCulture);

            return decision;
        }

    }
}
