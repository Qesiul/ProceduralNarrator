using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Liczy dopasowanie kontekstowe GOTOWEGO kandydata (decyzja projektowa: contextFit
    /// jest wlasnoscia zlozonego wydarzenia, nie pojedynczego klocka).
    ///
    /// Metoda: preferencje wszystkich klockow kandydata trafiaja do jednej puli
    /// i licza sie jako srednia wazona. Dzieki temu nie ma dwupoziomowej agregacji
    /// (najpierw w klocku, potem w wydarzeniu), ktora trudno bylo by wytlumaczyc
    /// i rozbic w sladzie scoringu.
    ///
    /// Kandydat bez zadnych preferencji dostaje 1.0 - brak informacji nie jest kara.
    /// </summary>
    public static class ContextEvaluator
    {
        public static void Evaluate(ComposedEvent candidate, WorldSnapshot snapshot)
        {
            if (candidate == null)
            {
                return;
            }

            if (snapshot == null)
            {
                candidate.ContextFit = 1f;
                candidate.FitTrace = "brak snapshotu";
                return;
            }

            var wklady = new List<string>();
            float sumaWazona = 0f;
            float sumaWag = 0f;

            foreach (Block block in candidate.Blocks)
            {
                foreach (NarrativeCondition pref in block.Preferences)
                {
                    float fit = pref.Fit(snapshot);
                    float waga = pref.weight <= 0f ? 1f : pref.weight;

                    sumaWazona += fit * waga;
                    sumaWag += waga;

                    wklady.Add(block.Id + "/" + pref.Describe()
                               + "=" + fit.ToString("0.00", CultureInfo.InvariantCulture)
                               + (waga != 1f ? "*" + waga.ToString("0.#", CultureInfo.InvariantCulture) : ""));
                }
            }

            if (sumaWag <= 0f)
            {
                candidate.ContextFit = 1f;
                candidate.FitTrace = "brak preferencji";
                return;
            }

            candidate.ContextFit = sumaWazona / sumaWag;

            var sb = new StringBuilder();
            sb.Append(candidate.ContextFit.ToString("0.00", CultureInfo.InvariantCulture)).Append(" = [");
            sb.Append(string.Join(", ", wklady.ToArray()));
            sb.Append(']');
            candidate.FitTrace = sb.ToString();
        }

        /// <summary>Sumuje odchylenia intensywnosci klockow i zacieka wynik do skali enuma.</summary>
        public static IntensityLevel AggregateIntensity(IEnumerable<Block> blocks)
        {
            int nieuzywana;
            return AggregateIntensity(blocks, out nieuzywana);
        }

        /// <summary>
        /// Agregacja intensywnosci z surowa suma PRZED klamrowaniem.
        ///
        /// PO CO SUROWA SUMA. Piec slotow wnosi wklad na skale o pieciu poziomach, wiec suma
        /// siega od -3 do +3, a skala konczy sie na -2 i +2. Zmierzone na biezacym katalogu:
        /// 11 z 80 kandydatow (13.8%) przekracza zakres, wiec wklad ktoregos slotu zostaje
        /// ODRZUCONY. Samo klamrowanie jest POPRAWNE - ponizej VeryLow nie ma gdzie zejsc -
        /// problemem byla NIEWIDOCZNOSC: ComposedEvent niosl tylko wynik po klamrze, wiec
        /// nigdzie nie zostawal slad, ze deklaracja kompozycji zostala czesciowo zignorowana.
        ///
        /// CZEGO TU CELOWO NIE ROBIMY: nie poszerzamy skali ani nie zamieniamy sumy na srednia.
        /// IntensityLevel mapuje sie przez IntensityTable na punkty zagrozenia gry, wiec kazda
        /// zmiana zakresu jest zmiana kalibracji trudnosci - a nie poprawka diagnostyki.
        /// </summary>
        public static IntensityLevel AggregateIntensity(IEnumerable<Block> blocks, out int surowaSuma)
        {
            surowaSuma = blocks.Sum(b => (int)b.Intensity);

            int suma = surowaSuma;
            if (suma < (int)IntensityLevel.VeryLow)
            {
                suma = (int)IntensityLevel.VeryLow;
            }
            if (suma > (int)IntensityLevel.VeryHigh)
            {
                suma = (int)IntensityLevel.VeryHigh;
            }
            return (IntensityLevel)suma;
        }
    }
}
