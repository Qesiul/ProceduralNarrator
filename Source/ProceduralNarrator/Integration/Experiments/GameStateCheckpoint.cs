using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using ProceduralNarrator.Integration.Persistence;
using ProceduralNarrator.Integration.Storyteller;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration.Experiments
{
    /// <summary>
    /// Zrzut stanu gry Z LISTY PONIZEJ, ktory nasz narrator dotyka podczas symulacji przyszlych
    /// interwalow - i jego przywrocenie. Lista jest wyprowadzona z dekompilacji sciezki
    /// Storyteller.MakeIncidentsForInterval -&gt; nasz comp -&gt; akceptor -&gt; CanFireNow, a nie
    /// zgadnieta:
    ///
    ///   zegar gry            TickManager.ticksGameInt (DebugSetTicksGame)
    ///   losowosc gry         Verse.Rand - zewnetrzne PushState na caly eksperyment
    ///   pamiec narratora     NarratorMemoryComponent - podmiana na glebokie kopie
    ///   StoryState celow     lastFireTicks (FiredTooRecently), lastThreatBigTick i reszta - KOPIA
    ///                        obiektu. Waniliowe DebugGetFutureIncidents trzyma tu REFERENCJE
    ///                        i "przywraca" stan kopiujac obiekt sam na siebie, czyli go traci.
    ///   licznik napadow      StoryWatcher.statsRecord.numThreatBigs (Notify_IncidentFired)
    ///   cache workerow       IncidentWorker.lastCheckCanRunTick/lastCanRunResult - bez przywrocenia
    ///                        prawdziwa gra dostalaby w tickach symulacji werdykty z symulacji
    ///   cache questow        QuestScriptDef.lastCheckCanRunTick/lastCanRunResult - ta sama klasa
    ///                        cache'u na sciezce CanFireNowSub dla WandererJoin i RefugeePodCrash
    ///                        (IncidentWorker_GiveQuest -&gt; questScriptDef.CanRun); dzis neutralny,
    ///                        bo TestRunInt ich rootow zwraca true, ale obsluzony jak workery
    ///   obserwatory map      WealthWatcher (lastCountTick w przyszlosci zamrozilby bogactwo
    ///                        w prawdziwej grze na dlugosc symulacji) i DangerWatcher
    ///   stan compa           rejestr werdyktow na tick i straznik zegara
    ///
    /// CZEGO ZRZUT NIE OBEJMUJE - swiadomie, zapisane tez w CLAUDE.md: stanu innych modow
    /// (patche na CanFireNow i pokrewne), cache'y wanilii osiagalnych przechodnio z 13 sciezek
    /// CanFireNowSub (nie przeszlismy ich wyczerpujaco), UnityEngine.Random, linii wypisanych do
    /// Verse.Log przez kod wanilii. Kolejki IncidentQueue nie dotyka - nasz comp do niej nie pisze.
    ///
    /// ODPORNOSC: wszystkie FieldInfo sa rozwiazywane PRZED jakakolwiek mutacja - brak ktoregos
    /// konczy Capture bledem, zanim cokolwiek zostanie zmienione. RestoreAll jest idempotentne,
    /// a kazdy krok ma wlasny try/catch, zeby awaria jednego nie pominela pozostalych.
    /// </summary>
    internal sealed class GameStateCheckpoint
    {
        private static readonly BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private StorytellerComp_Generative comp;
        private NarratorMemoryComponent memory;
        private NarratorMemoryComponent.MemoryCheckpoint memoryCheckpoint;

        private int ticks0;
        private int numThreatBigs0;
        private readonly List<KeyValuePair<IIncidentTarget, StoryState>> storyStates =
            new List<KeyValuePair<IIncidentTarget, StoryState>>();
        private readonly List<FieldSnapshot> watchers = new List<FieldSnapshot>();
        private readonly Dictionary<IncidentDef, KeyValuePair<int, bool>> workerCaches =
            new Dictionary<IncidentDef, KeyValuePair<int, bool>>();

        private FieldInfo fWorkerInt;
        private FieldInfo fLastCheckTick;
        private FieldInfo fLastCanRun;
        private FieldInfo fQuestCheckTick;
        private FieldInfo fQuestCanRun;
        private readonly Dictionary<QuestScriptDef, KeyValuePair<int, bool>> questCaches =
            new Dictionary<QuestScriptDef, KeyValuePair<int, bool>>();

        private bool randPushed;
        private bool restored;

        public int Ticks0
        {
            get { return ticks0; }
        }

        public NarratorMemoryComponent.MemoryCheckpoint Memory
        {
            get { return memoryCheckpoint; }
        }

        /// <summary>Zrzut. Zwraca null (z bledem w logu), gdy nie da sie go wykonac bezpiecznie.</summary>
        public static GameStateCheckpoint Capture(StorytellerComp_Generative comp, NarratorMemoryComponent memory,
                                                  List<IIncidentTarget> targets, out string problem)
        {
            problem = null;
            var cp = new GameStateCheckpoint { comp = comp, memory = memory };

            // ---- faza 1: rozwiazanie refleksji, ZERO mutacji ----
            cp.fWorkerInt = typeof(IncidentDef).GetField("workerInt", Inst);
            cp.fLastCheckTick = typeof(IncidentWorker).GetField("lastCheckCanRunTick", Inst);
            cp.fLastCanRun = typeof(IncidentWorker).GetField("lastCanRunResult", Inst);
            if (cp.fWorkerInt == null || cp.fLastCheckTick == null || cp.fLastCanRun == null)
            {
                problem = "nie znaleziono pol cache'u workerow (IncidentDef.workerInt / "
                          + "IncidentWorker.lastCheckCanRunTick / lastCanRunResult) - inna wersja gry?";
                return null;
            }
            cp.fQuestCheckTick = typeof(QuestScriptDef).GetField("lastCheckCanRunTick", Inst);
            cp.fQuestCanRun = typeof(QuestScriptDef).GetField("lastCanRunResult", Inst);
            if (cp.fQuestCheckTick == null || cp.fQuestCanRun == null)
            {
                problem = "nie znaleziono pol cache'u QuestScriptDef (lastCheckCanRunTick / "
                          + "lastCanRunResult) - inna wersja gry?";
                return null;
            }

            var mapy = Find.Maps;
            for (int i = 0; mapy != null && i < mapy.Count; i++)
            {
                if (mapy[i].wealthWatcher != null)
                {
                    cp.watchers.Add(FieldSnapshot.Of(mapy[i].wealthWatcher));
                }
                if (mapy[i].dangerWatcher != null)
                {
                    cp.watchers.Add(FieldSnapshot.Of(mapy[i].dangerWatcher));
                }
            }

            // ---- faza 2: zrzut (nadal bez mutacji stanu gry) ----
            cp.ticks0 = Find.TickManager.TicksGame;
            cp.numThreatBigs0 = Find.StoryWatcher.statsRecord.numThreatBigs;

            for (int i = 0; i < targets.Count; i++)
            {
                IIncidentTarget t = targets[i];
                if (t == null || t.StoryState == null)
                {
                    continue;
                }
                var kopia = new StoryState(t);
                t.StoryState.CopyTo(kopia);
                cp.storyStates.Add(new KeyValuePair<IIncidentTarget, StoryState>(t, kopia));
            }

            List<IncidentDef> defs = DefDatabase<IncidentDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                IncidentWorker w = cp.fWorkerInt.GetValue(defs[i]) as IncidentWorker;
                if (w != null)
                {
                    cp.workerCaches[defs[i]] = new KeyValuePair<int, bool>(
                        (int)cp.fLastCheckTick.GetValue(w), (bool)cp.fLastCanRun.GetValue(w));
                }
            }

            List<QuestScriptDef> skrypty = DefDatabase<QuestScriptDef>.AllDefsListForReading;
            for (int i = 0; i < skrypty.Count; i++)
            {
                cp.questCaches[skrypty[i]] = new KeyValuePair<int, bool>(
                    (int)cp.fQuestCheckTick.GetValue(skrypty[i]), (bool)cp.fQuestCanRun.GetValue(skrypty[i]));
            }

            // ---- faza 3: pierwsze mutacje - pamiec i losowosc ----
            cp.memoryCheckpoint = memory.BeginExperiment();
            Rand.PushState();
            cp.randPushed = true;
            return cp;
        }

        /// <summary>
        /// Identyczne wejscie dla kazdego ramienia: ta sama pamiec (glebsza kopia), ten sam
        /// StoryState, ten sam licznik napadow, te same obserwatory, UNIEWAZNIONY cache workerow
        /// i czysty rejestr werdyktow. Jedyna zmienna miedzy ramionami jest profil.
        ///
        /// Cache workerow jest UNIEWAZNIANY, a nie przywracany: bez tego pierwszy interwal ramienia
        /// mogl dostac werdykt policzony przez prawdziwa ture (albo przez poprzednie ramie) dla
        /// innych parametrow - a wyczyszczony rejestr werdyktow by tego nie zauwazyl, bo to
        /// pierwsze pytanie w ticku. Ramie kontrolne tez by tego nie wykrylo: blad bylby spojny.
        /// </summary>
        public int ResetForArm(string profileId)
        {
            int odrzuconych = memory.InstallExperimentArm(memoryCheckpoint, profileId);
            comp.ResetTickState();
            RestoreStoryStates();
            Find.StoryWatcher.statsRecord.numThreatBigs = numThreatBigs0;
            for (int i = 0; i < watchers.Count; i++)
            {
                watchers[i].Restore();
            }
            InvalidateWorkerCaches();
            Find.TickManager.DebugSetTicksGame(ticks0);
            return odrzuconych;
        }

        /// <summary>Przywraca wszystko. Idempotentne; kazdy krok niezalezny od pozostalych.</summary>
        public List<string> RestoreAll()
        {
            var bledy = new List<string>();
            if (restored)
            {
                return bledy;
            }
            restored = true;

            Krok(bledy, "zegar", () => Find.TickManager.DebugSetTicksGame(ticks0));
            Krok(bledy, "pamiec", () => memory.EndExperiment(memoryCheckpoint));
            Krok(bledy, "stan compa", () => comp.ResetTickState());
            Krok(bledy, "StoryState", RestoreStoryStates);
            Krok(bledy, "numThreatBigs", () => Find.StoryWatcher.statsRecord.numThreatBigs = numThreatBigs0);
            Krok(bledy, "cache workerow", RestoreWorkerCaches);
            Krok(bledy, "cache questow", () =>
            {
                foreach (KeyValuePair<QuestScriptDef, KeyValuePair<int, bool>> q in questCaches)
                {
                    fQuestCheckTick.SetValue(q.Key, q.Value.Key);
                    fQuestCanRun.SetValue(q.Key, q.Value.Value);
                }
            });
            Krok(bledy, "opis profilu", () => comp.InvalidateProfileRuntime());
            Krok(bledy, "obserwatory map", () =>
            {
                for (int i = 0; i < watchers.Count; i++)
                {
                    watchers[i].Restore();
                }
            });

            // LOSOWOSC OSTATNIA i dokladnie raz - globalny generator gry wraca wtedy bit w bit.
            if (randPushed)
            {
                randPushed = false;
                Krok(bledy, "Rand", Rand.PopState);
            }
            return bledy;
        }

        private void RestoreStoryStates()
        {
            for (int i = 0; i < storyStates.Count; i++)
            {
                KeyValuePair<IIncidentTarget, StoryState> para = storyStates[i];
                if (para.Key != null && para.Key.StoryState != null)
                {
                    para.Value.CopyTo(para.Key.StoryState);
                }
            }
        }

        private void InvalidateWorkerCaches()
        {
            List<IncidentDef> defs = DefDatabase<IncidentDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                IncidentWorker w = fWorkerInt.GetValue(defs[i]) as IncidentWorker;
                if (w != null)
                {
                    fLastCheckTick.SetValue(w, -1);
                }
            }
            foreach (QuestScriptDef q in questCaches.Keys)
            {
                fQuestCheckTick.SetValue(q, -1);
            }
        }

        private void RestoreWorkerCaches()
        {
            List<IncidentDef> defs = DefDatabase<IncidentDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                IncidentWorker w = fWorkerInt.GetValue(defs[i]) as IncidentWorker;
                if (w == null)
                {
                    continue;
                }
                KeyValuePair<int, bool> zapisany;
                if (workerCaches.TryGetValue(defs[i], out zapisany))
                {
                    fLastCheckTick.SetValue(w, zapisany.Key);
                    fLastCanRun.SetValue(w, zapisany.Value);
                }
                else
                {
                    // Worker powstal W TRAKCIE eksperymentu - jego cache pochodzi z tickow symulacji.
                    fLastCheckTick.SetValue(w, -1);
                }
            }
        }

        private static void Krok(List<string> bledy, string nazwa, Action akcja)
        {
            try
            {
                akcja();
            }
            catch (Exception e)
            {
                bledy.Add(nazwa + ": " + e.Message);
            }
        }

        /// <summary>
        /// Zrzut wszystkich pol INSTANCYJNYCH typu wartosciowego (liczby, bool, enumy) obiektu
        /// i jego klas bazowych. Pola referencyjne (mapa, listy robocze) sa pomijane - nie
        /// zmieniaja sie przy odczycie. Uzywany dla WealthWatcher i DangerWatcher, ktore
        /// cache'uja wyniki po ticku w polach prywatnych.
        /// </summary>
        internal sealed class FieldSnapshot
        {
            private object target;
            private readonly List<KeyValuePair<FieldInfo, object>> values = new List<KeyValuePair<FieldInfo, object>>();

            public static FieldSnapshot Of(object o)
            {
                var snap = new FieldSnapshot { target = o };
                for (Type t = o.GetType(); t != null && t != typeof(object); t = t.BaseType)
                {
                    FieldInfo[] pola = t.GetFields(Inst | BindingFlags.DeclaredOnly);
                    for (int i = 0; i < pola.Length; i++)
                    {
                        if (pola[i].FieldType.IsValueType && !pola[i].IsLiteral && !pola[i].IsInitOnly)
                        {
                            snap.values.Add(new KeyValuePair<FieldInfo, object>(pola[i], pola[i].GetValue(o)));
                        }
                    }
                }
                return snap;
            }

            public void Restore()
            {
                for (int i = 0; i < values.Count; i++)
                {
                    values[i].Key.SetValue(target, values[i].Value);
                }
            }

            public override string ToString()
            {
                return target.GetType().Name + "(" + values.Count.ToString(CultureInfo.InvariantCulture) + " pol)";
            }
        }
    }
}
