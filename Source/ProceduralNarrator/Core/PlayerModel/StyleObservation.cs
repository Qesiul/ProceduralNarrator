using System.Collections.Generic;

namespace ProceduralNarrator.Core.PlayerModel
{
    /// <summary>Probka zagrozenia z jednej mapy domowej (co threatSampleTicks tickow).</summary>
    public struct MapThreatSample
    {
        public int MapId;

        /// <summary>Czy na mapie jest aktywne zagrozenie dla gracza.</summary>
        public bool Threat;

        /// <summary>Koloniscy zdolni do walki pod bronia.</summary>
        public int Drafted;

        /// <summary>Koloniscy zdolni do walki (bez powalonych, pacyfistow, w stanie psychicznym, niewolnikow, lokatorow).</summary>
        public int Eligible;
    }

    /// <summary>Rekordy jednego kolonisty w chwili zamkniecia doby (wartosci narastajace gry, zaokraglone).</summary>
    public class PawnRecordSample
    {
        public int Id;

        /// <summary>Kolonista w sensie pomiaru: IsColonist, nie niewolnik, nie lokator zadania.</summary>
        public bool Eligible;

        public long Productive;
        public long Support;
        public long Recruited;
        public long Captured;
    }

    /// <summary>Stan oferty dolaczenia (zadania z listy offerQuestRoots) w rozumieniu pomiaru Przyjecia.</summary>
    public enum OfferState
    {
        /// <summary>Jeszcze nierozstrzygnieta (nieprzyjeta albo w toku).</summary>
        Pending,

        /// <summary>
        /// Rozstrzygnieta pozytywnie: EndedSuccess. Dla wedrowca = przyjecie; dla rozbitka sukces zapada juz przy pierwszym
        /// opatrzeniu, zwerbowaniu albo odejsciu w zdrowiu - to pomoc, nie dolaczenie (znane ograniczenie, przeglad S8).
        /// </summary>
        Success,

        /// <summary>Odrzucona: EndedFailed albo wygasla (EndedOfferExpired). U wedrowca gra nie odroznia odmowy od zignorowania listu.</summary>
        Fail,

        /// <summary>Niewazna (EndedInvalid, EndedUnknownOutcome) - nie liczy sie ani na plus, ani na minus.</summary>
        Void
    }

    public class OfferQuestSample
    {
        public int Id;
        public OfferState State;
    }

    public enum WildManStatus
    {
        /// <summary>Dziki, na mapie domowej, bez frakcji.</summary>
        Wild,

        /// <summary>Wziety do niewoli - oferta dalej otwarta, bez limitu czasu.</summary>
        Prisoner,

        /// <summary>Dolaczyl do gracza (oswojony albo zwerbowany) - oferta przyjeta.</summary>
        Joined,

        /// <summary>Nie zyje albo zniknal z gry - oferta odrzucona.</summary>
        Gone
    }

    public class WildManSample
    {
        public int Id;
        public WildManStatus Status;
    }

    /// <summary>
    /// Obserwacja kolonii w chwili zamkniecia doby - wszystko, co warstwa integracji odczytuje z gry.
    /// Czysty rdzen dostaje tylko liczby; kazda decyzja "co sie liczy" zapada w PlayerStyleLedger
    /// i jest testowalna offline (TEST 14i).
    /// </summary>
    public class DayObservation
    {
        /// <summary>Wartosc budynkow gracza z kategorii obrony (MarketValueIgnoreHp).</summary>
        public long DefenseValue;

        /// <summary>Wartosc budynkow gracza z kategorii produkcji i zasilania.</summary>
        public long ProductionValue;

        /// <summary>Wartosc wszystkich budynkow gracza na mapach domowych.</summary>
        public long BuildingValue;

        public readonly List<PawnRecordSample> Pawns = new List<PawnRecordSample>();

        /// <summary>WSZYSTKIE zadania-oferty obecne w menedzerze zadan (dowolny stan).</summary>
        public readonly List<OfferQuestSample> Offers = new List<OfferQuestSample>();

        /// <summary>Dzicy ludzie na mapach domowych oraz stan wszystkich sledzonych (brak wpisu = Gone).</summary>
        public readonly List<WildManSample> WildMen = new List<WildManSample>();

        /// <summary>History.lastTickPlayerRaidedSomeone w chwili obserwacji.</summary>
        public long LastPlayerRaidTick = -1;
    }
}
