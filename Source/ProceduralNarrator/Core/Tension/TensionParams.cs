using System.Globalization;
using System.Text;

namespace ProceduralNarrator.Core.Tension
{
    /// <summary>
    /// Parametry krzywej dramaturgicznej (sekcja 5.5 koncepcji). Razem z wektorem wag
    /// scoringu stanowia CALA definicje osobowosci narratora - patrz NarratorProfile.
    ///
    /// WSZYSTKIE POLA MAJA INICJALIZATOR. To wymog, nie styl: RimWorld deserializuje wezly
    /// XML wprost na pola, wiec brak wezla zostawia wartosc z inicjalizatora. Pole bez niego
    /// wpadloby po cichu na 0, a zero jest tu wszedzie wartoscia skrajna, nie neutralna.
    ///
    /// Nazwy pol sa camelCase, bo nazwa pola == nazwa wezla XML - ten sam niezmiennik,
    /// ktory obowiazuje w ScoringWeights i PassScoringParams.
    /// </summary>
    public class TensionParams
    {
        /// <summary>
        /// Waga czlonu NARRACYJNEGO: ile napiecia bierze sie z tego, co narrator ostatnio zrobil.
        /// </summary>
        public float narrativeWeight = 1.0f;

        /// <summary>
        /// Waga czlonu SYTUACYJNEGO: ile napiecia bierze sie z tego, co dzieje sie na mapie TERAZ.
        ///
        /// Oba czlony sa potrzebne, bo mierza rozlaczne rzeczy. Historia wie, ze dwa dni temu
        /// poszedl napad, ale nie wie, czy najezdzcy juz nie zyja, czy wlasnie wywazaja drzwi.
        /// </summary>
        public float situationalWeight = 1.0f;

        /// <summary>
        /// Polowiczny zanik czlonu narracyjnego W CISZY, w dniach gry.
        ///
        /// TEN PARAMETR JEST KONSTRUKCYJNIE KONIECZNY, NIE KOSMETYCZNY. Rytm z
        /// Factor_DramaticContrast zanika po INDEKSIE wpisu (lambda^i), a nie po czasie -
        /// osiem zdarzen wstecz wazy tyle samo, czy bylo to piec dni temu, czy piecdziesiat.
        /// Bez zaniku czasowego napiecie nigdy nie opadaloby w ciszy, wiec intencja Breathe,
        /// raz osiagnieta, ZATRZASNELABY SIE NA STALE: narrator milczalby, cisza nie
        /// obnizalaby napiecia, wiec dalej by milczal. Ten czlon domyka petle sprzezenia
        /// zwrotnego i ma na to osobna asercje w walidatorze.
        ///
        /// Domyslne 5 dni to ta sama stala co PassScoringParams.halfLifeDays (2 * mtbDays),
        /// czyli pamiec o dwa typowe odstepy miedzy wydarzeniami.
        /// </summary>
        public float halfLifeDays = 5f;

        /// <summary>
        /// Jaki ULAMEK kolonii musi lezec powalony, zeby czlon sytuacyjny osiagnal maksimum.
        /// 0.5 znaczy: polowa kolonii nieprzytomna to szczyt napiecia sytuacyjnego.
        /// </summary>
        public float downedFractionForMax = 0.5f;

        /// <summary>
        /// Ponizej tego napiecia narrator chce ESKALOWAC (nic sie nie dzieje, czas podniesc stawke).
        /// </summary>
        public float calmBelow = 0.30f;

        /// <summary>
        /// Powyzej tego napiecia narrator chce dac ODDECH.
        ///
        /// Niezmiennik: calmBelow &lt;= tenseAbove. Odwrocenie dawaloby przedzial pusty dla Hold
        /// i skok Escalate -&gt; Breathe z pominieciem stanu posredniego; Sanitize() to prostuje.
        /// </summary>
        public float tenseAbove = 0.60f;

        /// <summary>
        /// Rozpietosc docelowej intensywnosci na skali IntensityLevel.
        ///
        /// Docelowa intensywnosc jest interpolowana LINIOWO z napiecia:
        ///     target = intensitySpan * (1 - 2 * napiecie)
        /// czyli napiecie 0 -&gt; +span (mocniej), 0.5 -&gt; 0 (Normal), 1 -&gt; -span (lagodniej).
        ///
        /// Domyslne 1.0 celowo NIE siega skrajnosci skali (+/-2 = VeryLow/VeryHigh): krzywa
        /// ma modulowac dobor zdarzen, a nie wypychac go na krance katalogu. Zakres 0.70-1.35
        /// mnoznika punktow z IntensityTable i tak jest wezszy niz waniliowy 0.40-2.00.
        /// </summary>
        public float intensitySpan = 1.0f;

        public static TensionParams Default()
        {
            return new TensionParams();
        }

        /// <summary>
        /// Kopia gleboka. Potrzebna, bo profil trzyma wzorzec parametrow, a warstwa integracji
        /// nie powinna moc go zmodyfikowac przez referencje wspoldzielona z Defem.
        /// </summary>
        public TensionParams Clone()
        {
            return new TensionParams
            {
                narrativeWeight = narrativeWeight,
                situationalWeight = situationalWeight,
                halfLifeDays = halfLifeDays,
                downedFractionForMax = downedFractionForMax,
                calmBelow = calmBelow,
                tenseAbove = tenseAbove,
                intensitySpan = intensitySpan
            };
        }

        /// <summary>
        /// Klamruje wartosci do sensownych zakresow i ZWRACA OPIS POPRAWEK (pusty, gdy nic
        /// nie poprawiono). Ten sam wzorzec co PassScoringParams.Sanitize: zla liczba w XML
        /// ma zostac naprawiona i ZGLOSZONA, a nie przewrocic rozgrywke ani przejsc po cichu.
        /// </summary>
        public string Sanitize()
        {
            var sb = new StringBuilder();

            narrativeWeight = Fix(narrativeWeight, 0f, 100f, 1.0f, "narrativeWeight", sb);
            situationalWeight = Fix(situationalWeight, 0f, 100f, 1.0f, "situationalWeight", sb);
            halfLifeDays = Fix(halfLifeDays, 0.01f, 1000f, 5f, "halfLifeDays", sb);
            downedFractionForMax = Fix(downedFractionForMax, 0.01f, 1f, 0.5f, "downedFractionForMax", sb);
            calmBelow = Fix(calmBelow, 0f, 1f, 0.30f, "calmBelow", sb);
            tenseAbove = Fix(tenseAbove, 0f, 1f, 0.60f, "tenseAbove", sb);
            intensitySpan = Fix(intensitySpan, 0f, 2f, 1.0f, "intensitySpan", sb);

            if (calmBelow > tenseAbove)
            {
                // Zamiana, a nie zerowanie: odwrocone progi to najpewniej literowka w XML,
                // a intencja autora jest oczywista. Zerowanie skasowaloby kalibracje.
                float t = calmBelow;
                calmBelow = tenseAbove;
                tenseAbove = t;
                Note(sb, "progi calmBelow i tenseAbove byly odwrocone - zamieniono miejscami");
            }

            return sb.ToString();
        }

        private static float Fix(float value, float min, float max, float fallback, string name, StringBuilder sb)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                Note(sb, name + " byl NaN/Inf -> " + fallback.ToString("0.###", CultureInfo.InvariantCulture));
                return fallback;
            }
            if (value < min)
            {
                Note(sb, name + " " + value.ToString("0.###", CultureInfo.InvariantCulture)
                         + " -> " + min.ToString("0.###", CultureInfo.InvariantCulture));
                return min;
            }
            if (value > max)
            {
                Note(sb, name + " " + value.ToString("0.###", CultureInfo.InvariantCulture)
                         + " -> " + max.ToString("0.###", CultureInfo.InvariantCulture));
                return max;
            }
            return value;
        }

        private static void Note(StringBuilder sb, string text)
        {
            if (sb.Length > 0)
            {
                sb.Append("; ");
            }
            sb.Append(text);
        }

        public override string ToString()
        {
            return "napiecie: wNarr=" + narrativeWeight.ToString("0.##", CultureInfo.InvariantCulture)
                   + " wSyt=" + situationalWeight.ToString("0.##", CultureInfo.InvariantCulture)
                   + " polowicznyZanik=" + halfLifeDays.ToString("0.##", CultureInfo.InvariantCulture) + "d"
                   + " powalonychDoMax=" + downedFractionForMax.ToString("0.##", CultureInfo.InvariantCulture)
                   + " spokojPonizej=" + calmBelow.ToString("0.##", CultureInfo.InvariantCulture)
                   + " napieciePowyzej=" + tenseAbove.ToString("0.##", CultureInfo.InvariantCulture)
                   + " rozpietoscMocy=" + intensitySpan.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
