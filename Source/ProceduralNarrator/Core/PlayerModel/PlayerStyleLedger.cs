using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProceduralNarrator.Core.PlayerModel
{
    /// <summary>Baza rekordow jednego kolonisty z poprzedniej doby (delty licza sie od niej).</summary>
    public class PawnBaseline
    {
        public long Productive;
        public long Support;
        public long Recruited;
        public long Captured;

        public PawnBaseline Clone()
        {
            return new PawnBaseline { Productive = Productive, Support = Support, Recruited = Recruited, Captured = Captured };
        }
    }

    /// <summary>Epizod zagrozenia w toku na jednej mapie domowej.</summary>
    public class ThreatEpisode
    {
        public int MapId;
        public long StartTick;
        public int ThreatSamples;
        public long DraftedSum;
        public long EligibleSum;

        /// <summary>Tick pierwszej probki z kims pod bronia; -1 = nikt jeszcze nie zostal powolany.</summary>
        public long FirstDraftTick = -1;

        /// <summary>Kolejne ciche probki od ostatniej probki z zagrozeniem (histereza zamkniecia).</summary>
        public int QuietSamples;

        /// <summary>
        /// Epizod zamkniety limitem dlugosci (maxEpisodeTicks, przeglad S8): juz policzony, zostaje tylko jako
        /// blokada - mapa czeka na quietSamplesToClose cichych probek, zanim zacznie nowy epizod.
        /// </summary>
        public bool Capped;

        public ThreatEpisode Clone()
        {
            return new ThreatEpisode
            {
                MapId = MapId, StartTick = StartTick, ThreatSamples = ThreatSamples, DraftedSum = DraftedSum,
                EligibleSum = EligibleSum, FirstDraftTick = FirstDraftTick, QuietSamples = QuietSamples, Capped = Capped
            };
        }
    }

    /// <summary>
    /// KSIEGA STYLU GRACZA (krok 7) - kolejka FIFO dni z surowymi licznikami (decyzje autora nr 8 i 10:
    /// "worek" dni o rownej wadze, pojemnosc 60; dzien 61 wypycha dzien 1) oraz stan obserwacji
    /// potrzebny, zeby zapis gry NIE zmienial nastepnej probki: dzien w toku, bazy rekordow
    /// kolonistow, epizody zagrozenia w toku, oczekujace oferty dolaczenia i sledzeni dzicy ludzie.
    ///
    /// Stan jest per GRE (styl gracza), nie per mape - decyzja zgloszona autorowi przy planie kroku 7.
    /// Czysty rdzen: zadnego API gry. Warstwa integracji podaje obserwacje w postaci liczb (S2).
    ///
    /// KODEK (wezel Scribe "stylGracza", lista linii, liczby calkowite, separator "|"):
    ///   N|1|biezacyDzien|zainicjowany|ostatniRajdTick|maxIdOferty   naglowek (najwyzej jeden)
    ///   D|dzien|n0|d0|...|n7|d7                                    dzien zamkniety (FIFO, od najstarszego)
    ///   A|dzien|n0|d0|...|n7|d7                                    dzien w toku (najwyzej jeden)
    ///   B|thingId|produktywne|pomocnicze|zwerbowani|schwytani       baza rekordow kolonisty
    ///   E|mapa|start|probekZagrozenia|sumaPoborowych|sumaZdolnych|pierwszyPobor|probekCiszy
    ///   O|questId                                                   oferta oczekujaca na rozstrzygniecie
    ///   W|thingId|dzienPierwszejObserwacji                          sledzony dziki czlowiek
    /// </summary>
    public class PlayerStyleLedger
    {
        public const int FormatVersion = 1;
        public const char FieldSeparator = '|';

        /// <summary>Dzien w toku (tick/60000). -1 przed pierwsza obserwacja.</summary>
        public int CurrentDay = -1;

        /// <summary>Czy obserwator juz ustawil bazy (pierwsza obserwacja po starcie albo migracji).</summary>
        public bool Initialized;

        /// <summary>Ostatnio widziana wartosc History.lastTickPlayerRaidedSomeone (-1 = brak).</summary>
        public long LastRaidTick = -1;

        /// <summary>Znak wodny id zadan: oferty o id nie wiekszym nigdy nie sa liczone ponownie.</summary>
        public int MaxOfferId = -1;

        /// <summary>Dni zamkniete, od najstarszego. Po PushDay najwyzej capacityDays.</summary>
        public readonly List<StyleDaySample> Days = new List<StyleDaySample>();

        /// <summary>Dzien w toku - liczniki zbierane w trakcie doby (epizody, ataki). null przed inicjalizacja.</summary>
        public StyleDaySample Partial;

        public readonly SortedDictionary<int, PawnBaseline> Baselines = new SortedDictionary<int, PawnBaseline>();
        public readonly SortedDictionary<int, ThreatEpisode> Episodes = new SortedDictionary<int, ThreatEpisode>();
        public readonly SortedSet<int> PendingOffers = new SortedSet<int>();
        public readonly SortedDictionary<int, int> WildMen = new SortedDictionary<int, int>();

        /// <summary>
        /// Dopisuje dzien zamkniety na koniec kolejki i wypycha najstarsze ponad pojemnosc (FIFO).
        /// </summary>
        public void PushDay(StyleDaySample day, int capacityDays)
        {
            if (day == null)
            {
                return;
            }
            Days.Add(day);
            int cap = capacityDays < 1 ? 1 : capacityDays;
            while (Days.Count > cap)
            {
                Days.RemoveAt(0);
            }
        }

        // ------------------------------------------------------------------ obserwacja (S2)

        public const long TicksPerDay = 60000;

        /// <summary>
        /// Pierwsza obserwacja (nowa gra, pierwsze wczytanie z ksiega pusta, migracja zapisu v3): bazy
        /// rekordow dla kolonistow, znak wodny zadan, sledzeni dzicy ludzie, ostatni atak gracza. Nic
        /// z tego stanu nie jest liczone jako styl - historia sprzed obserwacji nie jest widoczna.
        /// </summary>
        public void Initialize(long tick, DayObservation obs, PlayerStyleParams p)
        {
            CurrentDay = (int)(tick / TicksPerDay);
            Partial = new StyleDaySample(CurrentDay);
            Baselines.Clear();
            Episodes.Clear();
            PendingOffers.Clear();
            WildMen.Clear();
            LastRaidTick = obs == null ? -1 : obs.LastPlayerRaidTick;
            MaxOfferId = -1;
            if (obs != null)
            {
                foreach (PawnRecordSample ps in obs.Pawns)
                {
                    if (ps != null && ps.Eligible)
                    {
                        Baselines[ps.Id] = Baza(ps);
                    }
                }
                foreach (OfferQuestSample o in obs.Offers)
                {
                    if (o == null) continue;
                    if (o.Id > MaxOfferId) MaxOfferId = o.Id;
                    // Oferta w toku zostaje sledzona: wybor gracza zapadnie juz pod obserwacja.
                    if (o.State == OfferState.Pending) PendingOffers.Add(o.Id);
                }
                foreach (WildManSample w in obs.WildMen)
                {
                    if (w != null && (w.Status == WildManStatus.Wild || w.Status == WildManStatus.Prisoner))
                    {
                        WildMen[w.Id] = CurrentDay;
                    }
                }
            }
            Initialized = true;
        }

        /// <summary>
        /// Atak gracza na osade: gra pamieta tylko tick OSTATNIEGO (History.lastTickPlayerRaidedSomeone),
        /// wiec liczy sie zmiana wartosci miedzy probkami. Dwa ataki miedzy probkami licza sie jako jeden.
        /// </summary>
        public void ObserveRaidTick(long lastTickPlayerRaidedSomeone)
        {
            if (!Initialized || Partial == null)
            {
                return;
            }
            if (lastTickPlayerRaidedSomeone > LastRaidTick)
            {
                Partial.Add(StyleSignal.Inicjatywa, 1, 0);
                LastRaidTick = lastTickPlayerRaidedSomeone;
            }
        }

        /// <summary>
        /// Probka zagrozenia z map domowych. Epizod zaczyna pierwsza probka z zagrozeniem, konczy
        /// quietSamplesToClose kolejnych cichych probek (histereza: zasieg wrogow potrafi migotac).
        /// Epizod mapy, ktorej nie ma juz w probce (zamknieta mapa), przepada bez liczenia.
        /// </summary>
        public void OnThreatSample(long tick, IList<MapThreatSample> samples, PlayerStyleParams p)
        {
            if (!Initialized || Partial == null || samples == null)
            {
                return;
            }
            var obecne = new HashSet<int>();
            foreach (MapThreatSample s in samples)
            {
                obecne.Add(s.MapId);
                ThreatEpisode ep;
                Episodes.TryGetValue(s.MapId, out ep);
                if (s.Threat && ep != null && ep.Capped)
                {
                    // Zagrozenie trwa dalej po limicie: epizod juz policzony, czekamy na cisze.
                    ep.QuietSamples = 0;
                }
                else if (s.Threat)
                {
                    if (ep == null)
                    {
                        ep = new ThreatEpisode { MapId = s.MapId, StartTick = tick };
                        Episodes[s.MapId] = ep;
                    }
                    ep.ThreatSamples++;
                    ep.DraftedSum += s.Drafted < 0 ? 0 : s.Drafted;
                    ep.EligibleSum += s.Eligible < 0 ? 0 : s.Eligible;
                    if (s.Drafted > 0 && ep.FirstDraftTick < 0)
                    {
                        ep.FirstDraftTick = tick;
                    }
                    ep.QuietSamples = 0;
                    // LIMIT DLUGOSCI (przeglad S8): oblezenie, dlugi napad - zamykamy i liczymy raz, dalej blokada.
                    if (tick - ep.StartTick >= p.maxEpisodeTicks)
                    {
                        CloseEpisode(ep, p);
                        ep.Capped = true;
                    }
                }
                else if (ep != null)
                {
                    ep.QuietSamples++;
                    if (ep.QuietSamples >= p.quietSamplesToClose)
                    {
                        if (!ep.Capped)
                        {
                            CloseEpisode(ep, p);
                        }
                        Episodes.Remove(s.MapId);
                    }
                }
            }
            var zniknete = new List<int>();
            foreach (int map in Episodes.Keys)
            {
                if (!obecne.Contains(map)) zniknete.Add(map);
            }
            foreach (int map in zniknete)
            {
                Episodes.Remove(map);
            }
        }

        private void CloseEpisode(ThreatEpisode ep, PlayerStyleParams p)
        {
            // Migotanie (za malo probek) i epizod bez nikogo zdolnego do walki nic nie mowia o reakcji.
            if (ep.ThreatSamples < p.minEpisodeSamples || ep.EligibleSum <= 0)
            {
                return;
            }
            long pobor = (long)Math.Round(1000.0 * ep.DraftedSum / ep.EligibleSum, MidpointRounding.AwayFromZero);
            long szybkosc = 0;
            if (ep.FirstDraftTick >= 0)
            {
                long opoznienie = ep.FirstDraftTick - ep.StartTick;
                long limit = p.reactionCapTicks < 1 ? 1 : p.reactionCapTicks;
                if (opoznienie < 0) opoznienie = 0;
                if (opoznienie > limit) opoznienie = limit;
                szybkosc = (long)Math.Round(1000.0 * (1.0 - opoznienie / (double)limit), MidpointRounding.AwayFromZero);
            }
            Partial.Add(StyleSignal.Poborowi, pobor, 1);
            Partial.Add(StyleSignal.Szybkosc, szybkosc, 1);
        }

        /// <summary>
        /// Zamyka dobe w toku obserwacja z poczatku nowej doby i wpycha ja do kolejki (FIFO).
        /// Zwraca dzien wepchniety (do linii [PN-GRACZ]) albo null, gdy ksiega nie byla zainicjowana
        /// (wtedy robi Initialize). Przeskok o kilka dni (skoki czasu dev) daje JEDEN dzien w kolejce.
        /// </summary>
        public StyleDaySample CloseDay(long tick, DayObservation obs, PlayerStyleParams p)
        {
            if (!Initialized || Partial == null)
            {
                Initialize(tick, obs, p);
                return null;
            }
            if (obs == null)
            {
                obs = new DayObservation();
            }
            StyleDaySample dzien = Partial;

            // Stan: udzialy budynkow w chwili zamkniecia doby.
            dzien.Set(StyleSignal.Obrona, obs.DefenseValue, obs.BuildingValue);
            dzien.Set(StyleSignal.Produkcja, obs.ProductionValue, obs.BuildingValue);

            // Rekordy: delta tylko dla kolonisty, ktory ma baze z poprzedniej doby i jest kolonista TERAZ
            // (rekordy niosa historie sprzed rekrutacji; nowy kolonista dostaje najpierw baze).
            long prod = 0, pom = 0, zwerb = 0, schw = 0;
            var aktualne = new SortedDictionary<int, PawnBaseline>();
            foreach (PawnRecordSample ps in obs.Pawns)
            {
                if (ps == null || !ps.Eligible || aktualne.ContainsKey(ps.Id))
                {
                    continue;
                }
                PawnBaseline b;
                if (Baselines.TryGetValue(ps.Id, out b))
                {
                    prod += Delta(ps.Productive, b.Productive);
                    pom += Delta(ps.Support, b.Support);
                    zwerb += Delta(ps.Recruited, b.Recruited);
                    schw += Delta(ps.Captured, b.Captured);
                }
                aktualne[ps.Id] = Baza(ps);
            }
            Baselines.Clear();
            foreach (KeyValuePair<int, PawnBaseline> kv in aktualne)
            {
                Baselines[kv.Key] = kv.Value;
            }
            dzien.Set(StyleSignal.Praca, prod, prod + pom);
            dzien.Add(StyleSignal.Werbunek, zwerb, schw);

            // Oferty: kazda rozstrzygnieta najwyzej raz; znak wodny chroni przed ponownym policzeniem.
            long przyjete = 0, rozstrzygniete = 0;
            var widziane = new HashSet<int>();
            // Znak wodny z POCZATKU doby (przeglad S8): aktualizowany w petli uzaleznial wynik od kolejnosci listy -
            // oferta 14 po ofercie 15 wypadala jako "stara". W wanilii lista idzie po id, ale regula nie moze na tym
            // polegac.
            int znakWodny = MaxOfferId;
            foreach (OfferQuestSample o in obs.Offers)
            {
                if (o == null || !widziane.Add(o.Id))
                {
                    continue;
                }
                bool nowa = o.Id > znakWodny;
                if (nowa)
                {
                    if (o.Id > MaxOfferId)
                    {
                        MaxOfferId = o.Id;
                    }
                }
                else if (!PendingOffers.Contains(o.Id))
                {
                    continue;
                }
                switch (o.State)
                {
                    case OfferState.Pending:
                        PendingOffers.Add(o.Id);
                        break;
                    case OfferState.Success:
                        przyjete++;
                        rozstrzygniete++;
                        PendingOffers.Remove(o.Id);
                        break;
                    case OfferState.Fail:
                        rozstrzygniete++;
                        PendingOffers.Remove(o.Id);
                        break;
                    default:
                        PendingOffers.Remove(o.Id);
                        break;
                }
            }
            // Oferta oczekujaca, ktorej juz nie ma w menedzerze zadan - bez rozstrzygniecia, wypada.
            var bezZadania = new List<int>();
            foreach (int q in PendingOffers)
            {
                if (!widziane.Contains(q)) bezZadania.Add(q);
            }
            foreach (int q in bezZadania)
            {
                PendingOffers.Remove(q);
            }

            // Dzicy ludzie: dolaczyl = przyjety; zniknal albo nieoswojony przez limit dni = odrzucony.
            var stany = new Dictionary<int, WildManStatus>();
            foreach (WildManSample w in obs.WildMen)
            {
                if (w != null && !stany.ContainsKey(w.Id)) stany[w.Id] = w.Status;
            }
            foreach (KeyValuePair<int, WildManStatus> kv in stany)
            {
                if ((kv.Value == WildManStatus.Wild || kv.Value == WildManStatus.Prisoner) && !WildMen.ContainsKey(kv.Key))
                {
                    WildMen[kv.Key] = dzien.Day;
                }
            }
            var rozstrzygnieci = new List<int>();
            foreach (KeyValuePair<int, int> kv in WildMen)
            {
                WildManStatus st;
                if (!stany.TryGetValue(kv.Key, out st))
                {
                    st = WildManStatus.Gone;
                }
                if (st == WildManStatus.Joined)
                {
                    przyjete++;
                    rozstrzygniete++;
                    rozstrzygnieci.Add(kv.Key);
                }
                // Limit od dnia REJESTRACJI (przeglad S8): dawne "Day + 1 - pierwszy" odrzucalo ~dobe za wczesnie,
                // a przy limicie 1 - w tej samej dobie, w ktorej dzikus zostal zarejestrowany.
                else if (st == WildManStatus.Gone
                         || (st == WildManStatus.Wild && dzien.Day - kv.Value >= p.wildManTimeoutDays))
                {
                    rozstrzygniete++;
                    rozstrzygnieci.Add(kv.Key);
                }
            }
            foreach (int id in rozstrzygnieci)
            {
                WildMen.Remove(id);
            }
            dzien.Add(StyleSignal.Przyjecia, przyjete, rozstrzygniete);

            // Inicjatywa: mianownik = jeden dzien; licznik dopisuje ObserveRaidTick w trakcie doby.
            ObserveRaidTick(obs.LastPlayerRaidTick);
            dzien.Add(StyleSignal.Inicjatywa, 0, 1);

            PushDay(dzien, p.capacityDays);
            CurrentDay = (int)(tick / TicksPerDay);
            Partial = new StyleDaySample(CurrentDay);
            return dzien;
        }

        private static long Delta(long teraz, long baza)
        {
            long d = teraz - baza;
            return d < 0 ? 0 : d;
        }

        private static PawnBaseline Baza(PawnRecordSample ps)
        {
            return new PawnBaseline
            {
                Productive = ps.Productive, Support = ps.Support, Recruited = ps.Recruited, Captured = ps.Captured
            };
        }

        // ------------------------------------------------------------------ kodek

        public List<string> ToPersistableLines()
        {
            var l = new List<string>();
            l.Add(Join("N", I(FormatVersion), I(CurrentDay), Initialized ? "1" : "0", L(LastRaidTick), I(MaxOfferId)));
            foreach (StyleDaySample d in Days)
            {
                l.Add(EncodeDay("D", d));
            }
            if (Partial != null)
            {
                l.Add(EncodeDay("A", Partial));
            }
            foreach (KeyValuePair<int, PawnBaseline> kv in Baselines)
            {
                PawnBaseline b = kv.Value;
                l.Add(Join("B", I(kv.Key), L(b.Productive), L(b.Support), L(b.Recruited), L(b.Captured)));
            }
            foreach (KeyValuePair<int, ThreatEpisode> kv in Episodes)
            {
                ThreatEpisode e = kv.Value;
                l.Add(Join("E", I(kv.Key), L(e.StartTick), I(e.ThreatSamples), L(e.DraftedSum), L(e.EligibleSum),
                           L(e.FirstDraftTick), I(e.QuietSamples), e.Capped ? "1" : "0"));
            }
            foreach (int q in PendingOffers)
            {
                l.Add(Join("O", I(q)));
            }
            foreach (KeyValuePair<int, int> kv in WildMen)
            {
                l.Add(Join("W", I(kv.Key), I(kv.Value)));
            }
            return l;
        }

        /// <summary>
        /// Odtwarza ksiege z linii. Nigdy nie rzuca; zwraca liczbe linii odrzuconych. Stan sprzed
        /// wywolania jest ZASTEPOWANY. Odrzucane: nieznany znacznik, zla liczba pol, blad liczby,
        /// drugie N albo A, dzien D nie wiekszy od poprzedniego (duplikat albo zla kolejnosc),
        /// zdublowany klucz B/E/O/W. Przycinanie do pojemnosci robi PushDay, nie dekoder.
        /// </summary>
        public int RestoreFromLines(IEnumerable<string> lines)
        {
            Clear();
            if (lines == null)
            {
                return 0;
            }
            int odrzucone = 0;
            bool bylN = false;
            foreach (string line in lines)
            {
                if (!Decode(line, ref bylN))
                {
                    odrzucone++;
                }
            }
            // Ksiega zainicjowana bez dnia w toku (linia A odrzucona albo brak) - zaczynamy dzien w toku od zera,
            // zamiast gubic probki do granicy doby i ponownie inicjowac (przeglad S8: reset baz, epizodow i ofert).
            if (Initialized && Partial == null)
            {
                Partial = new StyleDaySample(CurrentDay);
            }
            return odrzucone;
        }

        private bool Decode(string line, ref bool bylN)
        {
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }
            string[] f = line.Split(FieldSeparator);
            switch (f[0])
            {
                case "N":
                {
                    int ver, day, maxId;
                    long raid;
                    if (bylN || f.Length != 6 || !TI(f[1], out ver) || ver != FormatVersion || !TI(f[2], out day)
                        || (f[3] != "0" && f[3] != "1") || !TL(f[4], out raid) || !TI(f[5], out maxId))
                    {
                        return false;
                    }
                    bylN = true;
                    CurrentDay = day;
                    Initialized = f[3] == "1";
                    LastRaidTick = raid;
                    MaxOfferId = maxId;
                    return true;
                }
                case "D":
                case "A":
                {
                    StyleDaySample s;
                    if (!TryDecodeDay(f, out s))
                    {
                        return false;
                    }
                    if (f[0] == "A")
                    {
                        if (Partial != null)
                        {
                            return false;
                        }
                        Partial = s;
                        return true;
                    }
                    if (Days.Count > 0 && s.Day <= Days[Days.Count - 1].Day)
                    {
                        return false;
                    }
                    Days.Add(s);
                    return true;
                }
                case "B":
                {
                    int id;
                    long p, sp, r, c;
                    if (f.Length != 6 || !TI(f[1], out id) || !TL(f[2], out p) || !TL(f[3], out sp)
                        || !TL(f[4], out r) || !TL(f[5], out c) || Baselines.ContainsKey(id))
                    {
                        return false;
                    }
                    Baselines[id] = new PawnBaseline { Productive = p, Support = sp, Recruited = r, Captured = c };
                    return true;
                }
                case "E":
                {
                    int map, samples, quiet;
                    long start, drafted, eligible, first;
                    if (f.Length != 9 || !TI(f[1], out map) || !TL(f[2], out start) || !TI(f[3], out samples)
                        || !TL(f[4], out drafted) || !TL(f[5], out eligible) || !TL(f[6], out first)
                        || !TI(f[7], out quiet) || (f[8] != "0" && f[8] != "1") || Episodes.ContainsKey(map))
                    {
                        return false;
                    }
                    Episodes[map] = new ThreatEpisode
                    {
                        MapId = map, StartTick = start, ThreatSamples = samples, DraftedSum = drafted,
                        EligibleSum = eligible, FirstDraftTick = first, QuietSamples = quiet, Capped = f[8] == "1"
                    };
                    return true;
                }
                case "O":
                {
                    int q;
                    if (f.Length != 2 || !TI(f[1], out q) || PendingOffers.Contains(q))
                    {
                        return false;
                    }
                    PendingOffers.Add(q);
                    return true;
                }
                case "W":
                {
                    int id, day;
                    if (f.Length != 3 || !TI(f[1], out id) || !TI(f[2], out day) || WildMen.ContainsKey(id))
                    {
                        return false;
                    }
                    WildMen[id] = day;
                    return true;
                }
                default:
                    return false;
            }
        }

        private static bool TryDecodeDay(string[] f, out StyleDaySample s)
        {
            s = null;
            int day;
            if (f.Length != 2 + 2 * StyleSignals.Count || !TI(f[1], out day))
            {
                return false;
            }
            var wynik = new StyleDaySample(day);
            for (int i = 0; i < StyleSignals.Count; i++)
            {
                long n, d;
                if (!TL(f[2 + 2 * i], out n) || !TL(f[3 + 2 * i], out d))
                {
                    return false;
                }
                wynik.Num[i] = n;
                wynik.Den[i] = d;
            }
            s = wynik;
            return true;
        }

        private static string EncodeDay(string tag, StyleDaySample d)
        {
            var sb = new StringBuilder(96);
            sb.Append(tag).Append(FieldSeparator).Append(I(d.Day));
            for (int i = 0; i < StyleSignals.Count; i++)
            {
                sb.Append(FieldSeparator).Append(L(d.Num[i])).Append(FieldSeparator).Append(L(d.Den[i]));
            }
            return sb.ToString();
        }

        public void Clear()
        {
            CurrentDay = -1;
            Initialized = false;
            LastRaidTick = -1;
            MaxOfferId = -1;
            Days.Clear();
            Partial = null;
            Baselines.Clear();
            Episodes.Clear();
            PendingOffers.Clear();
            WildMen.Clear();
        }

        /// <summary>Gleboka kopia przez kodek - checkpoint ramion symulatora.</summary>
        public PlayerStyleLedger Clone()
        {
            var c = new PlayerStyleLedger();
            c.RestoreFromLines(ToPersistableLines());
            return c;
        }

        // ------------------------------------------------------------------ pomocnicze

        private static string Join(params string[] pola)
        {
            return string.Join(FieldSeparator.ToString(), pola);
        }

        private static string I(int v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }

        private static string L(long v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TI(string s, out int v)
        {
            return int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v);
        }

        private static bool TL(string s, out long v)
        {
            return long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v);
        }
    }
}
