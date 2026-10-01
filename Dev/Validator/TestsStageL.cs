using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Evaluation;
using ProceduralNarrator.Core.Model;

/// <summary>
/// TEST 26 - krok 9, etap L (logowanie do ewaluacji): czesc rdzeniowa obserwatorow i akcji debugowych.
/// Integracja (odczyty gry, Stopwatch, listy, pionki) jest dla walidatora niewidoczna; tu sprawdzamy logike:
/// kubelki czasu, granice doby, sklad kolonii, nowe listy, stan akcji do wymuszenia, wybor wariantu,
/// kolumne lustroOdcina. Oczekiwania wyprowadzone z definicji w komentarzach klas, nie z uruchomienia kodu.
/// </summary>
static class TestsStageL
{
    static readonly double[] GraniceMs = { 0.5, 1, 2, 5, 10, 50 };

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

    static ScoredCandidate K(string akcja, float u, string klucz, bool weto = false, bool pass = false)
    {
        return new ScoredCandidate
        {
            Event = pass ? null : new ComposedEvent { ActionBlockId = akcja },
            IsPass = pass, Utility = u, SortKey = klucz, Vetoed = weto
        };
    }

    public static void Run(EventComposer composer)
    {
        T.Section("TEST 26 - krok 9, etap L: obserwatory i akcje debugowe (rdzen)");

        // ---- 26a. PerfAccumulator: kubelek i = [granica(i-1), granica(i)), ostatni od najwyzszej granicy w gore.
        var p = new PerfAccumulator(GraniceMs);
        T.EqI("26a liczba kubelkow = granice + 1", p.BucketCount, 7);
        T.EqI("26a 0.2 -> kubelek 0", p.BucketIndex(0.2), 0);
        T.EqI("26a granica 0.5 nalezy do kubelka wyzszego", p.BucketIndex(0.5), 1);
        T.EqI("26a 4.999 -> [2,5)", p.BucketIndex(4.999), 3);
        T.EqI("26a granica H8 (5 ms) -> [5,10)", p.BucketIndex(5), 4);
        T.EqI("26a 60 -> ostatni kubelek", p.BucketIndex(60), 6);
        T.EqI("26a ujemna -> 0", p.BucketIndex(-1), 0);
        T.EqI("26a NaN -> 0", p.BucketIndex(double.NaN), 0);
        p.Add(0.5);
        p.Add(1.0);
        p.Add(5.0);
        T.EqS("26a fragment: liczba, suma, max, kubelki, granice", p.Fragment("decyzji", "Ms", true),
              "; decyzji=3; decyzjiSumaMs=6.5; decyzjiMaxMs=5; decyzjiKubelki=0/1/1/0/1/0/0; decyzjiGranice=0.5/1/2/5/10/50");
        p.Add(-3);
        T.EqI("26a wartosc ujemna liczy sie jako 0: liczba", p.Count, 4);
        T.Eq("26a wartosc ujemna liczy sie jako 0: suma bez zmian", p.Sum, 6.5, 1e-12);
        int suma = 0;
        for (int i = 0; i < p.BucketCount; i++)
        {
            suma += p.Bucket(i);
        }
        T.EqI("26a kubelki sumuja sie do liczby pomiarow", suma, p.Count);
        p.Reset();
        T.EqS("26a po resecie zera (bez kubelkow)", p.Fragment("obs", "Us", false), "; obs=0; obsSumaUs=0; obsMaxUs=0");
        bool rzucil = false;
        try
        {
            new PerfAccumulator(1, 1);
        }
        catch (ArgumentException)
        {
            rzucil = true;
        }
        T.Ok("26a granice nierosnace odrzucone", rzucil, null);

        // ---- 26b. DayClock: pierwsze wywolanie ustawia dobe; zmiana doby zwraca dobe zakonczona (biezaca - 1).
        var zegar = new DayClock();
        T.EqI("26b pierwsze wywolanie (tick 59999) bez linii", zegar.Advance(59999), -1);
        T.EqI("26b tick 60000 zamyka dobe 0", zegar.Advance(60000), 0);
        T.EqI("26b ta sama doba - bez linii", zegar.Advance(60001), -1);
        T.EqI("26b tick 179999 (doba 2) zamyka dobe 1", zegar.Advance(179999), 1);
        T.EqI("26b skok do doby 5 zamyka dobe 4 (doby 2-3 bez linii)", zegar.Advance(300000), 4);
        T.EqI("26b cofniecie zegara bez linii", zegar.Advance(120000), -1);
        T.EqI("26b po cofnieciu kolejna granica dziala", zegar.Advance(180000), 2);
        zegar.Reset();
        T.EqI("26b po resecie (wczytanie w srodku doby 1) bez linii", zegar.Advance(90000), -1);
        T.EqI("26b doba 1 zamknieta na granicy mimo wczytania w jej srodku", zegar.Advance(120000), 1);
        T.EqI("26b ujemny tick ignorowany", zegar.Advance(-5), -1);

        // ---- 26c. RosterTracker: kotwica, potem ubytki i dolaczenia rosnaco po Id, liczebnosc +-1.
        var sklad = new RosterTracker();
        var e0 = sklad.Update(new[] { new RosterMember(1, 0), new RosterMember(2, 0), new RosterMember(3, -1) }, null);
        T.EqS("26c pierwszy przeglad = kotwica z liczebnoscia",
              string.Join(",", e0.Select(x => x.Kind + "/" + x.CountAfter)), "start/3");
        var e1 = sklad.Update(new[] { new RosterMember(2, 0), new RosterMember(3, 0), new RosterMember(5, 0), new RosterMember(4, 0) },
                              id => id == 1 ? RosterTracker.Died : null);
        T.EqS("26c ubytek przed dolaczeniami, rosnaco, liczebnosc po kazdej zmianie",
              string.Join(",", e1.Select(x => x.Kind + ":" + x.Id + "@" + x.Map + "/" + x.CountAfter)),
              "zginal:1@0/2,dolaczyl:4@0/3,dolaczyl:5@0/4");
        T.EqI("26c liczebnosc koncowa = rozmiar przegladu", e1.Last().CountAfter, 4);
        var e2 = sklad.Update(new[] { new RosterMember(2, 0), new RosterMember(4, 0) },
                              id => id == 3 ? "cos-spoza-listy" : RosterTracker.Kidnapped);
        T.EqS("26c rodzaj spoza listy = inne; mapa ubytku = ostatnia znana (3 przeszedl z -1 na 0)",
              string.Join(",", e2.Select(x => x.Kind + ":" + x.Id + "@" + x.Map + "/" + x.CountAfter)),
              "inne:3@0/3,porwany:5@0/2");
        T.EqI("26c przeglad bez zmian - bez linii", sklad.Update(new[] { new RosterMember(2, 0), new RosterMember(4, 0) }, null).Count, 0);
        T.Ok("26c dolaczyl i start nie sa ubytkami",
             !RosterTracker.IsLossKind(RosterTracker.Joined) && !RosterTracker.IsLossKind(RosterTracker.Start)
             && RosterTracker.IsLossKind(RosterTracker.WentWild), null);
        sklad.Reset();
        T.EqS("26c po resecie znowu kotwica", sklad.Update(new RosterMember[0], null).Single().Kind + "/0", "start/0");

        // ---- 26d. NewIdTracker: pierwszy przeglad niczego nie zglasza, potem nowe rosnaco.
        var listy = new NewIdTracker();
        T.EqI("26d pierwszy przeglad (listy sprzed wczytania) - nic nowego", listy.Update(new[] { 5, 6 }).Count, 0);
        T.EqS("26d nowe rosnaco", string.Join(",", listy.Update(new[] { 6, 9, 5, 7 })), "7,9");
        T.EqI("26d usuniete nie sa nowe", listy.Update(new[] { 9 }).Count, 0);
        listy.Reset();
        T.EqI("26d po resecie - znowu tylko zapamietanie", listy.Update(new[] { 1 }).Count, 0);

        // ---- 26e. ForcedActionPicker: najwyzsza uzytecznosc bez weta, remis po kluczu, weto tylko w ostatecznosci.
        var pula = new List<ScoredCandidate>
        {
            K("A", 0.7f, "b"), K("A", 0.7f, "a"), K("A", 0.9f, "c", weto: true), K("B", 0.95f, "d"), K(null, 0.99f, "p", pass: true)
        };
        T.EqS("26e remis uzytecznosci -> klucz ordynalnie mniejszy", ForcedActionPicker.Pick(pula, "A").SortKey, "a");
        T.EqS("26e tylko zawetowane -> najlepszy zawetowany",
              ForcedActionPicker.Pick(new[] { K("A", 0.2f, "x", true), K("A", 0.9f, "y", true) }, "A").SortKey, "y");
        T.Ok("26e akcja bez wariantow -> null", ForcedActionPicker.Pick(pula, "C") == null, null);
        T.Ok("26e pusta pula -> null", ForcedActionPicker.Pick(null, "A") == null, null);

        // ---- 26f. DescribeActions na prawdziwym katalogu: zbior dostepnych == AvailableActions bez bramy,
        // liczba "lustro" == licznik lustra z AvailableActions (dwie drogi), kazda akcja dokladnie raz.
        var swiat = Pelny();
        swiat.EngineMirrorActive = true;
        swiat.EngineBlockedPayloads = EngineMirror.Canonical(new[] { "Flashstorm", "RaidEnemy" });
        int lustra;
        var dostepne = composer.AvailableActions(swiat, new EventRecipe(), out lustra).Select(b => b.Id).ToList();
        var opis = composer.DescribeActions(swiat, null);
        T.EqS("26f dostepne == AvailableActions (ta sama kolejnosc)",
              string.Join(",", opis.Where(a => a.IsAvailable).Select(a => a.Action.Id)), string.Join(",", dostepne));
        T.EqI("26f liczba 'lustro' == licznik lustra", opis.Count(a => a.Status == ActionAvailability.Mirror), lustra);
        T.Ok("26f STRAZNIK: lustro odcina w tym swiecie co najmniej jedna akcje", lustra >= 1, null);
        T.EqI("26f kazda akcja katalogu dokladnie raz",
              opis.Select(a => a.Action.Id).Distinct().Count(), opis.Count);
        T.Ok("26f tylko akcje", opis.All(a => a.Action.Type == BlockType.Action), null);
        T.Ok("26f STRAZNIK: sa akcje niedostepne z warunkow", opis.Any(a => a.Status == ActionAvailability.Conditions), null);
        // Kolejnosc jak w AvailableActions: akcja, ktora nie przechodzi warunkow I jest odcieta lustrem, to "warunki".
        var bezGor = Pelny();
        bezGor.MountainRoofCellsNearColony = 0;
        bezGor.EngineMirrorActive = true;
        bezGor.EngineBlockedPayloads = EngineMirror.Canonical(new[] { "Infestation" });
        var rojenie = composer.DescribeActions(bezGor, null).Where(a => a.Action.Payload == "Infestation").ToList();
        T.EqS("26f warunki przed lustrem (rojenie bez stropu, odciete lustrem)",
              string.Join(",", rojenie.Select(a => a.Status)), ActionAvailability.Conditions);
        var zTagiem = composer.DescribeActions(swiat, "militarny");
        T.Ok("26f tag z przepisu: akcje bez tagu oznaczone 'tag', z tagiem nie",
             zTagiem.All(a => (a.Status == ActionAvailability.Tag) == !a.Action.HasTag("militarny")), null);

        // ---- 26h. [PN-STAN]: RN1 = co najmniej polowa kolonistow na mapie powalona ostro (2*powaleni >= na mapie, ktos jest,
        // ktos powalony); pusta = 0 na mapie; linia przy pierwszym stanie mapy i przy zmianie KTOREGOKOLWIEK predykatu.
        T.Ok("26h 0 na mapie - nie kryzys", !RuleStateTracker.IsCrisis(0, 0), null);
        T.Ok("26h 1 z 1 powalony - kryzys", RuleStateTracker.IsCrisis(1, 1), null);
        T.Ok("26h 1 z 2 (dokladnie polowa) - kryzys", RuleStateTracker.IsCrisis(2, 1), null);
        T.Ok("26h 1 z 3 - nie kryzys", !RuleStateTracker.IsCrisis(3, 1), null);
        T.Ok("26h 2 z 4 - kryzys", RuleStateTracker.IsCrisis(4, 2), null);
        T.Ok("26h 2 z 5 - nie kryzys", !RuleStateTracker.IsCrisis(5, 2), null);
        T.Ok("26h 0 powalonych przy 1 na mapie - nie kryzys", !RuleStateTracker.IsCrisis(1, 0), null);
        T.Ok("26h pusta baza tylko przy 0 na mapie",
             RuleStateTracker.Of(0, 0, false).Empty && !RuleStateTracker.Of(1, 0, false).Empty, null);
        var stan = new RuleStateTracker();
        RuleState spokoj = RuleStateTracker.Of(3, 0, false);
        T.EqS("26h pierwszy stan mapy = start", stan.Update(0, spokoj), RuleStateTracker.Start);
        T.Ok("26h ten sam stan - bez linii", stan.Update(0, RuleStateTracker.Of(3, 0, false)) == null, null);
        T.Ok("26h inna liczba bez zmiany predykatow - bez linii", stan.Update(0, RuleStateTracker.Of(4, 1, false)) == null, null);
        T.EqS("26h samo zagrozenie - zmiana", stan.Update(0, RuleStateTracker.Of(4, 1, true)), RuleStateTracker.Change);
        T.Ok("26h ten sam stan po zmianie - bez linii (stan zapamietany)", stan.Update(0, RuleStateTracker.Of(3, 0, true)) == null, null);
        T.EqS("26h sam kryzys - zmiana", stan.Update(0, RuleStateTracker.Of(4, 2, true)), RuleStateTracker.Change);
        T.EqS("26h koniec kryzysu - zmiana", stan.Update(0, RuleStateTracker.Of(1, 0, true)), RuleStateTracker.Change);
        T.EqS("26h sama pusta baza (kryzys i zagrozenie bez zmian) - zmiana", stan.Update(0, RuleStateTracker.Of(0, 0, true)), RuleStateTracker.Change);
        T.EqS("26h druga mapa - wlasny start", stan.Update(7, spokoj), RuleStateTracker.Start);
        T.EqS("26h trzecia mapa - wlasny start", stan.Update(3, spokoj), RuleStateTracker.Start);
        List<int> koniec = stan.Ended(new List<int> { 7 });
        T.EqS("26h koniec: mapy nieobecne w przegladzie, rosnaco", string.Join(",", koniec), "0,3");
        T.Ok("26h koniec raz: druga proba nic nie zwraca", stan.Ended(new List<int> { 7 }).Count == 0, null);
        T.EqS("26h mapa po koncu wraca ze startem", stan.Update(0, spokoj), RuleStateTracker.Start);
        T.Ok("26h mapa obecna nie konczy sie", stan.Ended(new List<int> { 0, 7 }).Count == 0, null);
        stan.Reset();
        T.EqS("26h po resecie znowu start", stan.Update(0, RuleStateTracker.Of(0, 0, true)), RuleStateTracker.Start);

        // ---- 26g. kolumna lustroOdcina
        T.EqS("26g lustro nieaktywne -> pusto", EngineMirror.DataColumn(new WorldSnapshot { EngineBlockedPayloads = ";A;" }), "");
        T.EqS("26g lustro aktywne, nic nie odciete -> '-'", EngineMirror.DataColumn(new WorldSnapshot { EngineMirrorActive = true }), "-");
        T.EqS("26g lista po przecinku w porzadku kanonicznym",
              EngineMirror.DataColumn(new WorldSnapshot { EngineMirrorActive = true, EngineBlockedPayloads = EngineMirror.Canonical(new[] { "b", "a" }) }),
              "a,b");
        T.EqS("26g brak snapshotu -> pusto", EngineMirror.DataColumn(null), "");
    }
}
