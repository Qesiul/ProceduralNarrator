using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

/// <summary>
/// TEST 22 - lustro sprawdzen gry, czesc rdzeniowa (krok 9, K0; decyzja autora E0-2).
///
/// Integracja (EngineCheckMirror) odczytuje warunki gry i wpisuje zablokowane payloady do snapshotu - tego
/// walidator nie widzi. Tu sprawdzamy kontrakt rdzenia: postac kanoniczna, dopasowanie po pelnym tokenie,
/// akcja z zablokowanym payloadem wypada przed generowaniem, licznik liczy TYLKO akcje, ktore bez lustra
/// bylyby dostepne (miara wplywu lustra), a m i K generatora licza sie juz bez nich.
/// </summary>
static class TestsEngineMirror
{
    static WorldSnapshot Pelny()
    {
        return new WorldSnapshot
        {
            DaysPassed = 40, ColonistCount = 6, ColonistsOnMap = 6, ColonyWealth = 40000, WealthRelative = 1.5f,
            MountainRoofCellsNearColony = 250, HasHostileFaction = true, Season = 2, IsNight = true, WildAnimalCount = 8,
            MaddenableAnimalCount = 6, ThreatPoints = 600f, DaysSinceLastEvent = 6f, KidnappedColonistCount = 2,
            HasPoweredCommsConsole = true, SeasonAcceptableForHumans = true
        };
    }

    public static void Run(EventComposer composer)
    {
        T.Section("TEST 22 - lustro sprawdzen gry, rdzen (krok 9, K0)");

        // ---- 22a. postac kanoniczna
        T.EqS("22a sortowanie ordynalne i bez powtorzen", EngineMirror.Canonical(new[] { "b", "a", "b", "B" }), ";B;a;b;");
        T.EqS("22a pusty zbior = pusty napis", EngineMirror.Canonical(new string[0]), "");
        T.EqS("22a null = pusty napis", EngineMirror.Canonical(null), "");
        T.EqS("22a wpisy puste i ze srednikiem pominiete", EngineMirror.Canonical(new[] { "", null, "x;y", "z" }), ";z;");

        // ---- 22b. dopasowanie po pelnym tokenie
        var s = new WorldSnapshot { EngineBlockedPayloads = ";RaidEnemy;" };
        T.Ok("22b pelny token trafia", EngineMirror.IsBlocked(s, "RaidEnemy"), null);
        T.Ok("22b prefiks nie trafia (Raid w RaidEnemy)", !EngineMirror.IsBlocked(s, "Raid"), null);
        T.Ok("22b sufiks nie trafia (Enemy)", !EngineMirror.IsBlocked(s, "Enemy"), null);
        T.Ok("22b brak snapshotu = nic nie zablokowane", !EngineMirror.IsBlocked(null, "RaidEnemy"), null);
        T.Ok("22b domyslny snapshot = nic nie zablokowane", !EngineMirror.IsBlocked(new WorldSnapshot(), "RaidEnemy"), null);

        // ---- 22c. dostepne akcje na prawdziwym katalogu
        int zbl;
        var bazowe = composer.AvailableActions(Pelny(), new EventRecipe(), out zbl).Select(b => b.Payload).ToList();
        T.EqI("22c bez lustra nic nie zablokowane", zbl, 0);
        T.Ok("22c STRAZNIK: w pelnym snapshocie dostepne sa burza i rojenie",
             bazowe.Contains("Flashstorm") && bazowe.Contains("Infestation"), string.Join(",", bazowe));

        var zBurza = Pelny();
        zBurza.EngineBlockedPayloads = EngineMirror.Canonical(new[] { "Flashstorm" });
        var poBurzy = composer.AvailableActions(zBurza, new EventRecipe(), out zbl).Select(b => b.Payload).ToList();
        T.EqS("22c zablokowana burza: znika dokladnie ona",
              string.Join(",", bazowe.Except(poBurzy).OrderBy(x => x, StringComparer.Ordinal)), "Flashstorm");
        T.EqI("22c zablokowana burza: licznik 1", zbl, 1);
        T.EqI("22c zablokowana burza: nic nie przybywa", poBurzy.Except(bazowe).Count(), 0);

        // Akcja, ktora i tak nie przechodzi wlasnych warunkow (rojenie bez stropu gorskiego), nie liczy sie
        // do wplywu lustra - licznik ma mierzyc akcje, ktore zniknely PRZEZ lustro.
        var bezGor = Pelny();
        bezGor.MountainRoofCellsNearColony = 0;
        bezGor.EngineBlockedPayloads = EngineMirror.Canonical(new[] { "Infestation" });
        composer.AvailableActions(bezGor, new EventRecipe(), out zbl);
        T.EqI("22c akcja niedostepna z wlasnych warunkow nie liczy sie do lustra", zbl, 0);

        // ---- 22d. generator: m i K bez zablokowanych, licznik w zestawie kandydatow
        var gen = new CandidateGenerator(composer);
        CandidateSet przed = gen.Generate(new EventRecipe(), Pelny(), new SeededRandom(3), 1600);
        var dwie = Pelny();
        dwie.EngineBlockedPayloads = EngineMirror.Canonical(new[] { "Flashstorm", "RaidEnemy" });
        CandidateSet po = gen.Generate(new EventRecipe(), dwie, new SeededRandom(3), 1600);
        T.EqI("22d m mniejsze o liczbe zablokowanych akcji", po.ActionCount, przed.ActionCount - 2);
        T.EqI("22d licznik zablokowanych w zestawie kandydatow", po.EngineBlocked, 2);
        T.EqI("22d K liczone od m bez zablokowanych", po.PerActionQuota, 1600 / po.ActionCount);
        T.Ok("22d zaden kandydat z zablokowanym payloadem",
             po.Candidates.All(c => c.ActionPayload != "Flashstorm" && c.ActionPayload != "RaidEnemy"), null);
        T.Ok("22d STRAZNIK: przed blokada byli kandydaci burzy i napadu",
             przed.Candidates.Any(c => c.ActionPayload == "Flashstorm") && przed.Candidates.Any(c => c.ActionPayload == "RaidEnemy"), null);
    }
}
