using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Tension;

/// <summary>
/// TEST 19 - drobne dlugi kroku 8: pora roku dzikiego czlowieka (dlug 6) i regula "powalony ostro" (dlug 12,
/// decyzja autora K8-8).
/// </summary>
static class TestsMinorDebts
{
    public static void Run(EventComposer composer, XmlConfig cfg)
    {
        T.Section("TEST 19 - drobne dlugi: pora roku i skazone powietrze dzikiego czlowieka, powalony ostro (krok 8)");

        // ---- 19a. Warunek pory roku (odwzorowanie IncidentWorker_WildManWandersIn.CanFireNowSub).
        var ok = new WorldSnapshot { SeasonAcceptableForHumans = true };
        var zla = new WorldSnapshot { SeasonAcceptableForHumans = false };
        var c = new Cond_SeasonAcceptableForHumans();
        T.Ok("19a pora znosna -> warunek spelniony", c.IsMet(ok), null);
        T.Ok("19a pora nieznosna -> warunek niespelniony", !c.IsMet(zla), null);
        T.Ok("19a domyslny snapshot (bez mapy) nie wyklucza akcji", new WorldSnapshot().SeasonAcceptableForHumans, null);
        var odwr = new Cond_SeasonAcceptableForHumans { wantAcceptable = false };
        T.Ok("19a wantAcceptable=false odwraca warunek (przeglad S10, K10)", odwr.IsMet(zla) && !odwr.IsMet(ok), null);

        // ---- 19a'. Skazone powietrze (przeglad S10): gra odmawia dzikiego czlowieka przy opadzie toksycznym
        // i toksycznej mgle - warunek domyslnie wymaga CZYSTEGO powietrza.
        var czyste = new WorldSnapshot { ToxicAirActive = false };
        var skazone = new WorldSnapshot { ToxicAirActive = true };
        var tox = new Cond_ToxicAir();
        T.Ok("19a' czyste powietrze -> warunek spelniony", tox.IsMet(czyste), null);
        T.Ok("19a' skazone powietrze -> warunek niespelniony", !tox.IsMet(skazone), null);
        T.Ok("19a' wantToxic=true odwraca warunek", new Cond_ToxicAir { wantToxic = true }.IsMet(skazone) && !new Cond_ToxicAir { wantToxic = true }.IsMet(czyste), null);
        T.Ok("19a' domyslny snapshot (bez mapy) = czyste powietrze", !new WorldSnapshot().ToxicAirActive, null);

        // ---- 19b. Prawdziwy katalog: dziki czlowiek dostepny wylacznie w porze znosnej.
        var s = new WorldSnapshot { DaysPassed = 40, ColonistCount = 3, ColonistsOnMap = 3, WealthRelative = 1f, HasHostileFaction = true,
                                    WildAnimalCount = 5, MaddenableAnimalCount = 5, SeasonAcceptableForHumans = true };
        var sZima = new WorldSnapshot { DaysPassed = 40, ColonistCount = 3, ColonistsOnMap = 3, WealthRelative = 1f, HasHostileFaction = true,
                                        WildAnimalCount = 5, MaddenableAnimalCount = 5, SeasonAcceptableForHumans = false };
        var akcjeLato = composer.AvailableActions(s, new EventRecipe()).Select(b => b.Payload).ToList();
        var akcjeZima = composer.AvailableActions(sZima, new EventRecipe()).Select(b => b.Payload).ToList();
        T.Ok("19b pora znosna -> WildManWandersIn wsrod akcji", akcjeLato.Contains("WildManWandersIn"), string.Join(",", akcjeLato));
        T.Ok("19b pora nieznosna -> bez WildManWandersIn", !akcjeZima.Contains("WildManWandersIn"), string.Join(",", akcjeZima));
        T.EqS("19b pora roku wyklucza TYLKO dzikiego czlowieka",
              string.Join(",", akcjeLato.Except(akcjeZima).OrderBy(x => x, System.StringComparer.Ordinal)), "WildManWandersIn");
        var sToks = new WorldSnapshot { DaysPassed = 40, ColonistCount = 3, ColonistsOnMap = 3, WealthRelative = 1f, HasHostileFaction = true,
                                        WildAnimalCount = 5, MaddenableAnimalCount = 5, SeasonAcceptableForHumans = true, ToxicAirActive = true };
        var akcjeToks = composer.AvailableActions(sToks, new EventRecipe()).Select(b => b.Payload).ToList();
        T.EqS("19b skazone powietrze wyklucza TYLKO dzikiego czlowieka",
              string.Join(",", akcjeLato.Except(akcjeToks).OrderBy(x => x, System.StringComparer.Ordinal)), "WildManWandersIn");
        T.EqI("19b skazone powietrze niczego nie dodaje", akcjeToks.Except(akcjeLato).Count(), 0);

        // ---- 19c. Regula "powalony ostro" - kazda galaz osobno, prog 0,5.
        const float prog = 0.5f;
        var przypadki = new List<(string opis, PawnAcuteFlags f, bool oczek)>
        {
            ("niepowalony z krwawieniem", new PawnAcuteFlags { Downed = false, Bleeding = true }, false),
            ("niemowle (trwale powalone) z krwawieniem", new PawnAcuteFlags { Downed = true, AlwaysDowned = true, Bleeding = true }, false),
            ("krwawi", new PawnAcuteFlags { Downed = true, Bleeding = true }, true),
            ("do opatrzenia", new PawnAcuteFlags { Downed = true, NeedsTend = true }, true),
            ("szok bolowy", new PawnAcuteFlags { Downed = true, InPainShock = true }, true),
            ("szok bolowy przy porodzie", new PawnAcuteFlags { Downed = true, InPainShock = true, InLabor = true }, false),
            ("porod z krwawieniem", new PawnAcuteFlags { Downed = true, InPainShock = true, InLabor = true, Bleeding = true }, true),
            ("hipotermia 0.5 progu smierci", new PawnAcuteFlags { Downed = true, MaxLethalFraction = 0.5f }, true),
            ("hipotermia 0.49 progu smierci", new PawnAcuteFlags { Downed = true, MaxLethalFraction = 0.49f }, false),
            ("stan trwaly bez niczego ostrego", new PawnAcuteFlags { Downed = true }, false),
        };
        int n = 0;
        foreach (var (opis, f, oczek) in przypadki)
        {
            T.Ok("19c " + opis + " -> " + (oczek ? "ostry" : "nie"), AcuteDownedRule.IsAcute(f, prog) == oczek, null);
            n++;
        }
        T.EqI("19c sprawdzono wszystkie przypadki", n, przypadki.Count);
        // Obrona w glab: progu 0 nie da sie ustawic (Sanitize daje >= 0,05), ale IsAcute i tak nie liczy wtedy
        // kazdego stanu z progiem smierci jako ostrego (przeglad S10, K13 - nazwa mowila o zachowaniu gry).
        T.Ok("19c obrona w glab: IsAcute przy progu 0 (niedostepnym po Sanitize) wylacza galaz smiertelnosci",
             !AcuteDownedRule.IsAcute(new PawnAcuteFlags { Downed = true, MaxLethalFraction = 1f }, 0f), null);

        // ---- 19d. Prog z XML (straznik dryfu) i Sanitize/Clone.
        T.Eq("19d XML crisis.lethalFraction == 0.5 (decyzja K8-8)", cfg.crisis.lethalFraction, 0.5, 1e-6);
        var p = new CrisisParams { lethalFraction = float.NaN };
        p.Sanitize();
        T.Eq("19d Sanitize: NaN -> 0.5", p.lethalFraction, 0.5, 1e-6);
        p.lethalFraction = 0f; p.Sanitize();
        T.Eq("19d Sanitize: 0 -> 0.05", p.lethalFraction, 0.05, 1e-6);
        p.lethalFraction = 3f; p.Sanitize();
        T.Eq("19d Sanitize: 3 -> 1", p.lethalFraction, 1.0, 1e-6);
        T.Eq("19d Clone kopiuje lethalFraction", new CrisisParams { lethalFraction = 0.7f }.Clone().lethalFraction, 0.7, 1e-6);
    }
}
