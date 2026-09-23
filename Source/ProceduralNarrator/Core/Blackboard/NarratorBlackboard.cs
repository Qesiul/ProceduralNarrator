using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Blackboard
{
    /// <summary>
    /// BLACKBOARD z sekcji 5.2 koncepcji - wspoldzielona pamiec narratora jednej mapy.
    ///
    /// Nie jest to czwarty magazyn, tylko FASADA TYLKO DO ODCZYTU nad trzema istniejacymi:
    ///   historia zdarzen  -> EventHistory (append-only w sensie "wpis raz zapisany sie nie zmienia")
    ///   rejestr watkow    -> ArcLedger    (koncepcja: "osobny rejestr otwartych watkow")
    ///   fakty o kolonii   -> FactLedger
    /// plus "proste wnioskowanie", czyli zapytania, ktorych zaden z magazynow nie ma sam z siebie:
    /// czy watek jest otwarty, ile TUR minelo od zdarzenia o danym temacie, jak stary jest fakt.
    ///
    /// DLACZEGO FASADA, A NIE CZWARTY MAGAZYN: gdyby blackboard trzymal wlasny stan, istnialyby dwa
    /// zrodla prawdy o tym samym i cykl zapis-wczytanie musialby je uzgadniac. Fasada nie ma czego
    /// zgubic przy zapisie, bo nie ma nic wlasnego.
    ///
    /// JEST WIDOKIEM ZYWYM, A ZAMROZONE SA JEGO NAPISY. SPROSTOWANIE (S6): wczesniejszy komentarz
    /// nazywal fasade "zamrozona projekcja" - nieprawda. Pola sa referencjami do ksiag, wiec
    /// zapytania (IsThreadOpen, HasFact, ...) czytaja stan BIEZACY; zamrozony jest tylko dzien gry.
    /// Zamrozone na ture sa POSTACIE KANONICZNE w WorldSnapshot, budowane raz w WorldSnapshotBuilder
    /// - i tylko one docieraja do warunkow. TEST 13d pilnuje zachowania zywego. Kto przechowa
    /// obiekt fasady przez ture (np. krok 7 w kontekscie decyzji), zobaczy zmiany z
    /// ArcDirector.OnExecuted i rozstrzygniecia kolejki faktow - przy zamrozonym dniu.
    /// </summary>
    public sealed class NarratorBlackboard
    {
        /// <summary>Status watku otwartego w postaci kanonicznej.</summary>
        public const string StatusOpen = "O";

        /// <summary>Status watku zamknietego w postaci kanonicznej.</summary>
        public const string StatusClosed = "Z";

        /// <summary>Separator elementow postaci kanonicznej - takze na poczatku i na koncu.</summary>
        public const char EntrySeparator = ';';

        /// <summary>Separator pol wewnatrz elementu postaci kanonicznej.</summary>
        public const char PartSeparator = ':';

        /// <summary>Wartosc "poza horyzontem pamieci" dla zapytan o wiek w turach.</summary>
        public const int BeyondHorizon = -1;

        private readonly EventHistory history;
        private readonly ArcLedger arcs;
        private readonly FactLedger facts;
        private readonly float gameDay;

        /// <summary>
        /// Kazdy z magazynow moze byc null (mapa bez pamieci, luki wylaczone, stary zapis) - wtedy
        /// odpowiedzi sa "puste", a nie wyjatkowe. Wyjatek w tej warstwie zabilby cala ture narratora.
        /// </summary>
        public NarratorBlackboard(EventHistory history, ArcLedger arcs, FactLedger facts, float gameDay)
        {
            this.history = history;
            this.arcs = arcs;
            this.facts = facts;
            this.gameDay = gameDay;
        }

        public float GameDay
        {
            get { return gameDay; }
        }

        // --- WATKI -----------------------------------------------------------------------------

        /// <summary>Czy watek o tym identyfikatorze jest teraz otwarty (koncepcja: "watek ruiny wciaz otwarty").</summary>
        public bool IsThreadOpen(string arcId)
        {
            return arcs != null && !string.IsNullOrEmpty(arcId) && arcs.Find(arcId) != null;
        }

        /// <summary>Faza, na ktorej stoi otwarty watek, albo null.</summary>
        public string PhaseOfThread(string arcId)
        {
            if (arcs == null || string.IsNullOrEmpty(arcId))
            {
                return null;
            }
            ArcInstance inst = arcs.Find(arcId);
            return inst == null ? null : inst.PhaseId;
        }

        /// <summary>
        /// Wynik OSTATNIEGO zamkniecia watku (ArcDirector.OutcomeResolved / OutcomeFaded) albo null.
        /// Watek otwarty po raz drugi nie kasuje sladu po pierwszym przebiegu - to sa dwa rozne pytania.
        /// </summary>
        public string ThreadOutcome(string arcId)
        {
            if (arcs == null || string.IsNullOrEmpty(arcId))
            {
                return null;
            }
            ArcClosure z = arcs.LastClosure(arcId);
            return z == null ? null : z.Outcome;
        }

        // --- FAKTY -----------------------------------------------------------------------------

        public bool HasFact(string key)
        {
            return facts != null && facts.Has(key, gameDay);
        }

        public float FactValue(string key, float missing)
        {
            return facts == null ? missing : facts.ValueOf(key, gameDay, missing);
        }

        /// <summary>Ile dni od ustawienia faktu; -1 gdy faktu nie ma albo wygasl.</summary>
        public float FactAgeDays(string key)
        {
            return facts == null ? -1f : facts.AgeDays(key, gameDay);
        }

        // --- HISTORIA --------------------------------------------------------------------------

        /// <summary>
        /// Ile TUR (decyzji narratora, nie dni) minelo od ostatniego zdarzenia o danym temacie.
        /// Realizuje przyklad wnioskowania z koncepcji: "3 tury bez zdarzenia militarnego".
        ///
        /// Zwraca BeyondHorizon (-1), gdy w buforze nie ma takiego zdarzenia. Ta wartosc znaczy
        /// "poza horyzontem pamieci", a NIE "nieskonczenie dawno" - bufor ma 24 wpisy, wiec temat
        /// nieuzywany od 24 emisji wyglada tak samo jak nigdy nieuzyty i warunek musi to wiedziec.
        /// Miekkie potraktowanie tego przypadku bylo by powtorzeniem bledu "spokoj od zawsze",
        /// ktory pusta historia raz juz wprowadzila do Cond_CalmPeriod.
        /// </summary>
        public int TurnsSinceTheme(Theme theme)
        {
            if (history == null)
            {
                return BeyondHorizon;
            }

            IReadOnlyList<EventHistoryEntry> entries = history.Entries;
            // Bufor idzie od najstarszego, wiec szukamy od konca - pierwszy trafiony jest najnowszy.
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                EventHistoryEntry e = entries[i];
                if (e != null && e.Theme == theme)
                {
                    return history.AgeInDecisions(e);
                }
            }
            return BeyondHorizon;
        }

        // --- POSTACIE KANONICZNE DLA WorldSnapshot ----------------------------------------------

        /// <summary>
        /// Watki w postaci ";O:luk:faza;Z:luk:wynik;".
        ///
        /// STATUS IDZIE PIERWSZY, a nie na koncu elementu. Powod jest praktyczny: ten sam luk moze
        /// byc jednoczesnie otwarty po raz drugi i miec slad po pierwszym zamknieciu, wiec warunek
        /// szukajacy ";luk:" trafilby w dowolny z dwoch elementow. Przy statusie z przodu
        /// ";O:luk:" i ";Z:luk:" sa rozlaczne i jedno IndexOf wystarcza.
        /// </summary>
        public string ThreadsCanonical()
        {
            if (arcs == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (ArcInstance a in arcs.Active.OrderBy(x => x.ArcId, StringComparer.Ordinal))
            {
                Dopisz(sb, StatusOpen, a.ArcId, a.PhaseId);
            }

            // Tylko OSTATNIE zamkniecie kazdego luku - bufor trzyma ich wiecej, ale pytanie
            // "czym skonczyl sie ten watek" ma jedna odpowiedz.
            var ostatnie = new Dictionary<string, ArcClosure>(StringComparer.Ordinal);
            for (int i = 0; i < arcs.Closed.Count; i++)
            {
                ArcClosure z = arcs.Closed[i];
                if (z != null && z.ArcId != null)
                {
                    ostatnie[z.ArcId] = z;
                }
            }
            foreach (var kv in ostatnie.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                Dopisz(sb, StatusClosed, kv.Key, kv.Value.Outcome);
            }

            if (sb.Length > 0)
            {
                sb.Append(EntrySeparator);
            }
            return sb.ToString();
        }

        private static void Dopisz(StringBuilder sb, string status, string id, string ogon)
        {
            sb.Append(EntrySeparator).Append(status).Append(PartSeparator)
              .Append(id).Append(PartSeparator).Append(ogon ?? string.Empty);
        }

        /// <summary>Fakty w postaci ";klucz=wartosc@wiekWdniach;" - wygasle juz odsiane.</summary>
        public string FactsCanonical()
        {
            return facts == null ? string.Empty : facts.Canonical(gameDay);
        }

        /// <summary>
        /// Wiek tematow w TURACH, w postaci ";Temat=N;" dla wszystkich tematow osi, w kolejnosci
        /// deklaracji enuma (deterministycznej). N = -1 znaczy "poza horyzontem pamieci".
        /// </summary>
        public string TurnsSinceThemesCanonical()
        {
            var sb = new StringBuilder();
            Array tematy = Enum.GetValues(typeof(Theme));
            for (int i = 0; i < tematy.Length; i++)
            {
                var t = (Theme)tematy.GetValue(i);
                sb.Append(EntrySeparator).Append(t).Append(FactLedger.ValueSeparator)
                  .Append(TurnsSinceTheme(t).ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(EntrySeparator);
            return sb.ToString();
        }

        // --- ODCZYT POSTACI KANONICZNEJ (uzywaja warunki, ktore widza tylko WorldSnapshot) -------

        /// <summary>Czy w postaci kanonicznej watkow jest wpis o tym statusie i luku.</summary>
        public static bool ThreadHasStatus(string canonical, string status, string arcId)
        {
            if (string.IsNullOrEmpty(canonical) || string.IsNullOrEmpty(arcId))
            {
                return false;
            }
            string szukane = EntrySeparator + status + PartSeparator + arcId + PartSeparator;
            return canonical.IndexOf(szukane, StringComparison.Ordinal) >= 0;
        }

        /// <summary>Ogon wpisu (faza dla otwartego, wynik dla zamknietego) albo null.</summary>
        public static string ThreadTail(string canonical, string status, string arcId)
        {
            if (string.IsNullOrEmpty(canonical) || string.IsNullOrEmpty(arcId))
            {
                return null;
            }
            string szukane = EntrySeparator + status + PartSeparator + arcId + PartSeparator;
            int od = canonical.IndexOf(szukane, StringComparison.Ordinal);
            if (od < 0)
            {
                return null;
            }
            od += szukane.Length;
            int do_ = canonical.IndexOf(EntrySeparator, od);
            return do_ < 0 ? canonical.Substring(od) : canonical.Substring(od, do_ - od);
        }

        /// <summary>Wartosc faktu z postaci kanonicznej; NaN gdy faktu nie ma.</summary>
        public static float FactValueFrom(string canonical, string key)
        {
            string segment = SegmentFaktu(canonical, key);
            if (segment == null)
            {
                return float.NaN;
            }
            int at = segment.IndexOf(FactLedger.DaySeparator);
            string liczba = at < 0 ? segment : segment.Substring(0, at);
            float v;
            return ArcInstance.Flt(liczba, out v) ? v : float.NaN;
        }

        /// <summary>Liczba obowiazujacych faktow w postaci kanonicznej - do kolumny danych "faktow".</summary>
        public static int FactCountIn(string canonical)
        {
            if (string.IsNullOrEmpty(canonical))
            {
                return 0;
            }
            int n = 0;
            foreach (string segment in canonical.Split(FactLedger.PairSeparator))
            {
                if (segment.Length > 0)
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>Czy fakt jest obecny w postaci kanonicznej (zero jest POPRAWNA wartoscia faktu,
        /// wiec obecnosci nie wolno sprawdzac porownaniem wartosci z zerem).</summary>
        public static bool FactPresentIn(string canonical, string key)
        {
            return SegmentFaktu(canonical, key) != null;
        }

        /// <summary>Wiek faktu w dniach z postaci kanonicznej; NaN gdy faktu nie ma.</summary>
        public static float FactAgeFrom(string canonical, string key)
        {
            string segment = SegmentFaktu(canonical, key);
            if (segment == null)
            {
                return float.NaN;
            }
            int at = segment.IndexOf(FactLedger.DaySeparator);
            if (at < 0)
            {
                return float.NaN;
            }
            float v;
            return ArcInstance.Flt(segment.Substring(at + 1), out v) ? v : float.NaN;
        }

        private static string SegmentFaktu(string canonical, string key)
        {
            if (string.IsNullOrEmpty(canonical) || string.IsNullOrEmpty(key))
            {
                return null;
            }
            string szukane = FactLedger.PairSeparator + key + FactLedger.ValueSeparator;
            int od = canonical.IndexOf(szukane, StringComparison.Ordinal);
            if (od < 0)
            {
                return null;
            }
            od += szukane.Length;
            int do_ = canonical.IndexOf(FactLedger.PairSeparator, od);
            return do_ < 0 ? canonical.Substring(od) : canonical.Substring(od, do_ - od);
        }

        /// <summary>Wiek tematu w turach z postaci kanonicznej; BeyondHorizon gdy brak wpisu.</summary>
        public static int TurnsSinceThemeFrom(string canonical, Theme theme)
        {
            if (string.IsNullOrEmpty(canonical))
            {
                return BeyondHorizon;
            }
            string szukane = EntrySeparator + theme.ToString() + FactLedger.ValueSeparator;
            int od = canonical.IndexOf(szukane, StringComparison.Ordinal);
            if (od < 0)
            {
                return BeyondHorizon;
            }
            od += szukane.Length;
            int do_ = canonical.IndexOf(EntrySeparator, od);
            string liczba = do_ < 0 ? canonical.Substring(od) : canonical.Substring(od, do_ - od);
            int v;
            return ArcInstance.Int(liczba, out v) ? v : BeyondHorizon;
        }
    }
}
