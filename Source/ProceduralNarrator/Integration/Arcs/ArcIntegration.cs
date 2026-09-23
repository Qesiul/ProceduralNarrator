using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using ProceduralNarrator.Core.Arcs;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration.Arcs
{
    /// <summary>
    /// Obserwacja swiata dla lukow (krok 5, decyzja autora nr 5) - budowana przy KAZDYM wywolaniu
    /// compa, wiec LEKKA i BEZ LOSOWOSCI: kilka licznikow i stan frakcji zwiazanych z lukami.
    /// Celowo nie przez WorldSnapshotBuilder.Build: ten liczy m.in. punkty zagrozenia i dach
    /// gorski, a przyszla zmiana gry mogla by wciagnac tam Verse.Rand - obserwacja co 1000 tickow
    /// przesuwala by wtedy generator gry. Pilnuje tego dodatkowo kanarek (RandCanary).
    /// </summary>
    internal static class ArcObservationBuilder
    {
        public static ArcObservation Build(Map map, ArcLedger ledger, int tick)
        {
            var o = new ArcObservation
            {
                Tick = tick,
                GameDay = tick / 60000f,
                ColonistLosses = ledger == null ? 0 : ledger.ColonistLosses,
                KidnappedCount = WorldSnapshotBuilder.CountKidnappedColonists(),
                Danger = map == null ? Core.Model.DangerLevel.None : WorldSnapshotBuilder.MapDanger(map)
            };
            if (ledger != null)
            {
                for (int i = 0; i < ledger.Active.Count; i++)
                {
                    string id = ledger.Active[i].BoundFactionId;
                    if (!string.IsNullOrEmpty(id) && !o.Factions.ContainsKey(id))
                    {
                        o.Factions[id] = FactionStatusOf(id);
                    }
                }
            }
            return o;
        }

        /// <summary>Frakcja po identyfikatorze (Faction.loadID jako tekst) albo null.</summary>
        public static Faction ResolveFaction(string id)
        {
            int loadId;
            if (string.IsNullOrEmpty(id) || Find.FactionManager == null
                || !int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out loadId))
            {
                return null;
            }
            List<Faction> wszystkie = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < wszystkie.Count; i++)
            {
                if (wszystkie[i] != null && wszystkie[i].loadID == loadId)
                {
                    return wszystkie[i];
                }
            }
            return null;
        }

        public static string FactionId(Faction f)
        {
            return f == null ? null : f.loadID.ToString(CultureInfo.InvariantCulture);
        }

        private static FactionStatus FactionStatusOf(string id)
        {
            Faction f = ResolveFaction(id);
            if (f == null)
            {
                return new FactionStatus { Exists = false };
            }
            return new FactionStatus
            {
                Exists = true,
                Defeated = f.defeated,
                Hostile = Faction.OfPlayer != null && f.HostileTo(Faction.OfPlayer)
            };
        }
    }

    /// <summary>
    /// Komunikaty lukow w grze (decyzja autora nr 8): krotki Message po potwierdzonym przejsciu,
    /// tekst z Defa, {FRAKCJA} podstawiane nazwa zwiazanej frakcji. W eksperymencie symulacyjnym
    /// NIGDY nic nie pokazujemy - ramiona symulacji nie sa rozgrywka gracza.
    /// </summary>
    internal static class ArcMessages
    {
        public static void Show(ArcTransitionRecord r)
        {
            if (r == null || string.IsNullOrEmpty(r.Message) || PNLog.InExperiment)
            {
                return;
            }
            string tekst = r.Message;
            if (tekst.IndexOf(ArcCatalog.FactionPlaceholder, System.StringComparison.Ordinal) >= 0)
            {
                Faction f = ArcObservationBuilder.ResolveFaction(r.FactionId);
                if (f == null)
                {
                    // Frakcja zniknela - tekst z jej nazwa nie mialby prawdziwej tresci.
                    return;
                }
                tekst = tekst.Replace(ArcCatalog.FactionPlaceholder, f.Name);
            }
            MessageTypeDef typ = string.IsNullOrEmpty(r.MessageType)
                ? null
                : DefDatabase<MessageTypeDef>.GetNamedSilentFail(r.MessageType);
            Messages.Message(tekst, typ ?? MessageTypeDefOf.NeutralEvent, true);
        }
    }

    /// <summary>
    /// Kanarek losowosci: czy obserwacja lukow nie ruszyla generatora gry (Verse.Rand).
    /// Czyta prywatny licznik Rand.iterations (dekompilacja 1.5.4063) refleksja PRZED i PO
    /// obserwacji. Rozjazd = jeden blad na sesje i wylaczenie kanarka (nie wylacza lukow).
    /// Gdy pole nie istnieje (inna wersja gry), kanarek milknie - bez bledu.
    /// </summary>
    internal static class RandCanary
    {
        private static readonly FieldInfo Iterations =
            typeof(Rand).GetField("iterations", BindingFlags.NonPublic | BindingFlags.Static);

        private static bool zgloszono;

        public static uint Read()
        {
            if (Iterations == null)
            {
                return 0u;
            }
            object v = Iterations.GetValue(null);
            return v is uint ? (uint)v : 0u;
        }

        public static void Check(uint before, string where)
        {
            if (zgloszono || Iterations == null)
            {
                return;
            }
            uint after = Read();
            if (after != before)
            {
                zgloszono = true;
                PNLog.Error("Obserwacja lukow ZUZYLA Verse.Rand (" + where + ", iteracji "
                            + (after - before).ToString(CultureInfo.InvariantCulture)
                            + ") - wywolanie co 1000 tickow przesuwa generator gry. Sprawdz ArcObservationBuilder.");
            }
        }
    }
}
