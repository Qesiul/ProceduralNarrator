using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;

/// <summary>
/// TEST 24 - krok 9, K1: tresc Core (pelne pokrycie toru bez DLC) i warunki z rejestru wymagan snapshotu.
///
/// 24a (decyzja autora K1-a, 2026-09-26): Cond_GrowthSeason odwzorowuje sprawdzenie pory wegetacji z CanFireNowSub
/// gry (TemperatureMemory.GrowthSeasonOutdoorsNow: temperatura na zewnatrz w (0, 58) C w ostatnich 30000 tickach).
/// Wyrocznia na prawdziwym katalogu to JAWNA tabela akcji, ktorych worker tego wymaga - nie odczyt z XML.
/// </summary>
static class TestsCoreContent
{
    /// <summary>
    /// Akcje, ktorych worker wymaga pory wegetacji (dekompilacja 1.5.4063). AmbrosiaSprout:
    /// IncidentWorker_AmbrosiaSprout.CanFireNowSub. Kazda nowa akcja K1 z tym wymogiem dopisuje sie TUTAJ.
    /// </summary>
    static readonly string[] WymagajaPoryWegetacji = { "AmbrosiaSprout" };

    /// <summary>
    /// Ustawia pola rejestru K1 tak, zeby KAZDA akcja K1 byla dostepna (warunki spelnione), poza temperatura sezonowa:
    /// fala mrozu (0; 15) i fala upalu (>= 20) wykluczaja sie, wiec test "caly katalog" bierze sume dwoch por roku.
    /// Pomocnik testow uzywajacych recznie zbudowanego snapshotu "pelnego swiata" (Program [5c]/[5d], luki).
    /// </summary>
    public static WorldSnapshot ZTrescia(WorldSnapshot s, float temperaturaSezonowa = 10f)
    {
        s.SeasonalTemp = temperaturaSezonowa;
        s.WeatherOkRaces = CanonicalSet.Of(new[] { "Alphabeaver", "Thrumbo" });
        s.MechanoidFactionExists = true;
        s.WildHerdMinCombatPower = 50f;
        s.FarmAnimalKindAvailable = true;
        s.SelfTameCandidates = 3;
        s.BlightablePlants = 10;
        s.ShortCircuitPossible = true;
        s.GrowthSeasonOutdoors = true;
        s.SeasonAcceptableForHumans = true;
        // Krok 9, K2: pola rejestru Royalty i Anomaly na "dostepne". PawnKindCounts i OngoingQuestScripts puste = zadnej
        // nocisfery i zadnego trwajacego zadania - tez stan "dostepne".
        s.FactionDefsPresent = CanonicalSet.Of(new[] { "HoraxCult" });
        s.WalkableWater = true;
        s.IdleRevenantSpines = 1;
        s.CubeCandidates = 2;
        s.UnnaturalCorpseCandidates = 2;
        s.MetalhorrorGateOpen = true;
        s.InfectablePawns = 2;
        return s;
    }

    /// <summary>
    /// DOPASOWANIA FAZ LUKOW (decyzja autora K1-e, 2026-09-26): dla KAZDEJ fazy kazdego luku dokladny zbior akcji, ktore
    /// do niej pasuja po osiach i tagu (moc pominieta - zalezy od kompozycji). Komunikat fazy musi byc prawdziwy dla
    /// kazdej z nich - tabela to zapis tego przegladu (Docs/K1_PROPOZYCJA.md 6a). Nowa akcja, ktora po cichu wejdzie do
    /// cudzej fazy, zapala test i wymusza przeglad komunikatu. Nazwy bez prefiksow PN_Luk_ / PN_Akcja_.
    /// </summary>
    static readonly Dictionary<string, string> DopasowaniaFazLukow = new Dictionary<string, string>
    {
        { "Wendeta/Zasiew", "Napad" },
        { "Wendeta/Eskalacja", "Napad" },
        { "Wendeta/Kulminacja", "Napad" },
        { "Wendeta/Rozwiazanie", "Ambrozja,Hodowlane,KojacaFala,Meteoryt,Oswojenie,Thrumbo,Wedrowiec,Zrzut" },
        { "GniewNatury/Zasiew", "Alfabobry,Amok" },
        { "GniewNatury/Eskalacja", "Burza,Mroz,Szal,Upal" },
        { "GniewNatury/Kulminacja", "Burza,Mroz,Opad,Rojenie,Szal,SzalStada,Upal,ZimaWulkaniczna" },
        { "GniewNatury/Rozwiazanie", "Ambrozja,Hodowlane,Meteoryt,Oswojenie,Zrzut" },
        { "ZnakiZNieba/Zasiew", "Meteoryt,Zrzut" },
        // Krok 9, K2 (Docs/K2_PROPOZYCJA.md 7a): krwawy deszcz i obeliski spadaja z nieba, klaster laduje kapsulami.
        { "ZnakiZNieba/Eskalacja", "Burza,KrwawyDeszcz,ObeliskA,ObeliskD,ObeliskM" },
        { "ZnakiZNieba/Kulminacja", "Defoliator,Emanator,KlasterMaszyn,Opad" },
        { "ZnakiZNieba/Rozwiazanie", "Meteoryt,Zrzut" },
        { "Scigani/Zasiew", "Abazja,Uchodzcy" },
        { "Scigani/Kulminacja", "Napad" },
        { "Scigani/Rozwiazanie", "Wedrowiec" },
        { "SlawaTwierdzy/Zasiew", "Napad" },
        { "SlawaTwierdzy/Eskalacja", "Okup,Szal" },
        { "SlawaTwierdzy/Kulminacja", "BramaOtchlani,Chimery,Defoliator,KlasterMaszyn,Kolcarze,Miesobestie,Napad,PiesnNienawisci,Pozeracze,"
                                      + "PozeraczeZWody,Rojenie,RytualKultu,SerceZMiesa,SzalStada,SzturmTrupow,Wrzaski" },
        { "SlawaTwierdzy/Rozwiazanie", "Wedrowiec" },
        { "Dostatek/Zasiew", "Ambrozja,Hodowlane,Meteoryt,Oswojenie,Zrzut" },
        { "Dostatek/Eskalacja", "Burza,Mroz,Szal,Upal" },
        { "Dostatek/Kulminacja", "Napad" },
        { "Dostatek/Rozwiazanie", "Ambrozja,Hodowlane,Meteoryt,Oswojenie,Zrzut" },
        { "ZiemiaObiecana/Zasiew", "Wedrowiec" },
        { "ZiemiaObiecana/Eskalacja", "Abazja,Dzikus,Przybysz,PrzybyszM,Uchodzcy" },
        { "ZiemiaObiecana/Kulminacja", "Defoliator,KlasterMaszyn,Napad,Okup,ZrodloKlopotow" },
        { "ZiemiaObiecana/Rozwiazanie", "Ambrozja,Hodowlane,Meteoryt,Oswojenie,Zrzut" },
        { "NiespokojneNoce/Zasiew", "Burza,Szal" },
        { "NiespokojneNoce/Eskalacja", "Burza,Szal" },
        { "NiespokojneNoce/Kulminacja", "Emanator,Kolcarze,Miesobestie,Napad,Pozeracze,PozeraczeZWody,Rojenie,SzalStada,SzturmTrupow,Wrzaski" },
        { "NiespokojneNoce/Rozwiazanie", "Ambrozja,Hodowlane,KojacaFala,Meteoryt,Oswojenie,Thrumbo,Wedrowiec,Zrzut" },
        { "KaprysyPogody/Zasiew", "Burza,Mroz,Upal" },
        { "KaprysyPogody/Kulminacja", "Opad,ZimaWulkaniczna" },
        { "KaprysyPogody/Rozwiazanie", "Ambrozja,Hodowlane,KojacaFala,Meteoryt,Oswojenie,Thrumbo,Wedrowiec,Zrzut" },
        { "RuchWDziczy/Zasiew", "Migracja,Thrumbo" },
        { "RuchWDziczy/Kulminacja", "Szal,SzalStada" },
        { "RuchWDziczy/Rozwiazanie", "Hodowlane,Oswojenie" },
        { "Szepty/Zasiew", "Emanator,FalaPsychiczna" },
        { "Szepty/Kulminacja", "Emanator" },
        { "Szepty/Rozwiazanie", "KojacaFala" },
        { "ChudeDni/Zasiew", "Zaraza,Zwarcie" },
        // K2-f: tag niszczy zamiast toksyny/mechanoidy - ten sam zbior, a klaster mechanoidow (tag mechanoidy) nie wchodzi.
        { "ChudeDni/Kulminacja", "Defoliator,Opad" },
        { "ChudeDni/Rozwiazanie", "Ambrozja,Hodowlane,Meteoryt,Oswojenie,Zrzut" },
        // Krok 9, K2-d: nowy luk. Zdarzenia ukryte (Oczyslepy, Zjawa, Wszczep) nie pasuja do zadnej fazy (K2-b).
        { "CosSieBudzi/Zasiew", "Calun,GarstkaTrupow,Ghul,Kregoslup,KrwawyDeszcz,ObeliskA,ObeliskD,ObeliskM,RojTrupow,RojZwierzat" },
        { "CosSieBudzi/Kulminacja", "BramaOtchlani,Chimery,Ciemnosc,Kolcarze,Miesobestie,Nocisfera,PiesnNienawisci,Pozeracze,PozeraczeZWody,"
                                    + "RytualKultu,SerceZMiesa,SzturmTrupow,Wrzaski" },
        { "CosSieBudzi/Rozwiazanie", "Ambrozja,Hodowlane,KojacaFala,Meteoryt,Oswojenie,Thrumbo,Wedrowiec,Zrzut" },
    };

    public static void Run(EventComposer composer, List<Block> klocki, XmlConfig cfg)
    {
        T.Section("TEST 24 - krok 9, K1: warunki rejestru i tresc Core");

        // ---- 24a. Cond_GrowthSeason - semantyka
        var wegetacja = new WorldSnapshot { GrowthSeasonOutdoors = true };
        var bez = new WorldSnapshot { GrowthSeasonOutdoors = false };
        var c = new Cond_GrowthSeason();
        T.Ok("24a pora wegetacji -> warunek spelniony", c.IsMet(wegetacja), null);
        T.Ok("24a poza pora wegetacji -> warunek niespelniony", !c.IsMet(bez), null);
        T.Ok("24a domyslny snapshot (bez mapy) nie wyklucza akcji", new WorldSnapshot().GrowthSeasonOutdoors, null);
        var odwr = new Cond_GrowthSeason { wantGrowth = false };
        T.Ok("24a wantGrowth=false odwraca warunek", odwr.IsMet(bez) && !odwr.IsMet(wegetacja), null);

        // ---- 24a'. Prawdziwy katalog: poza pora wegetacji odpadaja DOKLADNIE akcje z tabeli.
        Func<bool, List<string>> akcje = w => composer.AvailableActions(new WorldSnapshot
        {
            DaysPassed = 40, ColonistCount = 6, ColonistsOnMap = 6, ColonyWealth = 40000, WealthRelative = 1.5f,
            MountainRoofCellsNearColony = 250, HasHostileFaction = true, Season = 1, IsNight = true, WildAnimalCount = 8,
            MaddenableAnimalCount = 6, ThreatPoints = 600f, DaysSinceLastEvent = 6f, KidnappedColonistCount = 2,
            HasPoweredCommsConsole = true, SeasonAcceptableForHumans = true, GrowthSeasonOutdoors = w
        }, new EventRecipe()).Select(b => b.Payload).ToList();
        var zWegetacja = akcje(true);
        var bezWegetacji = akcje(false);
        T.Ok("24a' STRAZNIK: kazda akcja z tabeli jest dostepna w porze wegetacji",
             WymagajaPoryWegetacji.All(zWegetacja.Contains), string.Join(",", zWegetacja));
        T.EqS("24a' poza pora wegetacji odpadaja dokladnie akcje z tabeli",
              string.Join(",", zWegetacja.Except(bezWegetacji).OrderBy(x => x, StringComparer.Ordinal)),
              string.Join(",", WymagajaPoryWegetacji.OrderBy(x => x, StringComparer.Ordinal)));
        T.EqI("24a' brak pory wegetacji niczego nie dodaje", bezWegetacji.Except(zWegetacja).Count(), 0);

        Semantyka();
        Przelaczniki(composer);
        FazyLukow(klocki, cfg);
    }

    /// <summary>24l: dopasowania faz lukow z katalogu == tabela DopasowaniaFazLukow.</summary>
    static void FazyLukow(List<Block> klocki, XmlConfig cfg)
    {
        var akcje = klocki.Where(b => b.Type == BlockType.Action).ToList();
        int faz = 0;
        foreach (var luk in cfg.arcDefs)
        {
            foreach (var f in luk.phases)
            {
                string klucz = luk.defName.Replace("PN_Luk_", "") + "/" + f.id;
                var pasuja = akcje.Where(b =>
                {
                    var widok = new ProceduralNarrator.Core.Arcs.ArcEventView
                    {
                        ActionBlockId = b.Id, Payload = b.Payload, Theme = b.Theme, Valence = b.Valence, Scale = b.Scale,
                        Intensity = IntensityLevel.VeryHigh, CarriesFaction = b.CarriesFaction, FactionId = b.CarriesFaction ? "F1" : null
                    };
                    foreach (string t in b.Tags) widok.Tags.Add(t);
                    string why;
                    return f.expectations.Any(e => e.Matches(widok, e.sameFaction ? "F1" : null, out why));
                }).Select(b => b.Id.Replace("PN_Akcja_", "")).OrderBy(x => x, StringComparer.Ordinal).ToList();
                string spec;
                T.Ok("24l faza " + klucz + " ma wpis w tabeli dopasowan", DopasowaniaFazLukow.TryGetValue(klucz, out spec), string.Join(",", pasuja));
                if (spec != null)
                {
                    T.EqS("24l faza " + klucz + ": akcje == tabela", string.Join(",", pasuja), spec);
                }
                faz++;
            }
        }
        T.EqI("24l STRAZNIK: kazdy wpis tabeli to istniejaca faza", faz, DopasowaniaFazLukow.Count);
    }

    /// <summary>
    /// 24k: kazde pole rejestru K1 wylacza DOKLADNIE akcje, ktorych incydent tego wymaga - wyrocznia to jawna tabela
    /// z raportu K1 (dekompilacja 1.5.4063), nie odczyt warunkow z XML. Swiat bazowy = ZTrescia (wszystko dostepne);
    /// zmiana JEDNEGO pola, porownanie zbiorow akcji w obie strony.
    /// </summary>
    static void Przelaczniki(EventComposer composer)
    {
        Func<WorldSnapshot> baza = () => ZTrescia(new WorldSnapshot
        {
            DaysPassed = 40, ColonistCount = 6, ColonistsOnMap = 6, ColonyWealth = 40000, WealthRelative = 1.5f,
            MountainRoofCellsNearColony = 250, HasHostileFaction = true, Season = 1, IsNight = true, WildAnimalCount = 8,
            MaddenableAnimalCount = 6, ThreatPoints = 600f, DaysSinceLastEvent = 6f, KidnappedColonistCount = 2,
            HasPoweredCommsConsole = true
        });
        Func<WorldSnapshot, List<string>> dostepne = s => composer.AvailableActions(s, new EventRecipe()).Select(b => b.Payload)
                                                                   .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var wszystkie = dostepne(baza());
        Console.WriteLine("    24k swiat bazowy (10 C): " + wszystkie.Count + " akcji: " + string.Join(", ", wszystkie));
        var przypadki = new List<(string opis, Action<WorldSnapshot> zmien, string[] odpadaja)>
        {
            ("brak pory wegetacji", s => s.GrowthSeasonOutdoors = false, new[] { "AmbrosiaSprout" }),
            ("pogoda nieznosna dla alfabobrow", s => s.WeatherOkRaces = CanonicalSet.Of(new[] { "Thrumbo" }), new[] { "Alphabeavers" }),
            ("pogoda nieznosna dla thrumbo", s => s.WeatherOkRaces = CanonicalSet.Of(new[] { "Alphabeaver" }), new[] { "ThrumboPasses" }),
            ("skazone powietrze", s => s.ToxicAirActive = true, new[] { "ThrumboPasses", "WildManWandersIn" }),
            ("defoliator juz lezy", s => s.ThingCounts = CanonicalSet.OfCounts(new Dictionary<string, int> { { "DefoliatorShipPart", 1 } }),
                new[] { "DefoliatorShipPartCrash" }),
            ("emanator juz lezy", s => s.ThingCounts = CanonicalSet.OfCounts(new Dictionary<string, int> { { "PsychicDronerShipPart", 1 } }),
                new[] { "PsychicDrone", "PsychicSoothe" }),
            ("brak frakcji mechanoidow", s => s.MechanoidFactionExists = false, new[] { "DefoliatorShipPartCrash", "MechCluster" }),
            ("brak stada do szalu", s => s.WildHerdMinCombatPower = float.PositiveInfinity, new[] { "AnimalInsanityMass" }),
            ("brak gatunku hodowlanego", s => s.FarmAnimalKindAvailable = false, new[] { "FarmAnimalsWanderIn" }),
            ("brak zwierzat do oswojenia", s => s.SelfTameCandidates = 0, new[] { "SelfTame" }),
            ("brak upraw podatnych", s => s.BlightablePlants = 0, new[] { "CropBlight" }),
            ("zwarcie niemozliwe", s => s.ShortCircuitPossible = false, new[] { "ShortCircuit" }),
            ("trwa fala mrozu (mapa)", s => { s.GameConditionsMap = CanonicalSet.Of(new[] { "ColdSnap" }); s.GameConditionsAll = s.GameConditionsMap; },
                new[] { "ColdSnap" }),
            ("trwa fala upalu (mapa)", s => { s.GameConditionsMap = CanonicalSet.Of(new[] { "HeatWave" }); s.GameConditionsAll = s.GameConditionsMap; },
                new[] { "ColdSnap" }),
            ("trwa fala upalu tylko na swiecie", s => s.GameConditionsAll = CanonicalSet.Of(new[] { "HeatWave" }), new string[0]),
            ("trwa fala psychiczna", s => { s.GameConditionsMap = CanonicalSet.Of(new[] { "PsychicDrone" }); s.GameConditionsAll = s.GameConditionsMap; },
                new[] { "PsychicDrone", "PsychicSoothe" }),
            ("trwa opad toksyczny", s => { s.GameConditionsMap = CanonicalSet.Of(new[] { "ToxicFallout" }); s.GameConditionsAll = s.GameConditionsMap; },
                new[] { "ToxicFallout" }),
            ("trwa zima wulkaniczna", s => { s.GameConditionsMap = CanonicalSet.Of(new[] { "VolcanicWinter" }); s.GameConditionsAll = s.GameConditionsMap; },
                new[] { "VolcanicWinter" }),
            ("temperatura sezonowa 0 C (granica ostra)", s => s.SeasonalTemp = 0f, new[] { "ColdSnap" }),
            // ---- Krok 9, K2: kazde pole rejestru Royalty i Anomaly wylacza DOKLADNIE akcje z tabeli wymagan gry
            // (raporty rozpoznania K2; Docs/K2_PROPOZYCJA.md sekcja 3 pkt 4).
            ("brak kultu Horaksa", s => s.FactionDefsPresent = string.Empty, new[] { "HateChanters", "PsychicRitualSiege" }),
            ("trwa zadanie ProblemCauser", s => s.OngoingQuestScripts = CanonicalSet.Of(new[] { "ProblemCauser" }), new[] { "ProblemCauser" }),
            ("trwa inne zadanie (nie ProblemCauser)", s => s.OngoingQuestScripts = CanonicalSet.Of(new[] { "WandererJoinAbasia" }), new string[0]),
            ("nocisfera juz jest na mapie", s => s.PawnKindCounts = CanonicalSet.OfCounts(new Dictionary<string, int> { { "Nociosphere", 1 } }),
                new[] { "Nociosphere" }),
            ("brak chodliwej wody", s => s.WalkableWater = false, new[] { "DevourerWaterAssault" }),
            ("kazdy kregoslup juz buczy", s => s.IdleRevenantSpines = 0, new[] { "RevenantEmergence" }),
            ("brak kandydata do szescianu", s => s.CubeCandidates = 0, new[] { "MysteriousCargoCube" }),
            ("brak kandydata do nienaturalnych zwlok", s => s.UnnaturalCorpseCandidates = 0, new[] { "MysteriousCargoUnnaturalCorpse" }),
            ("brama biosygnatury metalhorroru zamknieta", s => s.MetalhorrorGateOpen = false,
                new[] { "CreepJoinerJoin_Metalhorror", "MetalhorrorImplantation" }),
            ("brak pionka z droga zakazenia", s => s.InfectablePawns = 0, new[] { "MetalhorrorImplantation" }),
            ("brama otchlani juz stoi", s => s.ThingCounts = CanonicalSet.OfCounts(new Dictionary<string, int> { { "PitGate", 1 } }),
                new[] { "PitGate" }),
            ("ziemia pod brame juz sie zapada", s => s.ThingCounts = CanonicalSet.OfCounts(new Dictionary<string, int> { { "PitGateSpawner", 1 } }),
                new[] { "PitGate" }),
            ("trwa krwawy deszcz tylko na swiecie", s => s.GameConditionsAll = CanonicalSet.Of(new[] { "BloodRain" }), new[] { "BloodRain" }),
            ("trwa szary calun tylko na swiecie", s => s.GameConditionsAll = CanonicalSet.Of(new[] { "GrayPall" }), new[] { "BloodRain" }),
            ("trwa calun smierci (mapa)", s => { s.GameConditionsMap = CanonicalSet.Of(new[] { "DeathPall" }); s.GameConditionsAll = s.GameConditionsMap; },
                new[] { "BloodRain", "DeathPall" }),
            ("nienaturalna ciemnosc na mapie", s => { s.GameConditionsMap = CanonicalSet.Of(new[] { "UnnaturalDarkness" }); s.GameConditionsAll = s.GameConditionsMap; },
                new[] { "BloodRain", "DeathPall" }),
            ("nienaturalna ciemnosc tylko na swiecie", s => s.GameConditionsAll = CanonicalSet.Of(new[] { "UnnaturalDarkness" }), new string[0]),
        };
        T.Ok("24k STRAZNIK: swiat bazowy daje kazda akcje z tabel przelacznikow",
             przypadki.SelectMany(p => p.odpadaja).All(wszystkie.Contains), string.Join(",", przypadki.SelectMany(p => p.odpadaja).Where(x => !wszystkie.Contains(x))));
        foreach (var (opis, zmien, odpadaja) in przypadki)
        {
            WorldSnapshot s = baza();
            zmien(s);
            var po = dostepne(s);
            T.EqS("24k " + opis + ": odpadaja dokladnie",
                  string.Join(",", wszystkie.Except(po).OrderBy(x => x, StringComparer.Ordinal)),
                  string.Join(",", odpadaja.OrderBy(x => x, StringComparer.Ordinal)));
            T.EqI("24k " + opis + ": nic nie dochodzi", po.Except(wszystkie).Count(), 0);
        }
        // Temperatura: upal od 20 C (nieostra), a fala mrozu wtedy odpada - jedyna para wykluczajaca sie.
        WorldSnapshot cieplo = baza();
        cieplo.SeasonalTemp = 20f;
        var wCiepla = dostepne(cieplo);
        T.EqS("24k temperatura sezonowa 20 C: dochodzi fala upalu, odpada fala mrozu",
              "+" + string.Join(",", wCiepla.Except(wszystkie)) + " -" + string.Join(",", wszystkie.Except(wCiepla)), "+HeatWave -ColdSnap");

        // Fala upalu (25 C): wyklucza ja fala mrozu i zima wulkaniczna NA MAPIE (HeatWave.exclusiveConditions, CanCoexistWith
        // tylko wobec mapy), a sama siebie takze na swiecie (ConditionIsActive siega do rodzica).
        Func<WorldSnapshot> upal = () => { WorldSnapshot s = baza(); s.SeasonalTemp = 25f; return s; };
        var wUpale = dostepne(upal());
        var przypadkiUpalu = new List<(string opis, string mapa, string swiat, string[] odpadaja)>
        {
            ("25 C, fala mrozu na mapie", "ColdSnap", "", new[] { "HeatWave" }),
            ("25 C, zima wulkaniczna na mapie", "VolcanicWinter", "", new[] { "HeatWave", "VolcanicWinter" }),
            ("25 C, zima wulkaniczna tylko na swiecie", "", "VolcanicWinter", new[] { "VolcanicWinter" }),
            ("25 C, fala upalu tylko na swiecie", "", "HeatWave", new[] { "HeatWave" }),
        };
        T.Ok("24k STRAZNIK: w 25 C fala upalu i zima wulkaniczna sa dostepne", wUpale.Contains("HeatWave") && wUpale.Contains("VolcanicWinter"),
             string.Join(",", wUpale));
        foreach (var (opis, mapa, swiat, odpadaja) in przypadkiUpalu)
        {
            WorldSnapshot s = upal();
            s.GameConditionsMap = CanonicalSet.Of(mapa.Length == 0 ? new string[0] : new[] { mapa });
            s.GameConditionsAll = CanonicalSet.Of(new[] { mapa, swiat }.Where(x => x.Length > 0));
            var po = dostepne(s);
            T.EqS("24k " + opis + ": odpadaja dokladnie", string.Join(",", wUpale.Except(po).OrderBy(x => x, StringComparer.Ordinal)),
                  string.Join(",", odpadaja.OrderBy(x => x, StringComparer.Ordinal)));
        }
    }

    /// <summary>
    /// 24b-24j: semantyka warunkow rejestru na wartosciach granicznych. Wyrocznie z kodu gry (dekompilacja 1.5.4063):
    /// ColdSnap 0 &lt; T &lt; 15 (ostre), HeatWave T &gt;= 20 (nieostra), ConditionIsActive = mapa + swiat, CanCoexistWith
    /// = tylko mapa, korekta punktow szalu p &gt; 250 -&gt; 250 + (p - 250)/2.
    /// </summary>
    static void Semantyka()
    {
        // ---- 24b. Temperatura sezonowa: granice ostre i nieostra
        var mroz = new Cond_SeasonalTemp { greaterThan = 0f, lessThan = 15f };
        var upal = new Cond_SeasonalTemp { atLeast = 20f };
        Func<float, WorldSnapshot> t = x => new WorldSnapshot { SeasonalTemp = x };
        T.Ok("24b mroz: T = 0 odrzucone (granica ostra)", !mroz.IsMet(t(0f)), null);
        T.Ok("24b mroz: T = 0,01 i 14,99 przyjete", mroz.IsMet(t(0.01f)) && mroz.IsMet(t(14.99f)), null);
        T.Ok("24b mroz: T = 15 odrzucone (granica ostra)", !mroz.IsMet(t(15f)), null);
        T.Ok("24b upal: T = 20 przyjete (granica nieostra), 19,99 odrzucone", upal.IsMet(t(20f)) && !upal.IsMet(t(19.99f)), null);
        T.Ok("24b bez granic: kazda temperatura", new Cond_SeasonalTemp().IsMet(t(-60f)) && new Cond_SeasonalTemp().IsMet(t(60f)), null);

        // ---- 24c. Warunki gry: zasieg mapa+swiat i sama mapa
        var tylkoSwiat = new WorldSnapshot { GameConditionsMap = "", GameConditionsAll = CanonicalSet.Of(new[] { "HeatWave" }) };
        var naMapie = new WorldSnapshot { GameConditionsMap = CanonicalSet.Of(new[] { "HeatWave" }), GameConditionsAll = CanonicalSet.Of(new[] { "HeatWave" }) };
        var wszedzie = new Cond_GameConditionAbsent { defs = new List<string> { "HeatWave" } };
        var mapa = new Cond_GameConditionAbsent { defs = new List<string> { "HeatWave" }, mapOnly = true };
        T.Ok("24c warunek tylko na swiecie blokuje zasieg mapa+swiat", !wszedzie.IsMet(tylkoSwiat), null);
        T.Ok("24c warunek tylko na swiecie NIE blokuje zasiegu mapy", mapa.IsMet(tylkoSwiat), null);
        T.Ok("24c warunek na mapie blokuje oba zasiegi", !wszedzie.IsMet(naMapie) && !mapa.IsMet(naMapie), null);
        T.Ok("24c inny warunek nie blokuje", new Cond_GameConditionAbsent { defs = new List<string> { "ColdSnap" } }.IsMet(naMapie), null);
        T.Ok("24c pusty snapshot nie blokuje", wszedzie.IsMet(new WorldSnapshot()), null);
        T.Ok("24c nazwa bedaca przedrostkiem innej nie blokuje (HeatWav)",
             new Cond_GameConditionAbsent { defs = new List<string> { "HeatWav" } }.IsMet(naMapie), null);

        // ---- 24d. Rasy i rzeczy z rejestru
        var rasy = new WorldSnapshot { WeatherOkRaces = CanonicalSet.Of(new[] { "Thrumbo", "Alphabeaver" }) };
        T.Ok("24d rasa w zbiorze -> spelniony", new Cond_RaceWeatherOk { race = "Thrumbo" }.IsMet(rasy), null);
        T.Ok("24d rasy spoza zbioru -> niespelniony", !new Cond_RaceWeatherOk { race = "Muffalo" }.IsMet(rasy), null);
        T.Ok("24d snapshot bez rejestru -> niespelniony", !new Cond_RaceWeatherOk { race = "Thrumbo" }.IsMet(new WorldSnapshot()), null);
        var rzeczy = new WorldSnapshot { ThingCounts = CanonicalSet.OfCounts(new Dictionary<string, int> { { "DefoliatorShipPart", 1 }, { "PsychicDronerShipPart", 0 } }) };
        T.Ok("24e max 0 przy 1 rzeczy -> niespelniony", !new Cond_ThingCount { thing = "DefoliatorShipPart", max = 0 }.IsMet(rzeczy), null);
        T.Ok("24e max 0 przy 0 rzeczy -> spelniony", new Cond_ThingCount { thing = "PsychicDronerShipPart", max = 0 }.IsMet(rzeczy), null);
        T.Ok("24e brak klucza = 0 rzeczy", new Cond_ThingCount { thing = "Nic", max = 0 }.IsMet(rzeczy)
             && !new Cond_ThingCount { thing = "Nic", min = 1 }.IsMet(rzeczy), null);
        T.Ok("24e min 1 przy 1 rzeczy -> spelniony", new Cond_ThingCount { thing = "DefoliatorShipPart", min = 1 }.IsMet(rzeczy), null);

        // ---- 24f. Flagi z odwroceniem
        T.Ok("24f frakcja mechanoidow: want i odwrocenie",
             new Cond_MechanoidFaction().IsMet(new WorldSnapshot { MechanoidFactionExists = true })
             && !new Cond_MechanoidFaction().IsMet(new WorldSnapshot())
             && new Cond_MechanoidFaction { want = false }.IsMet(new WorldSnapshot()), null);
        T.Ok("24f gatunek hodowlany: want i odwrocenie",
             new Cond_FarmAnimalKind().IsMet(new WorldSnapshot { FarmAnimalKindAvailable = true })
             && !new Cond_FarmAnimalKind().IsMet(new WorldSnapshot())
             && new Cond_FarmAnimalKind { want = false }.IsMet(new WorldSnapshot()), null);
        T.Ok("24f zwarcie: want i odwrocenie",
             new Cond_ShortCircuitPossible().IsMet(new WorldSnapshot { ShortCircuitPossible = true })
             && !new Cond_ShortCircuitPossible().IsMet(new WorldSnapshot())
             && new Cond_ShortCircuitPossible { want = false }.IsMet(new WorldSnapshot()), null);

        // ---- 24g. Szal stada: korekta punktow gry i sito z mnoznikiem
        T.Ok("24g korekta punktow: 100 -> 100, 250 -> 250, 450 -> 350, 1250 -> 750",
             Cond_WildHerd.AdjustedPoints(100f) == 100f && Cond_WildHerd.AdjustedPoints(250f) == 250f
             && Cond_WildHerd.AdjustedPoints(450f) == 350f && Cond_WildHerd.AdjustedPoints(1250f) == 750f, null);
        var szal = new Cond_WildHerd { pointsFactor = 1.35f };
        // 200 * 1,35 = 270 -> 250 + 20 / 2 = 260
        T.Ok("24g brak stada (+nieskonczonosc) -> niespelniony", !szal.IsMet(new WorldSnapshot { ThreatPoints = 5000f }), null);
        T.Ok("24g stado o combatPower 260 przy 200 punktach x 1,35 -> spelniony",
             szal.IsMet(new WorldSnapshot { ThreatPoints = 200f, WildHerdMinCombatPower = 260f }), null);
        T.Ok("24g stado o combatPower 261 -> niespelniony",
             !szal.IsMet(new WorldSnapshot { ThreatPoints = 200f, WildHerdMinCombatPower = 261f }), null);
        T.Ok("24g mnoznik domyslny 1 (bez sita): 200 punktow -> prog 200",
             new Cond_WildHerd().IsMet(new WorldSnapshot { ThreatPoints = 200f, WildHerdMinCombatPower = 200f })
             && !new Cond_WildHerd().IsMet(new WorldSnapshot { ThreatPoints = 200f, WildHerdMinCombatPower = 201f }), null);

        // ---- 24h. Liczniki z progiem
        T.Ok("24h samooswojenie: 0 -> nie, 1 -> tak",
             !new Cond_SelfTameCandidate().IsMet(new WorldSnapshot()) && new Cond_SelfTameCandidate().IsMet(new WorldSnapshot { SelfTameCandidates = 1 }), null);
        T.Ok("24h uprawy podatne: 0 -> nie, 1 -> tak, min 2 przy 1 -> nie",
             !new Cond_BlightablePlants().IsMet(new WorldSnapshot()) && new Cond_BlightablePlants().IsMet(new WorldSnapshot { BlightablePlants = 1 })
             && !new Cond_BlightablePlants { min = 2 }.IsMet(new WorldSnapshot { BlightablePlants = 1 }), null);

        // ---- 24i. Postac kanoniczna
        T.EqS("24i zbior: sortowanie ordynalne, bez duplikatow i bez zlych kluczy",
              CanonicalSet.Of(new[] { "b", "B", "a", "b", "", "x;y", "k=v" }), ";B;a;b;");
        T.EqS("24i pusty zbior -> pusty tekst", CanonicalSet.Of(new string[0]), "");
        string liczby = CanonicalSet.OfCounts(new Dictionary<string, int> { { "Zeta", 3 }, { "Alfa", 0 }, { "zly;klucz", 5 } });
        T.EqS("24i slownik: sortowanie i pominiecie zlego klucza", liczby, ";Alfa=0;Zeta=3;");
        T.Ok("24i odczyt liczb: 3, 0, brak = 0", CanonicalSet.Count(liczby, "Zeta") == 3 && CanonicalSet.Count(liczby, "Alfa") == 0
             && CanonicalSet.Count(liczby, "Beta") == 0 && CanonicalSet.Count(liczby, "Zet") == 0, liczby);

        // ---- 24j. Rejestr wymagan: klucze z warunkow klockow, wariantow tekstu i dowolnych list
        var klocek = new Block { Id = "PN_Test", Type = BlockType.Action };
        klocek.Conditions.Add(new Cond_RaceWeatherOk { race = "Thrumbo" });
        klocek.Preferences.Add(new Cond_ThingCount { thing = "DefoliatorShipPart", max = 0 });
        klocek.TextVariants.Add(new TextVariant { id = "w", conditions = new List<NarrativeCondition> { new Cond_RaceWeatherOk { race = "Alphabeaver" } } });
        var rej = SnapshotRequirements.Collect(SnapshotRequirements.ConditionsOf(new[] { klocek })
                                                  .Concat(new NarrativeCondition[] { new Cond_ThingCount { thing = "PsychicDronerShipPart" }, new Cond_Night() }));
        T.EqS("24j rasy z warunku i wariantu tekstu", string.Join(",", rej.Races), "Alphabeaver,Thrumbo");
        T.EqS("24j rzeczy z preferencji i listy dodatkowej", string.Join(",", rej.Things), "DefoliatorShipPart,PsychicDronerShipPart");
        T.Ok("24j pusty wklad -> pusty rejestr", SnapshotRequirements.Collect(null).Races.Count == 0, null);
    }
}
