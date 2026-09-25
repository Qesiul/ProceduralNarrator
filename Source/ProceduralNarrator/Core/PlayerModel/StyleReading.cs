using System.Globalization;
using System.Text;

namespace ProceduralNarrator.Core.PlayerModel
{
    /// <summary>
    /// ODCZYT STYLU w danej chwili - wynik PlayerStyleModel.Evaluate (z ksiegi) albo FromVector
    /// (styl narzucony: ramiona syntetycznych graczy w symulatorze, testy). Tylko dane, bez logiki.
    /// </summary>
    public class StyleReading
    {
        /// <summary>Liczba dni zamknietych w kolejce.</summary>
        public int Days;

        /// <summary>Styl dziala (dni &gt;= rozgrzewka). Poza tym: cechy nieznane, brak etykiety i mocnych stron.</summary>
        public bool Active;

        /// <summary>Czy odczyt pochodzi z wektora narzuconego (FromVector), a nie z obserwacji.</summary>
        public bool Imposed;

        // ---- pomiary (tylko z Evaluate; w FromVector NaN / false)
        public readonly float[] SignalX = new float[StyleSignals.Count];
        public readonly float[] SignalZ = new float[StyleSignals.Count];
        public readonly bool[] SignalKnown = new bool[StyleSignals.Count];

        // ---- cechy na wspolnej skali 0..1 (przecietna kolonia = 0.5)
        public readonly float[] Z = new float[StyleDimensions.Count];

        /// <summary>Cecha znana: styl aktywny i co najmniej jeden znany pomiar o dodatniej wadze.</summary>
        public readonly bool[] Known = new bool[StyleDimensions.Count];

        /// <summary>Profil WZGLEDNY: (Z - srednia znanych Z)/skala, klamrowany do [-1,1]; 0 dla nieznanych.</summary>
        public readonly float[] C = new float[StyleDimensions.Count];

        /// <summary>Mocna strona: cecha znana i C &gt;= prog.</summary>
        public readonly bool[] Strong = new bool[StyleDimensions.Count];

        /// <summary>Etykieta najblizszego prototypu (pusta, gdy styl nieaktywny albo brak znanych cech).</summary>
        public string Label = string.Empty;

        /// <summary>Druga najblizsza etykieta i przewaga odleglosci pierwszej nad nia (diagnostyka).</summary>
        public string SecondLabel = string.Empty;
        public float Margin;

        // ---- liczebnosci zdarzen w oknie (diagnostyka, [PN-GRACZ])
        public long EpisodesInWindow;
        public long OffersInWindow;
        public long CapturesInWindow;

        public int KnownCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < StyleDimensions.Count; i++)
                {
                    if (Known[i]) n++;
                }
                return n;
            }
        }

        /// <summary>Mocne strony w postaci kanonicznej snapshotu (";Walka;") - pusto, gdy styl nieaktywny.</summary>
        public string StrongCanonical()
        {
            return Active ? StyleDimensions.Canonical(Strong) : string.Empty;
        }

        /// <summary>Mocne strony w postaci danych ("Walka/Ekspansja", "-" gdy brak).</summary>
        public string StrongData()
        {
            return StyleDimensions.DataForm(Strong);
        }

        public string Describe()
        {
            var sb = new StringBuilder(160);
            sb.Append("dni=").Append(Days.ToString(CultureInfo.InvariantCulture))
              .Append(Active ? " aktywny" : " rozgrzewka");
            if (Imposed) sb.Append(" (narzucony)");
            for (int i = 0; i < StyleDimensions.Count; i++)
            {
                sb.Append(' ').Append(StyleDimensions.Name((StyleDimension)i)).Append('=');
                sb.Append(Known[i] ? Z[i].ToString("0.000", CultureInfo.InvariantCulture) : "?");
            }
            if (Active)
            {
                sb.Append(" mocne=").Append(StrongData())
                  .Append(" etykieta=").Append(string.IsNullOrEmpty(Label) ? "-" : Label);
            }
            return sb.ToString();
        }
    }
}
