using System.Globalization;

namespace ProceduralNarrator.Core.Arcs
{
    /// <summary>
    /// Slad po ZAMKNIETYM watku: czym sie skonczyl i kiedy.
    ///
    /// Po co, skoro ksiega ma juz LastCloseDay: tamto pole niesie wylacznie dzien i sluzy odstepowi
    /// przed ponownym otwarciem. Model danych pracy opisuje jednak watek jako
    /// Thread {identyfikator, status: otwarty|zamkniety, faza_luku} - bez tego rekordu status
    /// "zamkniety" zylby wylacznie w linii [PN-ARC] w pliku danych, czyli poza pamiecia gry,
    /// i zaden warunek XML nie moglby o niego zapytac.
    ///
    /// Rekord jest dopisany NOWYM ZNACZNIKIEM LINII, a nie nowym polem linii istniejacej - dzieki
    /// temu zapisy sprzed tej zmiany wczytuja sie dalej bez jednej odrzuconej linii (liczba pol
    /// linii A, C, N i P zostaje nietknieta).
    /// </summary>
    public sealed class ArcClosure
    {
        /// <summary>Znacznik typu linii w pamieci gry (ArcLedger.ToPersistableLines).</summary>
        public const string LineTag = "Z";

        /// <summary>Liczba pol linii WLACZNIE ze znacznikiem - TryDecode odrzuca kazda inna.</summary>
        public const int FieldCount = 6;

        public string ArcId;
        public int Number;

        /// <summary>ArcDirector.OutcomeResolved albo OutcomeFaded.</summary>
        public string Outcome;

        /// <summary>Faza, na ktorej watek stal w chwili zamkniecia.</summary>
        public string FinalPhaseId;

        public float Day;

        public string Encode()
        {
            return LineTag
                   + ArcInstance.FieldSeparator + ArcInstance.Escape(ArcId)
                   + ArcInstance.FieldSeparator + Number.ToString(CultureInfo.InvariantCulture)
                   + ArcInstance.FieldSeparator + ArcInstance.Escape(Outcome)
                   + ArcInstance.FieldSeparator + ArcInstance.Escape(FinalPhaseId)
                   + ArcInstance.FieldSeparator + Day.ToString("G9", CultureInfo.InvariantCulture);
        }

        /// <summary>Dekoduje linie. NIGDY nie rzuca. Zgodnosci z katalogiem tu NIE sprawdzamy -
        /// kodek nie zna Defow, tak samo jak przy ArcInstance.</summary>
        public static bool TryDecode(string line, out ArcClosure closure)
        {
            closure = null;
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }
            string[] f = line.Split(ArcInstance.FieldSeparator);
            if (f.Length != FieldCount || f[0] != LineTag)
            {
                return false;
            }

            string arcId = ArcInstance.Unescape(f[1]);
            string outcome = ArcInstance.Unescape(f[3]);
            string faza = ArcInstance.Unescape(f[4]);
            if (arcId == null || outcome == null || faza == null)
            {
                return false;
            }

            int numer;
            float dzien;
            if (!ArcInstance.Int(f[2], out numer) || numer < 1 || !ArcInstance.Flt(f[5], out dzien))
            {
                return false;
            }

            closure = new ArcClosure
            {
                ArcId = arcId,
                Number = numer,
                Outcome = outcome,
                FinalPhaseId = faza,
                Day = dzien
            };
            return true;
        }
    }
}
