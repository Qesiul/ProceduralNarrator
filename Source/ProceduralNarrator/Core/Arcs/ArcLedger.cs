using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ProceduralNarrator.Core.Arcs
{
    /// <summary>
    /// Stan lukow JEDNEJ mapy (odpowiednik EventHistory dla watkow): otwarte instancje, dzien
    /// ostatniego zamkniecia kazdego luku (odstep przed ponownym otwarciem), zdarzenie czekajace
    /// na potwierdzenie wykonania i licznik strat kolonistow (zasila Guard_ColonistsLost).
    ///
    /// Zapis gry idzie przez linie tekstowe (jak EventHistory.ToPersistableLines), wiec Core
    /// nie zna Scribe. Kazdy typ linii ma znacznik i SCISLA liczbe pol:
    ///   N|nastepnyNumer|stratyKolonistow
    ///   C|arcId|dzienZamkniecia
    ///   Z|... (ArcClosure.Encode)
    ///   A|... (ArcInstance.Encode)
    ///   P|... (PendingExecution.Encode)
    /// RestoreFromLines tylko DEKODUJE - zgodnosc z katalogiem (nieznany luk albo faza po zmianie
    /// Defow) rozstrzyga ArcDirector.Reconcile, bo tylko on zna katalog i umie to zalogowac.
    /// </summary>
    public sealed class ArcLedger
    {
        public const string CountersTag = "N";
        public const string CloseTag = "C";

        public List<ArcInstance> Active = new List<ArcInstance>();

        /// <summary>Dzien gry ostatniego zamkniecia per luk (klucz: defName).</summary>
        public Dictionary<string, float> LastCloseDay = new Dictionary<string, float>(StringComparer.Ordinal);

        public PendingExecution Pending;

        /// <summary>
        /// Zamkniete watki tej mapy, od najstarszego. Lista jest OGRANICZONA (MaxClosed), bo inaczej
        /// rosla by przez cala rozgrywke - a jedyny jej konsument pyta o OSTATNIE zamkniecie luku.
        /// </summary>
        public List<ArcClosure> Closed = new List<ArcClosure>();

        /// <summary>
        /// Ile zamkniec pamietamy. Szesnascie to cztery pelne obroty dzisiejszego katalogu lukow
        /// (cztery luki), czyli wystarczajaco, by kazdy luk mial w pamieci swoje ostatnie zamkniecie
        /// nawet przy rozgrywce, w ktorej wszystkie krecily sie na zmiane.
        /// </summary>
        public const int MaxClosed = 16;

        /// <summary>Numer nastepnej instancji luku na tej mapie (od 1).</summary>
        public int NextNumber = 1;

        /// <summary>Straty kolonistow tej mapy (zgony + porwania) od poczatku - licznik, nie stan.</summary>
        public int ColonistLosses;

        public ArcInstance Find(string arcId)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                if (string.Equals(Active[i].ArcId, arcId, StringComparison.Ordinal))
                {
                    return Active[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Dopisuje slad po zamknietym watku i przycina liste do MaxClosed (PrzytnijZamkniecia).
        /// Wolane z JEDNEGO miejsca (ArcDirector.Close), zeby nie powstala druga sciezka zamykania.
        /// </summary>
        public void RecordClosure(ArcInstance inst, string outcome, float day)
        {
            if (inst == null)
            {
                return;
            }
            Closed.Add(new ArcClosure
            {
                ArcId = inst.ArcId,
                Number = inst.Number,
                Outcome = outcome,
                FinalPhaseId = inst.PhaseId,
                Day = day
            });
            PrzytnijZamkniecia();
        }

        /// <summary>
        /// REGULA PRZYCINANIA (S6): usuwany jest najstarszy wpis luku, ktory ma w liscie NOWSZY wpis;
        /// dopiero gdy kazdy luk ma tylko jeden wpis - najstarszy w ogole. Dawne RemoveAt(0) wypychalo
        /// jedyne zamkniecie rzadkiego luku po 16 zamknieciach innych, wiec Cond_WatekZamkniety
        /// przestawal byc spelniony bez zadnego nowego zdarzenia. Ta sama regula przy wczytaniu -
        /// wczesniej odczyt zostawial NAJSTARSZE 16 wpisow, odwrotnie niz gra.
        /// </summary>
        private void PrzytnijZamkniecia()
        {
            while (Closed.Count > MaxClosed)
            {
                int ofiara = 0;
                for (int i = 0; i < Closed.Count; i++)
                {
                    if (MaNowszyWpis(i))
                    {
                        ofiara = i;
                        break;
                    }
                }
                Closed.RemoveAt(ofiara);
            }
        }

        private bool MaNowszyWpis(int i)
        {
            for (int j = i + 1; j < Closed.Count; j++)
            {
                if (string.Equals(Closed[j].ArcId, Closed[i].ArcId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Ostatnie zapamietane zamkniecie danego luku albo null.</summary>
        public ArcClosure LastClosure(string arcId)
        {
            for (int i = Closed.Count - 1; i >= 0; i--)
            {
                if (string.Equals(Closed[i].ArcId, arcId, StringComparison.Ordinal))
                {
                    return Closed[i];
                }
            }
            return null;
        }

        public List<string> ToPersistableLines()
        {
            var lines = new List<string>
            {
                CountersTag + "|" + NextNumber.ToString(CultureInfo.InvariantCulture)
                + "|" + ColonistLosses.ToString(CultureInfo.InvariantCulture)
            };
            foreach (var kv in LastCloseDay.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                lines.Add(CloseTag + "|" + ArcInstance.Escape(kv.Key) + "|" + kv.Value.ToString("G9", CultureInfo.InvariantCulture));
            }
            // Kolejnosc zamkniec jest CHRONOLOGICZNA, nie posortowana po kluczu jak LastCloseDay:
            // lista jest buforem z ewikcja od przodu, wiec to wlasnie kolejnosc niesie informacje,
            // ktore zamkniecie wypadnie nastepne.
            foreach (ArcClosure z in Closed)
            {
                lines.Add(z.Encode());
            }
            foreach (ArcInstance a in Active.OrderBy(x => x.Number))
            {
                lines.Add(a.Encode());
            }
            if (Pending != null)
            {
                lines.Add(Pending.Encode());
            }
            return lines;
        }

        /// <summary>
        /// Odtwarza stan z linii. Nigdy nie rzuca; zwraca liczbe linii odrzuconych (uszkodzonych).
        /// Stan sprzed wywolania jest ZASTEPOWANY, nie laczony.
        /// </summary>
        public int RestoreFromLines(IEnumerable<string> lines)
        {
            Active = new List<ArcInstance>();
            LastCloseDay = new Dictionary<string, float>(StringComparer.Ordinal);
            Closed = new List<ArcClosure>();
            Pending = null;
            NextNumber = 1;
            ColonistLosses = 0;
            if (lines == null)
            {
                return 0;
            }

            int odrzucone = 0;
            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    odrzucone++;
                    continue;
                }
                string[] f = line.Split(ArcInstance.FieldSeparator);
                if (f[0] == CountersTag && f.Length == 3)
                {
                    int nr, straty;
                    if (ArcInstance.Int(f[1], out nr) && ArcInstance.Int(f[2], out straty) && nr >= 1 && straty >= 0)
                    {
                        NextNumber = nr;
                        ColonistLosses = straty;
                        continue;
                    }
                }
                else if (f[0] == CloseTag && f.Length == 3)
                {
                    float dzien;
                    string id = ArcInstance.Unescape(f[1]);
                    if (id != null && ArcInstance.Flt(f[2], out dzien))
                    {
                        LastCloseDay[id] = dzien;
                        continue;
                    }
                }
                else if (f[0] == ArcClosure.LineTag)
                {
                    ArcClosure z;
                    // Przyciecie do MaxClosed PO wczytaniu wszystkich linii (PrzytnijZamkniecia) - ta
                    // sama regula co w grze, wiec zostaja NAJNOWSZE wpisy; nadmiar nie jest "odrzucony".
                    if (ArcClosure.TryDecode(line, out z))
                    {
                        Closed.Add(z);
                        continue;
                    }
                }
                else if (f[0] == ArcInstance.LineTag)
                {
                    ArcInstance a;
                    if (ArcInstance.TryDecode(line, out a) && Find(a.ArcId) == null)
                    {
                        Active.Add(a);
                        continue;
                    }
                }
                else if (f[0] == PendingExecution.LineTag)
                {
                    PendingExecution p;
                    if (Pending == null && PendingExecution.TryDecode(line, out p))
                    {
                        Pending = p;
                        continue;
                    }
                }
                odrzucone++;
            }

            // Numer nastepnej instancji nie moze zdublowac numeru juz otwartej (uszkodzona linia N).
            foreach (ArcInstance a in Active)
            {
                if (a.Number >= NextNumber)
                {
                    NextNumber = a.Number + 1;
                }
            }
            Active = Active.OrderBy(x => x.Number).ToList();
            PrzytnijZamkniecia();
            return odrzucone;
        }

        /// <summary>Gleboka kopia przez kodek - do checkpointu ramion eksperymentu symulatora.</summary>
        public ArcLedger Clone()
        {
            var c = new ArcLedger();
            c.RestoreFromLines(ToPersistableLines());
            return c;
        }

        /// <summary>Opis do kolumny lukFazy i logu: "luk:faza:status" - status dopisuje fokus (S3).</summary>
        public string Summary()
        {
            if (Active.Count == 0)
            {
                return string.Empty;
            }
            return string.Join(",", Active.OrderBy(a => a.Number).Select(a => a.ArcId + ":" + a.PhaseId).ToArray());
        }
    }
}
