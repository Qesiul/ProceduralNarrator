using System;
using System.Collections.Generic;

namespace ProceduralNarrator.Core.Evaluation
{
    /// <summary>Jedno wykryte odpalenie incydentu: cel (mapa/swiat/karawana), incydent i tick odpalenia.</summary>
    public struct DetectedFire
    {
        public string Target;
        public string Incident;
        public int Tick;
    }

    /// <summary>
    /// WYKRYWANIE ODPALEN INCYDENTOW BEZ HARMONY (krok 8, decyzja autora K8-2) - logika czysta, bez gry.
    ///
    /// Gra po kazdym odpaleniu przez narratora (Storyteller.TryFire, takze z kolejki incydentow i z
    /// incydentow interwalowych zadan) wpisuje tick do StoryState.lastFireTicks[incydent] celu. Wpis
    /// o wartosci wiekszej niz tick naszego poprzedniego przegladu tego celu = odpalenie od tamtej pory.
    /// Przegladamy co tick (w GameComponentTick, PO StorytellerTick), wiec tick odpalenia jest dokladny,
    /// a zgubic mozna tylko DRUGIE odpalenie tego samego incydentu na tym samym celu W TYM SAMYM ticku.
    ///
    /// PUNKT STARTU: cele obecne przy PIERWSZYM przegladzie po wczytaniu (i przy nowej grze) zaczynaja od
    /// StartTick (tick wczytania) - wpisy sprzed zapisu maja tick &lt;= StartTick, wiec nie wracaja jako nowe.
    /// Cel widziany pierwszy raz POZNIEJ (nowa karawana, mapa tymczasowa) zaczyna od chwili zobaczenia (now):
    /// gra KOPIUJE StoryState miedzy karawana a mapa tymczasowa (RetainedCaravanData.Set,
    /// Notify_CaravanFormed), wiec nowy cel przychodzi z cudzymi wpisami z przeszlosci. Liczone od StartTick
    /// wracalyby jako fantomowe odpalenia (przeglad S10). Koszt: odpalenie na celu w tym samym ticku, w ktorym
    /// cel powstal, nie zostanie zauwazone.
    ///
    /// WPISY Z PRZYSZLOSCI (&gt; now) nie sa odpaleniami: zostawia je waniliowe "Future incidents" (czysci
    /// StoryState i wpisuje ticki symulacji) albo nieudane przywrocenie symulatora. Zapamietujemy je jako
    /// FANTOMY (cel, incydent, wartosc) i nie logujemy, gdy zegar do nich dojdzie; prawdziwe nadpisanie
    /// (inna wartosc) jest odpaleniem. Scan zwraca liczbe NOWYCH fantomow - obserwator ostrzega.
    /// Przeglad nigdy nie cofa znacznika celu (max), wiec cofniety zegar nie powtarza odpalen.
    /// </summary>
    public sealed class FireDetector
    {
        private readonly Dictionary<string, int> ostatniPrzeglad = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Fantomy: "cel|incydent" -&gt; wartosc z przyszlosci widziana w lastFireTicks.</summary>
        private readonly Dictionary<string, int> fantomy = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Tick pierwszego przegladu (cykl startowy); -1 = jeszcze nie bylo przegladu.</summary>
        private int pierwszyPrzeglad = -1;

        public FireDetector(int startTick)
        {
            StartTick = startTick;
        }

        /// <summary>Tick, od ktorego liczy sie kazdy cel widziany po raz pierwszy.</summary>
        public int StartTick { get; private set; }

        /// <summary>Liczba celow z zapamietanym przegladem (do diagnostyki i testow).</summary>
        public int KnownTargets
        {
            get { return ostatniPrzeglad.Count; }
        }

        /// <summary>Liczba zapamietanych fantomow (do diagnostyki i testow).</summary>
        public int Phantoms
        {
            get { return fantomy.Count; }
        }

        /// <summary>Tick ostatniego przegladu celu albo StartTick dla celu nieznanego.</summary>
        public int LastScan(string cel)
        {
            int t;
            return cel != null && ostatniPrzeglad.TryGetValue(cel, out t) ? t : StartTick;
        }

        /// <summary>
        /// Przeglad jednego celu. Dopisuje do wynik odpalenia z tickiem w (prog, now], posortowane po (tick,
        /// incydent) ordynalnie - kolejnosc w logu nie zalezy od kolejnosci slownika. Prog: ostatni przeglad celu;
        /// dla celu nieznanego StartTick w cyklu pierwszego przegladu, a pozniej now (zasady w komentarzu klasy).
        /// Zwraca liczbe NOWYCH fantomow (wpisow z przyszlosci).
        /// </summary>
        public int Scan(string cel, IEnumerable<KeyValuePair<string, int>> ostatnieOdpalenia, int now,
                        List<DetectedFire> wynik)
        {
            if (cel == null || wynik == null)
            {
                return 0;
            }
            if (pierwszyPrzeglad < 0)
            {
                pierwszyPrzeglad = now;
            }
            int prog;
            if (!ostatniPrzeglad.TryGetValue(cel, out prog))
            {
                prog = now == pierwszyPrzeglad ? StartTick : now;
            }
            int start = wynik.Count;
            int noweFantomy = 0;
            if (ostatnieOdpalenia != null)
            {
                foreach (KeyValuePair<string, int> kv in ostatnieOdpalenia)
                {
                    if (kv.Key == null)
                    {
                        continue;
                    }
                    string klucz = cel + "|" + kv.Key;
                    int fantom;
                    bool znany = fantomy.TryGetValue(klucz, out fantom);
                    if (kv.Value > now)
                    {
                        if (!znany || fantom != kv.Value)
                        {
                            fantomy[klucz] = kv.Value;
                            noweFantomy++;
                        }
                        continue;
                    }
                    if (znany)
                    {
                        fantomy.Remove(klucz);
                        if (fantom == kv.Value)
                        {
                            // Fantom, do ktorego doszedl zegar - to nie jest odpalenie.
                            continue;
                        }
                        // Inna wartosc: gra nadpisala fantom prawdziwym odpaleniem.
                    }
                    if (kv.Value > prog)
                    {
                        wynik.Add(new DetectedFire { Target = cel, Incident = kv.Key, Tick = kv.Value });
                    }
                }
            }
            if (wynik.Count - start > 1)
            {
                wynik.Sort(start, wynik.Count - start, Porzadek.Instance);
            }
            ostatniPrzeglad[cel] = Math.Max(prog, now);
            return noweFantomy;
        }

        // CELOW NIE ZAPOMINAMY. Cel zapomniany i widziany ponownie wrocilby do StartTick i powtorzyl
        // odpalenia juz zalogowane. Slownik rosnie o liczbe roznych celow w sesji (mapy i karawany -
        // dziesiatki), a znika przy wczytaniu zapisu razem z komponentem.

        private sealed class Porzadek : IComparer<DetectedFire>
        {
            public static readonly Porzadek Instance = new Porzadek();

            public int Compare(DetectedFire a, DetectedFire b)
            {
                int c = a.Tick.CompareTo(b.Tick);
                return c != 0 ? c : string.CompareOrdinal(a.Incident, b.Incident);
            }
        }
    }
}
