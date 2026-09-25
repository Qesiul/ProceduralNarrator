using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.PlayerModel
{
    /// <summary>
    /// STYL W TURZE (krok 7): odczyt stylu + kierunek d dla intencji tury (po regule kryzysu).
    /// Nadaje kandydatom WARTOSC STYLU v w [-1, 1]; premie liczy dopiero SelectionPolicy w C6 jako
    /// (best - pasmo) * v, wylacznie w puli pasma - brama, prog, pasmo i faza 0 czytaja sama Utility
    /// (decyzja autora nr 3: styl zmienia tylko CO, nie tempo).
    ///
    ///   a = suma(w_d * c_d) / suma(w_d)   po cechach ZNANYCH, wagi WYLACZNIE klocka akcji,
    ///   v = clamp(d * a).
    /// v jest jednakowe dla wszystkich wariantow jednej akcji, wiec styl przechyla wybor AKCJI (B1),
    /// a nie oprawy (B2).
    ///
    /// ZGODNOSC LADUNKU (decyzja autora nr 21): styl dotyka tylko kandydatow, ktorych walencja jest
    /// zgodna z intencja (ArcIntentRules.Compatible - ta sama tabela co luki). W eskalacji wybiera
    /// wiec sposrod zagrozen i zdarzen neutralnych, w oddechu i kryzysie sposrod darow i neutralnych.
    /// Bez tej reguly "wojownik" w kryzysie (d &gt; 0) dostawalby premie dla napadow.
    /// </summary>
    public sealed class StyleFocus
    {
        public StyleReading Reading { get; private set; }
        public float Orientation { get; private set; }
        public Intent Intent { get; private set; }

        /// <summary>Kierunek d = clamp(wo*o + wr*r) dla intencji tury.</summary>
        public float Direction { get; private set; }

        /// <summary>Czy Apply ostatnio nadalo wartosci (styl aktywny).</summary>
        public bool Applied { get; private set; }

        /// <summary>Kandydaci z niezerowa wartoscia stylu po ostatnim Apply.</summary>
        public int NonZero { get; private set; }

        public bool Active
        {
            get { return Reading != null && Reading.Active; }
        }

        public static StyleFocus Build(StyleReading reading, float orientation, Intent intent, PlayerStyleParams p)
        {
            return new StyleFocus
            {
                Reading = reading,
                Orientation = orientation,
                Intent = intent,
                Direction = StyleDirection.Compute(orientation, intent, p)
            };
        }

        /// <summary>Dopasowanie zdarzenia do profilu wzglednego (bez kierunku), w [-1, 1].</summary>
        public float Alignment(ComposedEvent e)
        {
            Block akcja = ActionBlock(e);
            if (akcja == null || akcja.StyleWeights == null || Reading == null)
            {
                return 0f;
            }
            double sumaW = 0.0, sumaWC = 0.0;
            for (int d = 0; d < StyleDimensions.Count; d++)
            {
                float w = akcja.StyleWeights.Get((StyleDimension)d);
                if (!Reading.Known[d] || !(w > 0f))
                {
                    continue;
                }
                sumaW += w;
                sumaWC += w * Reading.C[d];
            }
            return sumaW > 0.0 ? Curves.ClampSigned((float)(sumaWC / sumaW)) : 0f;
        }

        public float ValueFor(ComposedEvent e, out string why)
        {
            if (e == null)
            {
                why = "brak zdarzenia";
                return 0f;
            }
            if (!Active)
            {
                why = "styl nieaktywny";
                return 0f;
            }
            if (!ArcIntentRules.Compatible(e.Valence, Intent))
            {
                why = "walencja " + e.Valence + " niezgodna z intencja " + Intent;
                return 0f;
            }
            float a = Alignment(e);
            float v = Curves.ClampSigned(Direction * a);
            why = "a=" + F(a) + " d=" + F(Direction);
            return v;
        }

        public void Apply(IList<ScoredCandidate> scored)
        {
            Applied = false;
            NonZero = 0;
            if (scored == null)
            {
                return;
            }
            foreach (ScoredCandidate k in scored)
            {
                if (k != null)
                {
                    k.StyleApplied = false;
                    k.StyleValue = 0f;
                    k.StyleTrace = null;
                }
            }
            if (!Active)
            {
                return;
            }
            Applied = true;
            foreach (ScoredCandidate k in scored)
            {
                if (k == null || k.IsPass || k.Vetoed || k.Event == null)
                {
                    continue;
                }
                string why;
                k.StyleApplied = true;
                k.StyleValue = ValueFor(k.Event, out why);
                k.StyleTrace = why;
                if (k.StyleValue != 0f)
                {
                    NonZero++;
                }
            }
        }

        private static Block ActionBlock(ComposedEvent e)
        {
            if (e == null || e.Blocks == null)
            {
                return null;
            }
            for (int i = 0; i < e.Blocks.Count; i++)
            {
                Block b = e.Blocks[i];
                if (b != null && b.Type == BlockType.Action)
                {
                    return b;
                }
            }
            return null;
        }

        // ---------------------------------------------------------- kolumny danych (v9, preambula)

        /// <summary>
        /// Wartosci dziewieciu kolumn preambuly [PN-DATA] v9 w kolejnosci kontraktu. Liczone w rdzeniu,
        /// zeby reguly pustych pol byly testowalne offline (TEST 14n); PNLog wpisuje je literalnymi
        /// Append (sprawdz_kolumny.py czyta nazwy kolumn z kodu).
        /// Puste = brak pomiaru: dziedziny z rozgrzewki (z, mocne, etykieta) sa puste, kierunek d jest
        /// zawsze, a "-" w mocnych stronach znaczy "styl aktywny, mocnych stron brak".
        /// </summary>
        public PreambleColumns Preamble()
        {
            var c = new PreambleColumns();
            StyleReading r = Reading;
            c.Dni = r == null ? string.Empty : r.Days.ToString(CultureInfo.InvariantCulture);
            c.Aktywny = r == null ? string.Empty : (r.Active ? "true" : "false");
            for (int d = 0; d < StyleDimensions.Count; d++)
            {
                c.Z[d] = r != null && r.Active && r.Known[d] ? F(r.Z[d]) : string.Empty;
            }
            c.Mocne = r != null && r.Active ? r.StrongData() : string.Empty;
            c.Etykieta = r != null && r.Active && !string.IsNullOrEmpty(r.Label) ? r.Label : string.Empty;
            c.Kierunek = F(Direction);
            return c;
        }

        public sealed class PreambleColumns
        {
            public string Dni = string.Empty;
            public string Aktywny = string.Empty;
            public readonly string[] Z = { string.Empty, string.Empty, string.Empty, string.Empty };
            public string Mocne = string.Empty;
            public string Etykieta = string.Empty;
            public string Kierunek = string.Empty;
        }

        public string Describe()
        {
            var sb = new StringBuilder(96);
            sb.Append("styl: ").Append(Reading == null ? "brak" : Reading.Describe())
              .Append(" d=").Append(F(Direction)).Append(" (").Append(Intent).Append(')');
            return sb.ToString();
        }

        public static string F(float v)
        {
            return v.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }
}
