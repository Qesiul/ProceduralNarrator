using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Czynnik "logiczna zasadnosc w kontekscie": czyta GOTOWE dopasowanie kontekstowe
    /// kandydata (ComposedEvent.ContextFit) i podaje je warstwie decyzyjnej razem ze sladem.
    ///
    /// DLACZEGO CZYNNIK NICZEGO TU NIE LICZY. contextFit jest wlasnoscia ZLOZONEGO
    /// wydarzenia, a nie pojedynczego klocka (decyzja projektowa kroku 2), i jest liczony
    /// dokladnie raz - przez ContextEvaluator, w chwili materializacji kandydata przez
    /// warstwe kompozycji. Przeliczanie go tutaj mialoby trzy wady naraz: (1) rozdwoilo by
    /// zrodlo prawdy o dopasowaniu, (2) zlamalo budzet ocen (koszt Evaluate jest zaplacony
    /// raz na kandydata, nie raz na czynnik), (3) wymagalo od czynnika dostepu do puli
    /// preferencji klockow, czyli zaleznosci, ktorej warstwa decyzyjna nie ma i miec nie musi.
    /// Ten czynnik jest wiec CELOWO trywialny - i to jest jego zaleta, a nie brak.
    ///
    /// TO JEST OS WETA. UtilityScorer czyta SUROWA wartosc tego czynnika (po nazwie) i przy
    /// wartosci ponizej progu weta wykresla kandydata niezaleznie od jego pozostalych ocen.
    /// Dlatego wartosc zwracana w przypadkach zdegenerowanych jest dobrana tak, zeby psula sie
    /// W STRONE ODRZUCENIA: kandydat bez zlozonego wydarzenia dostaje 0, a nie wartosc
    /// neutralna. Kandydat-widmo ma zniknac glosno w logu, a nie po cichu konkurowac.
    ///
    /// ZNANY ARTEFAKT DANYCH (nie kodu): ContextEvaluator zwraca 1.0, gdy kandydat nie ma
    /// ZADNEJ preferencji ("brak informacji nie jest kara"). Na dzisiejszym katalogu dotyczy
    /// to dokladnie 2 z 84 kandydatow (Ambrozja + Natura + Cisza + cel + Slabo), ktore maja
    /// przez to staly, bezwarunkowy bonus na osi o najwiekszej wadze. Naprawa nalezy do
    /// WARSTWY DANYCH - dopisania preferencji w XML - a nie do tego czynnika: kara za brak
    /// pokrycia preferencjami wpisana w kod przeniosla by regule tresci do logiki decyzyjnej,
    /// wbrew zasadzie z sekcji 15 CLAUDE.md. Do zalatwienia przed kalibracja wag.
    /// </summary>
    public class Factor_ContextFit : IScoringFactor
    {
        /// <summary>
        /// Nazwa czynnika wyprowadzona WPROST z nazwy pola wagi, a nie wpisana literalem.
        /// Niezmiennika "nazwa czynnika == nazwa pola wagi == nazwa wezla XML" pilnuje wtedy
        /// kompilator: zmiana nazwy pola przenosi sie tutaj sama, a literal cicho zostalby
        /// przy starej nazwie i czynnik dostalby wage 0, czyli przestal cokolwiek znaczyc.
        /// </summary>
        public const string FactorName = nameof(ScoringWeights.contextFit);

        /// <summary>
        /// Wartosc dla przypadkow, w ktorych czynnik nie ma czego zmierzyc, ale kandydat
        /// NIE jest uszkodzony (pseudo-kandydat PASS). 0.5 to przyjeta w calym projekcie
        /// umowa "brak informacji" - ta sama, ktora zwraca zaslepka zgodnosci z intencja
        /// i pusta historia w czynniku swiezosci.
        /// </summary>
        public const float NeutralValue = 0.5f;

        public string Name
        {
            get { return FactorName; }
        }

        /// <summary>
        /// Zwraca ComposedEvent.ContextFit kandydata, a jako slad - rozbicie tej wartosci
        /// na poszczegolne preferencje (ComposedEvent.FitTrace).
        ///
        /// Klamrowanie do [0,1] jest tu redundantne wobec ScoreMath.Sanitize01 w UtilityScorer,
        /// ale ma inny cel: Sanitize01 tylko ratuje matematyke, a my chcemy, zeby w sladzie
        /// BYLO WIDAC, ze zmierzona wartosc byla spoza zakresu. Wartosc spoza [0,1] oznacza
        /// blad w ContextEvaluator albo w wagach preferencji i nie ma prawa przejsc niezauwazona.
        /// </summary>
        public float Evaluate(ScoredCandidate candidate, DecisionContext context, out string explanation)
        {
            if (candidate == null)
            {
                // Blad okablowania warstwy decyzyjnej. Zero, bo przy zerze zadziala weto
                // spojnosci i kandydat-widmo wypadnie z rankingu, zamiast w nim zostac.
                explanation = "brak kandydata - wartosc 0 (kandydat zostanie zawetowany)";
                return 0f;
            }

            if (candidate.IsPass)
            {
                // MARTWA ASERCJA OBRONNA. PASS ma wlasna, ROZLACZNA tablice czynnikow i nigdy
                // nie powinien tu trafic. Gdyby jednak trafil, alternatywa - odczyt Event.ContextFit
                // z Event rownego null - bylaby wyjatkiem w srodku tury narratora. Cisza nie ma
                // przedmiotu dopasowania kontekstowego, wiec wartosc jest neutralna, nie zerowa:
                // zero uruchomiloby weto na kandydacie, ktory z definicji weta nie podlega.
                explanation = "PASS - dopasowanie kontekstowe nie dotyczy (wartosc neutralna 0.50)";
                return NeutralValue;
            }

            if (candidate.Event == null)
            {
                // IsPass == false przy Event == null lamie niezmiennik ScoredCandidate
                // ("Event jest null wtedy i tylko wtedy, gdy IsPass"). Glosno i do weta.
                explanation = "kandydat bez zlozonego wydarzenia - wartosc 0 (niespojny stan)";
                return 0f;
            }

            float raw = candidate.Event.ContextFit;
            float value = Curves.Clamp01(raw);

            string trace = candidate.Event.FitTrace;
            if (string.IsNullOrEmpty(trace))
            {
                // Pusty slad znaczy, ze kandydat nie przeszedl przez ContextEvaluator.
                // Wartosc domyslna pola ContextFit to 1.0, wiec taki kandydat wygladalby
                // na idealnie dopasowany - to jest dokladnie ta cicha awaria, przed ktora
                // ostrzega CLAUDE.md przy pustym katalogu klockow. Slad musi ja nazwac.
                trace = "BRAK SLADU DOPASOWANIA - czy ContextEvaluator byl wolany?";
            }

            var sb = new StringBuilder();
            sb.Append("contextFit=").Append(value.ToString("0.00", CultureInfo.InvariantCulture));

            // Porownanie przez !(raw == value) zamiast raw != value: dla NaN oba operatory
            // dzialaja tak samo, ale zapis wprost mowi, ze interesuje nas KAZDA rozbieznosc,
            // lacznie z ta wywolana przez wartosc nieokreslona.
            if (!(raw == value))
            {
                sb.Append(" [WARTOSC SPOZA ZAKRESU: ")
                  .Append(raw.ToString("0.000", CultureInfo.InvariantCulture))
                  .Append(" -> ")
                  .Append(value.ToString("0.000", CultureInfo.InvariantCulture))
                  .Append(']');
            }

            sb.Append(" | ").Append(trace);
            explanation = sb.ToString();
            return value;
        }
    }
}
