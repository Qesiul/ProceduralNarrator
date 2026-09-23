using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Arcs;

namespace ProceduralNarrator.Core.Blackboard
{
    /// <summary>
    /// Fakty zdarzenia, ktore narrator WYBRAL, a ktorego wykonanie nie jest jeszcze rozstrzygniete
    /// albo nie zostalo jeszcze zastosowane do ksiegi.
    ///
    /// DLACZEGO KOLEJKA, A NIE ZAPIS W CHWILI POTWIERDZENIA. Wykonanie potwierdzaja dwie sciezki:
    /// normalna (kod po yield return, ten sam tick) i spozniona (nastepne wywolanie compa, gdy
    /// iterator nie zostal wznowiony). Sciezka normalna widzi snapshot zamrozony na poczatku tury,
    /// a spozniona buduje nowy - zapis faktu w chwili potwierdzenia sprawilby, ze warunek startu
    /// luku widzi slad zdarzenia na jednej sciezce, a na drugiej nie. Ten sam stan dawalby dwa
    /// rozne wyniki. Dlatego fakt trafia do ksiegi w JEDNYM miejscu: na poczatku nastepnego
    /// wywolania compa, PO obsludze lukow (FactLedger.ResolvePending).
    ///
    /// Konsekwencja, zatwierdzona przez autora: warunki widza swiat z poczatku tury, wiec slad
    /// zdarzenia NIE jest widoczny dla luku, ktory to samo zdarzenie otwiera. Progi w XML licza
    /// zdarzenia WCZESNIEJSZE.
    ///
    /// NIEZALEZNE OD LUKOW. Oczekujace wykonanie lukow (PendingExecution) zyje w ksiedze lukow,
    /// ktorej nie ma, gdy luki sa wylaczone albo uszkodzone. Fakty sa czescia blackboardu, nie
    /// lukow - nie moga ginac razem z nimi.
    /// </summary>
    public sealed class PendingFacts
    {
        /// <summary>Znacznik typu linii w pamieci gry (FactLedger.ToPersistableLines).</summary>
        public const string LineTag = "Q";

        /// <summary>Liczba pol linii WLACZNIE ze znacznikiem - TryDecode odrzuca kazda inna.</summary>
        public const int FieldCount = 8;

        /// <summary>Separator deklaracji wewnatrz pola zapisow. Alfabet klucza go wyklucza.</summary>
        public const char WriteSeparator = ';';

        /// <summary>Separator pol jednej deklaracji. Alfabet klucza go wyklucza.</summary>
        public const char WriteFieldSeparator = ',';

        /// <summary>Tick decyzji (i yield return).</summary>
        public int Tick;

        /// <summary>
        /// Dzien gry decyzji. Fakty dostaja TEN dzien, a nie dzien zastosowania - zdarzenie
        /// stalo sie wtedy, a opoznienie kolejki (do 1000 tickow) nie moze przesuwac wieku faktu.
        /// </summary>
        public float Day;

        public int DecisionIndex;

        /// <summary>defName incydentu - do spoznionej klasyfikacji przez lastFireTicks.</summary>
        public string IncidentDefName;

        /// <summary>lastFireTicks[def] przed yield (-1 = nigdy nie odpalal).</summary>
        public int LastFireBefore;

        /// <summary>Sciezka normalna potwierdzila wykonanie; zostaje tylko zastosowac.</summary>
        public bool Confirmed;

        /// <summary>Deklaracje faktow wszystkich klockow zwyciezcy, w kolejnosci klockow.</summary>
        public List<FactWrite> Writes = new List<FactWrite>();

        public string Encode()
        {
            var zapisy = new StringBuilder();
            for (int i = 0; i < Writes.Count; i++)
            {
                FactWrite w = Writes[i];
                if (i > 0)
                {
                    zapisy.Append(WriteSeparator);
                }
                zapisy.Append(w.key ?? string.Empty).Append(WriteFieldSeparator)
                      .Append(w.value.ToString("G9", CultureInfo.InvariantCulture)).Append(WriteFieldSeparator)
                      .Append(w.accumulate ? "1" : "0").Append(WriteFieldSeparator)
                      .Append(w.lifespanDays.ToString("G9", CultureInfo.InvariantCulture));
            }

            return LineTag
                   + ArcInstance.FieldSeparator + Tick.ToString(CultureInfo.InvariantCulture)
                   + ArcInstance.FieldSeparator + Day.ToString("G9", CultureInfo.InvariantCulture)
                   + ArcInstance.FieldSeparator + DecisionIndex.ToString(CultureInfo.InvariantCulture)
                   + ArcInstance.FieldSeparator + ArcInstance.Escape(IncidentDefName)
                   + ArcInstance.FieldSeparator + LastFireBefore.ToString(CultureInfo.InvariantCulture)
                   + ArcInstance.FieldSeparator + (Confirmed ? "1" : "0")
                   + ArcInstance.FieldSeparator + (zapisy.Length == 0 ? ArcInstance.EmptyToken : zapisy.ToString());
        }

        /// <summary>Dekoduje linie. NIGDY nie rzuca - uszkodzona linia to odrzucenie, nie awaria wczytania.</summary>
        public static bool TryDecode(string line, out PendingFacts pending)
        {
            pending = null;
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }
            string[] f = line.Split(ArcInstance.FieldSeparator);
            if (f.Length != FieldCount || f[0] != LineTag)
            {
                return false;
            }

            int tick, decyzja, przed;
            float dzien;
            if (!ArcInstance.Int(f[1], out tick) || !ArcInstance.Flt(f[2], out dzien)
                || !ArcInstance.Int(f[3], out decyzja) || !ArcInstance.Int(f[5], out przed))
            {
                return false;
            }
            if (f[6] != "0" && f[6] != "1")
            {
                return false;
            }

            var zapisy = new List<FactWrite>();
            if (f[7] != ArcInstance.EmptyToken)
            {
                foreach (string z in f[7].Split(WriteSeparator))
                {
                    string[] p = z.Split(WriteFieldSeparator);
                    float wartosc, zycie;
                    if (p.Length != 4 || !FactLedger.IsValidKey(p[0])
                        || !ArcInstance.Flt(p[1], out wartosc) || (p[2] != "0" && p[2] != "1")
                        || !ArcInstance.Flt(p[3], out zycie))
                    {
                        return false;
                    }
                    zapisy.Add(new FactWrite { key = p[0], value = wartosc, accumulate = p[2] == "1", lifespanDays = zycie });
                }
            }

            pending = new PendingFacts
            {
                Tick = tick,
                Day = dzien,
                DecisionIndex = decyzja,
                IncidentDefName = ArcInstance.Unescape(f[4]),
                LastFireBefore = przed,
                Confirmed = f[6] == "1",
                Writes = zapisy
            };
            return true;
        }
    }

    /// <summary>Rodzaj zdarzenia ksiegi faktow - do linii [PN-FACT].</summary>
    public enum FactEventKind
    {
        /// <summary>Fakt zastosowany do ksiegi po potwierdzonym wykonaniu.</summary>
        Set,
        /// <summary>Zdarzenie sie nie wykonalo - jego fakty odrzucone w calosci.</summary>
        Dropped,
        /// <summary>Deklaracja odrzucona przez ksiege (klucz spoza alfabetu, wartosc niepoprawna).</summary>
        Rejected
    }

    /// <summary>
    /// Jeden wpis do linii [PN-FACT]. WYGASANIE NIE MA WPISU - i to jest celowe: fakty wygasaja
    /// leniwie (liczone przy odczycie), wiec "chwila wygasniecia" nie istnieje jako zdarzenie.
    /// Logowanie jej wymagaloby sledzenia, co bylo aktywne przy poprzednim wywolaniu, czyli stanu
    /// zaleznego od liczby wywolan compa - dokladnie tego, czego leniwe wygasanie ma unikac.
    /// Analiza danych wylicza wygasniecie z dnia ustawienia i czasu zycia.
    /// </summary>
    public sealed class FactEvent
    {
        public FactEventKind Kind;
        public string Key;

        /// <summary>Wartosc faktu PO zastosowaniu (przy Add - suma), NaN przy odrzuceniu.</summary>
        public float Value = float.NaN;

        public float LifespanDays;

        /// <summary>Dzien, ktorym fakt zostal ostemplowany (dzien decyzji zdarzenia).</summary>
        public float Day;

        /// <summary>Tick decyzji zdarzenia, ktore fakt zostawilo.</summary>
        public int SourceTick;

        /// <summary>Numer decyzji zdarzenia, ktore fakt zostawilo.</summary>
        public int SourceDecision;

        /// <summary>Status wykonania albo przyczyna odrzucenia (ASCII, bez ';').</summary>
        public string Reason;

        public string KindLabel()
        {
            switch (Kind)
            {
                case FactEventKind.Set: return "ustawienie";
                case FactEventKind.Dropped: return "odrzucenie";
                default: return "niepoprawny";
            }
        }
    }
}
