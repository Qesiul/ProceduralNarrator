using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Arcs
{
    /// <summary>
    /// "Oczekiwany typ" wydarzenia w fazie luku (koncepcja 5.4) - predykat na WYKONANYM albo
    /// kandydujacym zdarzeniu. Decyzja autora nr 10: osie klocka AKCJI (motyw, walencja, skala)
    /// plus opcjonalny wymagany tag i minimalna moc. Osie, nie lista defName-ow: nowy klocek akcji
    /// wpasowuje sie w istniejace luki sam, bez edycji luku (tresc w danych, sekcja 15).
    ///
    /// Pola camelCase = wezly XML (Defs/Arcs). Lista pusta = "dowolny" dla motywow i skal.
    /// WALENCJE SA OBOWIAZKOWE (pilnuje ArcCatalog.Validate): od nich zalezy, czy faza w ogole
    /// moze sterowac przy danej intencji (ArcIntentRules), wiec "dowolna walencja" dawalaby faze
    /// raportowana jako aktywna, w ktorej zaden kandydat nie dostaje premii.
    ///
    /// Ten SAM predykat sluzy rozpoznaniu (czy wykonane zdarzenie przesuwa faze) i sterowaniu
    /// (czy kandydat dostaje premie) - decyzja autora R4-2: wartosc czynnika jest binarna i rowna 1
    /// dokladnie wtedy, gdy wykonanie kandydata przesunelo by faze.
    /// </summary>
    public class ArcExpectation
    {
        public List<Theme> themes = new List<Theme>();
        public List<Valence> valences = new List<Valence>();
        public List<EventScale> scales = new List<EventScale>();

        /// <summary>Tag wymagany na klocku AKCJI (np. "niebo", "kapsula"). Pusty = bez wymagania.</summary>
        public string requiredTag;

        /// <summary>Minimalna wypadkowa moc zdarzenia. VeryLow = bez wymagania.</summary>
        public IntensityLevel minIntensity = IntensityLevel.VeryLow;

        /// <summary>
        /// Zdarzenie musi przyjsc od frakcji zwiazanej z lukiem (decyzja autora nr 7 - ciaglosc
        /// frakcji tylko dla napadu). Wymaga, zeby klocek akcji niosl frakcje (CarriesFaction),
        /// a luk mial ja juz zwiazana we wczesniejszej fazie.
        /// </summary>
        public bool sameFaction;

        public bool Matches(ArcEventView e, string boundFactionId, out string why)
        {
            if (e == null)
            {
                why = "brak zdarzenia";
                return false;
            }
            if (themes != null && themes.Count > 0 && !themes.Contains(e.Theme))
            {
                why = "motyw " + e.Theme;
                return false;
            }
            if (valences == null || !valences.Contains(e.Valence))
            {
                why = "walencja " + e.Valence;
                return false;
            }
            if (scales != null && scales.Count > 0 && !scales.Contains(e.Scale))
            {
                why = "skala " + e.Scale;
                return false;
            }
            if (!string.IsNullOrEmpty(requiredTag) && (e.Tags == null || !e.Tags.Contains(requiredTag)))
            {
                why = "brak tagu " + requiredTag;
                return false;
            }
            if ((int)e.Intensity < (int)minIntensity)
            {
                why = "moc " + e.Intensity + " < " + minIntensity;
                return false;
            }
            if (sameFaction)
            {
                if (!e.CarriesFaction)
                {
                    why = "akcja nie niesie frakcji";
                    return false;
                }
                if (string.IsNullOrEmpty(boundFactionId))
                {
                    why = "luk bez zwiazanej frakcji";
                    return false;
                }
                if (!string.Equals(e.FactionId, boundFactionId, System.StringComparison.Ordinal))
                {
                    why = "inna frakcja";
                    return false;
                }
            }
            why = "pasuje";
            return true;
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(themes == null || themes.Count == 0 ? "dowolny" : string.Join("/", themes.Select(t => t.ToString()).ToArray()));
            sb.Append(' ').Append(valences == null ? "?" : string.Join("/", valences.Select(v => v.ToString()).ToArray()));
            if (scales != null && scales.Count > 0)
            {
                sb.Append(' ').Append(string.Join("/", scales.Select(s => s.ToString()).ToArray()));
            }
            if (!string.IsNullOrEmpty(requiredTag))
            {
                sb.Append(" tag=").Append(requiredTag);
            }
            if (minIntensity != IntensityLevel.VeryLow)
            {
                sb.Append(" moc>=").Append(minIntensity);
            }
            if (sameFaction)
            {
                sb.Append(" taSamaFrakcja");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Widok zdarzenia potrzebny lukom: osie i tagi klocka AKCJI, moc, ladunek i frakcja.
    /// Jeden typ dla kandydata (przed wykonaniem) i dla zdarzenia wykonanego - dzieki temu
    /// rozpoznanie i sterowanie ida przez ten sam predykat ArcExpectation.Matches.
    /// </summary>
    public sealed class ArcEventView
    {
        public string ActionBlockId;
        public string Payload;
        public Theme Theme;
        public Valence Valence;
        public EventScale Scale;
        public IntensityLevel Intensity;
        public HashSet<string> Tags = new HashSet<string>();

        /// <summary>Czy klocek akcji niesie frakcje (flaga carriesFaction, dzis tylko Napad).</summary>
        public bool CarriesFaction;

        /// <summary>Frakcja zdarzenia: dla kandydata - ta, ktora zostanie ustawiona; po wykonaniu - faktyczna.</summary>
        public string FactionId;

        /// <summary>
        /// Widok kandydata. Tagi i flaga frakcji pochodza z KLOCKA AKCJI (umowa danych: osie
        /// i charakter zdarzenia deklaruje wylacznie akcja; tagi pozostalych slotow opisuja
        /// okolicznosci, nie typ).
        /// </summary>
        public static ArcEventView FromCandidate(ComposedEvent e, string factionId)
        {
            var v = new ArcEventView
            {
                ActionBlockId = e.ActionBlockId,
                Payload = e.ActionPayload,
                Theme = e.Theme,
                Valence = e.Valence,
                Scale = e.Scale,
                Intensity = e.Intensity,
                FactionId = factionId
            };
            if (e.Blocks != null)
            {
                for (int i = 0; i < e.Blocks.Count; i++)
                {
                    Block b = e.Blocks[i];
                    if (b != null && b.Type == BlockType.Action)
                    {
                        if (b.Tags != null)
                        {
                            v.Tags.UnionWith(b.Tags);
                        }
                        v.CarriesFaction = b.CarriesFaction;
                        break;
                    }
                }
            }
            return v;
        }
    }
}
