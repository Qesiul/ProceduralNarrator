using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProceduralNarrator.Core.Blackboard;
using ProceduralNarrator.Core.Conditions;

namespace ProceduralNarrator.Core.Arcs
{
    /// <summary>
    /// Katalog lukow narracyjnych po walidacji. Luk z bledem w danych jest ODRZUCANY w calosci
    /// i zglaszany - czesciowo poprawny automat (np. przejscie do nieistniejacej fazy) dzialalby
    /// pozornie, a psul by dopiero konkretna rozgrywke.
    ///
    /// Walidacja zyje w rdzeniu, nie w audycie startowym, bo te same reguly musi sprawdzic
    /// walidator offline na prawdziwym XML (test 11a) - jedna implementacja, dwa miejsca uzycia.
    /// </summary>
    public sealed class ArcCatalog
    {
        private readonly List<ArcDefinition> arcs;
        private readonly Dictionary<string, ArcDefinition> byId;

        /// <summary>Znak "{FRAKCJA}" w tekscie komunikatu - podstawiany nazwa zwiazanej frakcji.</summary>
        public const string FactionPlaceholder = "{FRAKCJA}";

        private ArcCatalog(List<ArcDefinition> valid)
        {
            // Kolejnosc KANONICZNA: priorytet malejaco, potem defName porzadkiem ordinal. Od niej
            // zalezy, ktory luk otwiera sie pierwszy, gdy jedno zdarzenie pasuje do kilku zasiewow,
            // wiec nie moze zalezec od kolejnosci plikow ani Defow w DefDatabase.
            arcs = valid.OrderByDescending(a => a.priority)
                        .ThenBy(a => a.defName, StringComparer.Ordinal)
                        .ToList();
            // Pierwszy wygrywa: duplikaty odrzuca juz Build, ale konstruktor nie ma prawa rzucic,
            // gdyby kiedys dostal liste niezwalidowana (wyjatek przy starcie gry zamiast bledu w logu).
            byId = new Dictionary<string, ArcDefinition>(StringComparer.Ordinal);
            foreach (ArcDefinition a in arcs)
            {
                if (a.defName != null && !byId.ContainsKey(a.defName))
                {
                    byId[a.defName] = a;
                }
            }
        }

        public IReadOnlyList<ArcDefinition> Arcs
        {
            get { return arcs; }
        }

        public int Count
        {
            get { return arcs.Count; }
        }

        public ArcDefinition ById(string id)
        {
            ArcDefinition a;
            return id != null && byId.TryGetValue(id, out a) ? a : null;
        }

        public static ArcCatalog Empty()
        {
            return new ArcCatalog(new List<ArcDefinition>());
        }

        /// <summary>
        /// Buduje katalog z definicji, odrzucajac luki z bledami. `problems` dostaje po jednej
        /// linii na blad (pusta lista = katalog w pelni poprawny).
        /// </summary>
        public static ArcCatalog Build(IEnumerable<ArcDefinition> definitions, out List<string> problems)
        {
            problems = new List<string>();
            var all = (definitions ?? Enumerable.Empty<ArcDefinition>()).Where(d => d != null).ToList();
            var bad = new HashSet<ArcDefinition>();

            // Reguly pojedynczego luku.
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ArcDefinition a in all)
            {
                var local = new List<string>();
                ValidateOne(a, local);
                if (!string.IsNullOrEmpty(a.defName) && !seenIds.Add(a.defName))
                {
                    local.Add("powtorzony defName");
                }
                if (local.Count > 0)
                {
                    bad.Add(a);
                    foreach (string p in local)
                    {
                        problems.Add("luk " + (a.defName ?? "(bez defName)") + ": " + p);
                    }
                }
            }

            // Nastepniki: istnieja i graf jest acykliczny (inaczej zamkniecie luku moglo by
            // otwierac luki w kolko, bez zadnego zdarzenia w grze).
            var ids = new HashSet<string>(all.Where(a => !string.IsNullOrEmpty(a.defName)).Select(a => a.defName), StringComparer.Ordinal);
            foreach (ArcDefinition a in all)
            {
                if (string.IsNullOrEmpty(a.successor))
                {
                    continue;
                }
                if (!ids.Contains(a.successor) || string.Equals(a.successor, a.defName, StringComparison.Ordinal))
                {
                    bad.Add(a);
                    problems.Add("luk " + a.defName + ": nastepnik '" + a.successor + "' nie istnieje albo wskazuje na siebie");
                    continue;
                }
                // Nastepnik otwiera sie ZAMKNIECIEM poprzednika, bez wlasnego zdarzenia zasiewu -
                // nie ma wiec skad wziac frakcji. Zasiew wiazacy frakcje zrobil by z niego luk,
                // ktory nigdy sie nie otworzy (dyrektor odmowi), wiec to blad danych, nie cicha regula.
                // FirstOrDefault, nie First: nie zakladamy, ze sprawdzenie istnienia wyzej zawsze
                // zostanie - wyjatek w walidacji katalogu wywrocilby start gry, a brak nastepnika
                // i tak wylapie petla wiszacych referencji ponizej.
                ArcDefinition nast = all.FirstOrDefault(x => string.Equals(x.defName, a.successor, StringComparison.Ordinal));
                if (nast != null && nast.Seed != null && nast.Seed.bindsFaction)
                {
                    bad.Add(a);
                    problems.Add("luk " + a.defName + ": nastepnik '" + a.successor + "' wiaze frakcje w Zasiewie - otwarty zamknieciem nie ma skad jej wziac");
                    continue;
                }
                var visited = new HashSet<string>(StringComparer.Ordinal) { a.defName };
                string cur = a.successor;
                while (!string.IsNullOrEmpty(cur))
                {
                    if (!visited.Add(cur))
                    {
                        bad.Add(a);
                        problems.Add("luk " + a.defName + ": cykl w grafie nastepnikow (" + string.Join(" -> ", visited.ToArray()) + ")");
                        break;
                    }
                    ArcDefinition next = all.FirstOrDefault(x => string.Equals(x.defName, cur, StringComparison.Ordinal));
                    cur = next == null ? null : next.successor;
                }
            }

            // Najwyzej JEDNA zwiazana frakcja na mape: wszystkie luki wiazace frakcje musza nalezec
            // do jednej grupy wykluczajacej. Inaczej dwa aktywne luki mogly by chciec ustawic
            // rozne frakcje temu samemu napadowi - parms.faction jest jeden.
            var wiazace = all.Where(a => a.BindsFaction).ToList();
            var grupy = wiazace.Select(a => a.exclusionGroup ?? string.Empty).Distinct(StringComparer.Ordinal).ToList();
            if (wiazace.Count > 0 && (grupy.Count > 1 || string.IsNullOrEmpty(grupy[0])))
            {
                foreach (ArcDefinition a in wiazace)
                {
                    bad.Add(a);
                }
                problems.Add("luki wiazace frakcje (" + string.Join(", ", wiazace.Select(a => a.defName).ToArray())
                             + ") musza nalezec do JEDNEJ niepustej grupy wykluczajacej (dzis: "
                             + string.Join(", ", grupy.Select(g => g.Length == 0 ? "(brak)" : g).ToArray()) + ")");
            }

            // NASTEPNIK PYTAJACY O WATEK POPRZEDNIKA (S6). Nastepnik otwiera sie w kroku 2 OnExecuted,
            // na snapshocie z POCZATKU tury (decyzja autora: warunki widza swiat z poczatku tury), a
            // poprzednik zamyka sie w kroku 1 tego samego zdarzenia - w snapshocie jest wiec jeszcze
            // otwarty. Warunek na jego zamkniecie nigdy by sie nie spelnil (a "nieotwarty" tez nie).
            // Odrzucamy NASTEPNIKA, bo tam jest wadliwy warunek; poprzednik wypada kaskadowo regula
            // "nastepnik odrzucony" nizej. Samo otwarcie nastepnikiem juz gwarantuje zamkniecie poprzednika.
            foreach (ArcDefinition poprzednik in all)
            {
                if (string.IsNullOrEmpty(poprzednik.successor))
                {
                    continue;
                }
                foreach (ArcDefinition nast in all.Where(x => string.Equals(x.defName, poprzednik.successor, StringComparison.Ordinal)))
                {
                    if (WatkiWarunkow(nast).Any(w => string.Equals(w, poprzednik.defName, StringComparison.Ordinal)))
                    {
                        bad.Add(nast);
                        problems.Add("luk " + nast.defName + ": warunek startu pyta o watek swojego poprzednika '"
                                     + poprzednik.defName + "' - przy otwarciu nastepnikiem snapshot pokazuje go jeszcze jako otwarty");
                    }
                }
            }

            var valid = all.Where(a => !bad.Contains(a)).ToList();

            // Punkt staly: nastepnik odrzuconego luku nie moze zostac jako wiszaca referencja, a warunek
            // watku (Cond_Watek*) nie moze wskazywac luku spoza katalogu - nigdy by sie nie zmienil.
            bool zmiana = true;
            while (zmiana)
            {
                zmiana = false;
                var validIds = new HashSet<string>(valid.Select(a => a.defName), StringComparer.Ordinal);
                foreach (ArcDefinition a in valid.ToList())
                {
                    if (!string.IsNullOrEmpty(a.successor) && !validIds.Contains(a.successor))
                    {
                        valid.Remove(a);
                        problems.Add("luk " + a.defName + ": odrzucony, bo jego nastepnik '" + a.successor + "' zostal odrzucony");
                        zmiana = true;
                        continue;
                    }
                    string brak = WatkiWarunkow(a).FirstOrDefault(w => !validIds.Contains(w));
                    if (brak != null)
                    {
                        valid.Remove(a);
                        problems.Add("luk " + a.defName + ": warunek watku wskazuje luk '" + brak + "', ktorego nie ma w katalogu (albo zostal odrzucony)");
                        zmiana = true;
                        continue;
                    }
                    string zlyWynik = ZlyWynikZamkniecia(a, all);
                    if (zlyWynik != null)
                    {
                        valid.Remove(a);
                        problems.Add("luk " + a.defName + ": " + zlyWynik);
                        zmiana = true;
                    }
                }
            }

            return new ArcCatalog(valid);
        }

        /// <summary>Luki wskazywane przez warunki watkow (Cond_WatekOtwarty / Cond_WatekZamkniety) w warunkach startu.</summary>
        internal static IEnumerable<string> WatkiWarunkow(ArcDefinition a)
        {
            if (a == null || a.startConditions == null)
            {
                yield break;
            }
            foreach (NarrativeCondition c in a.startConditions)
            {
                var otwarty = c as Cond_WatekOtwarty;
                if (otwarty != null)
                {
                    yield return otwarty.arc ?? string.Empty;
                }
                var zamkniety = c as Cond_WatekZamkniety;
                if (zamkniety != null)
                {
                    yield return zamkniety.arc ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// Wynik zamkniecia w Cond_WatekZamkniety musi byc wynikiem, ktory wskazany luk MOZE dostac:
        /// stale dyrektora (rozwiazany, wygaszony) albo wartosc "close" ktoregos jego przejscia
        /// (np. "pojednanie" Wendety). Literowka dawala warunek nigdy niespelniony, bez bledu w logu.
        /// </summary>
        private static string ZlyWynikZamkniecia(ArcDefinition a, List<ArcDefinition> all)
        {
            if (a.startConditions == null)
            {
                return null;
            }
            foreach (Cond_WatekZamkniety c in a.startConditions.OfType<Cond_WatekZamkniety>())
            {
                if (string.IsNullOrEmpty(c.outcome))
                {
                    continue;
                }
                ArcDefinition cel = all.FirstOrDefault(x => string.Equals(x.defName, c.arc, StringComparison.Ordinal));
                var wyniki = new HashSet<string>(StringComparer.Ordinal) { ArcDirector.OutcomeResolved, ArcDirector.OutcomeFaded };
                if (cel != null)
                {
                    foreach (ArcTransition t in (cel.transitions ?? new List<ArcTransition>())
                                 .Concat((cel.phases ?? new List<ArcPhase>()).Where(f => f != null && f.transitions != null)
                                                                            .SelectMany(f => f.transitions)))
                    {
                        if (t != null && !string.IsNullOrEmpty(t.close))
                        {
                            wyniki.Add(t.close);
                        }
                    }
                }
                if (!wyniki.Contains(c.outcome))
                {
                    return "warunek watku zamknietego '" + c.arc + "' z wynikiem '" + c.outcome + "', ktorego ten luk nie moze dostac (mozliwe: "
                           + string.Join(", ", wyniki.OrderBy(w => w, StringComparer.Ordinal).ToArray()) + ")";
                }
            }
            return null;
        }

        private static void ValidateOne(ArcDefinition a, List<string> p)
        {
            if (string.IsNullOrEmpty(a.defName))
            {
                p.Add("pusty defName");
            }
            // SPROSTOWANIE (krok 7): w S6 ponizsza wstawka o kluczach faktow weszla MIEDZY "if" a "else if",
            // wiec kontrola SafeId przylgnela do "if (a.startConditions != null)" i dla lukow z warunkami
            // startu (czyli wszystkich) nigdy sie nie wykonywala. Teraz stoi przy swoim "if".
            else if (!SafeId(a.defName))
            {
                p.Add("defName moze zawierac tylko litery, cyfry i '_'");
            }
            // Klucz faktu w warunku startu (S6): zly klucz dawal warunek zawsze niespelniony, a
            // Cond_Fakt z required=false i pustym kluczem - ZAWSZE spelniony. Sprawdzamy ta sama regula
            // co ksiega faktow, a nie sama niepustoscia.
            if (a.startConditions != null)
            {
                foreach (NarrativeCondition c in a.startConditions)
                {
                    string klucz = c is Cond_Fakt ? ((Cond_Fakt)c).key
                                 : c is Cond_FaktOd ? ((Cond_FaktOd)c).key
                                 : c is Cond_FaktLiczba ? ((Cond_FaktLiczba)c).key
                                 : null;
                    if ((c is Cond_Fakt || c is Cond_FaktOd || c is Cond_FaktLiczba) && !FactLedger.IsValidKey(klucz))
                    {
                        p.Add("warunek " + c.GetType().Name + " z niepoprawnym kluczem faktu '" + (klucz ?? "") + "'");
                    }
                    // Styl gracza (krok 7): cecha jako tekst - nieznana cecha daje warunek zawsze falszywy.
                    Cond_StylMocnaStrona styl = c as Cond_StylMocnaStrona;
                    PlayerModel.StyleDimension cecha;
                    if (styl != null && !PlayerModel.StyleDimensions.TryParse(styl.dimension, out cecha))
                    {
                        p.Add("warunek Cond_StylMocnaStrona z nieznana cecha '" + (styl.dimension ?? "") + "'");
                    }
                }
            }
            if (a.cooldownDays < 0f || float.IsNaN(a.cooldownDays))
            {
                p.Add("cooldownDays ujemny");
            }
            if (a.phases == null || a.phases.Count < 2)
            {
                p.Add("luk musi miec co najmniej 2 fazy (Zasiew i Rozwiazanie)");
                return;
            }
            if (a.phases.Any(f => f == null))
            {
                p.Add("pusta faza na liscie");
                return;
            }
            if (a.phases[0].kind != ArcPhaseKind.Seed)
            {
                p.Add("pierwsza faza musi byc Seed");
            }
            if (a.phases[a.phases.Count - 1].kind != ArcPhaseKind.Resolution)
            {
                p.Add("ostatnia faza musi byc Resolution");
            }
            for (int i = 1; i < a.phases.Count; i++)
            {
                if ((int)a.phases[i].kind <= (int)a.phases[i - 1].kind)
                {
                    p.Add("rodzaje faz musza isc scisle rosnaco (Seed < Escalation < Climax < Resolution): "
                          + a.phases[i - 1].kind + " -> " + a.phases[i].kind);
                }
            }

            var phaseIds = new HashSet<string>(StringComparer.Ordinal);
            int pierwszaWiazaca = -1;
            for (int i = 0; i < a.phases.Count; i++)
            {
                ArcPhase f = a.phases[i];
                string fn = "faza " + (f.id ?? ("#" + i.ToString(CultureInfo.InvariantCulture)));
                if (string.IsNullOrEmpty(f.id) || !SafeId(f.id))
                {
                    p.Add(fn + ": id puste albo z niedozwolonymi znakami (litery, cyfry, '_')");
                }
                else if (!phaseIds.Add(f.id))
                {
                    p.Add(fn + ": powtorzone id fazy");
                }
                if (f.expectations == null || f.expectations.Count == 0)
                {
                    p.Add(fn + ": brak oczekiwanego typu (expectations)");
                }
                else
                {
                    foreach (ArcExpectation e in f.expectations)
                    {
                        if (e == null)
                        {
                            p.Add(fn + ": puste oczekiwanie");
                            continue;
                        }
                        if (e.valences == null || e.valences.Count == 0)
                        {
                            p.Add(fn + ": oczekiwanie bez walencji (" + e.Describe() + ") - walencje sa obowiazkowe");
                        }
                        if (e.sameFaction && (i == 0 || pierwszaWiazaca < 0 || pierwszaWiazaca >= i))
                        {
                            p.Add(fn + ": sameFaction wymaga fazy WCZESNIEJSZEJ z bindsFaction");
                        }
                    }
                }
                if (i > 0 && (!(f.maxDays > 0f) || float.IsInfinity(f.maxDays)))
                {
                    p.Add(fn + ": maxDays musi byc dodatni i skonczony");
                }
                if (f.minDaysAfterPrevious < 0f || float.IsNaN(f.minDaysAfterPrevious)
                    || (i > 0 && f.minDaysAfterPrevious >= f.maxDays))
                {
                    p.Add(fn + ": minDaysAfterPrevious ujemny albo nie mniejszy niz maxDays");
                }

                // {FRAKCJA} w komunikacie fazy i: faza wiazaca j <= i (komunikat fazy pokazuje sie
                // po rozpoznaniu jej zdarzenia, czyli juz po zwiazaniu). W przejsciu fazy i: j < i.
                if (f.bindsFaction && pierwszaWiazaca < 0)
                {
                    pierwszaWiazaca = i;
                }
                if (Mentions(f.message) && (pierwszaWiazaca < 0 || pierwszaWiazaca > i))
                {
                    p.Add(fn + ": komunikat uzywa " + FactionPlaceholder + ", a frakcja nie jest jeszcze zwiazana");
                }

                if (f.transitions != null)
                {
                    foreach (ArcTransition t in f.transitions)
                    {
                        ValidateTransition(a, t, i, fn, p);
                        if (t != null && Mentions(t.message) && (pierwszaWiazaca < 0 || pierwszaWiazaca >= i))
                        {
                            p.Add(fn + ": komunikat przejscia uzywa " + FactionPlaceholder + " przed zwiazaniem frakcji");
                        }
                    }
                }
                if (i == 0 && f.transitions != null && f.transitions.Count > 0)
                {
                    p.Add(fn + ": Zasiew nie moze miec przejsc (luk otwiera sie jego spelnieniem)");
                }
            }

            if (a.transitions != null)
            {
                foreach (ArcTransition t in a.transitions)
                {
                    if (t == null)
                    {
                        p.Add("puste przejscie luku");
                        continue;
                    }
                    if (!t.Closes || !string.IsNullOrEmpty(t.target))
                    {
                        p.Add("przejscie calego luku moze tylko zamykac (close), bez target");
                    }
                    if (t.guards == null || t.guards.Count == 0 || t.guards.Any(g => g == null))
                    {
                        p.Add("przejscie calego luku bez straznikow albo z pustym straznikiem");
                    }
                    if (t.Closes && !SafeId(t.close))
                    {
                        p.Add("wynik zamkniecia '" + t.close + "' z niedozwolonymi znakami");
                    }
                    if (Mentions(t.message) && !a.phases[0].bindsFaction)
                    {
                        p.Add("komunikat przejscia calego luku uzywa " + FactionPlaceholder + ", a Zasiew nie wiaze frakcji");
                    }
                }
            }
        }

        private static void ValidateTransition(ArcDefinition a, ArcTransition t, int phaseIndex, string fn, List<string> p)
        {
            if (t == null)
            {
                p.Add(fn + ": puste przejscie");
                return;
            }
            if (t.guards == null || t.guards.Count == 0 || t.guards.Any(g => g == null))
            {
                p.Add(fn + ": przejscie bez straznikow albo z pustym straznikiem (odpalilo by od razu)");
            }
            bool maTarget = !string.IsNullOrEmpty(t.target);
            if (maTarget == t.Closes)
            {
                p.Add(fn + ": przejscie musi miec DOKLADNIE jedno z target/close");
                return;
            }
            if (maTarget)
            {
                int j = a.IndexOf(t.target);
                if (j < 0)
                {
                    p.Add(fn + ": przejscie do nieistniejacej fazy '" + t.target + "'");
                }
                else if (j <= phaseIndex)
                {
                    p.Add(fn + ": przejscie wstecz albo w miejscu ('" + t.target + "') - automat idzie tylko naprzod");
                }
            }
            else if (!SafeId(t.close))
            {
                p.Add(fn + ": wynik zamkniecia '" + t.close + "' z niedozwolonymi znakami");
            }
        }

        private static bool Mentions(string text)
        {
            return text != null && text.IndexOf(FactionPlaceholder, StringComparison.Ordinal) >= 0;
        }

        /// <summary>Identyfikator bezpieczny dla kodeka pamieci i kolumn danych: litery, cyfry, '_'.</summary>
        public static bool SafeId(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            foreach (char c in s)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Jedna linia na luk do [PN-CONFIG] i logu startowego.</summary>
        public static string Describe(ArcDefinition a)
        {
            var fazy = a.phases.Select(f => f.id + ":" + f.kind + ":"
                                            + string.Join("/", f.expectations.Where(e => e != null)
                                                                 .SelectMany(e => e.valences ?? new List<Model.Valence>())
                                                                 .Distinct().Select(v => v.ToString()).ToArray()));
            var krawedzie = new List<string>();
            for (int i = 1; i < a.phases.Count; i++)
            {
                krawedzie.Add(a.phases[i - 1].id + ">" + a.phases[i].id);
                if (a.phases[i].transitions != null)
                {
                    foreach (ArcTransition t in a.phases[i].transitions.Where(x => x != null))
                    {
                        krawedzie.Add(a.phases[i].id + ">" + (t.Closes ? "zamkniecie:" + t.close : t.target));
                    }
                }
            }
            return "luk=" + a.defName
                   + "; priorytet=" + a.priority.ToString(CultureInfo.InvariantCulture)
                   + "; grupa=" + (string.IsNullOrEmpty(a.exclusionGroup) ? "-" : a.exclusionGroup)
                   + "; cooldownDni=" + a.cooldownDays.ToString("0.##", CultureInfo.InvariantCulture)
                   + "; nastepca=" + (string.IsNullOrEmpty(a.successor) ? "-" : a.successor)
                   + "; wiazeFrakcje=" + (a.BindsFaction ? "tak" : "nie")
                   + "; fazy=" + string.Join(",", fazy.ToArray())
                   + "; krawedzie=" + string.Join(",", krawedzie.ToArray())
                   // Krok 7: cecha wymagana jako mocna strona gracza ("!" = wymagany BRAK), "-" = bez warunku.
                   // Analiza sprawdza z tego, ze luk otwiera sie tylko przy tej cesze w stylMocne decyzji.
                   + "; warunekStylu=" + WarunekStylu(a);
        }

        private static string WarunekStylu(ArcDefinition a)
        {
            string[] czesci = (a.startConditions ?? new List<NarrativeCondition>())
                .OfType<Cond_StylMocnaStrona>()
                .Select(c => (c.required ? string.Empty : "!") + (c.dimension ?? "?"))
                .ToArray();
            return czesci.Length == 0 ? "-" : string.Join("/", czesci);
        }
    }
}
