using System.Reflection;
using ProceduralNarrator.Core.Decision;
using RimWorld;

namespace ProceduralNarrator.Integration.Incidents
{
    /// <summary>
    /// Odczyt i zapis cache'u werdyktu IncidentWorker.CanFireNow (krok 8, dlug 8) - refleksja z biblioteki
    /// standardowej, bez Harmony (decyzja autora K8-1). Pola zmierzone dekompilacja 1.5.4063:
    ///     [Unsaved] private int lastCheckCanRunTick;   [Unsaved] private bool lastCanRunResult;
    /// Gdy gra zmieni nazwy albo typy pol, Available = false: audyt startowy ostrzega, a comp pyta jak przed
    /// krokiem 8 (bez izolacji) - nic nie przestaje dzialac, znika tylko ochrona przed dlugiem 8.
    /// Uchwyty FieldInfo sa metadanymi typu gry, nie stanem rozgrywki - statyczne pola sa tu bezpieczne.
    /// </summary>
    internal static class IncidentCacheAccess
    {
        private static readonly FieldInfo TickField =
            typeof(IncidentWorker).GetField("lastCheckCanRunTick", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo ResultField =
            typeof(IncidentWorker).GetField("lastCanRunResult", BindingFlags.NonPublic | BindingFlags.Instance);

        public static bool Available
        {
            get
            {
                return TickField != null && ResultField != null
                       && TickField.FieldType == typeof(int) && ResultField.FieldType == typeof(bool);
            }
        }

        public static VerdictCacheState Read(IncidentWorker w)
        {
            if (w == null || !Available)
            {
                return new VerdictCacheState(VerdictCacheGuard.InvalidTick, false);
            }
            return new VerdictCacheState((int)TickField.GetValue(w), (bool)ResultField.GetValue(w));
        }

        public static void Write(IncidentWorker w, VerdictCacheState s)
        {
            if (w == null || !Available)
            {
                return;
            }
            TickField.SetValue(w, s.Tick);
            ResultField.SetValue(w, s.Result);
        }
    }
}
