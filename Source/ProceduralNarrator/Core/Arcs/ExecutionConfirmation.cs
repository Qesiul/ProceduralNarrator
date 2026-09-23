using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Arcs
{
    /// <summary>
    /// Wynik potwierdzenia, czy zdarzenie wybrane przez narratora NAPRAWDE sie wykonalo
    /// (decyzja autora nr 6: faza luku rusza tylko po potwierdzonym wykonaniu).
    ///
    /// Zrodlo prawdy w grze: StoryState.lastFireTicks[def] - wanilia wpisuje tam tick WYLACZNIE
    /// po udanym CanFireNow i TryExecute (Storyteller.TryFire -> Notify_IncidentFired). Nasz kod
    /// po `yield return` wykonuje sie PO TryFire w tym samym ticku (leniwy lancuch iteratorow),
    /// wiec odczyt "przed" i "po" rozstrzyga wykonanie bez Harmony.
    /// </summary>
    public enum ExecStatus
    {
        /// <summary>Po TryFire tick wykonania = biezacy, a przed nim nie byl.</summary>
        Executed,
        /// <summary>Po TryFire tick sie nie zmienil - silnik odmowil albo TryExecute zwrocil false.</summary>
        NotExecuted,
        /// <summary>Ten sam incydent odpalil juz w tym ticku ktos inny - nie da sie rozstrzygnac.</summary>
        Ambiguous,
        /// <summary>Symulator: swiat zamrozony, TryExecute nie istnieje - wykonanie = decyzja, jawnie oznaczone.</summary>
        Simulated,
        /// <summary>Potwierdzenie pozne (iterator nie zostal wznowiony): tick wykonania = tick decyzji.</summary>
        LateExecuted,
        /// <summary>Potwierdzenie pozne: brak sladu wykonania.</summary>
        LateNotExecuted
    }

    public static class ExecutionConfirmation
    {
        /// <param name="lastFireBefore">lastFireTicks[def] przed yield (-1 = nigdy nie odpalal).</param>
        /// <param name="lastFireAfter">lastFireTicks[def] po wznowieniu iteratora.</param>
        public static ExecStatus Classify(int lastFireBefore, int lastFireAfter, int tick, bool simulated)
        {
            if (simulated)
            {
                return ExecStatus.Simulated;
            }
            if (lastFireBefore == tick)
            {
                return ExecStatus.Ambiguous;
            }
            return lastFireAfter == tick ? ExecStatus.Executed : ExecStatus.NotExecuted;
        }

        /// <summary>
        /// Potwierdzenie PO FAKCIE, gdy iterator nie wrocil (np. wyjatek w TryFire). Tick pozniejszy
        /// niz tick decyzji znaczy, ze ten sam incydent odpalil ponownie i nadpisal slad - wtedy
        /// "niejednoznaczne", nie "niewykonane".
        /// </summary>
        public static ExecStatus ClassifyLate(int lastFireNow, PendingExecution p)
        {
            if (p == null)
            {
                return ExecStatus.Ambiguous;
            }
            return ClassifyLate(lastFireNow, p.LastFireBefore, p.Tick);
        }

        /// <summary>
        /// Ta sama klasyfikacja na golych liczbach - uzywaja jej takze oczekujace FAKTY (krok 6),
        /// ktore zyja niezaleznie od warstwy lukow. Jedna implementacja dla obu, zeby luk i fakt
        /// tego samego zdarzenia nie mogly dostac dwoch roznych werdyktow.
        /// </summary>
        public static ExecStatus ClassifyLate(int lastFireNow, int lastFireBefore, int decisionTick)
        {
            if (lastFireBefore == decisionTick)
            {
                return ExecStatus.Ambiguous;
            }
            if (lastFireNow == decisionTick)
            {
                return ExecStatus.LateExecuted;
            }
            return lastFireNow > decisionTick ? ExecStatus.Ambiguous : ExecStatus.LateNotExecuted;
        }

        public static bool CountsAsExecuted(ExecStatus s)
        {
            return s == ExecStatus.Executed || s == ExecStatus.Simulated || s == ExecStatus.LateExecuted;
        }

        /// <summary>Etykieta do [PN-EXEC] (ASCII, bez ';').</summary>
        public static string Label(ExecStatus s)
        {
            switch (s)
            {
                case ExecStatus.Executed: return "wykonane";
                case ExecStatus.NotExecuted: return "niewykonane";
                case ExecStatus.Ambiguous: return "niejednoznaczne";
                case ExecStatus.Simulated: return "symulacja";
                case ExecStatus.LateExecuted: return "pozno-wykonane";
                default: return "pozno-niewykonane";
            }
        }
    }

    /// <summary>
    /// Zdarzenie wyemitowane przez narratora i czekajace na potwierdzenie wykonania. Zapisywane
    /// w pamieci gry (linia "P|..."), zeby potwierdzenie dalo sie domknac takze wtedy, gdy
    /// iterator nie zostal wznowiony - a zapis nastapil miedzy decyzja a nastepnym wywolaniem.
    /// </summary>
    public sealed class PendingExecution
    {
        public const string LineTag = "P";
        public const int FieldCount = 18;

        /// <summary>
        /// Dlugosc linii sprzed S6 kroku 6 (bez pamieci decyzji). Nadal PRZYJMOWANA przy odczycie -
        /// zapis z trwajaca kolejka nie traci jej po aktualizacji moda; brakuje wtedy tylko pamieci
        /// decyzji (HasDecisionMemory == false) i sciezka spozniona buduje snapshot jak dawniej.
        /// </summary>
        public const int LegacyFieldCount = 15;

        public int Tick;
        public float GameDay;
        public int DecisionIndex;

        /// <summary>defName incydentu (klucz lastFireTicks).</summary>
        public string IncidentDefName;

        /// <summary>Widok zdarzenia; FactionId = frakcja USTAWIONA przez nas w parms (albo null).</summary>
        public ArcEventView Event = new ArcEventView();

        public int LastFireBefore = -1;

        /// <summary>
        /// PAMIEC Z CHWILI DECYZJI (krok 6, S6 - przeglad adwersarialny). Sciezka spozniona domyka
        /// wykonanie w NASTEPNYM wywolaniu compa, gdy historia zawiera juz to zdarzenie (RecordEvent
        /// idzie przed yield). Snapshot zbudowany wtedy od nowa mialby inny wiek tematow i inne
        /// "dni od ostatniego zdarzenia" niz snapshot decyzji - a warunki startu luku maja widziec
        /// swiat z poczatku tury NIEZALEZNIE od sciezki. Filtrowanie historii po DecisionIndex tego
        /// nie odtwarza: przy pelnym buforze RecordEvent wypycha najstarszy wpis, wiec pola sa
        /// zapamietywane tutaj, a nie odtwarzane.
        /// </summary>
        public bool HasDecisionMemory;
        public string DecisionTurnsSinceThemes;
        public float DecisionDaysSinceLastEvent;
        public int DecisionDaysPassed;

        /// <summary>Zapamietuje pola historii ze snapshotu decyzji (ten sam obiekt, ktory widza warunki).</summary>
        public void CaptureDecisionMemory(WorldSnapshot decyzji)
        {
            if (decyzji == null)
            {
                HasDecisionMemory = false;
                return;
            }
            HasDecisionMemory = true;
            DecisionTurnsSinceThemes = decyzji.TurnsSinceThemes ?? string.Empty;
            DecisionDaysSinceLastEvent = decyzji.DaysSinceLastEvent;
            DecisionDaysPassed = decyzji.DaysPassed;
        }

        /// <summary>
        /// Naklada zapamietane pola na snapshot sciezki spoznionej. Zwraca false (snapshot bez zmian),
        /// gdy linia pochodzi sprzed S6 i pamieci nie ma.
        /// </summary>
        public bool ApplyDecisionMemoryTo(WorldSnapshot spozniony)
        {
            if (!HasDecisionMemory || spozniony == null)
            {
                return false;
            }
            spozniony.TurnsSinceThemes = DecisionTurnsSinceThemes ?? string.Empty;
            spozniony.DaysSinceLastEvent = DecisionDaysSinceLastEvent;
            spozniony.DaysPassed = DecisionDaysPassed;
            return true;
        }

        public string Encode()
        {
            ArcEventView e = Event ?? new ArcEventView();
            return string.Join(ArcInstance.FieldSeparator.ToString(), new[]
            {
                LineTag,
                Tick.ToString(CultureInfo.InvariantCulture),
                GameDay.ToString("G9", CultureInfo.InvariantCulture),
                DecisionIndex.ToString(CultureInfo.InvariantCulture),
                ArcInstance.Escape(IncidentDefName),
                ArcInstance.Escape(e.ActionBlockId),
                ArcInstance.Escape(e.Payload),
                e.Theme.ToString(),
                e.Valence.ToString(),
                e.Scale.ToString(),
                e.Intensity.ToString(),
                // Tagi rozdzielone przecinkiem, posortowane - kodowanie deterministyczne.
                ArcInstance.Escape(string.Join(",", (e.Tags ?? new HashSet<string>()).OrderBy(t => t, System.StringComparer.Ordinal).ToArray())),
                e.CarriesFaction ? "1" : "0",
                ArcInstance.Escape(e.FactionId),
                LastFireBefore.ToString(CultureInfo.InvariantCulture),
                // Pamiec decyzji: "-" we WSZYSTKICH trzech polach = brak pamieci (HasDecisionMemory false).
                HasDecisionMemory ? ArcInstance.Escape(DecisionTurnsSinceThemes) : ArcInstance.EmptyToken,
                HasDecisionMemory ? DecisionDaysSinceLastEvent.ToString("G9", CultureInfo.InvariantCulture) : ArcInstance.EmptyToken,
                HasDecisionMemory ? DecisionDaysPassed.ToString(CultureInfo.InvariantCulture) : ArcInstance.EmptyToken
            });
        }

        public static bool TryDecode(string line, out PendingExecution p)
        {
            p = null;
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }
            string[] f = line.Split(ArcInstance.FieldSeparator);
            if ((f.Length != FieldCount && f.Length != LegacyFieldCount) || f[0] != LineTag)
            {
                return false;
            }
            int tick, dec, before;
            float day;
            Theme theme;
            Valence val;
            EventScale scale;
            IntensityLevel moc;
            if (!ArcInstance.Int(f[1], out tick) || !ArcInstance.Flt(f[2], out day) || !ArcInstance.Int(f[3], out dec)
                || !ArcInstance.EnumName(f[7], out theme) || !ArcInstance.EnumName(f[8], out val)
                || !ArcInstance.EnumName(f[9], out scale) || !ArcInstance.EnumName(f[10], out moc)
                || (f[12] != "0" && f[12] != "1") || !ArcInstance.Int(f[14], out before))
            {
                return false;
            }
            bool pamiec = false;
            float dniOdZdarzenia = 0f;
            int dniGry = 0;
            if (f.Length == FieldCount && !(f[16] == ArcInstance.EmptyToken && f[17] == ArcInstance.EmptyToken))
            {
                // Pamiec zapisana: oba pola liczbowe MUSZA sie odczytac. Polowiczna pamiec to
                // uszkodzona linia, a nie "brak pamieci" - odrzucamy cala, jak kazde zle pole.
                if (!ArcInstance.Flt(f[16], out dniOdZdarzenia) || !ArcInstance.Int(f[17], out dniGry))
                {
                    return false;
                }
                pamiec = true;
            }
            var e = new ArcEventView
            {
                ActionBlockId = ArcInstance.Unescape(f[5]),
                Payload = ArcInstance.Unescape(f[6]),
                Theme = theme,
                Valence = val,
                Scale = scale,
                Intensity = moc,
                CarriesFaction = f[12] == "1",
                FactionId = ArcInstance.Unescape(f[13])
            };
            string tagi = ArcInstance.Unescape(f[11]);
            if (tagi != null)
            {
                foreach (string t in tagi.Split(','))
                {
                    if (t.Length > 0)
                    {
                        e.Tags.Add(t);
                    }
                }
            }
            p = new PendingExecution
            {
                Tick = tick,
                GameDay = day,
                DecisionIndex = dec,
                IncidentDefName = ArcInstance.Unescape(f[4]),
                Event = e,
                LastFireBefore = before,
                HasDecisionMemory = pamiec,
                DecisionTurnsSinceThemes = pamiec ? (ArcInstance.Unescape(f[15]) ?? string.Empty) : null,
                DecisionDaysSinceLastEvent = dniOdZdarzenia,
                DecisionDaysPassed = dniGry
            };
            return true;
        }
    }
}
