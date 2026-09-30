using System;
using System.Collections.Generic;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Composition
{
    /// <summary>
    /// ROZWIJA LISTY DOZWOLONYCH (Block.OnlyWith) DO ZWYKLYCH ZAKAZOW GRAFU (krok 9, etap K0; decyzja autora E0-1).
    ///
    /// Po co: graf jest "brak zakazu = zgoda". Przy 62 akcjach, nowych aktorach i nowych konsekwencjach czarna
    /// lista oznaczalaby setki krawedzi, a kazdy nowy aktor przyklejalby sie po cichu do wszystkich starych akcji
    /// ("Piraci sprowadzaja fale mrozu"). Lista dozwolonych na akcji odwraca domysl dla dwoch slotow zaleznych
    /// od akcji: aktora (kto) i konsekwencji (co zostaje).
    ///
    /// SEMANTYKA: lista onlyWith klocka AKCJI to KOMPLET zgodnych aktorow i konsekwencji. Aktor albo konsekwencja
    /// spoza listy jest zabroniona - takze wtedy, gdy na liscie nie ma ZADNEJ konsekwencji (akcja bez
    /// konsekwencji, np. Okup). Pusta lista = brak ograniczenia (stare zachowanie). Wyzwalacze, cele
    /// i modyfikatory dalej deklaruja zakazy w incompatibleWith.
    ///
    /// Dlaczego komplet, a nie "tylko typy obecne na liscie": wpis klocka DLC na liscie akcji z gry podstawowej
    /// znika bez DLC (MayRequire). Gdyby ograniczenie obejmowalo tylko typy obecne na liscie, akcja moglaby po
    /// cichu stracic ograniczenie i przyjac wszystkich aktorow. Komplet nie ma tej luki.
    ///
    /// Jedno zrodlo prawdy: zakaz akcja-aktor albo akcja-konsekwencja zadeklarowany dodatkowo w incompatibleWith
    /// (po ktorejkolwiek stronie) jest zglaszany jako problem, tak samo jak sprzecznosc (klocek na liscie
    /// i jednoczesnie zabroniony). Wola to BlockCatalogLoader (gra) i Loader walidatora - jedna funkcja,
    /// zeby oba widzialy ten sam graf.
    /// </summary>
    public static class CatalogGraphBuilder
    {
        /// <summary>Typy slotow ograniczane lista onlyWith akcji.</summary>
        public static readonly BlockType[] RestrictedTypes = { BlockType.Actor, BlockType.Consequence };

        /// <summary>
        /// Dopisuje do grafu zakazy wynikajace z list onlyWith. Zwraca dopisane pary (akcja, klocek) w porzadku
        /// katalogu; problemy - opisy bledow danych (pusta lista = katalog czysty). Wywolac PO dodaniu zakazow
        /// z incompatibleWith, bo sprawdza sprzecznosci z nimi.
        /// </summary>
        public static List<KeyValuePair<string, string>> ExpandOnlyWith(IList<Block> blocks, CompatibilityGraph graph,
                                                                        out List<string> problems)
        {
            problems = new List<string>();
            var added = new List<KeyValuePair<string, string>>();
            if (blocks == null || graph == null)
            {
                return added;
            }

            var byId = new Dictionary<string, Block>(StringComparer.Ordinal);
            foreach (Block b in blocks)
            {
                if (b != null && b.Id != null && !byId.ContainsKey(b.Id))
                {
                    byId[b.Id] = b;
                }
            }

            foreach (Block b in blocks)
            {
                if (b == null || b.OnlyWith == null || b.OnlyWith.Count == 0)
                {
                    continue;
                }
                if (b.Type != BlockType.Action)
                {
                    problems.Add(b.Id + ": onlyWith jest dozwolone tylko na klocku akcji");
                    continue;
                }

                var allowed = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in b.OnlyWith)
                {
                    Block other;
                    if (string.IsNullOrEmpty(id))
                    {
                        problems.Add(b.Id + ": pusty wpis onlyWith");
                    }
                    else if (!byId.TryGetValue(id, out other))
                    {
                        problems.Add(b.Id + ": onlyWith wskazuje nieistniejacy klocek " + id);
                    }
                    else if (Array.IndexOf(RestrictedTypes, other.Type) < 0)
                    {
                        problems.Add(b.Id + ": onlyWith " + id + " nie jest aktorem ani konsekwencja");
                    }
                    else if (!allowed.Add(id))
                    {
                        problems.Add(b.Id + ": onlyWith " + id + " powtorzony");
                    }
                    else if (!graph.Allows(b.Id, id))
                    {
                        problems.Add(b.Id + ": onlyWith " + id + " jest jednoczesnie zabroniony w incompatibleWith (sprzecznosc)");
                    }
                }

                bool hasActor = false;
                foreach (string id in allowed)
                {
                    if (byId[id].Type == BlockType.Actor)
                    {
                        hasActor = true;
                    }
                }
                if (!hasActor)
                {
                    problems.Add(b.Id + ": onlyWith bez zadnego aktora - akcji nie da sie zlozyc");
                }

                foreach (Block other in blocks)
                {
                    if (other == null || other == b || Array.IndexOf(RestrictedTypes, other.Type) < 0
                        || allowed.Contains(other.Id))
                    {
                        continue;
                    }
                    if (!graph.Allows(b.Id, other.Id))
                    {
                        problems.Add(b.Id + ": zakaz z " + other.Id + " zadeklarowany takze w incompatibleWith "
                                     + "(drugie zrodlo prawdy - wystarczy lista onlyWith)");
                    }
                    graph.Forbid(b.Id, other.Id);
                    added.Add(new KeyValuePair<string, string>(b.Id, other.Id));
                }
            }
            return added;
        }
    }
}
