using System.Globalization;
using System.Text;

namespace ProceduralNarrator.Core.Arcs
{
    public enum ArcEventKind
    {
        Open,
        Advance,
        Close,
        Drop
    }

    /// <summary>
    /// Jeden krok automatu luku - do linii [PN-ARC] w danych i do komunikatu w grze. Budowany
    /// w rdzeniu (testowalny offline), wypisywany przez Integration.
    /// </summary>
    public sealed class ArcTransitionRecord
    {
        public ArcEventKind Kind;
        public string ArcId;
        public int Number;
        public string From;
        public string To;

        /// <summary>wykonanie | straznik:Guard_X | limitCzasu | nastepca | brakDefa</summary>
        public string Reason;

        /// <summary>Dla zamkniecia: rozwiazany | wygaszony | wynik z przejscia (np. pojednanie).</summary>
        public string Outcome;

        public string FactionId;

        /// <summary>Tekst z Defa (z {FRAKCJA} niepodstawionym) albo null - brak komunikatu.</summary>
        public string Message;
        public string MessageType;

        public int Tick;
        public float GameDay;

        public static string KindLabel(ArcEventKind k)
        {
            switch (k)
            {
                case ArcEventKind.Open: return "otwarcie";
                case ArcEventKind.Advance: return "przejscie";
                case ArcEventKind.Close: return "zamkniecie";
                default: return "odrzucenie";
            }
        }

        /// <summary>Tresc linii [PN-ARC] bez preambuly (runId, tryb, mapa dopisuje Integration).</summary>
        public string ToDataFragment()
        {
            var sb = new StringBuilder(160);
            sb.Append("luk=").Append(ArcId ?? "-")
              .Append("; instancja=").Append(Number.ToString(CultureInfo.InvariantCulture))
              .Append("; zdarzenie=").Append(KindLabel(Kind))
              .Append("; z=").Append(From ?? "-")
              .Append("; do=").Append(To ?? "-")
              .Append("; powod=").Append(Reason ?? "-")
              .Append("; wynik=").Append(Outcome ?? "-")
              .Append("; frakcja=").Append(string.IsNullOrEmpty(FactionId) ? "-" : FactionId)
              .Append("; komunikat=").Append(string.IsNullOrEmpty(Message) ? "nie" : "tak");
            return sb.ToString();
        }

        public override string ToString()
        {
            return KindLabel(Kind) + " " + ArcId + "#" + Number.ToString(CultureInfo.InvariantCulture)
                   + " " + (From ?? "-") + "->" + (To ?? "-") + " (" + (Reason ?? "-")
                   + (Outcome == null ? string.Empty : ", " + Outcome) + ")";
        }
    }
}
