using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Blackboard;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.PlayerModel;
using ProceduralNarrator.Core.Tension;
using ProceduralNarrator.Core.Util;
using ProceduralNarrator.Integration.Defs;
using ProceduralNarrator.Integration.PlayerModel;
using Verse;

namespace ProceduralNarrator.Integration.Persistence
{
    /// <summary>
    /// Trwaly stan narratora i obserwatory dzialajace w kazdej grze. GameComponent, bo comp NIE przezywa wczytania ani
    /// zmiany narratora (Storyteller.ExposeData buduje compy od zera z Defa - dekompilacja 1.5.4063). Rejestracja
    /// automatyczna (Game.FillComponents), stad konstruktor z parametrem Game.
    /// </summary>
    public class NarratorMemoryComponent : GameComponent
    {
        /// <summary>
        /// Wersja formatu zapisu pamieci - PODBIC przy zmianie ksztaltu MapMemoryRecord albo kodowania linii ksiag.
        /// v2 (krok 5) wezel "luki"; v3 (krok 6) osobny wezel "fakty" (blad jednego kodeka nie kaskaduje); v4 (krok 7)
        /// wezel "stylGracza" (jeden na gre). Starsze zapisy wczytuja sie bez straty - brakujace ksiegi startuja puste.
        /// </summary>
        private const int MemoryFormatVersion = 4;

        /// <summary>Wersja, od ktorej zapis niesie ksiege lukow - do rozroznienia komunikatu przy wczytaniu.</summary>
        private const int ArcsSinceVersion = 2;

        /// <summary>Wersja, od ktorej zapis niesie ksiege faktow - do rozroznienia komunikatu przy wczytaniu.</summary>
        private const int FactsSinceVersion = 3;

        /// <summary>Wersja, od ktorej zapis niesie ksiege stylu gracza - do rozroznienia komunikatu przy wczytaniu.</summary>
        private const int StyleSinceVersion = 4;

        /// <summary>
        /// Pamiec zdarzen OSOBNA DLA KAZDEJ MAPY (Map.uniqueID - nie Index, ktory sie przesuwa; uniqueID nie jest
        /// recyklingowane): kazda kolonia prowadzi wlasna narracje, a wspolny bufor psulby swiezosc, kontrast i gestosc PASS.
        /// </summary>
        private Dictionary<int, EventHistory> histories = new Dictionary<int, EventHistory>();

        /// <summary>
        /// Ksiegi LUKOW narracyjnych (krok 5), osobno dla kazdej mapy - z tego samego powodu co
        /// historie: kazda kolonia prowadzi wlasne watki. Klucz Map.uniqueID.
        /// </summary>
        private Dictionary<int, ArcLedger> ledgers = new Dictionary<int, ArcLedger>();

        /// <summary>
        /// Ksiegi FAKTOW blackboardu (krok 6), osobno dla kazdej mapy - fakty sa faktami o kolonii
        /// (decyzja autora: zakaz faktow globalnych). Klucz Map.uniqueID. Niezalezne od lukow:
        /// istnieja takze wtedy, gdy warstwa lukow jest wylaczona albo uszkodzona.
        /// </summary>
        private Dictionary<int, FactLedger> facts = new Dictionary<int, FactLedger>();

        /// <summary>
        /// KSIEGA STYLU GRACZA (krok 7) - jedna na gre, nie per mapa: styl opisuje gracza, a nie kolonie
        /// (epizody zagrozen sa liczone per mapa wewnatrz ksiegi). Pisze do niej WYLACZNIE obserwator
        /// w GameComponentTick; narrator tylko czyta (PlayerStyleModel.Evaluate).
        /// </summary>
        private PlayerStyleLedger styl = new PlayerStyleLedger();

        /// <summary>Bufor serializacji wezla "stylGracza" - wylacznie na czas zapisu albo wczytania.</summary>
        private List<string> stylZapis = new List<string>();

        /// <summary>
        /// Obserwator stylu rzucil wyjatek - obserwacja wylaczona do wczytania zapisu (komponent powstaje
        /// wtedy od nowa), raport raz. Narrator nie uzywa wtedy stylu (StorytellerComp_Generative.StylTury).
        /// </summary>
        private bool stylBroken;

        /// <summary>
        /// Parametry stylu dla obserwatora, rozwiazywane leniwie (PlayerStyleObserver.ResolveParams) -
        /// nie w kazdym ticku. Nie utrwalane; FinalizeInit je zeruje.
        /// </summary>
        private PlayerStyleParams stylParams;

        /// <summary>
        /// Bufor serializacji - WYLACZNIE do rozmowy ze Scribe'em, nigdy do odczytu w trakcie gry.
        /// Zrodlem prawdy jest histories; ten slownik zyje tylko przez czas zapisu albo wczytania.
        /// </summary>
        private Dictionary<int, MapMemoryRecord> zapis = new Dictionary<int, MapMemoryRecord>();

        /// <summary>
        /// Identyfikator ROZGRYWKI (nie sesji procesu) - kolumna runId, grupowanie danych; nadawany przy pierwszym
        /// FinalizeInit (Guid, nie Rand gry) i zapisywany. Wczytanie rozwidla rozgrywke pod tym samym runId (PNLog.Load);
        /// "PN: rozpocznij gre ewaluacyjna" nadaje nowy (etap L). Nie wchodzi do scoringu - tylko ziarno tekstu listu.
        /// </summary>
        private string runId = string.Empty;

        /// <summary>
        /// Profil narratora tej rozgrywki: losowany raz z runId (nie wybiera go gracz - decyzja autora, krok 4) i zapisywany,
        /// zeby wczytanie nie zmienilo narratora.
        /// </summary>
        private string profileId = string.Empty;

        private int memoryVersion;

        /// <summary>Czy stan przyszedl z zapisu (true) czy to swieza gra (false). Tylko do logu.</summary>
        private bool wczytanoZZapisu;

        /// <summary>Liczba linii pamieci odrzuconych przy wczytaniu (suma trzech ksiag). Tylko do logu.</summary>
        private int odrzuconychLinii;

        /// <summary>To samo rozbite na ksiegi (S6) - do [PN-LOAD] i ostrzezenia.</summary>
        private int odrzuconychHistorii;
        private int odrzuconychLukow;
        private int odrzuconychFaktow;
        private int odrzuconychStylu;

        private bool kanarekWypisany;

        /// <summary>Konstruktor MUSI przyjmowac Game (Game.FillComponents: Activator.CreateInstance(typ, this)).</summary>
        public NarratorMemoryComponent(Game game)
        {
        }

        public string RunId
        {
            get { return runId; }
        }

        public int MapCount
        {
            get { return histories == null ? 0 : histories.Count; }
        }

        /// <summary>
        /// defName profilu ZAPISANY w pamieci rozgrywki. Pusty zanim padnie StartedNewGame/LoadedGame
        /// albo gdy katalog profili byl pusty przy przydziale. To NIE musi byc profil, na ktorym
        /// narrator liczy - patrz EffectiveProfileId.
        /// </summary>
        public string ProfileId
        {
            get { return profileId; }
        }

        /// <summary>
        /// Profil FAKTYCZNIE uzyty: zapisany, jesli Def istnieje, inaczej profil awaryjny.
        /// Do danych badawczych idzie ta wartosc - zapisana nazwa przy nieistniejacym Defie
        /// opisywalaby narratora, ktory tej rozgrywki nie prowadzi.
        /// </summary>
        public string EffectiveProfileId
        {
            get
            {
                return NarratorProfileCatalog.ById(profileId) != null ? profileId : NarratorProfile.FallbackId;
            }
        }

        /// <summary>
        /// Profil rdzenia dla tej rozgrywki. Rozwiazywany PRZY KAZDYM WYWOLANIU, a nie
        /// cache'owany: Def moze zostac przeladowany, a cache przezylby wyjscie do menu.
        /// Koszt to slownikowy GetNamedSilentFail raz na 1000 tickow, czyli zero.
        /// </summary>
        public NarratorProfile ActiveProfile()
        {
            return NarratorProfileCatalog.Resolve(profileId);
        }

        /// <summary>
        /// Jak wyzej, z jawnie podanymi wagami profilu awaryjnego - comp podaje blok &lt;weights&gt;
        /// wlasnego StorytellerDefa, zeby sciezka awaryjna liczyla dokladnie na tym, co deklaruje.
        /// </summary>
        public NarratorProfile ActiveProfile(ScoringWeights fallbackWeights)
        {
            return NarratorProfileCatalog.Resolve(profileId, fallbackWeights);
        }

        /// <summary>
        /// Podmienia profil w trakcie rozgrywki. WYLACZNIE dla akcji debugowej prowadzacej
        /// serie kontrolne - normalna rozgrywka nie zmienia profilu, bo osobowosc narratora
        /// nie jest ustawieniem.
        ///
        /// Zmiana profilu NIE kasuje pamieci zdarzen: to jest celowe, bo dzieki temu da sie
        /// porownac dwa profile na IDENTYCZNEJ historii, czyli izolowac badany czynnik.
        /// </summary>
        public void ForceProfile(string defName)
        {
            string poprzedni = profileId;
            profileId = defName ?? string.Empty;
            PNLog.Decision("PROFIL NARRATORA WYMUSZONY recznie (akcja debugowa): "
                           + (string.IsNullOrEmpty(poprzedni) ? "(brak)" : poprzedni)
                           + " -> " + (string.IsNullOrEmpty(profileId) ? "(brak)" : profileId)
                           + ". Pamiec zdarzen NIE zostala skasowana, wiec oba profile widza te sama historie. "
                           + "UWAGA: zapis gry po tej akcji UTRWALA wymuszony profil w tej rozgrywce.");
            PNLog.Reset("wymusProfil",
                        "profilPoprzedni=" + (string.IsNullOrEmpty(poprzedni) ? "?" : poprzedni)
                        + "; profilNowy=" + (string.IsNullOrEmpty(profileId) ? "?" : profileId)
                        + "; mapy=" + OpisMap() + "; luki=" + OpisLukow() + "; fakty=" + OpisFaktow()
                        + "; styl=" + OpisStylu());
        }

        /// <summary>Widok do akcji debugowych i diagnostyki. Nie mutowac przez niego pamieci.</summary>
        public IEnumerable<KeyValuePair<int, EventHistory>> All
        {
            get { return histories ?? new Dictionary<int, EventHistory>(); }
        }

        /// <summary>
        /// Pamiec dla danej mapy; tworzona przy pierwszym pytaniu o nia.
        /// Przeniesione tu z StorytellerComp_Generative razem z uzasadnieniem rozdzialu per mapa.
        /// </summary>
        public EventHistory HistoryFor(int mapUniqueId)
        {
            if (histories == null)
            {
                histories = new Dictionary<int, EventHistory>();
            }

            EventHistory h;
            if (!histories.TryGetValue(mapUniqueId, out h))
            {
                h = new EventHistory();
                histories[mapUniqueId] = h;
                if (histories.Count > 1)
                {
                    PNLog.Decision("Nowa pamiec narratora dla mapy " + mapUniqueId.ToString(CultureInfo.InvariantCulture)
                                   + " (map z wlasna pamiecia: " + histories.Count.ToString(CultureInfo.InvariantCulture)
                                   + "). Kazda kolonia prowadzi osobna narracje.");
                }
            }
            return h;
        }

        /// <summary>Ksiega lukow dla danej mapy; tworzona przy pierwszym pytaniu o nia.</summary>
        public ArcLedger LedgerFor(int mapUniqueId)
        {
            if (ledgers == null)
            {
                ledgers = new Dictionary<int, ArcLedger>();
            }
            ArcLedger l;
            if (!ledgers.TryGetValue(mapUniqueId, out l))
            {
                l = new ArcLedger();
                ledgers[mapUniqueId] = l;
            }
            return l;
        }

        /// <summary>Widok ksiag lukow - do akcji debugowych i odcisku eksperymentu. Nie mutowac.</summary>
        public IEnumerable<KeyValuePair<int, ArcLedger>> AllLedgers
        {
            get { return ledgers ?? new Dictionary<int, ArcLedger>(); }
        }

        /// <summary>Ksiega faktow dla danej mapy; tworzona przy pierwszym pytaniu o nia.</summary>
        public FactLedger FactsFor(int mapUniqueId)
        {
            if (facts == null)
            {
                facts = new Dictionary<int, FactLedger>();
            }
            FactLedger f;
            if (!facts.TryGetValue(mapUniqueId, out f))
            {
                f = new FactLedger();
                facts[mapUniqueId] = f;
            }
            return f;
        }

        /// <summary>Widok ksiag faktow - do akcji debugowych i odcisku eksperymentu. Nie mutowac.</summary>
        public IEnumerable<KeyValuePair<int, FactLedger>> AllFacts
        {
            get { return facts ?? new Dictionary<int, FactLedger>(); }
        }

        /// <summary>Ksiega stylu gracza. Mutuje ja wylacznie obserwator; reszta tylko czyta.</summary>
        public PlayerStyleLedger Style
        {
            get { return styl ?? (styl = new PlayerStyleLedger()); }
        }

        /// <summary>Bezpiecznik stylu spalony (wyjatek obserwatora) - narrator liczy wtedy bez stylu.</summary>
        public bool StyleBroken
        {
            get { return stylBroken; }
        }

        /// <summary>Parametry stylu uzywane przez obserwatora (blok &lt;playerStyle&gt; naszego Defa, po Sanitize).</summary>
        public PlayerStyleParams StyleParams
        {
            get { return stylParams ?? (stylParams = PlayerStyleObserver.ResolveParams()); }
        }

        /// <summary>
        /// Kasuje ksiege stylu (akcja "PN: skasuj styl gracza") - obserwacja i rozgrzewka od nowa.
        /// "PN: skasuj pamiec" stylu NIE kasuje: styl opisuje gracza, a nie narracje (decyzja zgloszona
        /// autorowi przy planie kroku 7); ta akcja sluzy do sprawdzania rozgrzewki.
        /// </summary>
        public void ClearStyle()
        {
            PNLog.Reset("skasujStyl", "styl=" + OpisStylu());
            styl = new PlayerStyleLedger();
            PNLog.Decision("STYL GRACZA SKASOWANY recznie (akcja debugowa). Obserwacja zaczyna od nastepnego ticku, "
                           + "styl wroci po rozgrzewce (" + StyleParams.warmupDays.ToString(CultureInfo.InvariantCulture)
                           + " dni). Pamiec narratora (historia, luki, fakty) nietknieta.");
        }

        /// <summary>
        /// Kasuje pamiec narratora - historie, luki i fakty razem (inaczej pamiec bylaby wewnetrznie sprzeczna); styl zostaje
        /// (opisuje gracza, ma wlasna akcje). Akcja debugowa "PN: skasuj pamiec".
        /// </summary>
        public void ClearAll()
        {
            int map = histories == null ? 0 : histories.Count;
            // Znacznik PRZED kasowaniem, zeby niosl stan, ktory przepada (uid:decyzji).
            PNLog.Reset("skasujPamiec", "map=" + map.ToString(CultureInfo.InvariantCulture) + "; mapy=" + OpisMap()
                                        + "; luki=" + OpisLukow() + "; fakty=" + OpisFaktow() + "; styl=" + OpisStylu());
            histories = new Dictionary<int, EventHistory>();
            ledgers = new Dictionary<int, ArcLedger>();
            facts = new Dictionary<int, FactLedger>();
            PNLog.Decision("PAMIEC NARRATORA SKASOWANA recznie (akcja debugowa). Zwolniono map: "
                           + map.ToString(CultureInfo.InvariantCulture)
                           + ". Swiezosc, kontrast i gestosc PASS licza sie od zera.");
        }

        // =====================================================================================
        //  EKSPERYMENT SYMULACYJNY - podmiana pamieci na czas akcji "PN: test przyszlych incydentow"
        // =====================================================================================

        /// <summary>
        /// Zrzut pamieci na czas eksperymentu. Oryginalny slownik jest ODKLADANY (nie kopiowany),
        /// a kazde ramie dostaje GLEBOKA kopie odbudowana ta sama sciezka co zapis i wczytanie
        /// gry (ToPersistableLines -> RestoreFromLines). Ramie nie ma wiec jak dotknac prawdziwej
        /// pamieci (inny slownik, inne obiekty), a kazdy eksperyment WYKONUJE sciezke kodeka na
        /// zywych danych i wykrywa linie odrzucone przy odbudowie. NIE weryfikuje natomiast
        /// wiernosci kodeka: ramiona i odcisk pamieci przechodza przez Encode, wiec strata po
        /// stronie zapisu (np. zle kodowana walencja) bylaby w nich identyczna i niewidoczna.
        /// Wiernosc pole po polu sprawdza walidator offline (TEST 12, kodek pamieci).
        /// </summary>
        internal sealed class MemoryCheckpoint
        {
            internal Dictionary<int, EventHistory> Original;
            internal string ProfileId;
            internal Dictionary<int, MapMemoryRecord> Frozen;

            /// <summary>Ksiegi lukow gry (odlozone) i ich zamrozony kodek - ramie dostaje kopie, nie oryginal.</summary>
            internal Dictionary<int, ArcLedger> OriginalLedgers;
            internal Dictionary<int, List<string>> FrozenLedgers;

            /// <summary>Ksiegi faktow gry (odlozone) i ich zamrozony kodek - jak przy lukach.</summary>
            internal Dictionary<int, FactLedger> OriginalFacts;
            internal Dictionary<int, List<string>> FrozenFacts;

            /// <summary>Ksiega stylu gry (odlozona) i jej zamrozony kodek (krok 7) - jak przy faktach.</summary>
            internal PlayerStyleLedger OriginalStyle;
            internal List<string> FrozenStyle;
        }

        private MemoryCheckpoint eksperyment;

        internal bool InExperiment
        {
            get { return eksperyment != null; }
        }

        internal MemoryCheckpoint BeginExperiment()
        {
            if (eksperyment != null)
            {
                throw new InvalidOperationException("Eksperyment na pamieci narratora juz trwa.");
            }

            var c = new MemoryCheckpoint
            {
                Original = histories ?? new Dictionary<int, EventHistory>(),
                ProfileId = profileId,
                Frozen = new Dictionary<int, MapMemoryRecord>(),
                OriginalLedgers = ledgers ?? new Dictionary<int, ArcLedger>(),
                FrozenLedgers = new Dictionary<int, List<string>>(),
                OriginalFacts = facts ?? new Dictionary<int, FactLedger>(),
                FrozenFacts = new Dictionary<int, List<string>>(),
                // Styl: ramie go nie obserwuje (symulator nie tyka GameComponent), ale ramie ma czytac KOPIE -
                // odcisk pamieci obejmuje styl, wiec kopia przez kodek sprawdza tez jego sciezke.
                OriginalStyle = Style,
                FrozenStyle = Style.ToPersistableLines()
            };

            // Luki mutuja sie w KAZDYM wywolaniu compa (obserwacja), nie tylko w decyzji - bez
            // kopii ramie symulacji przesuwaloby prawdziwe watki gracza.
            foreach (KeyValuePair<int, ArcLedger> para in c.OriginalLedgers)
            {
                if (para.Value != null)
                {
                    c.FrozenLedgers[para.Key] = para.Value.ToPersistableLines();
                }
            }

            // Fakty tak samo: ramie zapisuje je po kazdym symulowanym zdarzeniu, a bez kopii
            // zostawialoby w pamieci gracza slady zdarzen, ktorych w jego swiecie nie bylo.
            foreach (KeyValuePair<int, FactLedger> para in c.OriginalFacts)
            {
                if (para.Value != null)
                {
                    c.FrozenFacts[para.Key] = para.Value.ToPersistableLines();
                }
            }

            foreach (KeyValuePair<int, EventHistory> para in c.Original)
            {
                if (para.Value == null)
                {
                    continue;
                }
                c.Frozen[para.Key] = new MapMemoryRecord
                {
                    decisionCount = para.Value.DecisionCount,
                    deliberateSilenceStreak = para.Value.DeliberateSilenceStreak,
                    lines = para.Value.ToPersistableLines()
                };
            }

            eksperyment = c;
            return c;
        }

        /// <summary>
        /// Instaluje swieza kopie zamrozonej pamieci i profil ramienia. Zwraca liczbe linii
        /// odrzuconych przy odbudowie - rozna od zera znaczy, ze ramie NIE startuje z tej samej
        /// pamieci co gra, i wolajacy ma to zglosic jako blad izolacji.
        /// </summary>
        internal int InstallExperimentArm(MemoryCheckpoint c, string armProfileId)
        {
            var kopia = new Dictionary<int, EventHistory>();
            int odrzuconych = 0;
            foreach (KeyValuePair<int, MapMemoryRecord> para in c.Frozen)
            {
                var h = new EventHistory();
                odrzuconych += h.RestoreFromLines(para.Value.decisionCount, para.Value.deliberateSilenceStreak,
                                                  para.Value.lines);
                kopia[para.Key] = h;
            }
            histories = kopia;

            var kopiaLukow = new Dictionary<int, ArcLedger>();
            if (c.FrozenLedgers != null)
            {
                foreach (KeyValuePair<int, List<string>> para in c.FrozenLedgers)
                {
                    var l = new ArcLedger();
                    odrzuconych += l.RestoreFromLines(para.Value);
                    kopiaLukow[para.Key] = l;
                }
            }
            ledgers = kopiaLukow;

            var kopiaFaktow = new Dictionary<int, FactLedger>();
            if (c.FrozenFacts != null)
            {
                foreach (KeyValuePair<int, List<string>> para in c.FrozenFacts)
                {
                    var f = new FactLedger();
                    odrzuconych += f.RestoreFromLines(para.Value);
                    kopiaFaktow[para.Key] = f;
                }
            }
            facts = kopiaFaktow;

            var kopiaStylu = new PlayerStyleLedger();
            if (c.FrozenStyle != null)
            {
                odrzuconych += kopiaStylu.RestoreFromLines(c.FrozenStyle);
            }
            styl = kopiaStylu;

            profileId = string.IsNullOrEmpty(armProfileId) ? c.ProfileId : armProfileId;
            return odrzuconych;
        }

        /// <summary>
        /// Przywraca oryginalna pamiec i profil. Samo przypisanie referencji - nie ma jak zawiesc
        /// w polowie. Idempotentne.
        /// </summary>
        internal void EndExperiment(MemoryCheckpoint c)
        {
            if (c == null || !ReferenceEquals(c, eksperyment))
            {
                return;
            }
            histories = c.Original;
            ledgers = c.OriginalLedgers ?? new Dictionary<int, ArcLedger>();
            facts = c.OriginalFacts ?? new Dictionary<int, FactLedger>();
            styl = c.OriginalStyle ?? new PlayerStyleLedger();
            profileId = c.ProfileId;
            eksperyment = null;
        }

        public override void ExposeData()
        {
            // KOLEJNOSC MA ZNACZENIE: bufor musi powstac PRZED Scribe_Collections.Look,
            // bo to jego zawartosc idzie na dysk.
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                BuildSaveBuffer();
            }

            Scribe_Values.Look(ref memoryVersion, "wersjaPamieci", 0, true);
            Scribe_Values.Look(ref runId, "runId", string.Empty, true);

            // PAS BEZPIECZENSTWA: zapis w trakcie eksperymentu (nie powinien sie zdarzyc - akcja jest
            // synchroniczna, autozapis nie wpadnie w jej srodek) utrwala ORYGINALNY profil i pamiec,
            // nigdy stan ramienia.
            if (Scribe.mode == LoadSaveMode.Saving && eksperyment != null)
            {
                string profilOryginalny = eksperyment.ProfileId;
                Scribe_Values.Look(ref profilOryginalny, "profil", string.Empty, true);
            }
            else
            {
                Scribe_Values.Look(ref profileId, "profil", string.Empty, true);
            }

            // LISTY ROBOCZE SA TU NIEPOTRZEBNE i to nie jest niedopatrzenie.
            // Scribe_Collections.Look przesuwa budowanie slownika na faze ResolvingCrossRefs
            // TYLKO wtedy, gdy ktorykolwiek LookMode jest Reference; przy Value + Deep
            // BuildDictionary leci w tym samym przebiegu LoadingVars, w ktorym wypelniane sa
            // listy pomocnicze, wiec krotkie przeciazenie jest poprawne. Ten sam wzorzec ma
            // waniliowy StoryState.lastFireTicks (LookMode.Def + LookMode.Value).
            Scribe_Collections.Look(ref zapis, "pamiecMap", LookMode.Value, LookMode.Deep);

            // WEZEL STYLU GRACZA (krok 7) - osobny, z tego samego powodu co "fakty": blad kodeka jednej
            // ksiegi nie moze kaskadowac na reszte pamieci. W trakcie eksperymentu - oryginal, nie ramie.
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                PlayerStyleLedger doZapisu = eksperyment != null && eksperyment.OriginalStyle != null
                    ? eksperyment.OriginalStyle
                    : Style;
                stylZapis = doZapisu.ToPersistableLines();
            }
            Scribe_Collections.Look(ref stylZapis, "stylGracza", LookMode.Value);

            // Rehydratacja idzie TUTAJ, a nie w PostLoadInit, mimo ze wanilia lubi tamta faze.
            // Powod: komponent DOLOZONY przez FillComponents() do zapisu, w ktorym go nie bylo
            // (czyli kazdy zapis sprzed tej zmiany), NIE przechodzi przez ScribeExtractor,
            // wiec NIGDY nie dostaje ani ResolvingCrossRefs, ani PostLoadInit. Odbudowa jest
            // przy tym czysto liczbowa (int -> EventHistory) i nie potrzebuje map, ktorych
            // w tej fazie i tak jeszcze nie ma.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                RestoreFromSaveBuffer();
            }
        }

        /// <summary>
        /// Przepisuje histories do bufora zapisu, POMIJAJAC mapy, ktorych juz nie ma.
        ///
        /// Sprzatanie sierot jest TYLKO tutaj, przy zapisie, i to jest istotne. W fazie
        /// LoadingVars ExposeData leci z Game.ExposeSmallComponents() PRZED
        /// Scribe_Collections.Look(ref maps, ...), wiec Find.Maps jest wtedy PUSTA - to samo
        /// sprzatanie wykonane tam skasowaloby CALA pamiec przy kazdym wczytaniu.
        /// </summary>
        private void BuildSaveBuffer()
        {
            zapis = new Dictionary<int, MapMemoryRecord>();
            memoryVersion = MemoryFormatVersion;

            if (histories == null)
            {
                histories = new Dictionary<int, EventHistory>();
                return;
            }

            // W trakcie eksperymentu zapisujemy ORYGINAL, nie kopie ramienia.
            Dictionary<int, EventHistory> zrodlo = eksperyment != null ? eksperyment.Original : histories;
            if (eksperyment != null)
            {
                PNLog.Error("Zapis gry W TRAKCIE eksperymentu symulacyjnego - utrwalono oryginalna pamiec, "
                            + "nie stan ramienia. To nie powinno sie zdarzyc (akcja jest synchroniczna).");
            }

            List<Map> mapy = Current.Game != null ? Current.Game.Maps : null;

            // Sprzatamy WYLACZNIE wtedy, gdy naprawde widzimy jakies mapy. Pusta lista map
            // przy zapisie nie powinna sie zdarzyc, ale gdyby sie zdarzyla (zapis w nietypowym
            // momencie cyklu zycia), interpretacja "wszystkie mapy sa sierotami" kosztowalaby
            // cala pamiec narratora. Cena tej ostroznosci to co najwyzej kilka martwych wpisow.
            bool moznaSprzatac = mapy != null && mapy.Count > 0;
            int sierot = 0;

            // Mapy z historia LUB z ksiega lukow (krok 5): luk moze ruszyc w obserwacji, zanim
            // mapa podejmie pierwsza decyzje, a historia moze istniec bez lukow.
            Dictionary<int, ArcLedger> zrodloLukow = eksperyment != null ? eksperyment.OriginalLedgers : ledgers;
            if (zrodloLukow == null)
            {
                zrodloLukow = new Dictionary<int, ArcLedger>();
            }
            // Fakty (krok 6) tak samo: mapa moze miec ksiege faktow bez ksiegi lukow (luki wylaczone,
            // wtedy LedgerFor nigdy nie jest wolane) - bez tej unii zostalaby uznana za pusta.
            Dictionary<int, FactLedger> zrodloFaktow = eksperyment != null ? eksperyment.OriginalFacts : facts;
            if (zrodloFaktow == null)
            {
                zrodloFaktow = new Dictionary<int, FactLedger>();
            }
            var klucze = new SortedSet<int>(zrodlo.Keys);
            klucze.UnionWith(zrodloLukow.Keys);
            klucze.UnionWith(zrodloFaktow.Keys);

            foreach (int klucz in klucze)
            {
                EventHistory h;
                zrodlo.TryGetValue(klucz, out h);
                ArcLedger l;
                zrodloLukow.TryGetValue(klucz, out l);
                FactLedger f;
                zrodloFaktow.TryGetValue(klucz, out f);
                if (h == null && l == null && f == null)
                {
                    continue;
                }

                if (moznaSprzatac && klucz >= 0 && !MapaIstnieje(mapy, klucz))
                {
                    sierot++;
                    continue;
                }

                MapMemoryRecord rekord = new MapMemoryRecord();
                rekord.decisionCount = h == null ? 0 : h.DecisionCount;
                rekord.deliberateSilenceStreak = h == null ? 0 : h.DeliberateSilenceStreak;
                rekord.lines = h == null ? new List<string>() : h.ToPersistableLines();
                rekord.arcLines = l == null ? new List<string>() : l.ToPersistableLines();
                rekord.factLines = f == null ? new List<string>() : f.ToPersistableLines();
                zapis[klucz] = rekord;
            }

            if (sierot > 0)
            {
                PNLog.Decision("Zapis pamieci narratora: pominieto " + sierot.ToString(CultureInfo.InvariantCulture)
                               + " wpis(ow) map, ktorych juz nie ma (porzucone kolonie).");
            }
        }

        private void RestoreFromSaveBuffer()
        {
            // Styl PRZED wczesnym powrotem ponizej: zapis moze miec styl bez zadnej mapy w pamieci
            // (gra zapisana przed pierwsza decyzja narratora - styl obserwuje sie od dnia 0).
            styl = new PlayerStyleLedger();
            odrzuconychStylu = 0;
            if (stylZapis != null && stylZapis.Count > 0)
            {
                odrzuconychStylu = styl.RestoreFromLines(stylZapis);
            }
            stylZapis = new List<string>();

            histories = new Dictionary<int, EventHistory>();
            ledgers = new Dictionary<int, ArcLedger>();
            facts = new Dictionary<int, FactLedger>();
            odrzuconychLinii = 0;
            odrzuconychHistorii = 0;
            odrzuconychLukow = 0;
            odrzuconychFaktow = 0;
            wczytanoZZapisu = true;

            if (zapis == null)
            {
                zapis = new Dictionary<int, MapMemoryRecord>();
                return;
            }

            foreach (KeyValuePair<int, MapMemoryRecord> para in zapis)
            {
                if (para.Value == null)
                {
                    continue;
                }

                EventHistory h = new EventHistory();
                odrzuconychHistorii += h.RestoreFromLines(para.Value.decisionCount,
                                                          para.Value.deliberateSilenceStreak,
                                                          para.Value.lines);
                histories[para.Key] = h;

                // Ksiega lukow: kodek tylko dekoduje; zgodnosc z katalogiem (nieznany luk albo faza
                // po zmianie Defow) rozstrzyga ArcDirector.Reconcile przy pierwszym wywolaniu compa.
                if (para.Value.arcLines != null && para.Value.arcLines.Count > 0)
                {
                    var l = new ArcLedger();
                    odrzuconychLukow += l.RestoreFromLines(para.Value.arcLines);
                    ledgers[para.Key] = l;
                }

                // Ksiega faktow (krok 6) - osobny wezel, osobny kodek. Zapis sprzed wersji 3 nie ma
                // wezla, wiec lista jest pusta, a fakty startuja puste bez ani jednej odrzuconej linii.
                if (para.Value.factLines != null && para.Value.factLines.Count > 0)
                {
                    var f = new FactLedger();
                    odrzuconychFaktow += f.RestoreFromLines(para.Value.factLines);
                    facts[para.Key] = f;
                }
            }

            odrzuconychLinii = odrzuconychHistorii + odrzuconychLukow + odrzuconychFaktow + odrzuconychStylu;

            // Bufor przestaje byc potrzebny natychmiast po odbudowie. Trzymanie go dalej
            // groziloby tym, ze ktos kiedys odczyta z niego nieaktualny stan.
            zapis = new Dictionary<int, MapMemoryRecord>();
        }

        private static bool MapaIstnieje(List<Map> mapy, int uniqueId)
        {
            for (int i = 0; i < mapy.Count; i++)
            {
                if (mapy[i] != null && mapy[i].uniqueID == uniqueId)
                {
                    return true;
                }
            }
            return false;
        }

        // =====================================================================================
        //  ZEGAR GRY - kotwica straznika wywolan spoza zegara (StorytellerComp_Generative)
        // =====================================================================================

        /// <summary>
        /// Tick ostatniego PRAWDZIWEGO przebiegu TickManager.DoSingleTick, znany dopiero po
        /// pierwszym ticku albo po FinalizeInit. int.MinValue = nieznany. NIE jest utrwalany.
        ///
        /// Zdekompilowane DoSingleTick: ticksGameInt++ -&gt; ... -&gt; Find.Storyteller.StorytellerTick()
        /// -&gt; ... -&gt; GameComponentUtility.GameComponentTick(). Prawdziwe wywolanie narratora
        /// w ticku T widzi wiec zegar rowny T-1 (a przy DebugSettings.fastEcology, gdzie tick
        /// skacze o 2000, rowny T-2000). Petle debugowe (waniliowe "future incidents" i pokrewne)
        /// przestawiaja zegar przez DebugSetTicksGame BEZ DoSingleTick, wiec ten warunek nigdy
        /// u nich nie zachodzi - takze przed minDaysPassed i przy pauzie na ticku siatki, gdzie
        /// poprzednia, czysto heurystyczna wersja straznika zawodzila.
        /// </summary>
        private int zegarGry = int.MinValue;

        internal int ZegarGry
        {
            get { return zegarGry; }
        }

        public override void GameComponentTick()
        {
            // Zegar NAJPIERW - obserwatory nie moga go opoznic ani zablokowac wyjatkiem.
            zegarGry = Find.TickManager.TicksGame;
            bool mierz = perf != null && !perfBroken && eksperyment == null;
            if (mierz)
            {
                try
                {
                    perf.BeginTick(zegarGry);
                }
                catch (Exception e)
                {
                    perfBroken = true;
                    mierz = false;
                    PNLog.Error("POMIAR CZASU ([PN-PERF]) rzucil wyjatek i jest WYLACZONY do wczytania zapisu.\n" + e);
                }
            }
            long t0 = Stopwatch.GetTimestamp();
            ObserwujOdpalenia(zegarGry);
            long t1 = Stopwatch.GetTimestamp();
            ObserwujStyl(zegarGry);
            long t2 = Stopwatch.GetTimestamp();
            ObserwujDobe(zegarGry);
            long t3 = Stopwatch.GetTimestamp();
            ObserwujKolonistow(zegarGry);
            ObserwujStanRegul(zegarGry);
            long t4 = Stopwatch.GetTimestamp();
            if (mierz)
            {
                perf.RecordObservers(Evaluation.PerfMonitor.Us(t0, t1), Evaluation.PerfMonitor.Us(t1, t2),
                                     Evaluation.PerfMonitor.Us(t2, t3), Evaluation.PerfMonitor.Us(t3, t4));
            }
        }

        // ---------------------------------------------------------------- OBSERWATORY ETAPU L (krok 9)

        private Evaluation.DayLogger doba;
        private Evaluation.ColonistWatcher kolonisci;
        private Evaluation.RuleStateWatcher stanRegul;
        private Evaluation.PerfMonitor perf;
        private bool dobaBroken;
        private bool kolonisciBroken;
        private bool stanRegulBroken;
        private bool perfBroken;

        /// <summary>Pomiar czasu dla compa; null w symulatorze albo po awarii.</summary>
        internal Evaluation.PerfMonitor Perf
        {
            get { return perfBroken || eksperyment != null ? null : perf; }
        }

        private void ObserwujDobe(int tick)
        {
            if (dobaBroken || eksperyment != null || doba == null)
            {
                return;
            }
            try
            {
                uint rand0 = Arcs.RandCanary.Read();
                doba.Tick(tick);
                Arcs.RandCanary.Check(rand0, "linia dobowa [PN-DZIEN]");
            }
            catch (Exception e)
            {
                dobaBroken = true;
                PNLog.Error("LINIA DOBOWA ([PN-DZIEN]) rzucila wyjatek i jest WYLACZONA do wczytania zapisu - luka w danych "
                            + "ewaluacji, nie brak zmian w kolonii.\n" + e);
            }
        }

        private void ObserwujKolonistow(int tick)
        {
            if (kolonisciBroken || eksperyment != null || kolonisci == null)
            {
                return;
            }
            try
            {
                uint rand0 = Arcs.RandCanary.Read();
                kolonisci.Tick(tick);
                Arcs.RandCanary.Check(rand0, "obserwator kolonistow [PN-KOLONISTA]");
            }
            catch (Exception e)
            {
                kolonisciBroken = true;
                PNLog.Error("OBSERWATOR KOLONISTOW ([PN-KOLONISTA]) rzucil wyjatek i jest WYLACZONY do wczytania zapisu.\n" + e);
            }
        }

        private void ObserwujStanRegul(int tick)
        {
            if (stanRegulBroken || eksperyment != null || stanRegul == null)
            {
                return;
            }
            try
            {
                uint rand0 = Arcs.RandCanary.Read();
                stanRegul.Tick(tick);
                Arcs.RandCanary.Check(rand0, "stan regul RN [PN-STAN]");
            }
            catch (Exception e)
            {
                stanRegulBroken = true;
                PNLog.Error("STAN REGUL RN ([PN-STAN]) rzucil wyjatek i jest WYLACZONY do wczytania zapisu - ekspozycja regul RN "
                            + "od tej chwili nieznana.\n" + e);
            }
        }

        /// <summary>Numer kolejnej akcji "PN: wymus akcje" w sesji (1, 2...) - kilka akcji w jednej pauzie ma ten sam tick.</summary>
        private int wymuszen;

        internal int NastepneWymuszenie()
        {
            return ++wymuszen;
        }

        /// <summary>Odpalenie z "PN: wymus akcje" - detektor go nie zobaczy (UI po przegladzie ticku), wiec logujemy wprost.</summary>
        internal void LogujWymuszoneOdpalenie(int tick, Map map, string incydent, List<Letter> listy, int numer)
        {
            if (strazOdpalen == null || odpaleniaBroken || eksperyment != null)
            {
                return;
            }
            try
            {
                strazOdpalen.LogForced(tick, map, incydent, listy, numer);
            }
            catch (Exception e)
            {
                PNLog.Warn("Nie udalo sie zalogowac wymuszonego odpalenia w [PN-FIRED]: " + e.Message);
            }
        }

        /// <summary>
        /// Start gry ewaluacyjnej (etap L, decyzja L-4): nowy runId "etykieta-narrator-guid", profil wymuszony albo
        /// wylosowany z nowego runId, wyczyszczona pamiec narratora i styl gracza, kotwica skladu kolonii od nowa.
        /// Linia [PN-EVAL] niesie stan sprzed czyszczenia.
        /// </summary>
        internal void StartEvaluationGame(string etykieta, string profil)
        {
            string poprzedni = runId;
            string mapy = OpisMap(), luki = OpisLukow(), fakty = OpisFaktow(), stylOpis = OpisStylu();
            string narrator = Current.Game == null || Current.Game.storyteller == null || Current.Game.storyteller.def == null
                ? "?" : Current.Game.storyteller.def.defName;
            runId = etykieta + "-" + narrator + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            histories = new Dictionary<int, EventHistory>();
            ledgers = new Dictionary<int, ArcLedger>();
            facts = new Dictionary<int, FactLedger>();
            styl = new PlayerStyleLedger();
            if (string.IsNullOrEmpty(profil))
            {
                PrzydzielProfil("gra ewaluacyjna " + etykieta);
            }
            else
            {
                profileId = profil;
            }
            PNLog.Eval(poprzedni, runId, etykieta, EffectiveProfileId, !string.IsNullOrEmpty(profil), mapy, luki, fakty, stylOpis);
            if (kolonisci != null)
            {
                kolonisci.Reset();
            }
            if (stanRegul != null)
            {
                stanRegul.Reset();
            }
            // Pomiar czasu od nowa: pomiary sprzed startu nie moga trafic do linii [PN-PERF] nowego runId.
            if (perf != null)
            {
                perf.Reset();
            }
            PNLog.Decision("GRA EWALUACYJNA: runId=" + runId + " (poprzednio " + poprzedni + "), narrator=" + narrator
                           + ", profil=" + EffectiveProfileId + (string.IsNullOrEmpty(profil) ? " (losowany)" : " (wymuszony)")
                           + ". Pamiec narratora i styl gracza wyczyszczone.");
        }

        // ---------------------------------------------------------------- LOG ODPALEN (krok 8)

        /// <summary>
        /// Obserwator odpalen incydentow ([PN-FIRED], decyzja autora K8-2). Tworzony od nowa w FinalizeInit
        /// z tickiem startu = tick wczytania, wiec odpalenia sprzed zapisu nie wracaja jako nowe. NIE jest
        /// utrwalany: po wczytaniu linia [PN-LOAD] i tak rozwidla analize.
        /// </summary>
        private Evaluation.IncidentFireWatcher strazOdpalen;

        /// <summary>Bezpiecznik obserwatora odpalen: wyjatek wylacza go do wczytania zapisu, jeden raport.</summary>
        private bool odpaleniaBroken;

        /// <summary>
        /// Comp rejestruje tu zdarzenie oddawane grze w tym ticku (przed yield) - obserwator oznaczy je pn=1.
        /// </summary>
        internal void ZarejestrujWlasneOdpalenie(int tick, int mapId, string incydent)
        {
            // Przy spalonym bezpieczniku obserwator nie sprzata rejestru - nie dopisujemy (przeglad S10).
            if (strazOdpalen != null && eksperyment == null && !odpaleniaBroken)
            {
                strazOdpalen.RegisterOwn(tick, mapId, incydent);
            }
        }

        /// <summary>Cofa rejestracje zdarzenia, ktorego nasz TryFire nie wykonal (przeglad S10).</summary>
        internal void WyrejestrujWlasneOdpalenie(int tick, int mapId, string incydent)
        {
            if (strazOdpalen != null)
            {
                strazOdpalen.UnregisterOwn(tick, mapId, incydent);
            }
        }

        private void ObserwujOdpalenia(int tick)
        {
            if (odpaleniaBroken || eksperyment != null || strazOdpalen == null)
            {
                return;
            }
            try
            {
                uint rand0 = Arcs.RandCanary.Read();
                strazOdpalen.Tick(tick);
                Arcs.RandCanary.Check(rand0, "obserwator odpalen incydentow");
            }
            catch (Exception e)
            {
                odpaleniaBroken = true;
                PNLog.Error("OBSERWATOR ODPALEN ([PN-FIRED]) rzucil wyjatek i jest WYLACZONY do wczytania zapisu. "
                            + "Narrator dziala dalej; brakujace linie [PN-FIRED] od tej chwili sa luka w danych "
                            + "ewaluacji, nie brakiem zdarzen.\n" + e);
            }
        }

        /// <summary>
        /// Krok obserwatora stylu gracza (krok 7) w kazdym ticku, od dnia 0, w kazdej grze. Wlasny
        /// bezpiecznik: wyjatek wylacza obserwacje do wczytania zapisu, jeden raport, narrator liczy dalej
        /// bez stylu. W trakcie eksperymentu nic (symulator i tak nie tyka komponentow gry).
        /// </summary>
        private void ObserwujStyl(int tick)
        {
            if (stylBroken || eksperyment != null)
            {
                return;
            }
            try
            {
                PlayerStyleParams p = StyleParams;
                StyleDaySample dzien = PlayerStyleObserver.Tick(Style, p, tick);
                if (dzien != null)
                {
                    PNLog.Player(dzien, PlayerStyleModel.Evaluate(Style, p));
                }
            }
            catch (Exception e)
            {
                stylBroken = true;
                PNLog.Error("OBSERWATOR STYLU GRACZA rzucil wyjatek i jest WYLACZONY do wczytania zapisu (komponent "
                            + "powstaje wtedy od nowa). Narrator dziala dalej BEZ stylu: kolumny stylu puste, luki pod styl "
                            + "sie nie otworza. Ksiega stylu zostaje w stanie sprzed wyjatku i tak trafi do zapisu.\n" + e);
            }
        }

        /// <summary>
        /// Wolane w OBU sciezkach - przy nowej grze i przy wczytaniu - PRZED StartedNewGame
        /// i LoadedGame (dekompilacja Verse.Game: InitNewGame i LoadGame). Robi to, co ma byc
        /// wspolne: zegar gry, straznik null kolekcji, runId i ostrzezenie o wersji formatu.
        /// Kanarek [PN-LOAD] CELOWO nie idzie stad, tylko z tamtych hookow - tu nie da sie odroznic
        /// nowej gry od wczytania.
        /// </summary>
        public override void FinalizeInit()
        {
            // Po wczytaniu gra stoi na ticku zapisu; pierwszy prawdziwy DoSingleTick to tick+1.
            zegarGry = Find.TickManager != null ? Find.TickManager.TicksGame : int.MinValue;

            // Obserwatory od nowa: odpalenia do ticku wczytania (wlacznie) sa juz za nami; doba i sklad kolonii
            // zaczynaja od pierwszego ticku (kotwica), czas od najblizszej granicy doby.
            strazOdpalen = new Evaluation.IncidentFireWatcher(Find.TickManager != null ? Find.TickManager.TicksGame : 0);
            odpaleniaBroken = false;
            doba = new Evaluation.DayLogger();
            kolonisci = new Evaluation.ColonistWatcher();
            stanRegul = new Evaluation.RuleStateWatcher();
            perf = new Evaluation.PerfMonitor();
            dobaBroken = kolonisciBroken = stanRegulBroken = perfBroken = false;

            if (histories == null)
            {
                histories = new Dictionary<int, EventHistory>();
            }
            if (ledgers == null)
            {
                ledgers = new Dictionary<int, ArcLedger>();
            }
            if (facts == null)
            {
                facts = new Dictionary<int, FactLedger>();
            }
            if (zapis == null)
            {
                zapis = new Dictionary<int, MapMemoryRecord>();
            }
            if (styl == null)
            {
                styl = new PlayerStyleLedger();
            }
            if (stylZapis == null)
            {
                stylZapis = new List<string>();
            }
            // Parametry stylu rozwiazywane od nowa w kazdej grze (Def mogl sie zmienic miedzy sesjami).
            stylParams = null;

            if (string.IsNullOrEmpty(runId))
            {
                runId = Guid.NewGuid().ToString("N").Substring(0, 8);
            }

            if (wczytanoZZapisu && memoryVersion > 0 && memoryVersion < ArcsSinceVersion)
            {
                // Zapis sprzed kroku 5: niczego nie pominieto - wezla "luki" po prostu nie bylo.
                // Osobny komunikat, bo ogolny ("wpisy pominiete") sugerowalby utrate danych.
                PNLog.Decision("Pamiec narratora z zapisu w wersji "
                               + memoryVersion.ToString(CultureInfo.InvariantCulture)
                               + " (sprzed lukow narracyjnych): historia wczytana, luki startuja puste, "
                               + "fakty i styl gracza od tego momentu.");
            }
            else if (wczytanoZZapisu && memoryVersion >= ArcsSinceVersion && memoryVersion < FactsSinceVersion)
            {
                // Zapis z kroku 5: historia i luki sa, wezla "fakty" nie bylo. Ta galaz MUSI stac
                // PRZED galezia ogolna - inaczej zapis w wersji 2 dostalby falszywe ostrzezenie
                // o pominietych wpisach, choc niczego nie pominieto.
                PNLog.Decision("Pamiec narratora z zapisu w wersji "
                               + memoryVersion.ToString(CultureInfo.InvariantCulture)
                               + " (sprzed faktow blackboardu): historia i luki wczytane, fakty startuja puste, "
                               + "styl gracza od tego momentu.");
            }
            else if (wczytanoZZapisu && memoryVersion >= FactsSinceVersion && memoryVersion < StyleSinceVersion)
            {
                // Zapis z kroku 6 (v3): wezla "stylGracza" nie bylo. Ta galaz tez MUSI stac przed ogolna -
                // inaczej v3 dostalby falszywe ostrzezenie o pominietych wpisach.
                PNLog.Decision("Pamiec narratora z zapisu w wersji "
                               + memoryVersion.ToString(CultureInfo.InvariantCulture)
                               + " (sprzed stylu gracza): historia, luki i fakty wczytane; styl gracza startuje "
                               + "od tego momentu (rozgrzewka liczy sie od wczytania).");
            }
            else if (wczytanoZZapisu && memoryVersion != MemoryFormatVersion)
            {
                // NIE KASUJEMY pamieci przy rozjezdzie wersji. EventHistoryEntry.TryDecode
                // odrzuca niepasujace linie POJEDYNCZO i nigdy nie rzuca, wiec proba wczytania
                // jest bezpieczna, a cicha utrata calego bufora bylaby gorsza niz ostrzezenie.
                PNLog.Warn("Pamiec narratora pochodzi z zapisu w wersji formatu "
                           + memoryVersion.ToString(CultureInfo.InvariantCulture)
                           + ", a biezaca jest " + MemoryFormatVersion.ToString(CultureInfo.InvariantCulture)
                           + ". Wpisy nie do odczytania zostaly pominiete pojedynczo.");
            }

            // memoryVersion ZOSTAJE wartoscia Z ZAPISU (0 przy nowej grze i przy zapisie bez
            // pamieci) - czyta ja kanarek [PN-LOAD]. Wczesniej nadpisywano ja tutaj biezaca wersja,
            // wiec kanarek pokazywal zawsze 1 i pole nie nioslo zadnej informacji. Przed zapisem
            // wersje ustawia BuildSaveBuffer.
        }

        /// <summary>
        /// Kanarek idzie z tego hooka, a NIE z FinalizeInit, bo FinalizeInit leci w obu
        /// sciezkach naraz i nie da sie w nim odroznic nowej gry od wczytania. Oba hooki sa
        /// wolane PO FinalizeInit (GameComponentUtility.StartedNewGame / LoadedGame chodza po
        /// calej liscie komponentow, takze po tych dolozonych przez FillComponents), wiec
        /// straznik null i runId sa juz gotowe.
        /// </summary>
        public override void StartedNewGame()
        {
            PrzydzielProfil("nowa rozgrywka");
            WypiszKanarka("nowaGra");
        }

        public override void LoadedGame()
        {
            // "zapis" = nasz blok wczytany; "zapisBezPamieci" = zapis bez bloku (sprzed trwalosci albo mod dolozony do
            // trwajacej gry) - bez tego rozroznienia "trwalosc nie dziala" i "zapis jest starszy" wygladalyby tak samo.
            // Zapis bez profilu dostaje go awaryjnie (odnotowane w logu).
            if (string.IsNullOrEmpty(profileId))
            {
                PrzydzielProfil("wczytany zapis bez profilu");
            }

            WypiszKanarka(wczytanoZZapisu ? "zapis" : "zapisBezPamieci");
        }

        /// <summary>
        /// Losuje profil deterministycznie z runId (GenText.StableStringHash + Avalanche), nie z Verse.Rand - pobranie
        /// z generatora gry przesunelo by kazdy pozniejszy losowy wynik sesji.
        /// </summary>
        private void PrzydzielProfil(string powod)
        {
            int ziarno = SeededRandom.Avalanche(runId == null ? 0 : GenText.StableStringHash(runId));
            NarratorProfileDef wybrany = NarratorProfileCatalog.PickWeighted(ziarno);

            if (wybrany == null)
            {
                profileId = string.Empty;
                PNLog.Error("KATALOG PROFILI NARRATORA PUSTY (albo wszystkie wagi zerowe) - "
                            + "narrator uzyje profilu awaryjnego z wartosciami domyslnymi z kodu. "
                            + "Sprawdz, czy Defs/Storytellers/Profiles_Core.xml uzywa PELNEJ nazwy typu "
                            + "<ProceduralNarrator.Integration.Defs.NarratorProfileDef>.");
                return;
            }

            profileId = wybrany.defName;
            PNLog.Decision("Profil narratora przydzielony (" + powod + "): " + profileId
                           + " \"" + wybrany.label + "\". Katalog: " + NarratorProfileCatalog.DescribeCatalog());
        }

        /// <summary>Kanarek wczytania [PN-LOAD] - diagnoza trwalosci i granica rozwidlenia dla analizy (PNLog.Load).</summary>
        private void WypiszKanarka(string zrodlo)
        {
            if (kanarekWypisany)
            {
                return;
            }
            kanarekWypisany = true;

            int wpisow = 0;
            int decyzji = 0;
            foreach (KeyValuePair<int, EventHistory> para in histories)
            {
                if (para.Value == null)
                {
                    continue;
                }
                wpisow += para.Value.Count;
                decyzji += para.Value.DecisionCount;
            }

            PNLog.Load(runId, zrodlo, EffectiveProfileId, profileId, histories.Count, wpisow, decyzji,
                       odrzuconychLinii, memoryVersion, OpisMap(), OpisLukow(), OpisFaktow(),
                       odrzuconychHistorii, odrzuconychLukow, odrzuconychFaktow, OpisStylu(), odrzuconychStylu);

            if (odrzuconychLinii > 0)
            {
                PNLog.Warn("Przy wczytaniu pamieci narratora odrzucono "
                           + odrzuconychLinii.ToString(CultureInfo.InvariantCulture)
                           + " linii nie do sparsowania (historia " + odrzuconychHistorii.ToString(CultureInfo.InvariantCulture)
                           + ", luki " + odrzuconychLukow.ToString(CultureInfo.InvariantCulture)
                           + ", fakty " + odrzuconychFaktow.ToString(CultureInfo.InvariantCulture)
                           + ", styl " + odrzuconychStylu.ToString(CultureInfo.InvariantCulture)
                           + "). Reszta pamieci zostala zachowana.");
            }
        }

        /// <summary>
        /// Stan stylu gracza - pole "styl" linii [PN-LOAD] i [PN-RESET]:
        /// "dni:N,dzien:D,aktywny:0|1,mocne:Walka/Ekspansja,etykieta:X" (dni w kolejce, dzien w toku,
        /// ewaluacja w tej chwili; mocne i etykieta puste w rozgrzewce). Analiza zaczyna od niego
        /// ciaglosc linii [PN-GRACZ] po rozwidleniu. Ewaluacja w try: pole logu nie moze zabic kanarka.
        /// </summary>
        private string OpisStylu()
        {
            PlayerStyleLedger l = Style;
            StyleReading r = null;
            try
            {
                r = PlayerStyleModel.Evaluate(l, StyleParams);
            }
            catch (Exception)
            {
                r = null;
            }
            bool aktywny = r != null && r.Active;
            return "dni:" + l.Days.Count.ToString(CultureInfo.InvariantCulture)
                   + ",dzien:" + l.CurrentDay.ToString(CultureInfo.InvariantCulture)
                   + ",aktywny:" + (aktywny ? "1" : "0")
                   + ",mocne:" + (aktywny ? r.StrongData() : string.Empty)
                   + ",etykieta:" + (aktywny ? r.Label ?? string.Empty : string.Empty);
        }

        /// <summary>
        /// Stan pamieci per mapa w formacie "uid:decyzji,uid:decyzji" (rosnaco po uid) - pole
        /// "mapy" linii [PN-LOAD] i [PN-RESET]. Suma decyzji po mapach nie wystarcza: regula
        /// uniewazniania porzuconej galezi dziala per mapa.
        /// </summary>
        private string OpisMap()
        {
            if (histories == null || histories.Count == 0)
            {
                return string.Empty;
            }
            var klucze = new List<int>(histories.Keys);
            klucze.Sort();
            var czesci = new List<string>(klucze.Count);
            for (int i = 0; i < klucze.Count; i++)
            {
                EventHistory h = histories[klucze[i]];
                czesci.Add(klucze[i].ToString(CultureInfo.InvariantCulture) + ":"
                           + (h == null ? 0 : h.DecisionCount).ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(",", czesci.ToArray());
        }

        /// <summary>
        /// Otwarte luki per mapa: "uid:luk#nr:faza,..." (rosnaco po uid i numerze) - pole "luki"
        /// linii [PN-LOAD] i [PN-RESET]. Analiza odtwarza z niego stan lukow w chwili wczytania
        /// (rozwidlenie galezi dotyczy lukow tak samo jak historii).
        /// </summary>
        private string OpisLukow()
        {
            if (ledgers == null || ledgers.Count == 0)
            {
                return string.Empty;
            }
            var klucze = new List<int>(ledgers.Keys);
            klucze.Sort();
            var czesci = new List<string>();
            foreach (int k in klucze)
            {
                ArcLedger l = ledgers[k];
                if (l == null)
                {
                    continue;
                }
                var aktywne = new List<ArcInstance>(l.Active);
                aktywne.Sort((a, b) => a.Number.CompareTo(b.Number));
                foreach (ArcInstance a in aktywne)
                {
                    czesci.Add(k.ToString(CultureInfo.InvariantCulture) + ":" + a.ArcId + "#"
                               + a.Number.ToString(CultureInfo.InvariantCulture) + ":" + a.PhaseId);
                }
            }
            return string.Join(",", czesci.ToArray());
        }

        /// <summary>
        /// Fakty per mapa: "uid:klucz=wartosc@dzienUstawienia/czasZycia,..." (rosnaco po uid, potem po
        /// kluczu), plus "uid:kolejka@tick", gdy mapa ma fakty czekajace na rozstrzygniecie - pole
        /// "fakty" linii [PN-LOAD] i [PN-RESET].
        ///
        /// Dzien USTAWIENIA i czas zycia, a nie wiek: analiza odtwarza z tego stan faktow w dowolnej
        /// chwili po wczytaniu (wygasanie jest leniwe, wiec wygasle fakty tez sa w ksiedze - i tez
        /// ida do opisu, bo inaczej odtworzony stan nie zgadzalby sie z zawartoscia zapisu).
        /// </summary>
        private string OpisFaktow()
        {
            if (facts == null || facts.Count == 0)
            {
                return string.Empty;
            }
            var klucze = new List<int>(facts.Keys);
            klucze.Sort();
            var czesci = new List<string>();
            foreach (int k in klucze)
            {
                FactLedger f = facts[k];
                if (f == null)
                {
                    continue;
                }
                string uid = k.ToString(CultureInfo.InvariantCulture);
                foreach (Fact fakt in f.AllSorted())
                {
                    czesci.Add(uid + ":" + fakt.Key + "="
                               + fakt.Value.ToString("G9", CultureInfo.InvariantCulture) + "@"
                               + fakt.SetDay.ToString("G9", CultureInfo.InvariantCulture) + "/"
                               + fakt.LifespanDays.ToString("G9", CultureInfo.InvariantCulture));
                }
                if (f.Pending != null)
                {
                    czesci.Add(uid + ":kolejka@" + f.Pending.Tick.ToString(CultureInfo.InvariantCulture));
                }
            }
            return string.Join(",", czesci.ToArray());
        }
    }
}
