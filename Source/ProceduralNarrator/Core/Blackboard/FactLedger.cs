using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProceduralNarrator.Core.Arcs;

namespace ProceduralNarrator.Core.Blackboard
{
    /// <summary>
    /// Jeden fakt o kolonii: co, o jakiej wartosci, od kiedy i jak dlugo obowiazuje.
    ///
    /// WARTOSC JEST LICZBA, NIE FLAGA. Fakt zero-jedynkowy ("bylo rojenie") to wartosc 1, a fakt
    /// licznikowy ("ile razy odparlismy napad") to ta sama struktura z Add zamiast Set - dzieki
    /// czemu warunek Cond_FaktLiczba dziala na obu bez rozgalezienia w kodzie.
    ///
    /// CZAS ZYCIA <= 0 ZNACZY "BEZ WYGASANIA". Nie uzywamy tu wartosci specjalnej w rodzaju
    /// float.MaxValue, bo ta po zakodowaniu do G9 i z powrotem potrafi sie rozjechac o ostatni bit,
    /// a porownanie "dzien + zycie >= teraz" zamienia sie wtedy w loterie przy dlugiej rozgrywce.
    /// </summary>
    public sealed class Fact
    {
        /// <summary>Znacznik typu linii w pamieci gry (FactLedger.ToPersistableLines).</summary>
        public const string LineTag = "F";

        /// <summary>Liczba pol linii WLACZNIE ze znacznikiem - TryDecode odrzuca kazda inna.</summary>
        public const int FieldCount = 5;

        public string Key;
        public float Value;

        /// <summary>Dzien gry, w ktorym fakt zostal ustawiony albo ostatni raz odswiezony.</summary>
        public float SetDay;

        /// <summary>Ile dni fakt obowiazuje. Wartosc <= 0 znaczy "bez wygasania".</summary>
        public float LifespanDays;

        /// <summary>
        /// Czy fakt obowiazuje w danym dniu gry. WYGASANIE JEST LENIWE - liczone przy odczycie,
        /// nigdy przez czyszczenie ksiegi. Wariant czyszczacy uzaleznilby zawartosc pamieci od
        /// liczby wywolan compa (czyli od liczby map), a to rozjechaloby odcisk pamieci miedzy
        /// ramionami eksperymentu symulatora.
        /// </summary>
        public bool IsActive(float nowDay)
        {
            if (LifespanDays <= 0f)
            {
                return true;
            }
            // Cofniety czas gry (wczytanie zapisu sprzed ustawienia faktu) traktujemy jak "jeszcze
            // obowiazuje", a nie jak wygasniecie - kierunek bledu bezpieczny, bo fakt i tak wygasnie.
            // TOLERANCJA GRANICY (S6): w ticku ustawienie + zycie*60000 fakt jest ZAWSZE wygasly.
            // Bez niej o werdykcie w tym jednym ticku decydowal szum float32 (tick/60000f): fakt
            // "zyl" o jeden interwal dluzej w ok. 10% ustawien, a analiza liczaca na dniach z logu
            // (3 miejsca po przecinku) nie umiala tego odtworzyc - falszywe naruszenie 28.
            return nowDay - SetDay < LifespanDays - FactLedger.BoundaryToleranceDays;
        }

        public string Encode()
        {
            return LineTag
                   + ArcInstance.FieldSeparator + ArcInstance.Escape(Key)
                   + ArcInstance.FieldSeparator + Value.ToString("G9", CultureInfo.InvariantCulture)
                   + ArcInstance.FieldSeparator + SetDay.ToString("G9", CultureInfo.InvariantCulture)
                   + ArcInstance.FieldSeparator + LifespanDays.ToString("G9", CultureInfo.InvariantCulture);
        }

        /// <summary>Dekoduje linie. NIGDY nie rzuca - uszkodzona linia to odrzucenie, nie awaria wczytania.</summary>
        public static bool TryDecode(string line, out Fact fact)
        {
            fact = null;
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }
            string[] f = line.Split(ArcInstance.FieldSeparator);
            if (f.Length != FieldCount || f[0] != LineTag)
            {
                return false;
            }

            string key = ArcInstance.Unescape(f[1]);
            // Klucz sprawdzamy tym samym predykatem co przy zapisie: linia z kluczem spoza alfabetu
            // powstalaby tylko przez reczna edycje zapisu albo przez blad kodeka, a w obu wypadkach
            // wpuszczenie jej do ksiegi zepsuloby postac kanoniczna widziana przez warunki.
            if (!FactLedger.IsValidKey(key))
            {
                return false;
            }

            float wartosc, dzien, zycie;
            if (!ArcInstance.Flt(f[2], out wartosc) || !ArcInstance.Flt(f[3], out dzien) || !ArcInstance.Flt(f[4], out zycie))
            {
                return false;
            }

            fact = new Fact { Key = key, Value = wartosc, SetDay = dzien, LifespanDays = zycie };
            return true;
        }
    }

    /// <summary>
    /// Fakty o kolonii JEDNEJ mapy - trzeci magazyn blackboardu obok historii zdarzen (EventHistory)
    /// i rejestru watkow (ArcLedger). Odpowiada czlonowi "fakty o swiecie" z sekcji 5.2 koncepcji,
    /// ktorego model danych pracy swiadomie nie opisuje.
    ///
    /// ZASIEG: WYLACZNIE MAPA (decyzja autora). Fakt o frakcji albo quescie nalezy do swiata, a nie
    /// do kolonii - zapisany tutaj albo rozjechalby sie miedzy dwiema koloniami, albo druga kolonia
    /// nigdy by sie o nim nie dowiedziala. Pilnuje tego audyt startowy warstwy integracji.
    ///
    /// KOLEJNOSC JEST CZESCIA KONTRAKTU. Kazde wyjscie (kodek, postac kanoniczna, iteracja) jest
    /// posortowane ordynalnie po kluczu. Bez tego swieza rozgrywka miala by kolejnosc chronologiczna,
    /// a wczytana leksykograficzna - i "to samo ziarno oraz stan daje te same decyzje" przestaloby
    /// byc prawda po cyklu zapis-wczytanie, bez zadnego objawu w logu.
    /// </summary>
    public sealed class FactLedger
    {
        /// <summary>Separator par w postaci kanonicznej - takze na poczatku i koncu, patrz Canonical.</summary>
        public const char PairSeparator = ';';

        /// <summary>Separator klucza od wartosci w postaci kanonicznej.</summary>
        public const char ValueSeparator = '=';

        /// <summary>
        /// Separator wartosci od WIEKU faktu w postaci kanonicznej.
        ///
        /// W postaci kanonicznej jest WIEK (ile dni od ustawienia), a nie dzien ustawienia - bo
        /// snapshot jest zamrozony na ture i warunek nie zna dzisiejszego dnia gry. Z dniem
        /// ustawienia warunek "fakt starszy niz N dni" musialby dobrac sobie "dzis" z innego pola
        /// snapshotu (DaysPassed liczy sie od ZALOZENIA kolonii, a zegar faktow od startu gry),
        /// czyli po cichu mieszalby dwie rozne osie czasu.
        /// </summary>
        public const char DaySeparator = '@';

        private Dictionary<string, Fact> facts = new Dictionary<string, Fact>(StringComparer.Ordinal);

        /// <summary>
        /// Fakty zdarzenia czekajace na rozstrzygniecie wykonania albo na zastosowanie (krok 6).
        /// Najwyzej jedno na mape: kolejne zdarzenie mapy moze zapasc najwczesniej w nastepnym
        /// wywolaniu compa, a to wywolanie zaczyna sie od ResolvePending.
        /// </summary>
        public PendingFacts Pending;

        /// <summary>Wszystkie fakty w ksiedze, takze wygasle (te sa odsiewane dopiero przy odczycie).</summary>
        public int Count
        {
            get { return facts.Count; }
        }

        /// <summary>
        /// Stawia fakty wybranego zdarzenia w kolejce (przed yield return). Deklaracje o kluczu spoza
        /// alfabetu NIE wchodza do kolejki - linia z jednym zlym kluczem bylaby przy wczytaniu
        /// odrzucona W CALOSCI, czyli jeden blad tresci kasowalby poprawne fakty tego samego zdarzenia.
        /// Zwraca odrzucone deklaracje do logu; overwritten = kolejka nie byla pusta (nie powinno sie
        /// zdarzyc, bo kazde wywolanie compa zaczyna sie od ResolvePending - wolajacy ma to zalogowac).
        /// </summary>
        public List<FactEvent> Queue(PendingFacts pending, out bool overwritten)
        {
            var odrzucone = new List<FactEvent>();
            overwritten = Pending != null;
            if (pending == null)
            {
                return odrzucone;
            }

            var poprawne = new List<FactWrite>();
            foreach (FactWrite w in pending.Writes ?? new List<FactWrite>())
            {
                if (w != null && IsValidKey(w.key) && !float.IsNaN(w.value) && !float.IsInfinity(w.value)
                    && !float.IsNaN(w.lifespanDays) && !float.IsInfinity(w.lifespanDays))
                {
                    poprawne.Add(w);
                    continue;
                }
                odrzucone.Add(new FactEvent
                {
                    Kind = FactEventKind.Rejected, Key = w == null ? null : w.key, Day = pending.Day,
                    SourceTick = pending.Tick, SourceDecision = pending.DecisionIndex, Reason = "zlyKlucz"
                });
            }

            pending.Writes = poprawne;
            pending.Confirmed = false;
            Pending = poprawne.Count == 0 ? null : pending;
            return odrzucone;
        }

        /// <summary>
        /// Potwierdzenie na SCIEZCE NORMALNEJ (kod po yield return, ten sam tick). Wykonane zdarzenie
        /// tylko OZNACZA kolejke - zastosowanie nastepuje w ResolvePending, w tym samym miejscu co
        /// po sciezce spoznionej. Niewykonane - kolejka wypada od razu, z wpisem do logu.
        /// Kolejka innej decyzji (inny numer albo tick) zostaje nietknieta: nie wolno potwierdzic
        /// cudzych faktow werdyktem, ktory dotyczy innego zdarzenia.
        /// </summary>
        public FactEvent ConfirmPending(int decisionIndex, int tick, ExecStatus status)
        {
            PendingFacts p = Pending;
            if (p == null || p.DecisionIndex != decisionIndex || p.Tick != tick)
            {
                return null;
            }
            if (ExecutionConfirmation.CountsAsExecuted(status))
            {
                p.Confirmed = true;
                return null;
            }
            Pending = null;
            return new FactEvent
            {
                Kind = FactEventKind.Dropped, Day = p.Day, SourceTick = p.Tick, SourceDecision = p.DecisionIndex,
                Reason = ExecutionConfirmation.Label(status)
            };
        }

        /// <summary>
        /// JEDYNE miejsce, w ktorym fakty zdarzen trafiaja do ksiegi. Wolane na poczatku kazdego
        /// wywolania compa, PO obsludze lukow - dzieki temu slad zdarzenia nie jest widoczny dla luku
        /// otwieranego tym samym zdarzeniem niezaleznie od tego, ktora sciezka potwierdzila wykonanie.
        ///
        /// Kolejka niepotwierdzona (iterator nie wrocil po yield) jest klasyfikowana spoznionie tym
        /// samym kodem co luki (ExecutionConfirmation.ClassifyLate). lastFireOf podaje lastFireTicks
        /// incydentu po nazwie - rdzen nie zna API gry, wiec dostaje to z zewnatrz.
        /// </summary>
        public List<FactEvent> ResolvePending(int nowTick, Func<string, int> lastFireOf)
        {
            var wynik = new List<FactEvent>();
            PendingFacts p = Pending;
            if (p == null || p.Tick >= nowTick)
            {
                return wynik;
            }

            string status = ExecutionConfirmation.Label(ExecStatus.Executed);
            if (!p.Confirmed)
            {
                int teraz = lastFireOf == null ? -1 : lastFireOf(p.IncidentDefName);
                ExecStatus st = ExecutionConfirmation.ClassifyLate(teraz, p.LastFireBefore, p.Tick);
                status = ExecutionConfirmation.Label(st);
                if (!ExecutionConfirmation.CountsAsExecuted(st))
                {
                    Pending = null;
                    wynik.Add(new FactEvent
                    {
                        Kind = FactEventKind.Dropped, Day = p.Day, SourceTick = p.Tick,
                        SourceDecision = p.DecisionIndex, Reason = status
                    });
                    return wynik;
                }
            }

            // Kolejnosc deklaracji = kolejnosc klockow zwyciezcy (deterministyczna), a kazda
            // dostaje dzien DECYZJI - opoznienie kolejki nie przesuwa wieku faktu.
            foreach (FactWrite w in p.Writes)
            {
                bool ok = w.ApplyTo(this, p.Day);
                wynik.Add(new FactEvent
                {
                    Kind = ok ? FactEventKind.Set : FactEventKind.Rejected,
                    Key = w.key,
                    Value = ok ? ValueOf(w.key, p.Day, float.NaN) : float.NaN,
                    LifespanDays = w.lifespanDays,
                    Day = p.Day,
                    SourceTick = p.Tick,
                    SourceDecision = p.DecisionIndex,
                    Reason = p.Confirmed ? "potwierdzone" : status
                });
            }
            Pending = null;
            return wynik;
        }

        /// <summary>
        /// Tolerancja granicy wygasania: pol interwalu compa (1000 tickow = 1/60 dnia), czyli 1/120 dnia.
        /// Decyzje i zastosowania faktow leza na siatce 1000 tickow, wiec wiek faktu w chwili decyzji
        /// jest wielokrotnoscia 1/60 dnia z bledem float32 rzedu 1e-4 - pol interwalu rozdziela
        /// "ostatni interwal zycia" i "tick wygasniecia" z zapasem dwoch rzedow wielkosci. Te sama
        /// regule stosuje Cond_FaktOd i analiza danych (analiza_v7.py).
        /// </summary>
        public const float BoundaryToleranceDays = 1f / 120f;

        /// <summary>
        /// Czy czas zycia ma sens: 0 albo mniej (fakt wieczny) albo co najmniej jeden dzien. Fakt
        /// o zyciu krotszym niz dzien jest bledem tresci - kolejka stosuje go w NASTEPNYM wywolaniu
        /// compa, wiec przy zyciu ponizej 1/60 + tolerancja dnia nie bylby widoczny ani razu.
        /// Pilnuja tego walidator i audyt startowy (PNStartup), nie ksiega - ksiega zapisuje, co dostanie.
        /// </summary>
        public static bool IsUsableLifespan(float lifespanDays)
        {
            return !float.IsNaN(lifespanDays) && (lifespanDays <= 0f || lifespanDays >= 1f);
        }

        /// <summary>
        /// Czy klucz nadaje sie na fakt. Alfabet jest WASKI celowo: klucz jedzie zarowno przez kodek
        /// pamieci (separator '|'), jak i przez postac kanoniczna w snapshocie (';', '=', '@'), wiec
        /// kazdy z tych znakow w kluczu rozbilby jedno z dwoch wyjsc. Escape() zamienilby '|' po cichu
        /// na '/', czyli dwa rozne klucze skleilyby sie w jeden - dlatego odrzucamy, zamiast naprawiac.
        /// </summary>
        public static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 64)
            {
                return false;
            }
            // PIERWSZY ZNAK: litera albo cyfra (S6). Klucz "-" przechodzil alfabet, ale jest tokenem
            // pustosci kodeka (ArcInstance.EmptyToken) - fakt dzialal do zapisu i znikal po
            // wczytaniu oraz w kazdym ramieniu symulatora (kopie ida przez kodek).
            char c0 = key[0];
            if (!((c0 >= 'a' && c0 <= 'z') || (c0 >= 'A' && c0 <= 'Z') || (c0 >= '0' && c0 <= '9')))
            {
                return false;
            }
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                          || c == '.' || c == '_' || c == '-';
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Ustawia fakt (nadpisuje wartosc i ODSWIEZA dzien). Zwraca false przy kluczu spoza alfabetu -
        /// wolajacy ma to zalogowac, bo to jest blad tresci w XML, a nie stan gry.
        /// </summary>
        public bool Set(string key, float value, float nowDay, float lifespanDays)
        {
            return Zapisz(key, value, nowDay, lifespanDays, false);
        }

        /// <summary>
        /// Dolicza do faktu licznikowego i odswieza dzien. Brak faktu = start od zera, wiec pierwsze
        /// Add daje dokladnie delte. Wygasly fakt tez startuje od zera.
        ///
        /// TO JEST LICZNIK SERII, NIE OKNO OSTATNICH N DNI. Kazde Add odswieza dzien, wiec fakt
        /// wygasa dopiero po przerwie dluzszej niz czas zycia, a do tego czasu wartosc rosnie
        /// o kazde zdarzenie serii. SPROSTOWANIE: wczesniejsza wersja tego komentarza (i komentarz
        /// w katalogu konsekwencji) opisywala "ile razy w ostatnich N dniach" - dane z symulatora
        /// 2026-09-23 pokazaly licznik 13 przy 2 walkach w ostatnich 20 dniach. Dla progu >= 1
        /// roznicy nie ma (fakt obowiazuje dokladnie wtedy, gdy ostatnie zdarzenie bylo mniej niz
        /// N dni temu); prawdziwe okno przesuwne wymagaloby pamietania chwili KAZDEGO zdarzenia.
        /// </summary>
        public bool Add(string key, float delta, float nowDay, float lifespanDays)
        {
            return Zapisz(key, delta, nowDay, lifespanDays, true);
        }

        private bool Zapisz(string key, float value, float nowDay, float lifespanDays, bool dolicz)
        {
            if (!IsValidKey(key) || float.IsNaN(value) || float.IsInfinity(value)
                || float.IsNaN(nowDay) || float.IsInfinity(nowDay)
                || float.IsNaN(lifespanDays) || float.IsInfinity(lifespanDays))
            {
                return false;
            }

            float wartosc = value;
            if (dolicz)
            {
                Fact stary;
                if (facts.TryGetValue(key, out stary) && stary.IsActive(nowDay))
                {
                    wartosc = stary.Value + value;
                }
            }

            facts[key] = new Fact { Key = key, Value = wartosc, SetDay = nowDay, LifespanDays = lifespanDays };
            return true;
        }

        /// <summary>Czy fakt obowiazuje w tym dniu gry.</summary>
        public bool Has(string key, float nowDay)
        {
            Fact f;
            return facts.TryGetValue(key, out f) && f.IsActive(nowDay);
        }

        /// <summary>Wartosc obowiazujacego faktu albo wartosc zastepcza, gdy go nie ma lub wygasl.</summary>
        public float ValueOf(string key, float nowDay, float missing)
        {
            Fact f;
            return facts.TryGetValue(key, out f) && f.IsActive(nowDay) ? f.Value : missing;
        }

        /// <summary>
        /// Ile dni minelo od ustawienia obowiazujacego faktu. Brak faktu daje -1, a NIE wiek kolonii -
        /// "nigdy sie nie zdarzylo" musi dac sie odroznic od "zdarzylo sie bardzo dawno". Ten sam blad
        /// w druga strone popelnialo kiedys DaysSinceLastEvent przy pustej historii.
        /// </summary>
        public float AgeDays(string key, float nowDay)
        {
            Fact f;
            if (!facts.TryGetValue(key, out f) || !f.IsActive(nowDay))
            {
                return -1f;
            }
            float wiek = nowDay - f.SetDay;
            return wiek < 0f ? 0f : wiek;
        }

        /// <summary>
        /// WSZYSTKIE fakty ksiegi, takze wygasle, POSORTOWANE ordynalnie po kluczu - do opisu stanu
        /// w logu ([PN-LOAD], [PN-RESET]) i akcji debugowej. Warunki czytaja Active/Canonical, nie to.
        /// </summary>
        public List<Fact> AllSorted()
        {
            return facts.Values.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
        }

        /// <summary>Obowiazujace fakty, POSORTOWANE ordynalnie po kluczu (kontrakt determinizmu).</summary>
        public List<Fact> Active(float nowDay)
        {
            return facts.Values
                        .Where(f => f.IsActive(nowDay))
                        .OrderBy(f => f.Key, StringComparer.Ordinal)
                        .ToList();
        }

        /// <summary>
        /// Postac kanoniczna dla WorldSnapshot: ";klucz=wartosc@wiekWdniach;klucz2=...;".
        ///
        /// Separator jest TAKZE na poczatku i na koncu, zeby warunek mogl szukac ";klucz=" jednym
        /// IndexOf bez ryzyka trafienia w srodek dluzszego klucza ("ruiny" w "ruinyOtwarte").
        /// Wygasle fakty sa juz odsiane - snapshot jest zamrozony na ture, wiec warunki nie musza
        /// (i nie moga) znac biezacego dnia.
        /// </summary>
        public string Canonical(float nowDay)
        {
            List<Fact> aktywne = Active(nowDay);
            if (aktywne.Count == 0)
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append(PairSeparator);
            for (int i = 0; i < aktywne.Count; i++)
            {
                Fact f = aktywne[i];
                float wiek = nowDay - f.SetDay;
                sb.Append(f.Key).Append(ValueSeparator)
                  .Append(f.Value.ToString("G9", CultureInfo.InvariantCulture))
                  .Append(DaySeparator)
                  .Append((wiek < 0f ? 0f : wiek).ToString("G9", CultureInfo.InvariantCulture))
                  .Append(PairSeparator);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Linie do zapisu gry. Wygasle fakty TEZ ida do zapisu: usuwanie ich tutaj uzaleznilo by
        /// zawartosc pliku od dnia, w ktorym gracz akurat zapisal gre, a liczba kluczy jest i tak
        /// ograniczona katalogiem XML, wiec ksiega nie rosnie bez konca.
        /// </summary>
        public List<string> ToPersistableLines()
        {
            List<string> linie = LinieFaktow();
            // Kolejka idzie do zapisu razem z faktami: zapis gry miedzy decyzja a nastepnym
            // wywolaniem compa nie moze zgubic faktow zdarzenia, ktore juz sie wykonalo.
            if (Pending != null)
            {
                linie.Add(Pending.Encode());
            }
            return linie;
        }

        private List<string> LinieFaktow()
        {
            return facts.Values
                        .OrderBy(f => f.Key, StringComparer.Ordinal)
                        .Select(f => f.Encode())
                        .ToList();
        }

        /// <summary>
        /// Odtwarza ksiege z linii. Nigdy nie rzuca; zwraca liczbe linii odrzuconych.
        /// Stan sprzed wywolania jest ZASTEPOWANY, nie laczony.
        /// </summary>
        public int RestoreFromLines(IEnumerable<string> lines)
        {
            facts = new Dictionary<string, Fact>(StringComparer.Ordinal);
            Pending = null;
            if (lines == null)
            {
                return 0;
            }

            int odrzucone = 0;
            foreach (string line in lines)
            {
                if (line != null && line.StartsWith(PendingFacts.LineTag + ArcInstance.FieldSeparator, StringComparison.Ordinal))
                {
                    // Najwyzej JEDNA kolejka - druga linia Q oznacza uszkodzony zapis.
                    PendingFacts p;
                    if (Pending == null && PendingFacts.TryDecode(line, out p))
                    {
                        Pending = p;
                        continue;
                    }
                    odrzucone++;
                    continue;
                }
                Fact f;
                if (Fact.TryDecode(line, out f) && !facts.ContainsKey(f.Key))
                {
                    facts[f.Key] = f;
                    continue;
                }
                odrzucone++;
            }
            return odrzucone;
        }

        /// <summary>Gleboka kopia przez kodek - do checkpointu ramion eksperymentu symulatora.</summary>
        public FactLedger Clone()
        {
            var c = new FactLedger();
            c.RestoreFromLines(ToPersistableLines());
            return c;
        }

        /// <summary>Opis do logu czytelnego i akcji debugowej: "klucz=wartosc" po przecinku.</summary>
        public string Summary(float nowDay)
        {
            List<Fact> aktywne = Active(nowDay);
            if (aktywne.Count == 0)
            {
                return string.Empty;
            }
            return string.Join(",", aktywne
                .Select(f => f.Key + "=" + f.Value.ToString("0.##", CultureInfo.InvariantCulture))
                .ToArray());
        }
    }
}
