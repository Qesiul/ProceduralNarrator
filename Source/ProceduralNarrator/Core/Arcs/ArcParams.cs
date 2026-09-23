using System.Globalization;
using System.Text;

namespace ProceduralNarrator.Core.Arcs
{
    /// <summary>
    /// Parametry warstwy lukow - WSPOLNA maszyneria (blok &lt;arcs&gt; w StorytellerCompProperties_Generative),
    /// nie profil. Ten sam powod co przy kryzysie: gdyby limit lukow siedzial w profilu, porownanie
    /// osobowosci mieszaloby osobowosc z budowa watkow.
    ///
    /// Wylacznik `enabled` sluzy seriom kontrolnym: ramie symulatora "bez lukow" mierzy efekt
    /// DRUGIEGO RZEDU lukow na tempo (luk nie zmienia decyzji "dzialac/cisza" w turze, ale zmienia
    /// historie, a przez nia gestosc, napiecie i intencje nastepnych tur).
    /// </summary>
    public class ArcParams
    {
        public const int MaxConcurrentCeiling = 4;

        public bool enabled = true;

        /// <summary>Ile lukow moze byc otwartych naraz na jednej mapie (decyzja autora nr 4: 2).</summary>
        public int maxConcurrent = 2;

        public static ArcParams Default()
        {
            return new ArcParams();
        }

        public ArcParams Clone()
        {
            return new ArcParams { enabled = enabled, maxConcurrent = maxConcurrent };
        }

        /// <summary>Klamruje i ZWRACA opis poprawek (pusty, gdy nic nie poprawiono).</summary>
        public string Sanitize()
        {
            var sb = new StringBuilder();
            if (maxConcurrent < 1)
            {
                sb.Append("maxConcurrent " + maxConcurrent.ToString(CultureInfo.InvariantCulture) + " -> 1");
                maxConcurrent = 1;
            }
            else if (maxConcurrent > MaxConcurrentCeiling)
            {
                sb.Append("maxConcurrent " + maxConcurrent.ToString(CultureInfo.InvariantCulture) + " -> "
                          + MaxConcurrentCeiling.ToString(CultureInfo.InvariantCulture));
                maxConcurrent = MaxConcurrentCeiling;
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            return "luki: " + (enabled ? "wlaczone" : "WYLACZONE")
                   + " maxRownoczesnych=" + maxConcurrent.ToString(CultureInfo.InvariantCulture);
        }
    }
}
