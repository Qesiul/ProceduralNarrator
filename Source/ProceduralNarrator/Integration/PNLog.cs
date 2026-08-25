using Verse;

namespace ProceduralNarrator.Integration
{
    /// <summary>
    /// Logowanie decyzji narratora (sekcja 12 - dane wejsciowe ewaluacji).
    /// Jednolity prefiks pozwala odfiltrowac nasze wpisy z Player.log.
    /// </summary>
    public static class PNLog
    {
        private const string Prefix = "[PN] ";

        public static void Decision(string message)
        {
            Log.Message(Prefix + message);
        }

        public static void Warn(string message)
        {
            Log.Warning(Prefix + message);
        }

        public static void Error(string message)
        {
            Log.Error(Prefix + message);
        }
    }
}
