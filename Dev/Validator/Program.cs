using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

class Program
{
    // Lista jest JAWNA, a nie globem po katalogu: walidator ma czytac dokladnie te pliki, ktore
    // czyta gra, i ma sie zapalic, gdy autor tresci doda plik, o ktorym testy nie wiedza.
    // SPROSTOWANIE (S6): dawny komentarz twierdzil, ze wylapuje to straznik liczby klockow - nie
    // wylapywal, bo liczy klocki z TEJ SAMEJ listy. Pilnuje tego TEST 1c (lista == Defs/Blocks/*.xml).
    static readonly string[] Xml = {
        @"D:\Games\RimWorld\Mods\ProceduralNarrator\Defs\Blocks\Blocks_Core.xml",
        @"D:\Games\RimWorld\Mods\ProceduralNarrator\Defs\Blocks\Blocks_Extended.xml",
        @"D:\Games\RimWorld\Mods\ProceduralNarrator\Defs\Blocks\Blocks_Consequences.xml"
    };

    static readonly List<string> Tagi = new List<string> {
        null, "militarny", "spoleczny", "zasoby", "pogoda", "psychika", "szantaz", "dzicz"
    };

    /// <summary>Plytka kopia snapshotu. Testy roznicowe MUSZA zmieniac jedno pole na kopii,
    /// inaczej mutacja wycieka do scenariuszy uzywanych przez wczesniejsze sekcje.</summary>
    static WorldSnapshot Kopia(WorldSnapshot s)
    {
        return new WorldSnapshot
        {
            DaysPassed = s.DaysPassed, ColonistCount = s.ColonistCount,
            ColonyWealth = s.ColonyWealth, WealthRelative = s.WealthRelative,
            MountainRoofCellsNearColony = s.MountainRoofCellsNearColony,
            HasHostileFaction = s.HasHostileFaction, Season = s.Season, IsNight = s.IsNight,
            WildAnimalCount = s.WildAnimalCount, DaysSinceLastEvent = s.DaysSinceLastEvent,
            KidnappedColonistCount = s.KidnappedColonistCount,
            HasPoweredCommsConsole = s.HasPoweredCommsConsole,
            AcuteDownedCount = s.AcuteDownedCount, ColonistsOnMap = s.ColonistsOnMap, Danger = s.Danger,
            ThreatPoints = s.ThreatPoints,
            // POLE DOPISANE PO PRZEGLADZIE: brak MaddenableAnimalCount w tej kopii sprawial,
            // ze testy roznicowe budowaly snapshot z wyzerowanym polem i mutacja wyciekala
            // do scenariuszy bazowych. Kazde NOWE pole WorldSnapshot musi tu wejsc.
            MaddenableAnimalCount = s.MaddenableAnimalCount,
            // Pamiec narratora w postaci kanonicznej (krok 6).
            Threads = s.Threads, Facts = s.Facts, TurnsSinceThemes = s.TurnsSinceThemes,
            // Mocne strony stylu gracza (krok 7).
            StyleStrongSides = s.StyleStrongSides,
            // Pora roku dla ludzi (krok 8, dlug 6) i skazone powietrze (przeglad S10).
            SeasonAcceptableForHumans = s.SeasonAcceptableForHumans,
            ToxicAirActive = s.ToxicAirActive
        };
    }

    /// <summary>
    /// Kopia() pilnowana REFLEKSJA, a nie komentarzem. Kazde publiczne pole WorldSnapshot dostaje
    /// wartosc niedomyslna, a kopia ma je wszystkie odtworzyc. Nowe pole bez kopiowania gasi
    /// walidator od razu - wzorzec, ktory raz juz zawiodl (MaddenableAnimalCount), a przeglad
    /// etapu 4 pokazal, ze usuniecie nowych pol z Kopia() zostawialo walidator zielony.
    /// </summary>
    static void TestKopiaSnapshotu()
    {
        T.Section("TEST K - Kopia() WorldSnapshot kopiuje KAZDE pole (refleksja)");
        var zrodlo = new WorldSnapshot();
        var pola = typeof(WorldSnapshot).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        int nr = 1;
        foreach (var f in pola)
        {
            object v;
            if (f.FieldType == typeof(int)) v = 100 + nr;
            else if (f.FieldType == typeof(float)) v = 100.5f + nr;
            else if (f.FieldType == typeof(bool)) v = !(bool)f.GetValue(zrodlo);
            else if (f.FieldType.IsEnum)
            {
                var wart = Enum.GetValues(f.FieldType);
                v = wart.GetValue(wart.Length - 1);
            }
            // Tekst (postacie kanoniczne pamieci narratora, krok 6): unikalna wartosc na pole.
            // Plytka kopia jest dla niego POPRAWNA, bo string jest niemutowalny - inaczej niz
            // kolekcja, ktora dzielilaby stan miedzy scenariuszami testow roznicowych.
            else if (f.FieldType == typeof(string)) v = ";pole" + nr + "=1@0;";
            else throw new Exception("TEST K: nieobslugiwany typ pola " + f.Name + ": " + f.FieldType);
            f.SetValue(zrodlo, v);
            nr++;
        }
        WorldSnapshot kopia = Kopia(zrodlo);
        var niezgodne = new List<string>();
        foreach (var f in pola)
        {
            if (!Equals(f.GetValue(zrodlo), f.GetValue(kopia))) niezgodne.Add(f.Name);
        }
        T.Ok("STRAZNIK: WorldSnapshot ma pola do sprawdzenia", pola.Length >= 16, "pol: " + pola.Length);
        T.Ok("Kopia() odtwarza wszystkie pola", niezgodne.Count == 0,
             niezgodne.Count == 0 ? "pol: " + pola.Length : "brak: " + string.Join(", ", niezgodne));
    }

    static HashSet<string> Kombinacje(EventComposer c, WorldSnapshot snap, int prob = 3000)
    {
        var set = new HashSet<string>();
        foreach (var tag in Tagi)
            for (int s = 0; s < prob; s++)
            {
                var e = c.TryCompose(new EventRecipe { RequiredActionTag = tag }, new SeededRandom(s), snap);
                if (e != null) set.Add(string.Join(" + ", e.Blocks.Select(b => b.Id)));
            }
        return set;
    }

    static void Main()
    {
        // TEST 0 PRZED wczytaniem: brakujacy plik na liscie wywraca pozniejsze testy wyjatkiem.
        TestsComposition.ListaPlikowKlockow(Xml);
        var (blocks, graph, incompat) = Loader.Load(Xml);
        var composer = new EventComposer(blocks, graph);

        Console.WriteLine($"Wczytano {blocks.Count} klockow, {graph.ForbiddenEdgeCount} zabronionych krawedzi");
        Console.WriteLine($"Warunkow twardych: {blocks.Sum(b => b.Conditions.Count)}, miekkich: {blocks.Sum(b => b.Preferences.Count)}");
        foreach (BlockType t in Enum.GetValues(typeof(BlockType)))
        {
            int n = blocks.Count(b => b.Type == t);
            if (n > 0) Console.Write($"  {t}={n}");
        }
        Console.WriteLine("\n");

        // ---- 1. Przestrzen wydarzen
        var wszystkie = Kombinacje(composer, null);
        Console.WriteLine("[1] Przestrzen wydarzen (snapshot=null, bez filtrowania kontekstem)");
        Console.WriteLine($"    przed dosypka : 32");
        Console.WriteLine($"    po dosypce    : {wszystkie.Count}   (wzrost x{wszystkie.Count / 32.0:0.0})\n");

        // ---- 2. Pokrycie osi
        Console.WriteLine("[2] Akcje wg tematu");
        foreach (var g in blocks.Where(b => b.Type == BlockType.Action).GroupBy(b => b.Theme).OrderBy(g => g.Key.ToString()))
            Console.WriteLine($"    {g.Key,-14} {g.Count()}  ({string.Join(", ", g.Select(x => x.Payload))})");
        var puste = Enum.GetValues(typeof(Theme)).Cast<Theme>()
                        .Where(t => !blocks.Any(b => b.Type == BlockType.Action && b.Theme == t));
        Console.WriteLine($"    puste tematy : {string.Join(", ", puste)}\n");

        // ---- 3. Integralnosc grafu
        var zlamane = wszystkie.Where(k => incompat.Any(pr => k.Contains(pr.Item1) && k.Contains(pr.Item2))).ToList();
        Console.WriteLine($"[3] Kombinacje lamiace graf: {zlamane.Count} / {wszystkie.Count}   (oczekiwane 0)");
        foreach (var z in zlamane.Take(3)) Console.WriteLine($"      BLAD: {z}");
        // ASERCJA, nie wydruk. Wczesniej ta sekcja drukowala "BLAD" i konczyla sie kodem 0,
        // wiec zlamanie grafu przechodzilo przez CI niezauwazone.
        T.EqI("[3] kombinacje lamiace graf kompatybilnosci", zlamane.Count, 0);
        T.Ok("[3] przestrzen kombinacji nie jest pusta", wszystkie.Count > 0, "kombinacji: " + wszystkie.Count);

        var bezsens = new (string opis, string a, string b)[] {
            ("Natura uderza zbrojnie",      "PN_Aktor_Natura",   "PN_Akcja_Napad"),
            ("Zwierze zada okupu",          "PN_Aktor_Dzikie",   "PN_Akcja_Okup"),
            ("Piraci rozpetuja burze",      "PN_Aktor_Piraci",   "PN_Akcja_Burza"),
            ("Obcy sieje ambrozje",         "PN_Aktor_Obcy",     "PN_Akcja_Ambrozja"),
            ("Bogactwo wywoluje infestacje","PN_Trig_Bogactwo",  "PN_Akcja_Rojenie"),
        };
        Console.WriteLine("    jawne bezsensy:");
        foreach (var (opis, a, b) in bezsens)
        {
            int n = wszystkie.Count(k => k.Contains(a) && k.Contains(b));
            Console.WriteLine($"      {(n == 0 ? "OK  " : "BLAD")} {opis,-30} wystapien: {n}");
            T.EqI("[3] jawny bezsens: " + opis, n, 0);
        }
        Console.WriteLine();

        // ---- 4. Twarde warunki wg kontekstu
        var scenariusze = new (string nazwa, WorldSnapshot s)[] {
            ("gorska, bogata, wrogowie, noc, zwierzeta", new WorldSnapshot { DaysPassed=40, ColonistCount=8, ColonyWealth=60000, WealthRelative=4.0f,  MountainRoofCellsNearColony=250, HasHostileFaction=true,  Season=3, IsNight=true,  WildAnimalCount=25, MaddenableAnimalCount=20 }),
            ("otwarta, uboga, wrogowie, dzien",          new WorldSnapshot { DaysPassed=40, ColonistCount=3, ColonyWealth=3000,  WealthRelative=0.35f, MountainRoofCellsNearColony=0,   HasHostileFaction=true,  Season=1, IsNight=false, WildAnimalCount=2,  MaddenableAnimalCount=2 }),
            ("otwarta, uboga, BRAK wrogow i zwierzat",   new WorldSnapshot { DaysPassed=40, ColonistCount=3, ColonyWealth=3000,  WealthRelative=0.35f, MountainRoofCellsNearColony=0,   HasHostileFaction=false, Season=1, IsNight=false, WildAnimalCount=0,  MaddenableAnimalCount=0 }),
            ("dzien 1, start gry",                       new WorldSnapshot { DaysPassed=1,  ColonistCount=3, ColonyWealth=2000,  WealthRelative=0.2f,  MountainRoofCellsNearColony=0,   HasHostileFaction=true,  Season=0, IsNight=false, WildAnimalCount=5,  MaddenableAnimalCount=5 }),
            // Scenariusz DOPELNIAJACY: wszystkie fakty badane w sekcji [5b] ZACHODZA naraz.
            // Bez niego PN_Akcja_Okup i PN_Trig_Cisza byly nieosiagalne w CALYM zestawie testow
            // (zaden scenariusz nie ustawial KidnappedColonistCount, HasPoweredCommsConsole ani
            // DaysSinceLastEvent), wiec asercje kierunku negatywnego przechodzily trywialnie,
            // a literowka typu <min>100</min> albo required=false nie zostalaby wykryta.
            ("po porwaniu: konsola, cisza, noc, gory",    new WorldSnapshot { DaysPassed=40, ColonistCount=6, ColonyWealth=40000, WealthRelative=1.5f,  MountainRoofCellsNearColony=250, HasHostileFaction=true,  Season=2, IsNight=true,  WildAnimalCount=8, MaddenableAnimalCount=6, DaysSinceLastEvent=6f, KidnappedColonistCount=2, HasPoweredCommsConsole=true }),
            // Szosty scenariusz ROZDZIELA wklad dwoch warunkow Okupu: porwany JEST, konsoli NIE MA.
            // Bez niego Cond_PoweredCommsConsole nie mial ani jednej asercji w zadnym kierunku -
            // dalo sie go usunac albo odwrocic bez sladu w wyniku walidatora.
            ("po porwaniu, ale BEZ konsoli lacznosci",    new WorldSnapshot { DaysPassed=40, ColonistCount=6, ColonyWealth=40000, WealthRelative=1.5f,  MountainRoofCellsNearColony=250, HasHostileFaction=true,  Season=2, IsNight=true,  WildAnimalCount=8, MaddenableAnimalCount=6, DaysSinceLastEvent=6f, KidnappedColonistCount=2, HasPoweredCommsConsole=false }),
            // Siodmy scenariusz ROZDZIELA Cond_MaddenableAnimals od Cond_WildAnimals - jedyne
            // pole, ktorym sie rozni, to MaddenableAnimalCount. Bez niego nowy warunek nie mial
            // by ANI JEDNEJ asercji w kierunku negatywnym: we wszystkich pozostalych scenariuszach
            // obie liczby sa albo dodatnie naraz, albo zerowe naraz, wiec warunek dalo by sie
            // podmienic na Cond_WildAnimals bez sladu w wyniku walidatora. To ta sama luka,
            // ktora wczesniej dotyczyla Cond_PoweredCommsConsole.
            // Realny odpowiednik: mapa, na ktorej zyja wylacznie zwierzeta o combatPower powyzej
            // progu workera (thrumbo, slon, megaslimak) - dzikie sa, ale oszalec pojedynczo
            // nie moze zadne.
            ("zwierzeta SA, ale zadne nie moze oszalec",  new WorldSnapshot { DaysPassed=40, ColonistCount=6, ColonyWealth=40000, WealthRelative=1.5f,  MountainRoofCellsNearColony=0,   HasHostileFaction=true,  Season=2, IsNight=false, WildAnimalCount=6, MaddenableAnimalCount=0, DaysSinceLastEvent=6f }),
        };

        Console.WriteLine("[4] Filtrowanie kontekstowe");
        foreach (var (nazwa, s) in scenariusze)
        {
            var k = Kombinacje(composer, s, 1200);
            var inc = k.Select(x => blocks.First(b => b.Type == BlockType.Action && x.Contains(b.Id)).Payload)
                       .Distinct().OrderBy(x => x).ToList();
            Console.WriteLine($"    {nazwa,-42} kombinacji: {k.Count,4}  incydentow: {inc.Count}");

            // ASERCJE, nie wydruk. Zaden scenariusz nie moze byc martwy - pusta przestrzen
            // znaczy, ze narrator w tym kontekscie zawsze milczy z braku materialu.
            T.Ok("[4] scenariusz niepusty: " + nazwa, k.Count > 0, "kombinacji: " + k.Count);

            // Bez wrogiej frakcji nie moze powstac zaden incydent jej wymagajacy.
            if (!s.HasHostileFaction)
            {
                bool wrogie = inc.Contains("RaidEnemy") || inc.Contains("RansomDemand");
                T.Ok("[4] brak wrogow -> brak RaidEnemy i RansomDemand (" + nazwa + ")", !wrogie,
                     "incydenty: " + string.Join(", ", inc));
            }
        }

        // ---- 5. Infestacja tylko w gorach
        bool wyciek = scenariusze.Where(x => x.s.MountainRoofCellsNearColony < 40)
                                 .Any(x => Kombinacje(composer, x.s, 1200).Any(k => k.Contains("PN_Akcja_Rojenie")));
        Console.WriteLine($"\n[5] Infestacja bez stropu gorskiego: {(wyciek ? "!!! WYCIEK !!!" : "nie wystepuje - OK")}");
        // ASERCJA, nie wydruk. Wczesniej "!!! WYCIEK !!!" konczyl sie kodem wyjscia 0.
        T.Ok("[5] Infestacja NIGDY nie powstaje bez stropu gorskiego", !wyciek, null);

        // ---- 5b. REGULA "fakt w tekscie = warunek TWARDY" - regresja
        // Cala ta sekcja CLAUDE.md powstala po realnym bledzie w grze: narrator napisal
        // "Wszystko rozgrywa sie po zmroku" przy noc=False, bo PN_Mod_Noc mial sama preferencje.
        // Do teraz naprawa nie miala ANI JEDNEJ asercji regresyjnej - Cond_Night i Cond_CalmPeriod
        // wystepowaly w walidatorze wylacznie w komentarzach.
        Console.WriteLine("\n[5b] Fakt w tekscie = warunek twardy");
        // PREDYKAT JEST WYPROWADZANY Z SAMEGO KLOCKA, a nie wpisywany z reki.
        // Pierwsza wersja miala literaly (m.in. "DaysSinceLastEvent >= 3f" przy Cond_CalmPeriod,
        // ktory w danych ma minDays 1.5) - test byl wiec OSTRZEJSZY od warunku, ktorego pilnuje,
        // i zglosilby naruszenie na POPRAWNYM zachowaniu, gdyby ktorys scenariusz trafil w luke
        // miedzy 1.5 a 3.0. Mina rozbroila by sie dokladnie wtedy, gdy ktos dolozy scenariusz.
        // Czytajac warunki z katalogu, test NIE MOZE rozjechac sie z danymi.
        var faktyKlockow = new (string klocek, string opis)[]
        {
            ("PN_Mod_Noc",     "jest noc"),
            ("PN_Trig_Cisza",  "byl okres spokoju"),
            ("PN_Akcja_Okup",  "ktos zostal porwany (i sa warunki, by zazadac okupu)"),
            ("PN_Aktor_Dzikie","sa dzikie zwierzeta"),
            ("PN_Aktor_Piraci","istnieje wroga frakcja"),
            // Tekst mowi "Dzika zwierzyna traci rozum i rzuca sie na kazdego, kogo napotka",
            // czyli stwierdza istnienie zwierzecia ZDOLNEGO do amoku - a to wezszy fakt niz
            // "sa dzikie zwierzeta". Predykat jest wyprowadzany z warunkow klocka, wiec test
            // pilnuje Cond_MaddenableAnimals, a nie zgadnietej liczby.
            ("PN_Akcja_Amok",  "jest zwierze zdolne do amoku (i minelo dosc dni)"),
        };
        foreach (var (klocek, opis) in faktyKlockow)
        {
            var blokFaktu = blocks.First(b => b.Id == klocek);
            Func<WorldSnapshot, bool> faktZachodzi = sn => blokFaktu.Conditions.All(c => c.IsMet(sn));
            int naruszen = 0;
            int sprawdzonych = 0;
            foreach (var (nazwa, s) in scenariusze)
            {
                if (faktZachodzi(s))
                {
                    continue;   // fakt zachodzi - klocek WOLNO uzyc
                }
                sprawdzonych++;
                naruszen += Kombinacje(composer, s, 800).Count(k => k.Contains(klocek));
            }
            // DRUGI KIERUNEK: gdy fakt ZACHODZI, klocek musi byc OSIAGALNY. Bez tego asercja
            // powyzej jest spelniana trywialnie przez klocek, ktorego nigdy nie da sie zlozyc -
            // a wtedy literowka w warunku (np. <min>100</min> albo required=false) przechodzi
            // na zielono i cala regula jest pilnowana tylko pozornie.
            int osiagalny = 0;
            int zFaktem = 0;
            foreach (var (nazwa, s) in scenariusze)
            {
                if (!faktZachodzi(s))
                {
                    continue;
                }
                zFaktem++;
                osiagalny += Kombinacje(composer, s, 800).Count(k => k.Contains(klocek));
            }

            Console.WriteLine($"    {klocek,-18} \"{opis}\"  bez faktu: {sprawdzonych} (naruszen {naruszen})  z faktem: {zFaktem} (wystapien {osiagalny})");
            T.EqI("[5b] " + klocek + " nie powstaje, gdy nieprawda ze " + opis, naruszen, 0);
            // STRAZNIK PUSTEGO TESTU: gdyby kazdy scenariusz spelnial fakt, petla nic by nie sprawdzila.
            T.Ok("[5b] STRAZNIK: istnieje scenariusz bez faktu \"" + opis + "\"", sprawdzonych > 0, null);
            T.Ok("[5b] STRAZNIK: istnieje scenariusz Z faktem \"" + opis + "\"", zFaktem > 0, null);
            T.Ok("[5b] " + klocek + " JEST osiagalny, gdy " + opis, osiagalny > 0,
                 "wystapien w scenariuszach z faktem: " + osiagalny);
        }

        // Rozdzielenie wkladu obu warunkow PN_Akcja_Okup: ten sam stan swiata rozni sie WYLACZNIE
        // konsola lacznosci, wiec roznica w wyniku moze pochodzic tylko od Cond_PoweredCommsConsole.
        var zKonsola = scenariusze.First(x => x.nazwa.Contains("konsola, cisza")).s;
        var bezKonsoli = scenariusze.First(x => x.nazwa.Contains("BEZ konsoli")).s;
        int okupZ = Kombinacje(composer, zKonsola, 800).Count(k => k.Contains("PN_Akcja_Okup"));
        int okupBez = Kombinacje(composer, bezKonsoli, 800).Count(k => k.Contains("PN_Akcja_Okup"));
        Console.WriteLine($"    PN_Akcja_Okup: z konsola {okupZ}, bez konsoli {okupBez}");
        T.Ok("[5b] Cond_PoweredCommsConsole DZIALA: z konsola Okup powstaje", okupZ > 0, "wystapien: " + okupZ);
        T.EqI("[5b] Cond_PoweredCommsConsole DZIALA: bez konsoli Okup nie powstaje", okupBez, 0);

        // STRAZNIK PODMIANY WARUNKU. Petla [5b] wyprowadza predykat z warunkow SAMEGO klocka,
        // wiec dowodzi, ze klocek szanuje swoje warunki - a NIE, ze warunki sa te wlasciwe.
        // Gdyby ktos podmienil Cond_MaddenableAnimals na luzniejszy Cond_WildAnimals "dla
        // uproszczenia", petla przeszlaby na zielono, bo predykat zmienilby sie razem z danymi.
        // Ta asercja mierzy RUZNICE ZACHOWANIA dwoch warunkow na jednym snapshocie: zwierzeta
        // sa (Szal wolno), ale zadne nie moze oszalec pojedynczo (Amok nie wolno). Po podmianie
        // warunku amokOgraniczony przestanie byc zerem.
        var zwierzetaNieamokowe = new WorldSnapshot {
            DaysPassed = 40, ColonistCount = 6, ColonyWealth = 40000, WealthRelative = 1.5f,
            MountainRoofCellsNearColony = 0, HasHostileFaction = true, Season = 3, IsNight = false,
            WildAnimalCount = 6, MaddenableAnimalCount = 0, DaysSinceLastEvent = 6f
        };
        var kombiNieamokowe = Kombinacje(composer, zwierzetaNieamokowe, 800);
        int szalTam = kombiNieamokowe.Count(k => k.Contains("PN_Akcja_Szal"));
        int amokTam = kombiNieamokowe.Count(k => k.Contains("PN_Akcja_Amok"));
        Console.WriteLine($"    zwierzeta bez zdolnosci do amoku: Szal {szalTam}, Amok {amokTam}");
        T.Ok("[5b] Cond_WildAnimals i Cond_MaddenableAnimals to ROZNE warunki: Szal powstaje",
             szalTam > 0, "wystapien Szalu: " + szalTam);
        T.EqI("[5b] Cond_WildAnimals i Cond_MaddenableAnimals to ROZNE warunki: Amok nie powstaje",
              amokTam, 0);

        // ---- 5c. Progi startowe: zagrozenia dopiero od dnia 11, reszta od dnia 5
        // Prog compa (5) odwzorowuje tor Misc, a wyzszy prog ThreatBig jest realizowany PER AKCJA
        // przez Cond_MinDaysPassed. Sprawdzamy to BEHAWIORALNIE - przez to, co da sie zlozyc -
        // a nie przez zagladanie do XML, zeby test nie zmienil sie w kopie konfiguracji.
        Console.WriteLine("\n[5c] Progi startowe akcji");
        var threatBig = new[] { "RaidEnemy", "Infestation", "ManhunterPack", "PsychicEmanatorShipPartCrash" };
        // ZNALEZIONE PRZEGLADEM ADWERSARIALNYM: te dwa snapshoty byly SLEPE na trzy akcje naraz.
        // Nie ustawialy MaddenableAnimalCount (domyslnie 0 -> Amok zablokowany) ani ThreatPoints
        // (domyslnie 0 -> Rojenie i Emanator zablokowane przez Cond_MinThreatPoints). Skutek:
        // sekcja istniejaca PO TO, zeby pilnowac progow startowych behawioralnie, nie pilnowala
        // progu jedynej akcji ThreatSmall - podmiana <min>11</min> na <min>2</min> w PN_Akcja_Amok
        // zostawiala walidator zielony. Kazde nowe pole snapshotu, ktore bramkuje jakas akcje,
        // musi tu wejsc, inaczej ta akcja cicho wypada z testu progow.
        var snapD10 = new WorldSnapshot { DaysPassed=10, ColonistCount=6, ColonyWealth=40000, WealthRelative=1.5f, MountainRoofCellsNearColony=250, HasHostileFaction=true, Season=2, IsNight=true, WildAnimalCount=8, MaddenableAnimalCount=6, ThreatPoints=600f, DaysSinceLastEvent=6f, KidnappedColonistCount=2, HasPoweredCommsConsole=true };
        var snapD40 = new WorldSnapshot { DaysPassed=40, ColonistCount=6, ColonyWealth=40000, WealthRelative=1.5f, MountainRoofCellsNearColony=250, HasHostileFaction=true, Season=2, IsNight=true, WildAnimalCount=8, MaddenableAnimalCount=6, ThreatPoints=600f, DaysSinceLastEvent=6f, KidnappedColonistCount=2, HasPoweredCommsConsole=true };

        Func<WorldSnapshot, List<string>> incydenty = sn => Kombinacje(composer, sn, 1200)
            .Select(x => blocks.First(b => b.Type == BlockType.Action && x.Contains(b.Id)).Payload)
            .Distinct().OrderBy(x => x).ToList();

        var incD10 = incydenty(snapD10);
        var incD40 = incydenty(snapD40);
        int wszystkichPayloadow = blocks.Count(b => b.Type == BlockType.Action);
        // Mianownik JAWNIE w wydruku - bez niego lista dnia 40 czyta sie jak komplet katalogu,
        // a nie jak podzbior. Wczesniej brzmiala "wszystkie 12" przy dziesieciu z trzynastu.
        Console.WriteLine($"    dzien 10: {incD10.Count}/{wszystkichPayloadow}: {string.Join(", ", incD10)}");
        Console.WriteLine($"    dzien 40: {incD40.Count}/{wszystkichPayloadow}: {string.Join(", ", incD40)}");

        var przedwczesne = threatBig.Where(x => incD10.Contains(x)).ToList();
        T.EqI("[5c] w dniu 10 NIE powstaje zadne ThreatBig (prog 11 jak u Cassandry)", przedwczesne.Count, 0);
        // Kierunek drugi - inaczej asercja wyzej bylaby spelniona przez katalog, w ktorym
        // zagrozen nie da sie zlozyc NIGDY.
        T.Ok("[5c] w dniu 40 ThreatBig JEST osiagalny", threatBig.Any(x => incD40.Contains(x)),
             "osiagalne: " + string.Join(", ", threatBig.Where(x => incD40.Contains(x))));
        // Tor Misc ma ruszac wczesniej (prog compa 5), wiec w dniu 10 musi juz cos dawac.
        T.Ok("[5c] w dniu 10 tor Misc juz dziala", incD10.Count > 0, "incydentow: " + incD10.Count);

        // ThreatSmall ma WLASNY prog 11 (OnOffCycle(ThreatSmall) u Cassandry) i do przegladu
        // nie mial zadnej asercji - zmiana <min>11</min> na <min>2</min> nie gasila niczego.
        // Sprawdzamy go tym samym behawioralnym sposobem co ThreatBig, w obie strony.
        T.Ok("[5c] w dniu 10 NIE powstaje ThreatSmall (prog 11 jak u Cassandry)",
             !incD10.Contains("AnimalInsanitySingle"), "dzien 10: " + string.Join(", ", incD10));
        T.Ok("[5c] w dniu 40 ThreatSmall JEST osiagalny",
             incD40.Contains("AnimalInsanitySingle"), "dzien 40: " + string.Join(", ", incD40));

        // STRAZNIK MIANOWNIKA: oba snapshoty maja dawac PELNY katalog w dniu 40. Gdyby ktore-
        // kolwiek nowe pole snapshotu znowu zostalo pominiete, akcja wypadnie z listy po cichu -
        // i wlasnie ta asercja ma to zlapac, zamiast zostawic "10 z 13" wygladajace na komplet.
        T.EqI("[5c] w dniu 40 osiagalny jest CALY katalog akcji", incD40.Count, wszystkichPayloadow);

        // ---- 5d. Prog punktow zagrozenia (Cond_MinThreatPoints)
        //
        // Bazowy IncidentWorker.CanFireNow odrzuca kandydata przy points < def.minThreatPoints,
        // a dwa nasze incydenty maja tam 400: PsychicEmanatorShipPartCrash i Infestation.
        // Bez odpowiadajacego warunku twardego kompozycja produkowala kandydatow skazanych
        // na odrzucenie, a czynnik swiezosci premiowal je w nieskonczonosc, bo akcja odrzucona
        // nie trafia do historii. Zmierzone w grze: 44 z 56 odmow z jednego klocka.
        //
        // KONSTRUKCJA TESTU: dwa snapshoty rozniace sie WYLACZNIE punktami zagrozenia, wiec
        // roznica w zbiorze osiagalnych incydentow moze pochodzic tylko od tego warunku.
        // Prog czytamy Z OBIEKTOW WARUNKOW, a nie z literalu 400 - test ma isc za danymi,
        // a nie duplikowac kalibracje (lekcja z sekcji [5b]).
        Console.WriteLine();
        Console.WriteLine("[5d] Prog punktow zagrozenia");

        var zProgiem = blocks
            .Where(b => b.Type == BlockType.Action
                        && b.Conditions.OfType<Cond_MinThreatPoints>().Any())
            .ToList();
        T.Ok("[5d] STRAZNIK: sa klocki z Cond_MinThreatPoints", zProgiem.Count > 0,
             "klockow: " + zProgiem.Count + " (" + string.Join(", ", zProgiem.Select(b => b.Id)) + ")");

        float progMax = zProgiem.SelectMany(b => b.Conditions.OfType<Cond_MinThreatPoints>())
                                .Select(c => c.min).DefaultIfEmpty(0f).Max();

        var snapUbogi = Kopia(snapD40);
        snapUbogi.ThreatPoints = progMax - 1f;
        var snapBogaty = Kopia(snapD40);
        snapBogaty.ThreatPoints = progMax + 1f;

        var incUbogi = incydenty(snapUbogi);
        var incBogaty = incydenty(snapBogaty);
        Console.WriteLine($"    punkty {progMax - 1f:0}: {incUbogi.Count} incydentow");
        Console.WriteLine($"    punkty {progMax + 1f:0}: {incBogaty.Count} incydentow");

        var chronione = zProgiem.Select(b => b.Payload).OrderBy(x => x).ToList();
        var wyciekPunkty = chronione.Where(x => incUbogi.Contains(x)).ToList();
        T.EqI("[5d] ponizej progu punktow zaden chroniony incydent NIE powstaje", wyciekPunkty.Count, 0);
        var brakujace = chronione.Where(x => !incBogaty.Contains(x)).ToList();
        T.EqI("[5d] powyzej progu KAZDY chroniony incydent jest osiagalny", brakujace.Count, 0);

        // Roznica miedzy dwoma zbiorami to DOKLADNIE chronione incydenty - nic wiecej nie
        // moglo sie zmienic, bo snapshoty roznia sie jednym polem.
        var roznica = incBogaty.Except(incUbogi).OrderBy(x => x).ToList();
        T.EqS("[5d] roznica zbiorow == dokladnie incydenty z progiem punktow",
              string.Join(", ", roznica), string.Join(", ", chronione));

        // ---- 6. contextFit rozroznia sytuacje
        Console.WriteLine("\n[6] Ten sam napad w dwoch kontekstach");
        var bogata = scenariusze[0].s;
        var uboga = scenariusze[1].s;
        // POPRAWKA: poprzednia wersja wolala TryCompose DWA RAZY - raz dla bogatej, raz dla ubogiej -
        // z tym samym ziarnem, ale twarde warunki filtruja pule slotow inaczej w kazdym kontekscie,
        // wiec rng.Pick wybieral z INNYCH list i powstawaly dwie ROZNE kompozycje. Asercja
        // "ten sam napad" mowila wiec o dwoch roznych zdarzeniach i zalezala od szczescia
        // w losowaniu. Teraz skladamy RAZ i przeliczamy ten sam obiekt w obu kontekstach,
        // a sprawdzamy WSZYSTKIE znalezione warianty, nie pierwszy z brzegu.
        int porownanych = 0;
        int zlamanych6 = 0;
        for (int s = 0; s < 600; s++)
        {
            var a = composer.TryCompose(new EventRecipe { RequiredActionTag = "militarny" }, new SeededRandom(s), bogata);
            if (a == null || a.ActionPayload != "RaidEnemy") continue;

            ContextEvaluator.Evaluate(a, bogata);
            float fitBogata = a.ContextFit;
            ContextEvaluator.Evaluate(a, uboga);
            float fitUboga = a.ContextFit;

            if (porownanych == 0)
            {
                Console.WriteLine($"    {string.Join(" + ", a.Blocks.Select(x => x.Id))}");
                Console.WriteLine($"      bogata: fit {fitBogata:0.000} | uboga: fit {fitUboga:0.000}");
            }
            porownanych++;
            if (!(fitBogata > fitUboga)) zlamanych6++;
        }
        T.EqI("[6] KAZDY wariant napadu ma wyzszy contextFit w bogatej kolonii niz w ubogiej", zlamanych6, 0);
        // STRAZNIK PUSTEGO TESTU: bez tego petla, ktora nie znalazla ani jednego napadu,
        // przechodzilaby "na zielono" nie sprawdzajac niczego.
        T.Ok("[6] STRAZNIK: znaleziono warianty napadu do porownania", porownanych > 0,
             "porownanych wariantow: " + porownanych);

        // ---- 7. Przyklady opisow
        Console.WriteLine("\n[7] Po jednym opisie na temat");
        foreach (var g in wszystkie.Select(k => composer.TryCompose(new EventRecipe(), new SeededRandom(StabilnyHash(k)), null))
                                   .Where(e => e != null).GroupBy(e => e.Theme).OrderBy(g => g.Key.ToString()))
        {
            var e = g.First();
            Console.WriteLine($"    [{g.Key}] {e.Description}");
        }

        // ============================================================================
        //  KROK 3 - WARSTWA DECYZYJNA. Testy wymagane przez plan (sekcja Weryfikacja).
        // ============================================================================
        var cfg = XmlConfig.Load(@"D:\Games\RimWorld\Mods\ProceduralNarrator\Defs\Storytellers\Storyteller_Generative.xml");
        TestsComposition.Run(blocks, graph, composer, cfg.candidateBudget);
        TestsDecision.Run(composer, cfg);
        TestsTension.Run(cfg);
        TestsAudit2.Run(cfg, blocks);
        TestsArcs.Run(blocks, composer, cfg);
        TestsComposition.KontraktTresci(blocks, graph, cfg, Xml);
        TestsBlackboard.Run();
        TestsPlayerStyle.Run(cfg);
        TestsPlayerStyleDecision.Run(cfg, composer);
        // Krok 8, czesc moda.
        TestsExecutionSpec.Run();
        TestsTextVariants.Run(blocks, composer);
        TestsFireDetector.Run();
        TestsCacheGuard.Run();
        TestsMinorDebts.Run(composer, cfg);
        TestKopiaSnapshotu();

        Console.WriteLine();
        Console.WriteLine("================================================================");
        Console.WriteLine($"  PODSUMOWANIE: {T.Passed} OK, {T.Failed} BLEDOW");
        Console.WriteLine("================================================================");
        foreach (var f in T.Failures) Console.WriteLine("  BLAD: " + f);
        Environment.ExitCode = T.Failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// Stabilny hash lancucha (FNV-1a). string.GetHashCode() jest w .NET Core RANDOMIZOWANY
    /// per proces, wiec uzyty jako ziarno dawal INNE przykladowe opisy przy kazdym uruchomieniu -
    /// walidator, ktorego wyjscie zmienia sie miedzy przebiegami, nie nadaje sie do diffowania.
    /// </summary>
    static int StabilnyHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) { h = (h ^ c) * 16777619; }
            return (int)(h & 0x7FFFFFFF);
        }
    }
}
