using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;

/// <summary>
/// TEST 25 - krok 9, K2: tresc Royalty i Anomaly (decyzje autora K2-a...K2-h, Docs/K2_PROPOZYCJA.md). Wyrocznie:
/// raporty rozpoznania K2 (dekompilacja 1.5.4063) i zatwierdzone teksty - NIE kod moda.
/// </summary>
static class TestsDlcContent
{
    /// <summary>Teksty BAZOWE klockow K2 - zatwierdzone slowo w slowo 2026-09-28 (K2-h).</summary>
    static readonly (string blok, string tekst)[] BazoweK2 =
    {
        ("PN_Aktor_Otchlan", "Otchlan"),
        ("PN_Aktor_Kult", "Mroczny kult"),
        ("PN_Kons_Groza", "Kolonisci dlugo beda o tym szeptac."),
        ("PN_Akcja_KlasterMaszyn", "zrzuca w poblizu uspiony klaster mechanoidow."),
        ("PN_Akcja_ZrodloKlopotow", "uruchamia w okolicy urzadzenie, ktore uprzykrza kolonii zycie."),
        ("PN_Akcja_Abazja", "rozbija sie w poblizu w kapsule transportowej."),
        ("PN_Akcja_RojTrupow", "posyla przez okolice nieumarle zlo."),
        ("PN_Akcja_RojZwierzat", "podnosi z martwych zwierzyne i puszcza ja przez okolice."),
        ("PN_Akcja_GarstkaTrupow", "posyla przez okolice kilka chodzacych trupow."),
        ("PN_Akcja_SzturmTrupow", "rzuca na kolonie horde nieumarlych."),
        ("PN_Akcja_Miesobestie", "wypycha spod ziemi zywe mieso, ktore rusza na kolonie."),
        ("PN_Akcja_Kolcarze", "posyla na kolonie potwory o ludzkich ksztaltach, ktore miotaja kolcami."),
        ("PN_Akcja_Pozeracze", "napuszcza na kolonie cos, co polyka ludzi w calosci."),
        ("PN_Akcja_PozeraczeZWody", "wypuszcza z wody cos, co polyka ludzi w calosci."),
        ("PN_Akcja_Chimery", "sprowadza pod kolonie chimery, ktore czaja sie do ataku."),
        ("PN_Akcja_Ghul", "nasyla na kolonie ghula, ktory nie czuje bolu."),
        ("PN_Akcja_PiesnNienawisci", "przysyla pod kolonie chor, ktory chce zaintonowac piesn nienawisci."),
        ("PN_Akcja_RytualKultu", "przysyla pod kolonie grupe kultystow, ktora chce odprawic psychiczny rytual."),
        ("PN_Akcja_Oczyslepy", "wypuszcza w okolicy niewidzialnych lowcow."),
        ("PN_Akcja_Wrzaski", "sprawia, ze z oddali dobiegaja nieludzkie wrzaski."),
        ("PN_Akcja_Zjawa", "wypuszcza w okolice niewidzialna zjawe."),
        ("PN_Akcja_Kregoslup", "sprawia, ze kregoslup zjawy zaczyna buczec."),
        ("PN_Akcja_Wszczep", "zasiewa w jednym z kolonistow metalowa zaraze."),
        ("PN_Akcja_BramaOtchlani", "zaczyna drazyc w okolicy przejscie w glab ziemi."),
        ("PN_Akcja_SerceZMiesa", "sprawia, ze cos przekopuje sie od spodu ku powierzchni."),
        ("PN_Akcja_Nocisfera", "przenosi tuz za kolonie dziwna metalowa kule."),
        ("PN_Akcja_ObeliskA", "zrzuca w okolicy tajemniczy obelisk."),
        ("PN_Akcja_ObeliskD", "zrzuca w okolicy tajemniczy obelisk."),
        ("PN_Akcja_ObeliskM", "zrzuca w okolicy tajemniczy obelisk."),
        ("PN_Akcja_LadunekC", "proponuje kolonii tajemniczy ladunek."),
        ("PN_Akcja_LadunekS", "proponuje kolonii tajemniczy ladunek."),
        ("PN_Akcja_LadunekZ", "proponuje kolonii tajemniczy ladunek."),
        ("PN_Akcja_KrwawyDeszcz", "sprowadza na okolice krwawy deszcz."),
        ("PN_Akcja_Calun", "rozciaga nad okolica calun, pod ktorym umarli moga powstac."),
        ("PN_Akcja_Ciemnosc", "zaczyna przyciemniac niebo nad kolonia."),
        ("PN_Akcja_Przybysz", "zbliza sie do kolonii i chce porozmawiac."),
        ("PN_Akcja_PrzybyszM", "zbliza sie do kolonii i chce porozmawiac."),
    };

    /// <summary>
    /// BLIZNIETA (decyzja autora K2-c): gra celowo nie mowi, ktore to zdarzenie, wiec nasz tekst i wszystko, co widac
    /// (osie przez komunikaty lukow, konsekwencje, styl), musi byc identyczne. Roznic moga sie tylko warunki i payload.
    /// </summary>
    static readonly string[][] Blizniaki =
    {
        new[] { "PN_Akcja_Przybysz", "PN_Akcja_PrzybyszM" },
        new[] { "PN_Akcja_LadunekC", "PN_Akcja_LadunekS", "PN_Akcja_LadunekZ" },
        new[] { "PN_Akcja_ObeliskA", "PN_Akcja_ObeliskD", "PN_Akcja_ObeliskM" },
    };

    /// <summary>Zdarzenia UKRYTE (K2-b, sprostowanie E0-6): worker gry nie wysyla listu (raporty K2, sprawdzone grepem).</summary>
    static readonly string[] Ukryte = { "MetalhorrorImplantation", "Revenant", "SightstealerArrival" };

    /// <summary>Minimum punktow sprawdzane przez sam worker (IncidentWorker_EntitySwarm: punkty &lt; 40 -> odmowa).</summary>
    static readonly string[] MinimumWorkera40 = { "ShamblerSwarm", "ShamblerSwarmAnimals", "SmallShamblerSwarm" };

    public static void Run(List<Block> klocki, List<Block> klockiBezDlc)
    {
        T.Section("TEST 25 - krok 9, K2: tresc Royalty i Anomaly (bliznieta, zdarzenia ukryte, warunki rejestru)");
        TekstyBazowe(klocki, klockiBezDlc);
        Bliznieta(klocki);
        ZdarzeniaUkryte(klocki);
        SitoPunktow(klocki);
        Semantyka();
        Rejestr(klocki);
    }

    static string Opis(Block b)
    {
        return b.Id;
    }

    // ---- 25a. Teksty bazowe K2 slowo w slowo; kazdy klocek DLC (jest ze wszystkimi DLC, znika bez nich) ma wpis.
    static void TekstyBazowe(List<Block> klocki, List<Block> klockiBezDlc)
    {
        foreach (var (blok, tekst) in BazoweK2)
        {
            Block b = klocki.FirstOrDefault(x => x.Id == blok);
            T.EqS("25a tekst bazowy K2: " + blok, b == null ? "(brak klocka)" : (b.TextFragment ?? ""), tekst);
        }
        var bezDlc = new HashSet<string>(klockiBezDlc.Select(x => x.Id), StringComparer.Ordinal);
        var tylkoZDlc = klocki.Where(x => !bezDlc.Contains(x.Id)).Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        T.Ok("25a STRAZNIK: sa klocki obecne tylko z DLC", tylkoZDlc.Count > 0, "");
        T.EqS("25a klocki tylko z DLC == tabela tekstow bazowych K2 (niezalezna droga: przycinanie MayRequire)",
              string.Join(",", tylkoZDlc), string.Join(",", BazoweK2.Select(p => p.blok).OrderBy(x => x, StringComparer.Ordinal)));
        T.EqI("25a bez DLC katalog wraca do gry podstawowej (53 klocki po K1)", klockiBezDlc.Count, 53);
    }

    // ---- 25b. Bliznieta - identyczne wszystko, co widac.
    static string Widoczne(Block b)
    {
        string warianty = string.Join(";", b.TextVariants.OrderBy(v => v.id, StringComparer.Ordinal)
            .Select(v => v.id + "=" + v.text + "{" + string.Join(",", (v.conditions ?? new List<NarrativeCondition>()).Select(c => c.Describe()))
                         + "}" + v.minIntensity + "/" + v.maxIntensity + "/" + v.requiresPointsScaling + "/" + v.requiresFaction));
        return b.TextFragment + " | " + warianty + " | " + b.Theme + "/" + b.Valence + "/" + b.Scale + "/" + b.Intensity
               + " | tagi " + string.Join(",", b.Tags.OrderBy(x => x, StringComparer.Ordinal))
               + " | lista " + string.Join(",", b.OnlyWith.OrderBy(x => x, StringComparer.Ordinal))
               + " | styl " + (b.StyleWeights == null ? "-" : b.StyleWeights.Describe()) + "/" + b.StyleNeutral
               + " | skaluje " + b.ScalesWithPoints + " | brama " + b.AnomalyGate;
    }

    static void Bliznieta(List<Block> klocki)
    {
        foreach (string[] grupa in Blizniaki)
        {
            var bl = grupa.Select(id => klocki.FirstOrDefault(x => x.Id == id)).ToList();
            T.Ok("25b STRAZNIK: grupa blizniat w katalogu: " + string.Join(",", grupa), bl.All(x => x != null), "");
            if (bl.Any(x => x == null)) continue;
            string wzor = Widoczne(bl[0]);
            for (int i = 1; i < bl.Count; i++)
            {
                T.EqS("25b " + bl[i].Id + " widziany tak samo jak " + bl[0].Id, Widoczne(bl[i]), wzor);
            }
            T.EqI("25b bliznieta maja rozne payloady: " + string.Join(",", grupa), bl.Select(x => x.Payload).Distinct().Count(), bl.Count);
        }
    }

    // ---- 25c. Zdarzenia ukryte (K2-b).
    static void ZdarzeniaUkryte(List<Block> klocki)
    {
        var akcje = klocki.Where(b => b.Type == BlockType.Action).ToList();
        var zTagiem = akcje.Where(b => b.IsHidden).Select(b => b.Payload).OrderBy(x => x, StringComparer.Ordinal).ToList();
        T.EqS("25c akcje z tagiem 'ukryte' == incydenty bez listu (raporty K2)", string.Join(",", zTagiem), string.Join(",", Ukryte));
        T.Ok("25c tag 'ukryte' tylko na klockach akcji", klocki.All(b => b.Type == BlockType.Action || !b.Tags.Contains(Block.HiddenTag)), "");
        foreach (Block b in akcje.Where(x => x.IsHidden))
        {
            var konsekwencje = b.OnlyWith.Where(id => klocki.Any(k => k.Id == id && k.Type == BlockType.Consequence)).ToList();
            T.Ok("25c zdarzenie ukryte " + b.Id + " nie ma konsekwencji (nie pisze faktow)", konsekwencje.Count == 0, string.Join(",", konsekwencje));
        }

        // Regula w Core: widok z tagiem ukryte nie pasuje do fazy, ktora pasuje do identycznego widoku bez tagu.
        var oczekiwanie = new ArcExpectation
        {
            themes = new List<Theme> { Theme.Supernatural }, valences = new List<Valence> { Valence.Negative },
            scales = new List<EventScale> { EventScale.Major }, requiredTag = "anomalia"
        };
        Func<bool, ArcEventView> widok = ukryte =>
        {
            var v = new ArcEventView { ActionBlockId = "PN_Akcja_X", Payload = "X", Theme = Theme.Supernatural, Valence = Valence.Negative,
                                       Scale = EventScale.Major, Intensity = IntensityLevel.High };
            v.Tags.Add("anomalia");
            if (ukryte) v.Tags.Add(Block.HiddenTag);
            return v;
        };
        string why;
        T.Ok("25c kontrola: widok BEZ tagu ukryte pasuje do fazy", oczekiwanie.Matches(widok(false), null, out why), why);
        bool pasuje = oczekiwanie.Matches(widok(true), null, out why);
        T.Ok("25c widok z tagiem ukryte NIE pasuje do fazy (nie otwiera, nie przesuwa, bez premii lukowej)", !pasuje, why);
        T.Ok("25c powod odrzucenia: zdarzenie ukryte", why == "zdarzenie ukryte", why);

        // Regula przezywa zapis gry: tagi ida przez linie P kolejki wykonania (sciezka spozniona po wczytaniu).
        var p = new PendingExecution { Tick = 1000, GameDay = 12f, DecisionIndex = 3, IncidentDefName = "Revenant", LastFireBefore = -1,
                                       Event = widok(true) };
        PendingExecution q;
        bool ok = PendingExecution.TryDecode(p.Encode(), out q);
        T.Ok("25c linia P zachowuje zdarzenie ukryte po zapisie i wczytaniu", ok && q.Event.IsHidden, ok ? q.Encode() : "dekodowanie");
    }

    // ---- 25d. Minimum punktow workera i prog sita dokladnego.
    static void SitoPunktow(List<Block> klocki)
    {
        var z40 = klocki.Where(b => b.Type == BlockType.Action && b.WorkerMinPoints > 0f).ToList();
        T.EqS("25d workerMinPoints > 0 dokladnie dla rojow trupow (IncidentWorker_EntitySwarm)",
              string.Join(",", z40.Select(b => b.Payload).OrderBy(x => x, StringComparer.Ordinal)), string.Join(",", MinimumWorkera40));
        T.Ok("25d workerMinPoints rojow = 40 (minimum grupy Shamblers: combatPower ShamblerSwarmer)",
             z40.All(b => b.WorkerMinPoints == 40f), string.Join(",", z40.Select(b => b.WorkerMinPoints)));
        T.Ok("25d workerMinPoints tylko na klockach akcji", klocki.All(b => b.Type == BlockType.Action || b.WorkerMinPoints == 0f), "");

        Func<float, ComposedEvent> zdarzenie = min => new ComposedEvent
        {
            // Aktor PRZED akcja i z duzym minimum: regula ma brac minimum wylacznie z klocka AKCJI, nie z pierwszego.
            Blocks = new List<Block> { new Block { Id = "PN_B", Type = BlockType.Actor, WorkerMinPoints = 999f },
                                       new Block { Id = "PN_A", Type = BlockType.Action, WorkerMinPoints = min } }
        };
        T.Eq("25d prog: Def 0, worker 40 -> 40", PointsSieve.EffectiveMinimum(0f, zdarzenie(40f)), 40, 1e-6);
        T.Eq("25d prog: Def 250, worker 0 -> 250", PointsSieve.EffectiveMinimum(250f, zdarzenie(0f)), 250, 1e-6);
        T.Eq("25d prog: Def 250, worker 40 -> 250 (wieksza z wartosci)", PointsSieve.EffectiveMinimum(250f, zdarzenie(40f)), 250, 1e-6);
        T.Eq("25d prog: Def 30, worker 40 -> 40 (wieksza z wartosci)", PointsSieve.EffectiveMinimum(30f, zdarzenie(40f)), 40, 1e-6);
        T.Eq("25d prog: minimum na klocku NIE-akcji nie liczy sie", PointsSieve.EffectiveMinimum(0f, zdarzenie(0f)), 0, 1e-6);
        T.Eq("25d prog: ujemne minimum Defa -> 0", PointsSieve.EffectiveMinimum(-5f, zdarzenie(0f)), 0, 1e-6);
        T.Eq("25d prog: brak zdarzenia -> minimum Defa", PointsSieve.EffectiveMinimum(120f, null), 120, 1e-6);
    }

    // ---- 25e. Semantyka warunkow K2 na wartosciach granicznych (wyrocznie: raporty K2).
    static void Semantyka()
    {
        var s = new WorldSnapshot();
        T.Ok("25e domyslny snapshot: brak kultu", !new Cond_FactionExists { faction = "HoraxCult" }.IsMet(s), "");
        s.FactionDefsPresent = CanonicalSet.Of(new[] { "HoraxCult", "Pirate" });
        T.Ok("25e kult jest -> Cond_FactionExists", new Cond_FactionExists { faction = "HoraxCult" }.IsMet(s), "");
        T.Ok("25e want=false odwraca", !new Cond_FactionExists { faction = "HoraxCult", want = false }.IsMet(s), "");
        T.Ok("25e inna frakcja nie wystarcza", !new Cond_FactionExists { faction = "Mechanoid" }.IsMet(s), "");
        T.Ok("25e dokladne dopasowanie klucza (Horax nie jest HoraxCult)", !new Cond_FactionExists { faction = "Horax" }.IsMet(s), "");

        T.Ok("25e brak trwajacych zadan -> Cond_NoOngoingQuest", new Cond_NoOngoingQuest { script = "ProblemCauser" }.IsMet(new WorldSnapshot()), "");
        s.OngoingQuestScripts = CanonicalSet.Of(new[] { "ProblemCauser" });
        T.Ok("25e trwa ProblemCauser -> odmowa", !new Cond_NoOngoingQuest { script = "ProblemCauser" }.IsMet(s), "");
        T.Ok("25e trwa ProblemCauser, pytanie o inne zadanie -> zgoda", new Cond_NoOngoingQuest { script = "Inne" }.IsMet(s), "");

        s.PawnKindCounts = CanonicalSet.OfCounts(new Dictionary<string, int> { { "Nociosphere", 1 } });
        T.Ok("25e jedna nocisfera, max 0 -> odmowa", !new Cond_PawnKindCount { kind = "Nociosphere", max = 0 }.IsMet(s), "");
        T.Ok("25e jedna nocisfera, max 1 -> zgoda (granica)", new Cond_PawnKindCount { kind = "Nociosphere", max = 1 }.IsMet(s), "");
        T.Ok("25e brak klucza = 0 -> max 0 spelnione", new Cond_PawnKindCount { kind = "Inny", max = 0 }.IsMet(s), "");
        T.Ok("25e min 1 przy braku klucza -> odmowa", !new Cond_PawnKindCount { kind = "Inny", min = 1 }.IsMet(s), "");

        T.Ok("25e brak wody -> Cond_WalkableWater odmawia", !new Cond_WalkableWater().IsMet(new WorldSnapshot()), "");
        T.Ok("25e woda -> Cond_WalkableWater", new Cond_WalkableWater().IsMet(new WorldSnapshot { WalkableWater = true }), "");

        T.Ok("25e zero cichych kregoslupow -> odmowa", !new Cond_IdleRevenantSpine().IsMet(new WorldSnapshot()), "");
        T.Ok("25e jeden cichy kregoslup -> zgoda (granica min 1)", new Cond_IdleRevenantSpine().IsMet(new WorldSnapshot { IdleRevenantSpines = 1 }), "");
        T.Ok("25e zero kandydatow szescianu -> odmowa", !new Cond_CubeCandidate().IsMet(new WorldSnapshot()), "");
        T.Ok("25e jeden kandydat szescianu -> zgoda", new Cond_CubeCandidate().IsMet(new WorldSnapshot { CubeCandidates = 1 }), "");
        T.Ok("25e zero kandydatow zwlok -> odmowa", !new Cond_UnnaturalCorpseCandidate().IsMet(new WorldSnapshot()), "");
        T.Ok("25e jeden kandydat zwlok -> zgoda", new Cond_UnnaturalCorpseCandidate().IsMet(new WorldSnapshot { UnnaturalCorpseCandidates = 1 }), "");
        T.Ok("25e kandydat szescianu nie wystarcza zwlokom (osobne pola)",
             !new Cond_UnnaturalCorpseCandidate().IsMet(new WorldSnapshot { CubeCandidates = 3 }), "");
        T.Ok("25e brama biosygnatury zamknieta -> odmowa", !new Cond_MetalhorrorGate().IsMet(new WorldSnapshot()), "");
        T.Ok("25e brama biosygnatury otwarta -> zgoda", new Cond_MetalhorrorGate().IsMet(new WorldSnapshot { MetalhorrorGateOpen = true }), "");
        T.Ok("25e zero pionkow do zakazenia -> odmowa", !new Cond_InfectablePawn().IsMet(new WorldSnapshot()), "");
        T.Ok("25e jeden pionek do zakazenia -> zgoda", new Cond_InfectablePawn().IsMet(new WorldSnapshot { InfectablePawns = 1 }), "");
    }

    // ---- 25f. Rejestr wymagan z prawdziwego katalogu (klocki + warianty) - klucze K2.
    static void Rejestr(List<Block> klocki)
    {
        SnapshotRequirements r = SnapshotRequirements.Collect(SnapshotRequirements.ConditionsOf(klocki));
        T.EqS("25f rejestr: frakcje == HoraxCult", string.Join(",", r.Factions), "HoraxCult");
        T.EqS("25f rejestr: skrypty zadan == ProblemCauser", string.Join(",", r.QuestScripts), "ProblemCauser");
        T.EqS("25f rejestr: rodzaje pionkow == Nociosphere", string.Join(",", r.PawnKinds), "Nociosphere");
        T.EqS("25f rejestr: rzeczy == defoliator, emanator i brama otchlani",
              string.Join(",", r.Things), "DefoliatorShipPart,PitGate,PitGateSpawner,PsychicDronerShipPart");
        T.Ok("25f opis rejestru wymienia nowe grupy kluczy", r.ToString().Contains("frakcje=HoraxCult") && r.ToString().Contains("zadania=ProblemCauser")
             && r.ToString().Contains("rodzaje=Nociosphere"), r.ToString());

        // Worker kultu bierze Faction.OfHoraxCult bez sprawdzenia: warunek musi stac na AKCJI (nie tylko na aktorze), bo
        // dostepnosc akcji liczy sie z jej warunkow, a o tresci zdania decyduje aktor.
        var kultowe = klocki.Where(b => b.Type == BlockType.Action && b.OnlyWith.Contains("PN_Aktor_Kult")).ToList();
        T.Ok("25f STRAZNIK: sa akcje kultu", kultowe.Count == 2, string.Join(",", kultowe.Select(Opis)));
        foreach (Block b in kultowe)
        {
            T.Ok("25f akcja kultu " + b.Id + " wymaga kultu Horaksa (Cond_FactionExists na akcji)",
                 b.Conditions.OfType<Cond_FactionExists>().Any(c => c.faction == "HoraxCult" && c.want), "");
        }
    }
}
