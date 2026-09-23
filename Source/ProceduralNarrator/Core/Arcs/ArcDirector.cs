using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Arcs
{
    /// <summary>
    /// Silnik automatow lukow (koncepcja 5.4) - czysty, deterministyczny, bez losowosci.
    ///
    /// Trzy wejscia, kazde z innego momentu cyklu gry:
    ///   Reconcile  - po wczytaniu zapisu: instancje nieznanego luku albo fazy (zmiana Defow)
    ///                sa odrzucane z sladem, a nie cicho przepisywane na inna faze;
    ///   Observe    - przy KAZDYM wywolaniu compa (co 1000 tickow): straznicy (reakcja na gracza)
    ///                i limity czasu. Nie zna intencji - czekanie na zgodna intencje NIE zatrzymuje
    ///                zegara fazy (decyzja autora nr 2), i to wynika z konstrukcji, nie z flagi;
    ///   OnExecuted - po POTWIERDZONYM wykonaniu zdarzenia (decyzja autora nr 6): przesuniecia,
    ///                nastepniki, otwarcia przez rozpoznanie (decyzja autora nr 3).
    ///
    /// KOLEJNOSC W OnExecuted JEST CZESCIA SEMANTYKI: najpierw przesuniecie juz otwartych lukow
    /// (najwyzej jeden krok na luk na zdarzenie), potem nastepniki zamknietych, na koncu otwarcia.
    /// Dzieki temu zdarzenie, ktore otwiera luk, nie przesuwa go od razu dalej (zasiew spelnia sie
    /// otwarciem), a jedno zdarzenie moze jednoczesnie przesunac luk A i otworzyc luk B - to jest
    /// splatanie przez wspolny stan z decyzji autora nr 4 (np. napad Scigonych otwiera Wendete).
    /// </summary>
    public sealed class ArcDirector
    {
        public const string OutcomeResolved = "rozwiazany";
        public const string OutcomeFaded = "wygaszony";
        public const string ReasonExecution = "wykonanie";
        public const string ReasonTimeout = "limitCzasu";
        public const string ReasonSuccessor = "nastepca";
        public const string ReasonMissingDef = "brakDefa";
        public const string ReasonGuardPrefix = "straznik:";

        private readonly ArcCatalog catalog;
        private readonly ArcParams prms;

        public ArcDirector(ArcCatalog catalog, ArcParams parameters)
        {
            this.catalog = catalog ?? ArcCatalog.Empty();
            prms = (parameters ?? ArcParams.Default()).Clone();
            prms.Sanitize();
        }

        public ArcCatalog Catalog
        {
            get { return catalog; }
        }

        public ArcParams Params
        {
            get { return prms; }
        }

        // ---------------------------------------------------------------------------------
        //  Reconcile
        // ---------------------------------------------------------------------------------

        public List<ArcTransitionRecord> Reconcile(ArcLedger ledger, int tick, float gameDay)
        {
            var wynik = new List<ArcTransitionRecord>();
            if (ledger == null)
            {
                return wynik;
            }
            foreach (ArcInstance inst in ledger.Active.ToList())
            {
                ArcDefinition def = catalog.ById(inst.ArcId);
                int idx = def == null ? -1 : def.IndexOf(inst.PhaseId);
                // idx 0 to Zasiew - nigdy nie jest faza oczekujaca (spelnia sie otwarciem).
                if (idx <= 0)
                {
                    ledger.Active.Remove(inst);
                    wynik.Add(new ArcTransitionRecord
                    {
                        Kind = ArcEventKind.Drop,
                        ArcId = inst.ArcId,
                        Number = inst.Number,
                        From = inst.PhaseId,
                        Reason = ReasonMissingDef,
                        FactionId = inst.BoundFactionId,
                        Tick = tick,
                        GameDay = gameDay
                    });
                }
            }
            return wynik;
        }

        // ---------------------------------------------------------------------------------
        //  Observe - straznicy i limity czasu
        // ---------------------------------------------------------------------------------

        public List<ArcTransitionRecord> Observe(ArcLedger ledger, ArcObservation now)
        {
            var wynik = new List<ArcTransitionRecord>();
            if (!prms.enabled || ledger == null || now == null)
            {
                return wynik;
            }
            foreach (ArcInstance inst in ledger.Active.OrderBy(a => a.Number).ToList())
            {
                ArcDefinition def = catalog.ById(inst.ArcId);
                int idx = def == null ? -1 : def.IndexOf(inst.PhaseId);
                if (idx <= 0)
                {
                    continue;
                }
                ArcPhase faza = def.phases[idx];

                if ((int)now.Danger > (int)inst.PeakDanger)
                {
                    inst.PeakDanger = now.Danger;
                }

                // 1. Przejscia calego luku (np. frakcja przestala byc wroga) - przed przejsciami fazy,
                //    bo zamykaja watek niezaleznie od tego, na co czeka.
                ArcTransition t = FirstHolding(def.transitions, now, inst);
                if (t != null)
                {
                    Close(ledger, inst, t.close, Reason(t), t.message, t.messageType, now, wynik);
                    continue;
                }

                // 2. Przejscia fazy (reakcja na gracza).
                t = FirstHolding(faza.transitions, now, inst);
                if (t != null)
                {
                    if (t.Closes)
                    {
                        Close(ledger, inst, t.close, Reason(t), t.message, t.messageType, now, wynik);
                    }
                    else
                    {
                        Move(inst, def, def.IndexOf(t.target), now, Reason(t), t.message, t.messageType, wynik);
                    }
                    continue;
                }

                // 3. Limit czasu (decyzja autora nr 12). Luk nigdy nie wymusza zdarzenia i zawsze
                //    sie konczy: Eskalacja/Kulminacja -> Rozwiazanie, Rozwiazanie -> "wygaszony".
                if (now.GameDay - inst.PhaseEnteredDay > faza.maxDays)
                {
                    if (faza.kind == ArcPhaseKind.Resolution)
                    {
                        Close(ledger, inst, OutcomeFaded, ReasonTimeout, null, null, now, wynik);
                    }
                    else
                    {
                        Move(inst, def, def.phases.Count - 1, now, ReasonTimeout, null, null, wynik);
                    }
                }
            }
            return wynik;
        }

        // ---------------------------------------------------------------------------------
        //  OnExecuted - przesuniecia, nastepniki, otwarcia
        // ---------------------------------------------------------------------------------

        /// <param name="snapshot">Stan swiata tej tury - do warunkow startu. null = zadnych otwarc
        /// (warunkow nie da sie sprawdzic, a otwarcie bez nich byloby cichym obejsciem danych).</param>
        public List<ArcTransitionRecord> OnExecuted(ArcLedger ledger, ArcEventView executed, ExecStatus status,
                                                    WorldSnapshot snapshot, ArcObservation now)
        {
            var wynik = new List<ArcTransitionRecord>();
            if (!prms.enabled || ledger == null || executed == null || now == null)
            {
                return wynik;
            }
            if (!ExecutionConfirmation.CountsAsExecuted(status))
            {
                return wynik;
            }

            // 1. Przesuniecie otwartych lukow - co najwyzej jeden krok na luk.
            var rozwiazane = new List<ArcDefinition>();
            foreach (ArcInstance inst in ledger.Active.OrderBy(a => a.Number).ToList())
            {
                ArcDefinition def = catalog.ById(inst.ArcId);
                int idx = def == null ? -1 : def.IndexOf(inst.PhaseId);
                if (idx <= 0)
                {
                    continue;
                }
                ArcPhase faza = def.phases[idx];
                if (!IsRipe(inst, faza, now.GameDay) || !Expects(faza, executed, inst.BoundFactionId))
                {
                    continue;
                }
                if (faza.bindsFaction && string.IsNullOrEmpty(inst.BoundFactionId) && !string.IsNullOrEmpty(executed.FactionId))
                {
                    inst.BoundFactionId = executed.FactionId;
                }
                if (idx == def.phases.Count - 1)
                {
                    Close(ledger, inst, OutcomeResolved, ReasonExecution, faza.message, faza.messageType, now, wynik);
                    rozwiazane.Add(def);
                }
                else
                {
                    Move(inst, def, idx + 1, now, ReasonExecution, faza.message, faza.messageType, wynik);
                }
            }

            // 2. Nastepniki lukow rozwiazanych w tym zdarzeniu.
            foreach (ArcDefinition def in rozwiazane)
            {
                ArcDefinition nastepca = catalog.ById(def.successor);
                if (nastepca != null)
                {
                    TryOpen(ledger, nastepca, null, snapshot, now, ReasonSuccessor, wynik);
                }
            }

            // 3. Otwarcia przez rozpoznanie zasiewu - w kolejnosci kanonicznej katalogu.
            foreach (ArcDefinition def in catalog.Arcs)
            {
                TryOpen(ledger, def, executed, snapshot, now, ReasonExecution, wynik);
            }
            return wynik;
        }

        // ---------------------------------------------------------------------------------
        //  BuildFocus - wyjscie lukow dla warstwy decyzyjnej (faza + oczekiwany typ)
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Fokus tury: dla kazdego otwartego luku jego biezaca faza i status (aktywna / czeka /
        /// niedojrzala). Nie zmienia ksiegi - w szczegolnosci zegarow faz: czekanie na zgodna
        /// intencje nie zatrzymuje limitu czasu (decyzja autora nr 2).
        /// </summary>
        public ArcFocus BuildFocus(ArcLedger ledger, Intent intent, float gameDay, IFactionUsability usability)
        {
            var wpisy = new List<ArcFocus.Entry>();
            if (prms.enabled && ledger != null)
            {
                foreach (ArcInstance inst in ledger.Active.OrderBy(a => a.Number))
                {
                    ArcDefinition def = catalog.ById(inst.ArcId);
                    int idx = def == null ? -1 : def.IndexOf(inst.PhaseId);
                    if (idx <= 0)
                    {
                        continue;
                    }
                    ArcPhase faza = def.phases[idx];
                    bool dojrzala = IsRipe(inst, faza, gameDay);
                    // Faza steruje, gdy KTORAKOLWIEK walencja, na ktora czeka, jest zgodna z intencja.
                    // Kandydat dostaje premie dopiero przy zgodnosci SWOJEJ walencji (ArcFocus.ValueFor).
                    bool zgodna = faza.expectations.Any(e => e != null && e.valences != null
                                                             && e.valences.Any(v => ArcIntentRules.Compatible(v, intent)));
                    wpisy.Add(new ArcFocus.Entry
                    {
                        Arc = inst,
                        Def = def,
                        Phase = faza,
                        Ripe = dojrzala,
                        Active = dojrzala && zgodna,
                        Status = !dojrzala ? ArcFocus.StatusUnripe : (zgodna ? ArcFocus.StatusActive : ArcFocus.StatusWaiting)
                    });
                }
            }
            return new ArcFocus(intent, wpisy, usability);
        }

        /// <summary>Czy wykonane zdarzenie pasuje do oczekiwanego typu fazy (ktorejkolwiek alternatywy).</summary>
        public static bool Expects(ArcPhase phase, ArcEventView e, string boundFactionId)
        {
            if (phase == null || phase.expectations == null)
            {
                return false;
            }
            string why;
            for (int i = 0; i < phase.expectations.Count; i++)
            {
                ArcExpectation x = phase.expectations[i];
                if (x != null && x.Matches(e, boundFactionId, out why))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Faza "dojrzala": minelo co najmniej minDaysAfterPrevious od wejscia w nia.</summary>
        public static bool IsRipe(ArcInstance inst, ArcPhase phase, float gameDay)
        {
            return gameDay - inst.PhaseEnteredDay >= phase.minDaysAfterPrevious;
        }

        private bool TryOpen(ArcLedger ledger, ArcDefinition def, ArcEventView executed, WorldSnapshot snapshot,
                             ArcObservation now, string reason, List<ArcTransitionRecord> wynik)
        {
            if (def == null || def.phases == null || def.phases.Count < 2 || ledger.Find(def.defName) != null)
            {
                return false;
            }
            if (ledger.Active.Count >= prms.maxConcurrent)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(def.exclusionGroup) && ledger.Active.Any(a =>
                {
                    ArcDefinition d = catalog.ById(a.ArcId);
                    return d != null && string.Equals(d.exclusionGroup, def.exclusionGroup, StringComparison.Ordinal);
                }))
            {
                return false;
            }
            float zamkniety;
            if (ledger.LastCloseDay.TryGetValue(def.defName, out zamkniety) && now.GameDay - zamkniety < def.cooldownDays)
            {
                return false;
            }
            string niespelniony;
            if (snapshot == null || !def.StartConditionsMet(snapshot, out niespelniony))
            {
                return false;
            }

            ArcPhase zasiew = def.Seed;
            string frakcja = null;
            if (executed != null)
            {
                if (!Expects(zasiew, executed, null))
                {
                    return false;
                }
                if (zasiew.bindsFaction)
                {
                    // Luk wiazacy frakcje bez znanej frakcji wykonanego zdarzenia bylby watkiem
                    // "zemsty" bez sprawcy - komunikat z {FRAKCJA} nie mialby prawdziwej tresci.
                    if (string.IsNullOrEmpty(executed.FactionId))
                    {
                        return false;
                    }
                    frakcja = executed.FactionId;
                }
            }
            else if (zasiew.bindsFaction)
            {
                return false;
            }

            var inst = new ArcInstance
            {
                ArcId = def.defName,
                Number = ledger.NextNumber++,
                OpenedDay = now.GameDay,
                BoundFactionId = frakcja
            };
            Enter(inst, def.phases[1], now);
            ledger.Active.Add(inst);
            wynik.Add(new ArcTransitionRecord
            {
                Kind = ArcEventKind.Open,
                ArcId = inst.ArcId,
                Number = inst.Number,
                From = zasiew.id,
                To = inst.PhaseId,
                Reason = reason,
                FactionId = frakcja,
                Message = executed != null ? zasiew.message : null,
                MessageType = executed != null ? zasiew.messageType : null,
                Tick = now.Tick,
                GameDay = now.GameDay
            });
            return true;
        }

        private static void Enter(ArcInstance inst, ArcPhase phase, ArcObservation now)
        {
            inst.PhaseId = phase.id;
            inst.PhaseEnteredDay = now.GameDay;
            inst.PhaseEnteredTick = now.Tick;
            inst.BaseColonistLosses = now.ColonistLosses;
            inst.BaseKidnapped = now.KidnappedCount;
            inst.PeakDanger = now.Danger;
        }

        private static void Move(ArcInstance inst, ArcDefinition def, int newIndex, ArcObservation now, string reason,
                                 string message, string messageType, List<ArcTransitionRecord> wynik)
        {
            string z = inst.PhaseId;
            Enter(inst, def.phases[newIndex], now);
            wynik.Add(new ArcTransitionRecord
            {
                Kind = ArcEventKind.Advance,
                ArcId = inst.ArcId,
                Number = inst.Number,
                From = z,
                To = inst.PhaseId,
                Reason = reason,
                FactionId = inst.BoundFactionId,
                Message = message,
                MessageType = messageType,
                Tick = now.Tick,
                GameDay = now.GameDay
            });
        }

        private static void Close(ArcLedger ledger, ArcInstance inst, string outcome, string reason, string message,
                                  string messageType, ArcObservation now, List<ArcTransitionRecord> wynik)
        {
            ledger.Active.Remove(inst);
            ledger.LastCloseDay[inst.ArcId] = now.GameDay;
            // Slad po watku zamknietym - LastCloseDay niesie tylko dzien (odstep przed ponownym
            // otwarciem), a model danych pracy wymaga takze statusu i fazy koncowej.
            ledger.RecordClosure(inst, outcome, now.GameDay);
            wynik.Add(new ArcTransitionRecord
            {
                Kind = ArcEventKind.Close,
                ArcId = inst.ArcId,
                Number = inst.Number,
                From = inst.PhaseId,
                Reason = reason,
                Outcome = outcome,
                FactionId = inst.BoundFactionId,
                Message = message,
                MessageType = messageType,
                Tick = now.Tick,
                GameDay = now.GameDay
            });
        }

        private static ArcTransition FirstHolding(List<ArcTransition> transitions, ArcObservation now, ArcInstance inst)
        {
            if (transitions == null)
            {
                return null;
            }
            foreach (ArcTransition t in transitions)
            {
                if (t == null || t.guards == null || t.guards.Count == 0)
                {
                    continue;
                }
                bool wszystkie = true;
                foreach (ArcGuard g in t.guards)
                {
                    if (g == null || !g.Holds(now, inst))
                    {
                        wszystkie = false;
                        break;
                    }
                }
                if (wszystkie)
                {
                    return t;
                }
            }
            return null;
        }

        private static string Reason(ArcTransition t)
        {
            return ReasonGuardPrefix + t.DescribeGuards();
        }
    }
}
