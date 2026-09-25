using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using ProceduralNarrator.Core.Evaluation;
using ProceduralNarrator.Integration.Arcs;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ProceduralNarrator.Integration.Evaluation
{
    /// <summary>
    /// LOG ODPALEN INCYDENTOW DLA KAZDEGO NARRATORA - [PN-FIRED] (krok 8, decyzja autora K8-2; dlug 7).
    ///
    /// Ewaluacja porownawcza (krok 9) zestawia nasz tor z torem Cassandry, Phoebe i Randy'ego, a nasze
    /// [PN-DATA] opisuje tylko NASZE decyzje. Ten obserwator mierzy obie strony tym samym narzedziem:
    /// kazdy incydent odpalony przez Storyteller.TryFire, na kazdym celu (mapy, swiat, karawany), w kazdej
    /// grze - pole narrator= mowi, kto prowadzil gre. Ktory incydent nalezy do "toru", rozstrzyga analiza.
    ///
    /// BEZ HARMONY (decyzja K8-1): wykrycie przez StoryState.lastFireTicks (FireDetector w Core). Nie widac
    /// incydentow wymuszonych w dev mode (parms.forced) i tych, ktore zadania, zdolnosci, rytualy albo
    /// scenariusz odpalaja wprost przez TryExecute - to nie sa decyzje narratora. SPROSTOWANIE (przeglad S10):
    /// "wszystkie z TryFire" nie jest doslowne - napady generatora zagrozen zadan (QuestPart_ThreatsGenerator,
    /// ThreatsGenerator ustawia forced) i wiekszosc zdarzen Anomaly przechodza przez TryFire z forced=true,
    /// wiec lastFireTicks ich nie zna i ten log ich nie widzi. Widzi za to ClassicIntro (ticki 204000/264000/324000)
    /// i kolejke (np. prowokacja otchlani) - klasyfikacja "toru" w analizie musi to uwzglednic.
    ///
    /// KONTEKST: koloniscy, powaleni ostro i zagrozenie sa zapisywane w ticku tuz PRZED interwalem narratora
    /// (tick = 999 mod 1000); odpalenie w ticku interwalu dostaje je jako kontekst=przed, bo w ticku
    /// odpalenia swiat juz zawiera skutek (napastnicy na mapie). Odpalenie poza interwalem (kolejka
    /// incydentow) i pierwsze po wczytaniu dostaje kontekst z chwili wykrycia (kontekst=po). Bogactwo i punkty
    /// sa z chwili wykrycia i TYLKO wtedy, gdy odczyt nie wymusi przeliczenia WealthWatcher (inaczej puste -
    /// przeglad S10: odczyt przestawialby faze przeliczania bogactwa w grze Cassandry).
    /// Zagrozenie z GenHostility - bez efektow ubocznych (DangerWatcher zapisuje wlasny cache, wiec jego odczyt
    /// w grze Cassandry zmienialby to, co widzi wanilia).
    ///
    /// BEZ RNG GRY (kanarek RandCanary). Instancja zyje w komponencie pamieci i powstaje od nowa przy kazdym
    /// wczytaniu - nic tu nie jest statyczne ani utrwalane.
    /// </summary>
    internal sealed class IncidentFireWatcher
    {
        private readonly FireDetector detektor;

        /// <summary>Kontekst "przed" per mapa domowa: tick zapisu i wartosci.</summary>
        private readonly Dictionary<int, KontekstMapy> przed = new Dictionary<int, KontekstMapy>();

        /// <summary>Nasze zdarzenia oddane grze: "tick|cel|incydent" (rejestrowane przed yield compa).</summary>
        private readonly HashSet<string> nasze = new HashSet<string>();

        private readonly List<DetectedFire> bufor = new List<DetectedFire>();
        private readonly List<KeyValuePair<string, int>> wpisy = new List<KeyValuePair<string, int>>();

        public IncidentFireWatcher(int startTick)
        {
            detektor = new FireDetector(startTick);
        }

        private struct KontekstMapy
        {
            public int Tick;
            public int Kolonisci;
            public int NaMapie;
            public int Powaleni;
            public bool Zagrozenie;
        }

        public static string MapKey(int mapId)
        {
            return "map:" + mapId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Rejestruje zdarzenie, ktore NASZ comp oddaje grze w tym ticku (przed yield). Obserwator oznaczy
        /// jego odpalenie pn=1. Rejestracja przed yield, a nie po potwierdzeniu: sciezka spozniona
        /// (iterator niewznowiony) tez ma wtedy poprawny znacznik.
        /// </summary>
        public void RegisterOwn(int tick, int mapId, string incydent)
        {
            if (incydent != null)
            {
                nasze.Add(tick.ToString(CultureInfo.InvariantCulture) + "|" + MapKey(mapId) + "|" + incydent);
            }
        }

        /// <summary>
        /// Cofa rejestracje, gdy nasz TryFire sie NIE wykonal (przeglad S10). Potwierdzenie biegnie po wznowieniu
        /// iteratora, jeszcze w StorytellerTick - przed GameComponentTick tego ticku, wiec zdazy. Bez tego odpalenie
        /// tego samego incydentu przez kogos innego w tym samym ticku (np. ClassicIntro) dostaloby pn=1.
        /// </summary>
        public void UnregisterOwn(int tick, int mapId, string incydent)
        {
            if (incydent != null)
            {
                nasze.Remove(tick.ToString(CultureInfo.InvariantCulture) + "|" + MapKey(mapId) + "|" + incydent);
            }
        }

        /// <summary>Liczba wpisow lastFireTicks per cel w poprzednim przegladzie (ostrzezenie o wyczyszczeniu).</summary>
        private readonly Dictionary<string, int> liczbaWpisow = new Dictionary<string, int>();

        private bool ostrzezonoSpadek;
        private bool ostrzezonoFantom;

        /// <summary>
        /// Uchwyt pola WealthWatcher.lastCountTick (float) - metadane typu gry. Bogactwo i punkty czytamy tylko
        /// wtedy, gdy getter NIE wymusi przeliczenia (przeglad S10): w grze z Cassandra odczyt przestawialby faze
        /// przeliczania bogactwa, a obserwator ma byc bierny.
        /// </summary>
        private static readonly FieldInfo OstatnieLiczenie =
            typeof(WealthWatcher).GetField("lastCountTick", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>Krok obserwatora w ticku gry. Wolane z GameComponentTick, po StorytellerTick.</summary>
        public void Tick(int tick)
        {
            if (Current.Game == null || Find.Storyteller == null)
            {
                return;
            }

            // 1. Odpalenia od ostatniego przegladu - na kazdym celu narratora.
            bufor.Clear();
            List<IIncidentTarget> cele = Find.Storyteller.AllIncidentTargets;
            var kopia = new List<IIncidentTarget>(cele);
            for (int i = 0; i < kopia.Count; i++)
            {
                IIncidentTarget cel = kopia[i];
                if (cel == null || cel.StoryState == null || cel.StoryState.lastFireTicks == null)
                {
                    continue;
                }
                wpisy.Clear();
                foreach (KeyValuePair<IncidentDef, int> kv in cel.StoryState.lastFireTicks)
                {
                    if (kv.Key != null)
                    {
                        wpisy.Add(new KeyValuePair<string, int>(kv.Key.defName, kv.Value));
                    }
                }
                string klucz = KluczCelu(cel);
                int fantomow = detektor.Scan(klucz, wpisy, tick, bufor);
                if (fantomow > 0 && !ostrzezonoFantom)
                {
                    ostrzezonoFantom = true;
                    PNLog.Warn("Stan odpalen celu " + klucz + " ma wpisy z PRZYSZLOSCI (tick > " + tick
                               + ") - zostawia je waniliowe narzedzie \"Future incidents\" albo nieudane przywrocenie "
                               + "symulatora. Nie beda logowane jako odpalenia; sesja jest skazona dla ewaluacji.");
                }
                // Wanilia nigdy nie USUWA wpisow lastFireTicks poza narzedziami debugowymi ("Future incidents"
                // czysci je na stale - przeglad S10). Spadek liczby wpisow = skazony stan gry.
                int poprzednio;
                if (liczbaWpisow.TryGetValue(klucz, out poprzednio) && wpisy.Count < poprzednio && !ostrzezonoSpadek)
                {
                    ostrzezonoSpadek = true;
                    PNLog.Warn("Stan odpalen gry na " + klucz + " zmalal (" + poprzednio + " -> " + wpisy.Count
                               + " wpisow) - najpewniej waniliowe \"Future incidents\" (dev mode), ktore czysci go na stale. "
                               + "FiredTooRecently przestaje dzialac; sesja NIE nadaje sie do ewaluacji.");
                }
                liczbaWpisow[klucz] = wpisy.Count;
            }
            for (int i = 0; i < bufor.Count; i++)
            {
                Zaloguj(bufor[i], tick);
            }

            // 2. Rejestr naszych zdarzen: starsze niz biezacy tick nie maja juz czego oznaczyc.
            if (nasze.Count > 0)
            {
                nasze.RemoveWhere(k => !k.StartsWith(tick.ToString(CultureInfo.InvariantCulture) + "|"));
            }

            // 3. Kontekst "przed" - w ticku tuz przed interwalem narratora (StorytellerTick co 1000 tickow).
            if ((tick + 1) % 1000 == 0)
            {
                ZapiszKontekstPrzed(tick);
            }
        }

        private void ZapiszKontekstPrzed(int tick)
        {
            if (Find.Maps == null)
            {
                return;
            }
            foreach (Map m in new List<Map>(Find.Maps))
            {
                if (m == null || !m.IsPlayerHome)
                {
                    continue;
                }
                przed[m.uniqueID] = Policz(m, tick);
            }
        }

        private static KontekstMapy Policz(Map m, int tick)
        {
            return new KontekstMapy
            {
                Tick = tick,
                Kolonisci = m.mapPawns == null ? 0 : m.mapPawns.FreeColonistsCount,
                NaMapie = WorldSnapshotBuilder.CountColonistsOnMap(m),
                Powaleni = WorldSnapshotBuilder.CountAcutelyDownedColonists(m),
                Zagrozenie = GenHostility.AnyHostileActiveThreatToPlayer(m)
            };
        }

        private void Zaloguj(DetectedFire f, int tick)
        {
            IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail(f.Incident);
            Map mapa = MapaCelu(f.Target);
            bool nasz = nasze.Contains(f.Tick.ToString(CultureInfo.InvariantCulture) + "|" + f.Target + "|" + f.Incident);

            string kontekst = "-";
            KontekstMapy k = default(KontekstMapy);
            bool maKontekst = false;
            float bogactwo = -1f, bogactwoWzgl = -1f, punkty = -1f;
            if (mapa != null)
            {
                KontekstMapy zapisany;
                if (f.Tick % 1000 == 0 && przed.TryGetValue(mapa.uniqueID, out zapisany) && zapisany.Tick == f.Tick - 1)
                {
                    k = zapisany;
                    kontekst = "przed";
                }
                else
                {
                    k = Policz(mapa, tick);
                    kontekst = "po";
                }
                maKontekst = true;
                // Bogactwo i punkty BEZ skutkow ubocznych (przeglad S10): tylko gdy getter nie wymusi przeliczenia
                // WealthWatcher i tylko na mapie, ktorej punkty licza sie z niej samej (mapa kieszeniowa bierze
                // AnyPlayerHomeMap - moze go nie byc). Pola pomocnicze nie moga wylaczyc obserwatora wyjatkiem.
                try
                {
                    if (!mapa.IsPocketMap && BogactwoBezPrzeliczenia(mapa))
                    {
                        bogactwo = mapa.PlayerWealthForStoryteller;
                        bogactwoWzgl = WealthReference.Relative(bogactwo, GenDate.DaysPassedSinceSettle);
                        punkty = StorytellerUtility.DefaultThreatPointsNow(mapa);
                    }
                }
                catch (Exception)
                {
                    bogactwo = bogactwoWzgl = punkty = -1f;
                }
            }

            PNLog.Fired(f.Tick, f.Target, mapa == null ? -1 : mapa.uniqueID, mapa != null && mapa.IsPlayerHome,
                        f.Incident, def == null || def.category == null ? "?" : def.category.defName, nasz,
                        kontekst, maKontekst ? k.Kolonisci : -1, maKontekst ? k.NaMapie : -1,
                        maKontekst ? k.Powaleni : -1, maKontekst ? (k.Zagrozenie ? 1 : 0) : -1,
                        bogactwo, bogactwoWzgl, punkty, f.Tick == tick ? 0 : tick - f.Tick);
        }

        /// <summary>
        /// Czy odczyt bogactwa mapy nie wymusi przeliczenia: WealthWatcher.RecountIfNeeded liczy od nowa, gdy od
        /// ostatniego liczenia minelo &gt; 5000 tickow. Bez dostepu do pola - nie czytamy wcale (pola puste).
        /// </summary>
        private static bool BogactwoBezPrzeliczenia(Map m)
        {
            if (OstatnieLiczenie == null || m.wealthWatcher == null || OstatnieLiczenie.FieldType != typeof(float))
            {
                return false;
            }
            float ostatnie = (float)OstatnieLiczenie.GetValue(m.wealthWatcher);
            return (float)Find.TickManager.TicksGame - ostatnie <= 5000f;
        }

        private static string KluczCelu(IIncidentTarget cel)
        {
            Map m = cel as Map;
            if (m != null)
            {
                return MapKey(m.uniqueID);
            }
            if (cel is World)
            {
                return "world";
            }
            Caravan c = cel as Caravan;
            if (c != null)
            {
                return "caravan:" + c.ID.ToString(CultureInfo.InvariantCulture);
            }
            return cel.GetType().Name + ":" + cel.Tile.ToString(CultureInfo.InvariantCulture);
        }

        private static Map MapaCelu(string klucz)
        {
            if (klucz == null || !klucz.StartsWith("map:") || Find.Maps == null)
            {
                return null;
            }
            int id;
            if (!int.TryParse(klucz.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            {
                return null;
            }
            List<Map> mapy = Find.Maps;
            for (int i = 0; i < mapy.Count; i++)
            {
                if (mapy[i] != null && mapy[i].uniqueID == id)
                {
                    return mapy[i];
                }
            }
            return null;
        }
    }
}
