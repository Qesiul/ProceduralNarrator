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

        /// <summary>
        /// Czy intencje i moc nadpisala regula kryzysu skrajnego (ApplyCrisis). Napiecie zostaje
        /// wtedy SUROWE - roznica miedzy nim a intencja jest sladem dzialania reguly.
        /// </summary>
        public bool CrisisOverride;

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

        /// <summary>
        /// REGULA KRYZYSU SKRAJNEGO, nakladana PO Select - decyzja autora po przegladzie etapu 4.
        ///
        /// Gdy kryzys zachodzi: intencja Breathe u KAZDEGO profilu, a docelowa moc
        /// min(moc profilu, 0). Select zostaje nietkniety, wiec jego wlasnosci (progi bez
        /// przeskoku, moc ciagla i nierosnaca wzgledem napiecia) obowiazuja nadal - a regula
        /// jest osobna, jawna nieciagloscia na granicy predykatu, ktora widac w sladzie.
        ///
        /// DLACZEGO min(x, 0), A NIE STALA. Moc 0 PODNIOSLABY moc profilu, ktory przy ciezkiej
        /// historii ma ja juz ujemna (napastliwy -0.875 -&gt; 0), czyli dala efekt odwrotny do celu.
        /// Moc "-span" oddalaby zachowanie w kryzysie parametrowi osobowosci i dawala najwiekszy
        /// skok. min(x, 0) nie dziala tam, gdzie profil juz lagodzi, i gwarantuje brak DODATNIEGO
        /// nacisku na moc wszedzie indziej. Roznice miedzy profilami w kryzysie zostaja w wagach
        /// i w mocy ujemnej - osobowosc dziala dalej, tylko ponad podloga bezpieczenstwa.
        ///
        /// Zwraca TEN SAM obiekt (zmieniony albo nie) - wolajacy nie musi wiedziec, czy regula
        /// zadzialala; wie to slad i pole CrisisOverride.
        /// </summary>
        public static IntentDecision ApplyCrisis(IntentDecision decision, CrisisReading crisis)
        {
            if (decision == null || crisis == null || !crisis.Extreme)
            {
                return decision;
            }

            Intent przed = decision.Intent;
            float mocPrzed = decision.TargetIntensity;

            decision.Intent = Intent.Breathe;
            decision.TargetIntensity = System.Math.Min(decision.TargetIntensity, 0f);
            decision.CrisisOverride = true;

            decision.Trace = (decision.Trace ?? string.Empty)
                             + " | " + crisis.Trace
                             + " -> intencja " + przed + "->" + decision.Intent
                             + ", moc " + mocPrzed.ToString("0.00", CultureInfo.InvariantCulture)
                             + "->" + decision.TargetIntensity.ToString("0.00", CultureInfo.InvariantCulture);
            return decision;
        }
    }
}
