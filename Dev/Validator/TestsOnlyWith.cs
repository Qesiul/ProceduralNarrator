using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Model;

/// <summary>
/// TEST 21 - listy dozwolonych onlyWith (krok 9, K0; decyzja autora E0-1).
///
/// Wyprowadzenie semantyki: lista onlyWith akcji to KOMPLET jej aktorow i konsekwencji. Stad trzy wlasnosci,
/// kazda sprawdzana osobno na syntetycznym katalogu: (1) aktor/konsekwencja spoza listy jest zabroniona,
/// (2) brak konsekwencji na liscie = akcja bez konsekwencji, (3) inne typy slotow (wyzwalacz) nie sa ruszane.
/// Do tego walidacja danych (kazdy rodzaj bledu osobno) i prawdziwy katalog: lista na kazdej akcji, zero problemow.
/// </summary>
static class TestsOnlyWith
{
    static Block B(string id, BlockType t, params string[] onlyWith)
    {
        var b = new Block { Id = id, Type = t };
        b.OnlyWith.AddRange(onlyWith);
        return b;
    }

    static List<Block> Katalog()
    {
        return new List<Block>
        {
            B("T1", BlockType.Trigger),
            B("Act1", BlockType.Actor), B("Act2", BlockType.Actor),
            B("K1", BlockType.Consequence), B("K2", BlockType.Consequence),
            B("A1", BlockType.Action, "Act1", "K1"),
            B("A2", BlockType.Action, "Act1"),
            B("A3", BlockType.Action),
        };
    }

    public static void Run(List<Block> prawdziwe)
    {
        T.Section("TEST 21 - listy dozwolonych onlyWith (krok 9, K0)");

        // ---- 21a. semantyka
        var kat = Katalog();
        var g = new CompatibilityGraph();
        List<string> problemy;
        var dodane = CatalogGraphBuilder.ExpandOnlyWith(kat, g, out problemy);
        T.EqI("21a czysty katalog: zero problemow", problemy.Count, 0);
        T.Ok("21a A1: dozwolony aktor i konsekwencja z listy", g.Allows("A1", "Act1") && g.Allows("A1", "K1"), null);
        T.Ok("21a A1: aktor i konsekwencja spoza listy zabronione", !g.Allows("A1", "Act2") && !g.Allows("A1", "K2"), null);
        T.Ok("21a A2: brak konsekwencji na liscie = akcja bez konsekwencji", !g.Allows("A2", "K1") && !g.Allows("A2", "K2"), null);
        T.Ok("21a A3 bez listy: bez ograniczen (stare zachowanie)", g.Allows("A3", "Act2") && g.Allows("A3", "K2"), null);
        T.Ok("21a wyzwalacz nie jest ruszany przez liste akcji", g.Allows("A1", "T1") && g.Allows("A2", "T1"), null);
        T.Ok("21a zakaz symetryczny", !g.Allows("Act2", "A1") && !g.Allows("K2", "A2"), null);
        T.EqS("21a dopisane pary w porzadku katalogu",
              string.Join(",", dodane.Select(p => p.Key + "-" + p.Value)), "A1-Act2,A1-K2,A2-Act2,A2-K1,A2-K2");
        T.EqI("21a liczba krawedzi == dopisane pary", g.ForbiddenEdgeCount, dodane.Count);

        // ---- 21b. walidacja danych: kazdy rodzaj bledu osobno (oczekiwany fragment komunikatu)
        var przypadki = new List<(string opis, Func<List<Block>> kat, Action<CompatibilityGraph> zakazy, string powod)>
        {
            ("lista na klocku innym niz akcja", () => { var k = Katalog(); k.Add(B("Act3", BlockType.Actor, "K1")); return k; }, null, "tylko na klocku akcji"),
            ("nieistniejacy klocek", () => { var k = Katalog(); k.Add(B("A4", BlockType.Action, "Act1", "Brak")); return k; }, null, "nieistniejacy"),
            ("klocek typu spoza aktora i konsekwencji", () => { var k = Katalog(); k.Add(B("A4", BlockType.Action, "Act1", "T1")); return k; }, null, "nie jest aktorem"),
            ("powtorzony wpis", () => { var k = Katalog(); k.Add(B("A4", BlockType.Action, "Act1", "Act1")); return k; }, null, "powtorzony"),
            ("lista bez aktora", () => { var k = Katalog(); k.Add(B("A4", BlockType.Action, "K1")); return k; }, null, "bez zadnego aktora"),
            ("sprzecznosc z incompatibleWith", Katalog, gr => gr.Forbid("A1", "Act1"), "sprzecznosc"),
            ("drugie zrodlo prawdy", Katalog, gr => gr.Forbid("K2", "A1"), "drugie zrodlo"),
        };
        int n = 0;
        foreach (var (opis, fk, zakazy, powod) in przypadki)
        {
            var gr = new CompatibilityGraph();
            zakazy?.Invoke(gr);
            List<string> pr;
            CatalogGraphBuilder.ExpandOnlyWith(fk(), gr, out pr);
            T.Ok("21b problem: " + opis, pr.Any(p => p.Contains(powod)), pr.Count == 0 ? "(brak problemu)" : string.Join(" | ", pr));
            n++;
        }
        T.EqI("21b sprawdzono wszystkie rodzaje bledow", n, przypadki.Count);

        // ---- 21c. prawdziwy katalog (wszystkie DLC): lista na KAZDEJ akcji, zero problemow
        var bezListy = prawdziwe.Where(b => b.Type == BlockType.Action && b.OnlyWith.Count == 0).Select(b => b.Id).ToList();
        T.Ok("21c kazda akcja katalogu ma liste onlyWith (inaczej nowi aktorzy i konsekwencje przykleja sie po cichu)",
             bezListy.Count == 0, string.Join(", ", bezListy));
        T.Ok("21c STRAZNIK: katalog ma akcje", prawdziwe.Any(b => b.Type == BlockType.Action), null);
        T.EqI("21c prawdziwy katalog: zero problemow list onlyWith", Loader.ProblemyOnlyWith.Count, 0);
        foreach (var p in Loader.ProblemyOnlyWith.Take(5)) Console.WriteLine("      " + p);
    }
}
