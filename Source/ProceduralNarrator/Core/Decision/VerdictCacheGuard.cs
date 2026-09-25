using System;
using System.Collections.Generic;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>Stan cache werdyktu jednego incydentu: tick ostatniego liczenia i wynik.</summary>
    public struct VerdictCacheState
    {
        public int Tick;
        public bool Result;

        public VerdictCacheState(int tick, bool result)
        {
            Tick = tick;
            Result = result;
        }
    }

    /// <summary>
    /// IZOLACJA NASZYCH PYTAN OD CACHE'U GRY (krok 8, dlug 8, decyzja autora K8-3).
    ///
    /// IncidentWorker.CanFireNow buforuje wynik CanFireNowSub w dwoch prywatnych polach workera - JEDEN wpis
    /// na incydent, kluczem jest sam tick (bez parametrow i bez celu). Nasz comp i waniliowe compy (oraz
    /// generatory zagrozen zadan i kolejka incydentow) pytaja w tym samym ticku o te same incydenty, wiec:
    ///   - pytajacy PRZED nami zostawia werdykt dla SWOICH parametrow, a my dostajemy go jako swiezy;
    ///   - my zostawiamy werdykt dla NASZYCH parametrow, a pytajacy PO nas dostaje go jako swiezy.
    ///
    /// Protokol (ta klasa trzyma tylko logike; odczyt i zapis pol robi warstwa integracji refleksja):
    ///   1. BeforeQuery: przy PIERWSZYM pytaniu o incydent w turze zapamietujemy stan cache'u, a przed
    ///      pytaniem cache uniewazniamy - liczymy werdykt dla naszych parametrow. Kolizja = w cache byl juz
    ///      werdykt z tego ticku od kogos innego (pomiar dlugu 8, linia [PN-CACHE]).
    ///   2. Nasz TryFire (miedzy yield a wznowieniem iteratora) korzysta z NASZEGO werdyktu - spojny z decyzja.
    ///   3. EndTurn: po wznowieniu (albo przy ciszy) przywracamy DOKLADNIE stan sprzed naszego pierwszego
    ///      pytania. Kolejni pytajacy w tym ticku widza to, co widzieliby bez nas - takze "brak werdyktu",
    ///      gdy przed nami byl werdykt z innego ticku.
    ///   4. (przeglad S10) Przywracamy TYLKO w ticku tury. Resztki tury przerwanej wyjatkiem sa porzucane:
    ///      cache z minionego ticku i tak jest martwy, a zapis nadpisalby werdykt, ktory ktos policzyl w biezacym
    ///      ticku (i zgubil kolizje [PN-CACHE]). BeforeQuery z innego ticku tez porzuca resztki.
    ///
    /// Bez Harmony (decyzja K8-1): refleksja z biblioteki standardowej .NET, zadnej zaleznosci.
    /// </summary>
    public sealed class VerdictCacheGuard
    {
        /// <summary>Tick uniewaznionego cache'u - nie rowna sie zadnemu tickowi gry.</summary>
        public const int InvalidTick = -1;

        private readonly Dictionary<string, VerdictCacheState> oryginaly =
            new Dictionary<string, VerdictCacheState>(StringComparer.Ordinal);

        private readonly List<string> kolejnosc = new List<string>();

        /// <summary>Tick tury, ktorej stany pamietamy (InvalidTick = brak).</summary>
        private int tickTury = InvalidTick;

        /// <summary>Ile incydentow czeka na przywrocenie stanu.</summary>
        public int PendingCount
        {
            get { return kolejnosc.Count; }
        }

        /// <summary>
        /// Przed naszym pytaniem o incydent. Zwraca stan do zapisania w workerze PRZED pytaniem (zawsze
        /// uniewazniony). Oryginal zapamietywany tylko przy pierwszym pytaniu w turze - drugie pytanie
        /// widzialoby w cache'u nasz wlasny werdykt, a nie cudzy.
        /// </summary>
        public VerdictCacheState BeforeQuery(string klucz, int now, VerdictCacheState obecny, out bool kolizja)
        {
            kolizja = false;
            if (kolejnosc.Count > 0 && tickTury != now)
            {
                // Resztki tury z innego ticku (przerwanej) - nieaktualne, porzucamy bez zapisu.
                Discard();
            }
            tickTury = now;
            if (klucz != null && !oryginaly.ContainsKey(klucz))
            {
                oryginaly[klucz] = obecny;
                kolejnosc.Add(klucz);
                kolizja = obecny.Tick == now;
            }
            return new VerdictCacheState(InvalidTick, false);
        }

        /// <summary>
        /// Koniec naszej tury: stany do przywrocenia, w kolejnosci pytan - TYLKO gdy now == tick tury; przy innym
        /// ticku lista jest pusta (resztki porzucone). Czysci straze w obu przypadkach.
        /// </summary>
        public List<KeyValuePair<string, VerdictCacheState>> EndTurn(int now)
        {
            var wynik = new List<KeyValuePair<string, VerdictCacheState>>(kolejnosc.Count);
            if (now == tickTury)
            {
                for (int i = 0; i < kolejnosc.Count; i++)
                {
                    wynik.Add(new KeyValuePair<string, VerdictCacheState>(kolejnosc[i], oryginaly[kolejnosc[i]]));
                }
            }
            Discard();
            return wynik;
        }

        /// <summary>Porzuca zapamietane stany bez przywracania (reset stanu ticku, np. po eksperymencie).</summary>
        public void Discard()
        {
            oryginaly.Clear();
            kolejnosc.Clear();
            tickTury = InvalidTick;
        }
    }
}
