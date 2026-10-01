using System.Diagnostics;
using ProceduralNarrator.Core.Evaluation;

namespace ProceduralNarrator.Integration.Evaluation
{
    /// <summary>
    /// [PN-PERF] - koszt czasu za dobe (krok 9, etap L, decyzja L-2; hipoteza H8: p95 decyzji &lt; 5 ms, obserwatory
    /// &lt; 50 us/tick). Obserwatory mierzy komponent pamieci w kazdym ticku, compa - sam comp (poza symulatorem).
    /// Dokladne czasy decyzji sa tez w kolumnie czasMs wiersza [PN-DATA]; tu agregaty i kubelki.
    /// </summary>
    internal sealed class PerfMonitor
    {
        /// <summary>Granice kubelkow czasu decyzji w ms (5 ms = prog H8).</summary>
        public static readonly double[] GraniceDecyzjiMs = { 0.5, 1, 2, 5, 10, 50 };

        /// <summary>Granice kubelkow kosztu obserwatorow na tick w us (50 us = prog H8).</summary>
        public static readonly double[] GraniceTickuUs = { 10, 50, 100, 500, 1000 };

        private readonly DayClock zegar = new DayClock();
        private readonly PerfAccumulator tik = new PerfAccumulator(GraniceTickuUs);
        private readonly PerfAccumulator odpalenia = new PerfAccumulator();
        private readonly PerfAccumulator styl = new PerfAccumulator();
        private readonly PerfAccumulator doba = new PerfAccumulator();
        private readonly PerfAccumulator kolonisci = new PerfAccumulator();
        private readonly PerfAccumulator interwaly = new PerfAccumulator();
        private readonly PerfAccumulator decyzje = new PerfAccumulator(GraniceDecyzjiMs);
        private readonly PerfAccumulator potwierdzenia = new PerfAccumulator();

        public static double Us(long od, long doTeraz)
        {
            return (doTeraz - od) * 1000000.0 / Stopwatch.Frequency;
        }

        public static double Ms(long od, long doTeraz)
        {
            return (doTeraz - od) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Na poczatku ticku: przy zmianie doby linia za dobe zamknieta i zerowanie.</summary>
        public void BeginTick(int tick)
        {
            int zamknieta = zegar.Advance(tick);
            if (zamknieta < 0)
            {
                return;
            }
            PNLog.Perf(zamknieta, tik.Fragment("tickow", "Us", true)
                                  + odpalenia.Fragment("odpalen", "Us", false)
                                  + styl.Fragment("styl", "Us", false)
                                  + doba.Fragment("doba", "Us", false)
                                  + kolonisci.Fragment("kolonistow", "Us", false)
                                  + interwaly.Fragment("interwalow", "Us", false)
                                  + decyzje.Fragment("decyzji", "Ms", true)
                                  + potwierdzenia.Fragment("potwierdzen", "Us", false));
            tik.Reset();
            odpalenia.Reset();
            styl.Reset();
            doba.Reset();
            kolonisci.Reset();
            interwaly.Reset();
            decyzje.Reset();
            potwierdzenia.Reset();
        }

        public void RecordObservers(double odpaleniaUs, double stylUs, double dobaUs, double kolonisciUs)
        {
            tik.Add(odpaleniaUs + stylUs + dobaUs + kolonisciUs);
            odpalenia.Add(odpaleniaUs);
            styl.Add(stylUs);
            doba.Add(dobaUs);
            kolonisci.Add(kolonisciUs);
        }

        /// <summary>Wywolanie compa: luki i fakty przy kazdym interwale (przed bramka tempa).</summary>
        public void RecordInterval(double us)
        {
            interwaly.Add(us);
        }

        public void RecordDecision(double ms)
        {
            decyzje.Add(ms);
        }

        public void RecordConfirmation(double us)
        {
            potwierdzenia.Add(us);
        }

        /// <summary>Od nowa: zegar doby i wszystkie akumulatory (start gry ewaluacyjnej - pierwsza doba niepelna).</summary>
        public void Reset()
        {
            zegar.Reset();
            tik.Reset();
            odpalenia.Reset();
            styl.Reset();
            doba.Reset();
            kolonisci.Reset();
            interwaly.Reset();
            decyzje.Reset();
            potwierdzenia.Reset();
        }
    }
}
