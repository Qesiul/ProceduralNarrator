using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;

/// <summary>
/// TEST 23 - brama Anomaly (krok 9, K0; decyzje E-8 i E0-2).
///
/// Wyprowadzenie: gra dla ThreatBig/ThreatSmall z prawdopodobienstwem AnomalyIncidentChanceNow bierze WYLACZNIE
/// pule Anomaly, inaczej WYLACZNIE pozostala; Misc bierze obie (StorytellerComp.cs:45-64). Stad: (a) czestosc
/// strony Anomaly = szansa (test dwumianowy), (b) bez DLC zawsze strona zwykla, (c) regula dopuszczania: klocek
/// bez bramy przechodzi zawsze, pozostale tylko po swojej stronie, (d) na prawdziwym katalogu tura po stronie
/// Anomaly zostawia wylacznie akcje Misc (dzis nie ma jeszcze akcji Anomaly - dojda w K2).
/// Zgodnosc deklaracji klockow z IncidentDef sprawdza wyrocznia z danych gry w TEST 20c.
/// </summary>
static class TestsAnomalyGate
{
    public static void Run(EventComposer composer, List<Block> klocki)
    {
        T.Section("TEST 23 - brama Anomaly (krok 9, K0)");

        // ---- 23a. losowanie strony
        var bez = new WorldSnapshot { AnomalyActive = false, AnomalyIncidentChance = 0.5f };
        bool zawszeZwykla = Enumerable.Range(0, 2000).All(i => AnomalyGate.Draw(bez, i * 1000, 0) == AnomalyGateKind.Regular);
        T.Ok("23a bez DLC Anomaly zawsze strona zwykla (nawet przy szansie 0,5)", zawszeZwykla, null);
        var zero = new WorldSnapshot { AnomalyActive = true, AnomalyIncidentChance = 0f };
        var jeden = new WorldSnapshot { AnomalyActive = true, AnomalyIncidentChance = 1f };
        T.Ok("23a szansa 0 -> zawsze zwykla", Enumerable.Range(0, 500).All(i => AnomalyGate.Draw(zero, i * 1000, 0) == AnomalyGateKind.Regular), null);
        T.Ok("23a szansa 1 -> zawsze Anomaly", Enumerable.Range(0, 500).All(i => AnomalyGate.Draw(jeden, i * 1000, 0) == AnomalyGateKind.Anomaly), null);
        T.Ok("23a brak snapshotu -> zwykla", AnomalyGate.Draw(null, 1000, 0) == AnomalyGateKind.Regular, null);

        const int N = 20000;
        foreach (float p in new[] { 0.075f, 0.3f, 0.55f })
        {
            var s = new WorldSnapshot { AnomalyActive = true, AnomalyIncidentChance = p };
            int anomalia = Enumerable.Range(0, N).Count(i => AnomalyGate.Draw(s, 1000 * (i + 1), 0) == AnomalyGateKind.Anomaly);
            double f = anomalia / (double)N;
            double sigma = Math.Sqrt(p * (1 - p) / N);
            T.Ok("23a czestosc strony Anomaly = szansa " + p + " (4 sigma, " + N + " tur na siatce 1000 tickow)",
                 Math.Abs(f - p) <= 4 * sigma, "czestosc " + f.ToString("0.0000") + ", sigma " + sigma.ToString("0.0000"));
        }
        var pol = new WorldSnapshot { AnomalyActive = true, AnomalyIncidentChance = 0.5f };
        T.Ok("23a determinizm: ten sam tick daje te sama strone",
             Enumerable.Range(0, 200).All(i => AnomalyGate.Draw(pol, i * 1000, 0) == AnomalyGate.Draw(pol, i * 1000, 0)), null);
        int roznych = Enumerable.Range(0, 400).Count(i => AnomalyGate.Draw(pol, i * 1000, 0) != AnomalyGate.Draw(pol, i * 1000, 17));
        T.Ok("23a sol powtorzenia zmienia strumien (symulator)", roznych > 100, "roznych: " + roznych);

        // ---- 23b. regula dopuszczania - pelna tabela 3 x 3
        var tabela = new (AnomalyGateKind strona, AnomalyGateKind brama, bool ok)[]
        {
            (AnomalyGateKind.None, AnomalyGateKind.None, true), (AnomalyGateKind.None, AnomalyGateKind.Regular, true), (AnomalyGateKind.None, AnomalyGateKind.Anomaly, true),
            (AnomalyGateKind.Regular, AnomalyGateKind.None, true), (AnomalyGateKind.Regular, AnomalyGateKind.Regular, true), (AnomalyGateKind.Regular, AnomalyGateKind.Anomaly, false),
            (AnomalyGateKind.Anomaly, AnomalyGateKind.None, true), (AnomalyGateKind.Anomaly, AnomalyGateKind.Regular, false), (AnomalyGateKind.Anomaly, AnomalyGateKind.Anomaly, true),
        };
        foreach (var (strona, brama, ok) in tabela)
            T.Ok("23b strona " + strona + ", klocek " + brama + " -> " + (ok ? "dopuszczony" : "odrzucony"), AnomalyGateRules.Allows(strona, brama) == ok, null);

        // ---- 23c. prawdziwy katalog
        var pelny = new WorldSnapshot
        {
            DaysPassed = 40, ColonistCount = 6, ColonistsOnMap = 6, ColonyWealth = 40000, WealthRelative = 1.5f,
            MountainRoofCellsNearColony = 250, HasHostileFaction = true, Season = 2, IsNight = true, WildAnimalCount = 8,
            MaddenableAnimalCount = 6, ThreatPoints = 600f, DaysSinceLastEvent = 6f, KidnappedColonistCount = 2,
            HasPoweredCommsConsole = true, SeasonAcceptableForHumans = true
        };
        // Krok 9, K2: pola rejestru na "dostepne", zeby kazda akcja (takze Anomaly z warunkami rejestru) byla w zbiorze.
        TestsCoreContent.ZTrescia(pelny);
        Func<AnomalyGateKind, List<Block>> dostepne = st => composer.AvailableActions(pelny, new EventRecipe { AnomalySide = st });
        var wszystkie = dostepne(AnomalyGateKind.None);
        var zwykle = dostepne(AnomalyGateKind.Regular);
        var anomalne = dostepne(AnomalyGateKind.Anomaly);
        T.Ok("23c STRAZNIK: katalog ma akcje z brama Regular i bez bramy",
             klocki.Any(b => b.AnomalyGate == AnomalyGateKind.Regular) && klocki.Any(b => b.Type == BlockType.Action && b.AnomalyGate == AnomalyGateKind.None), null);
        // Krok 9, K2: katalog ma akcje z brama Anomaly (17 zagrozen anomalii) - strony sa symetryczne.
        T.Ok("23c STRAZNIK: katalog ma akcje z brama Anomaly", wszystkie.Any(b => b.AnomalyGate == AnomalyGateKind.Anomaly), null);
        T.EqS("23c strona zwykla: odpadaja dokladnie akcje z brama Anomaly",
              string.Join(",", wszystkie.Except(zwykle).Select(b => b.Id).OrderBy(x => x, StringComparer.Ordinal)),
              string.Join(",", wszystkie.Where(b => b.AnomalyGate == AnomalyGateKind.Anomaly).Select(b => b.Id).OrderBy(x => x, StringComparer.Ordinal)));
        T.Ok("23c strona Anomaly: zostaja akcje bez bramy (Misc) i z brama Anomaly, zadna Regular",
             anomalne.Any(b => b.AnomalyGate == AnomalyGateKind.None) && anomalne.Any(b => b.AnomalyGate == AnomalyGateKind.Anomaly)
             && anomalne.All(b => b.AnomalyGate != AnomalyGateKind.Regular), string.Join(",", anomalne.Select(b => b.Payload)));
        // Strona trafia do zestawu kandydatow (log danych v10, kolumna anomaliaTura czyta ja z CandidateSet).
        var gen = new CandidateGenerator(composer);
        foreach (AnomalyGateKind st in new[] { AnomalyGateKind.None, AnomalyGateKind.Regular, AnomalyGateKind.Anomaly })
        {
            CandidateSet zestaw = gen.Generate(new EventRecipe { AnomalySide = st }, pelny, new ProceduralNarrator.Core.Util.SeededRandom(1), 0);
            T.Ok("23c zestaw kandydatow niesie strone bramy " + st, zestaw.AnomalySide == st, zestaw.AnomalySide.ToString());
        }
        T.Ok("23c brak przepisu -> strona None w zestawie",
             gen.Generate(null, pelny, new ProceduralNarrator.Core.Util.SeededRandom(1), 0).AnomalySide == AnomalyGateKind.None, null);
        T.EqS("23c strona Anomaly: odpadaja dokladnie akcje z brama Regular",
              string.Join(",", wszystkie.Except(anomalne).Select(b => b.Id).OrderBy(x => x, StringComparer.Ordinal)),
              string.Join(",", wszystkie.Where(b => b.AnomalyGate == AnomalyGateKind.Regular).Select(b => b.Id).OrderBy(x => x, StringComparer.Ordinal)));
    }
}
