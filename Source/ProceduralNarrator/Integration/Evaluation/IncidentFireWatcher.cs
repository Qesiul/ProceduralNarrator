using System;
using System.Collections.Generic;
using System.Globalization;
using ProceduralNarrator.Core.Evaluation;
using ProceduralNarrator.Integration.Arcs;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ProceduralNarrator.Integration.Evaluation
{
    /// <summary>
    /// [PN-FIRED] - odpalenia incydentow KAZDEGO narratora w kazdej grze (krok 8, K8-2; etap L kroku 9) i [PN-LIST] - pelna
    /// tresc kazdego nowego listu (poprawka metodologii etapu L: widoczne formy porownywane tak samo dla wszystkich narratorow).
    ///
    /// Wykrycie bez Harmony przez StoryState.lastFireTicks (FireDetector w Core), na kazdym celu. Nie widac
    /// incydentow z parms.forced (dev mode, generator zagrozen zadan, wiekszosc Anomaly) ani odpalanych wprost przez
    /// TryExecute; widac ClassicIntro i kolejke - tor klasyfikuje analiza. Akcja "PN: wymus akcje" odpala z UI po
    /// przegladzie ticku, wiec detektor jej nie zobaczy - loguje ja LogForced (wymuszone=1).
    ///
    /// Caly kontekst mapy (koloniscy, powaleni ostro, zagrozenie, bogactwo, punkty, pora, noc) z ticku tuz przed interwalem
    /// narratora (kontekst=przed), bo w ticku odpalenia swiat zawiera juz skutek; inaczej z chwili wykrycia (kontekst=po).
    /// Bogactwo z pol gry bez przeliczania (WealthReader), punkty tylko gdy nie wymuszaja przeliczenia. Frakcja tylko dla
    /// napadow (IncidentWorker_Raid ustawia StoryState.lastRaidFaction). Listy: nowe z ticku wykrycia; przy kilku odpaleniach
    /// w ticku kazde dostaje wszystkie (listyWspolne=tak). [PN-LIST] idzie dla KAZDEGO nowego listu, takze bez odpalenia
    /// w tym ticku (listy pozniejsze: meteoryt, zadania). Bez RNG gry (RandCanary).
    /// </summary>
    internal sealed class IncidentFireWatcher
    {
        private readonly FireDetector detektor;
        private readonly Dictionary<int, KontekstMapy> przed = new Dictionary<int, KontekstMapy>();

        /// <summary>Nasze zdarzenia oddane grze: "tick|cel|incydent" (rejestrowane przed yield compa).</summary>
        private readonly HashSet<string> nasze = new HashSet<string>();

        private readonly List<DetectedFire> bufor = new List<DetectedFire>();
        private readonly List<KeyValuePair<string, int>> wpisy = new List<KeyValuePair<string, int>>();
        private readonly Dictionary<string, int> liczbaWpisow = new Dictionary<string, int>();
        private readonly NewIdTracker listy = new NewIdTracker();
        private readonly List<int> idListow = new List<int>();

        private bool ostrzezonoSpadek;
        private bool ostrzezonoFantom;

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
            public int Pora;
            public int Noc;
            public float Bogactwo;
            public int BogactwoWiek;
            public float BogactwoWzgl;
            public float Punkty;
        }

        public static string MapKey(int mapId)
        {
            return "map:" + mapId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Nasz comp oddaje zdarzenie grze w tym ticku (przed yield) - odpalenie dostanie pn=1.</summary>
        public void RegisterOwn(int tick, int mapId, string incydent)
        {
            if (incydent != null)
            {
                nasze.Add(tick.ToString(CultureInfo.InvariantCulture) + "|" + MapKey(mapId) + "|" + incydent);
            }
        }

        /// <summary>Nasz TryFire sie nie wykonal - cudze odpalenie tego incydentu w tym ticku nie dostanie pn=1.</summary>
        public void UnregisterOwn(int tick, int mapId, string incydent)
        {
            if (incydent != null)
            {
                nasze.Remove(tick.ToString(CultureInfo.InvariantCulture) + "|" + MapKey(mapId) + "|" + incydent);
            }
        }

        /// <summary>Krok w ticku gry (GameComponentTick, po StorytellerTick).</summary>
        public void Tick(int tick)
        {
            if (Current.Game == null || Find.Storyteller == null)
            {
                return;
            }

            bufor.Clear();
            var cele = new List<IIncidentTarget>(Find.Storyteller.AllIncidentTargets);
            for (int i = 0; i < cele.Count; i++)
            {
                IIncidentTarget cel = cele[i];
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
                if (detektor.Scan(klucz, wpisy, tick, bufor) > 0 && !ostrzezonoFantom)
                {
                    ostrzezonoFantom = true;
                    PNLog.Warn("Stan odpalen celu " + klucz + " ma wpisy z PRZYSZLOSCI (tick > " + tick
                               + ") - zostawia je waniliowe \"Future incidents\" albo nieudane przywrocenie symulatora. "
                               + "Nie beda logowane jako odpalenia; sesja jest skazona dla ewaluacji.");
                }
                // Wanilia nie usuwa wpisow lastFireTicks poza narzedziami debugowymi - spadek = skazony stan gry.
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

            List<Letter> nowe = NoweListy();
            for (int i = 0; i < bufor.Count; i++)
            {
                Zaloguj(bufor[i], tick, nowe, bufor.Count > 1, 0);
            }
            ZalogujListy(tick, nowe, IncydentyTiku(), 0);

            if (nasze.Count > 0)
            {
                string teraz = tick.ToString(CultureInfo.InvariantCulture) + "|";
                nasze.RemoveWhere(k => !k.StartsWith(teraz, StringComparison.Ordinal));
            }

            // Kontekst "przed" w ticku tuz przed interwalem narratora (StorytellerTick co 1000 tickow).
            if ((tick + 1) % 1000 == 0)
            {
                ZapiszKontekstPrzed(tick);
            }
        }

        /// <summary>
        /// Odpalenie z akcji "PN: wymus akcje" (UI, po przegladzie ticku - detektor go nie zobaczy). Listy tego odpalenia
        /// podaje wolajacy (roznica migawki listow przed i po TryFire); inne nowe listy od ostatniego ticku ida bez odpalenia.
        /// numer = kolejna akcja w sesji (wymuszone=numer w obu liniach) - kilka akcji w jednej pauzie ma ten sam tick.
        /// </summary>
        public void LogForced(int tick, Map mapa, string incydent, List<Letter> jegoListy, int numer)
        {
            if (mapa == null || incydent == null)
            {
                return;
            }
            List<Letter> wlasne = jegoListy ?? new List<Letter>();
            int nr = numer < 1 ? 1 : numer;
            Zaloguj(new DetectedFire { Target = MapKey(mapa.uniqueID), Incident = incydent, Tick = tick }, tick, wlasne, false, nr);
            ZalogujListy(tick, wlasne, incydent, nr);
            // Listy tego odpalenia sa juz zalogowane - nie moga trafic do odpalenia w nastepnym ticku.
            var reszta = new List<Letter>();
            foreach (Letter l in NoweListy())
            {
                if (!wlasne.Contains(l))
                {
                    reszta.Add(l);
                }
            }
            ZalogujListy(tick, reszta, "-", 0);
        }

        /// <summary>Listy, ktore pojawily sie od poprzedniego ticku (pierwszy tick po wczytaniu - zadne), rosnaco po ID.</summary>
        private List<Letter> NoweListy()
        {
            var wynik = new List<Letter>();
            if (Find.LetterStack == null)
            {
                return wynik;
            }
            List<Letter> stos = Find.LetterStack.LettersListForReading;
            idListow.Clear();
            for (int i = 0; i < stos.Count; i++)
            {
                if (stos[i] != null)
                {
                    idListow.Add(stos[i].ID);
                }
            }
            List<int> nowe = listy.Update(idListow);
            for (int n = 0; n < nowe.Count; n++)
            {
                for (int i = 0; i < stos.Count; i++)
                {
                    if (stos[i] != null && stos[i].ID == nowe[n])
                    {
                        wynik.Add(stos[i]);
                        break;
                    }
                }
            }
            return wynik;
        }

        /// <summary>Incydenty wykryte w tym ticku (bez powtorzen, porzadek wykrycia) albo "-".</summary>
        private string IncydentyTiku()
        {
            var nazwy = new List<string>();
            for (int i = 0; i < bufor.Count; i++)
            {
                if (!nazwy.Contains(bufor[i].Incident))
                {
                    nazwy.Add(bufor[i].Incident);
                }
            }
            return nazwy.Count == 0 ? "-" : string.Join(",", nazwy.ToArray());
        }

        internal static string Etykieta(Letter l)
        {
            return l == null ? string.Empty : l.Label.Resolve().StripTags();
        }

        /// <summary>Linia [PN-LIST] dla kazdego listu: tytul, pelna tresc i nazwy wlasne do znacznikow (analiza w Pythonie).</summary>
        private static void ZalogujListy(int tick, List<Letter> nowe, string incydenty, int wymuszone)
        {
            for (int i = 0; i < nowe.Count; i++)
            {
                Letter l = nowe[i];
                if (l == null)
                {
                    continue;
                }
                ChoiceLetter cl = l as ChoiceLetter;
                Map mapa = l.lookTargets != null && l.lookTargets.Any ? l.lookTargets.PrimaryTarget.Map : null;
                PNLog.List(tick, mapa == null ? -1 : mapa.uniqueID, incydenty, wymuszone, l.def == null ? "?" : l.def.defName,
                           l.relatedFaction == null ? null : l.relatedFaction.Name, NazwyPionkow(l), Etykieta(l),
                           cl == null ? string.Empty : cl.Text.Resolve().StripTags());
            }
        }

        /// <summary>Imiona pionkow wskazanych przez list (pelne, krotkie, imie i nazwisko) - do znacznika PIONEK.</summary>
        private static List<string> NazwyPionkow(Letter l)
        {
            var wynik = new List<string>();
            if (l.lookTargets == null || l.lookTargets.targets == null)
            {
                return wynik;
            }
            foreach (GlobalTargetInfo t in l.lookTargets.targets)
            {
                Pawn p = t.Thing as Pawn;
                if (p == null || p.Name == null)
                {
                    continue;
                }
                Dodaj(wynik, p.Name.ToStringFull);
                Dodaj(wynik, p.Name.ToStringShort);
                NameTriple n3 = p.Name as NameTriple;
                if (n3 != null)
                {
                    Dodaj(wynik, n3.First);
                    Dodaj(wynik, n3.Last);
                }
            }
            return wynik;
        }

        private static void Dodaj(List<string> lista, string s)
        {
            if (!string.IsNullOrEmpty(s) && !lista.Contains(s))
            {
                lista.Add(s);
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
                if (m != null && m.IsPlayerHome)
                {
                    przed[m.uniqueID] = Policz(m, tick);
                }
            }
        }

        private static KontekstMapy Policz(Map m, int tick)
        {
            var k = new KontekstMapy
            {
                Tick = tick,
                Kolonisci = m.mapPawns == null ? 0 : m.mapPawns.FreeColonistsCount,
                NaMapie = WorldSnapshotBuilder.CountColonistsOnMap(m),
                Powaleni = WorldSnapshotBuilder.CountAcutelyDownedColonists(m),
                Zagrozenie = GenHostility.AnyHostileActiveThreatToPlayer(m),
                Pora = WorldSnapshotBuilder.SeasonIndex(GenLocalDate.Season(m)),
                Noc = GenCelestial.CurCelestialSunGlow(m) <= WorldSnapshotBuilder.NocnaJasnosc ? 1 : 0,
                Bogactwo = -1f,
                BogactwoWiek = -1,
                BogactwoWzgl = -1f,
                Punkty = -1f
            };
            // Pola pomocnicze nie moga wylaczyc obserwatora wyjatkiem.
            try
            {
                float b;
                int wiek;
                if (!m.IsPocketMap && WealthReader.TryRead(m, out b, out wiek))
                {
                    k.Bogactwo = b;
                    k.BogactwoWiek = wiek;
                    k.BogactwoWzgl = WealthReference.Relative(b, GenDate.DaysPassedSinceSettle);
                    if (WealthReader.NoRecount(m))
                    {
                        k.Punkty = StorytellerUtility.DefaultThreatPointsNow(m);
                    }
                }
            }
            catch (Exception)
            {
                k.Bogactwo = k.BogactwoWzgl = k.Punkty = -1f;
                k.BogactwoWiek = -1;
            }
            return k;
        }

        private void Zaloguj(DetectedFire f, int tick, List<Letter> listyTiku, bool wspolne, int wymuszone)
        {
            IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail(f.Incident);
            Map mapa = MapaCelu(f.Target);
            bool nasz = wymuszone > 0
                        || nasze.Contains(f.Tick.ToString(CultureInfo.InvariantCulture) + "|" + f.Target + "|" + f.Incident);
            var etykiety = new List<string>(listyTiku.Count);
            for (int i = 0; i < listyTiku.Count; i++)
            {
                etykiety.Add(Etykieta(listyTiku[i]));
            }

            var z = new PNLog.FiredFields
            {
                Tick = f.Tick, Cel = f.Target, Mapa = mapa == null ? -1 : mapa.uniqueID, Dom = mapa != null && mapa.IsPlayerHome,
                Incydent = f.Incident, Kategoria = def == null || def.category == null ? "?" : def.category.defName, Nasz = nasz,
                Kontekst = "-", Opoznienie = f.Tick == tick ? 0 : tick - f.Tick, Etykiety = etykiety,
                ListyWspolne = wspolne && etykiety.Count > 0, Wymuszone = wymuszone
            };
            if (mapa != null)
            {
                KontekstMapy k;
                if (wymuszone == 0 && f.Tick % 1000 == 0 && przed.TryGetValue(mapa.uniqueID, out k) && k.Tick == f.Tick - 1)
                {
                    z.Kontekst = "przed";
                }
                else
                {
                    k = Policz(mapa, tick);
                    z.Kontekst = "po";
                }
                z.Kolonisci = k.Kolonisci;
                z.NaMapie = k.NaMapie;
                z.Powaleni = k.Powaleni;
                z.Zagrozenie = k.Zagrozenie ? 1 : 0;
                z.Pora = k.Pora;
                z.Noc = k.Noc;
                z.Bogactwo = k.Bogactwo;
                z.BogactwoWiek = k.BogactwoWiek;
                z.BogactwoWzgl = k.BogactwoWzgl;
                z.Punkty = k.Punkty;
                if (def != null && def.Worker is IncidentWorker_Raid && mapa.StoryState != null
                    && mapa.StoryState.lastRaidFaction != null)
                {
                    z.Frakcja = ArcObservationBuilder.FactionId(mapa.StoryState.lastRaidFaction);
                    z.FrakcjaDef = mapa.StoryState.lastRaidFaction.def == null ? null : mapa.StoryState.lastRaidFaction.def.defName;
                }
            }
            PNLog.Fired(z);
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
            int id;
            if (klucz == null || !klucz.StartsWith("map:", StringComparison.Ordinal) || Find.Maps == null
                || !int.TryParse(klucz.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
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
