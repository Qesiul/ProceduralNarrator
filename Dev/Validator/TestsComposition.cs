using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ProceduralNarrator.Core.Blackboard;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

/// <summary>
/// Testy warstwy kompozycji wymagane przez plan kroku 3 (punkty 1, 2, 8) plus
/// jednostkowe testy BudgetSplittera. Wartosci oczekiwane pochodza z sekcji
/// --- WZORY --- specyfikacji (zweryfikowane numerycznie przez kontrole spojnosci).
/// </summary>
static class TestsComposition
{
    /// <summary>
    /// Rozbicie przestrzeni na akcje - SPEC 1, sekcja WZORY. To jest JEDYNE miejsce
    /// z recznie utrzymywanymi liczbami katalogu; suma i granice enumeracji sa z niej
    /// WYPROWADZANE (Lacznie, NajwiekszaAkcja), a nie przepisywane.
    ///
    /// Powod jest zmierzony: zakazanie jednej pary (Natura + Zrzut) wywalilo 12 asercji,
    /// z czego szesc powtarzalo te sama liczbe 84 jako literal. Kazdy taki literal to kopia
    /// katalogu, a kopia zestarzeje sie po cichu przy nastepnej zmianie tresci - dokladnie
    /// ta klasa bledu, ktora CLAUDE.md opisuje przy "liczbie, ktorej nie pilnuje asercja".
    /// </summary>
    public static readonly Dictionary<string, int> Oczekiwane = new Dictionary<string, int>
    {
        // KROK 6: liczby ponizej to baza (piec slotow) RAZY liczba zgodnych klockow konsekwencji.
        // Akcja bez zadnej zgodnej konsekwencji zostaje przy bazie, bo pusty slot opcjonalny
        // powstaje wylacznie jako WYMUSZONY - nie jest osobnym wariantem.
        //   x2  Napad, Szal, Rojenie, Amok        (SladWalki, Trofea)
        //   x2  Wedrowiec, Dzikus, Uchodzcy       (NowyCzlowiek, Plotki)
        //       (Uchodzcy: 24 -> 16 w S6 - zakaz Wrak x Uchodzcy, w 1.5 kapsula znika bez wraku)
        //       (Dzikus: 8 -> 4 w S10 kroku 8 - zakaz Plotki x Dzikus, zdziczaly czlowiek wiesci nie niesie)
        //   Rojenie: 16 -> 8 w S10 kroku 8 - zakaz Dzikie x Rojenie (owady z tuneli to nie okoliczna
        //       zwierzyna liczona przez Cond_WildAnimals); zostaje jeden aktor, PN_Aktor_Nieznane
        //   x1  Zrzut, Meteoryt, Burza, Emanator  (Wrak albo ZnakNaNiebie)
        //   x1  Okup, Ambrozja                    (brak zgodnej konsekwencji)
        //
        // 16 -> 8: doszla krawedz zabroniona PN_Aktor_Natura x PN_Akcja_Zrzut
        // ("Natura zostawia po sobie rozbita kapsule z zaopatrzeniem" bylo falszem).
        { "PN_Akcja_Zrzut", 8 },
        // Pierwsza akcja o profilu Negative/Minor i pierwsza w kategorii ThreatSmall.
        { "PN_Akcja_Amok", 8 },
        { "PN_Akcja_Napad", 16 },
        { "PN_Akcja_Rojenie", 8 },
        { "PN_Akcja_Wedrowiec", 16 },
        { "PN_Akcja_Okup", 8 },
        { "PN_Akcja_Uchodzcy", 16 },
        { "PN_Akcja_Meteoryt", 8 },
        { "PN_Akcja_Szal", 8 },
        { "PN_Akcja_Dzikus", 4 },
        { "PN_Akcja_Ambrozja", 4 },
        { "PN_Akcja_Burza", 4 },
        { "PN_Akcja_Emanator", 4 },
        // KROK 9, K1 (pelne pokrycie toru Core): zadna nowa akcja nie przyjmuje wyzwalacza Bogactwo, wiec baza
        // = 1 wyzwalacz (Cisza) x 2 cele x 2 modyfikatory = 4 NA AKTORA, razy liczba aktorow i konsekwencji z onlyWith.
        { "PN_Akcja_Mroz", 4 },              // Natura x Pogoda
        { "PN_Akcja_Upal", 4 },              // Natura x Pogoda
        { "PN_Akcja_Opad", 8 },              // Nieznane x (Pogoda, ZnakNaNiebie)
        { "PN_Akcja_ZimaWulkaniczna", 4 },   // Natura x Pogoda
        { "PN_Akcja_SzalStada", 8 },         // Dzikie x (SladWalki, Trofea)
        { "PN_Akcja_Alfabobry", 8 },         // Natura x (Zwierzyna, Straty)
        { "PN_Akcja_Hodowlane", 4 },         // Hodowlane x Inwentarz
        { "PN_Akcja_Migracja", 4 },          // Dzikie x Zwierzyna
        { "PN_Akcja_Oswojenie", 4 },         // Dzikie x Inwentarz
        { "PN_Akcja_Thrumbo", 4 },           // Natura x Zwierzyna
        { "PN_Akcja_Zaraza", 8 },            // (Los, Natura) x Straty
        { "PN_Akcja_Zwarcie", 4 },           // Los x Straty
        { "PN_Akcja_FalaPsychiczna", 4 },    // Nieznane x Psychika
        { "PN_Akcja_KojacaFala", 4 },        // Nieznane x Psychika
        { "PN_Akcja_Defoliator", 16 },       // (Maszyny, Nieznane) x (ZnakNaNiebie, Straty)
        // KROK 9, K2 (Royalty i Anomaly, Docs/K2_PROPOZYCJA.md): ta sama baza 4 NA AKTORA (bez wyzwalacza Bogactwo),
        // razy aktorzy i konsekwencje z onlyWith; akcja bez konsekwencji (zdarzenia ukryte, K2-b) zostaje przy bazie.
        { "PN_Akcja_KlasterMaszyn", 4 },     // Maszyny x ZnakNaNiebie
        { "PN_Akcja_ZrodloKlopotow", 4 },    // Nieznane x Plotki
        { "PN_Akcja_Abazja", 8 },            // Obcy x (NowyCzlowiek, Plotki)
        { "PN_Akcja_RojTrupow", 4 },         // Otchlan x Groza
        { "PN_Akcja_RojZwierzat", 4 },       // Otchlan x Groza
        { "PN_Akcja_GarstkaTrupow", 4 },     // Otchlan x Groza
        { "PN_Akcja_SzturmTrupow", 8 },      // Otchlan x (SladWalki, Groza)
        { "PN_Akcja_Miesobestie", 8 },       // Otchlan x (SladWalki, Groza)
        { "PN_Akcja_Kolcarze", 8 },          // Otchlan x (SladWalki, Groza)
        { "PN_Akcja_Pozeracze", 8 },         // Otchlan x (SladWalki, Groza)
        { "PN_Akcja_PozeraczeZWody", 8 },    // Otchlan x (SladWalki, Groza)
        { "PN_Akcja_Chimery", 4 },           // Otchlan x Groza
        { "PN_Akcja_Ghul", 8 },              // Otchlan x (SladWalki, Groza)
        { "PN_Akcja_PiesnNienawisci", 4 },   // Kult x Groza
        { "PN_Akcja_RytualKultu", 4 },       // Kult x Groza
        { "PN_Akcja_Oczyslepy", 4 },         // Otchlan, bez konsekwencji (ukryte)
        { "PN_Akcja_Wrzaski", 8 },           // (Otchlan, Nieznane) x Groza
        { "PN_Akcja_Zjawa", 4 },             // Otchlan, bez konsekwencji (ukryte)
        { "PN_Akcja_Kregoslup", 4 },         // Otchlan x Groza
        { "PN_Akcja_Wszczep", 4 },           // Otchlan, bez konsekwencji (ukryte)
        { "PN_Akcja_BramaOtchlani", 4 },     // Otchlan x Groza
        { "PN_Akcja_SerceZMiesa", 4 },       // Otchlan x Groza
        { "PN_Akcja_Nocisfera", 4 },         // Otchlan x Groza
        { "PN_Akcja_ObeliskA", 16 },         // (Otchlan, Nieznane) x (Groza, ZnakNaNiebie)
        { "PN_Akcja_ObeliskD", 16 },         // jw. (bliznieta)
        { "PN_Akcja_ObeliskM", 16 },         // jw. (bliznieta)
        { "PN_Akcja_LadunekC", 4 },          // Obcy x Plotki
        { "PN_Akcja_LadunekS", 4 },          // jw. (bliznieta)
        { "PN_Akcja_LadunekZ", 4 },          // jw. (bliznieta)
        { "PN_Akcja_KrwawyDeszcz", 8 },      // Otchlan x (Pogoda, Groza)
        { "PN_Akcja_Calun", 4 },             // Otchlan x Groza
        { "PN_Akcja_Ciemnosc", 4 },          // Otchlan x Groza
        { "PN_Akcja_Przybysz", 4 },          // Obcy x NowyCzlowiek
        { "PN_Akcja_PrzybyszM", 4 },         // jw. (bliznieta)
    };

    /// <summary>
    /// SPECYFIKACJA "akcja -> dozwolone konsekwencje" (decyzje autora S4 i S6). Graf jest "brak
    /// krawedzi = zgoda", a zakazy konsekwencji deklaruja klocki konsekwencji - wiec NOWA akcja,
    /// o ktorej Blocks_Consequences.xml nie wie, dostalaby po cichu wszystkie konsekwencje
    /// (przeglad S6: "karawana kupiecka... zostaja slady walki" i licznik walk Wendety).
    /// Akcja z katalogu bez wpisu tutaj to BLAD walidatora, nie domyslna zgoda.
    /// </summary>
    public static readonly Dictionary<string, string[]> DozwoloneKonsekwencje = new Dictionary<string, string[]>
    {
        { "PN_Akcja_Napad", new[] { "PN_Kons_SladWalki", "PN_Kons_Trofea" } },
        { "PN_Akcja_Szal", new[] { "PN_Kons_SladWalki", "PN_Kons_Trofea" } },
        { "PN_Akcja_Rojenie", new[] { "PN_Kons_SladWalki", "PN_Kons_Trofea" } },
        { "PN_Akcja_Amok", new[] { "PN_Kons_SladWalki", "PN_Kons_Trofea" } },
        { "PN_Akcja_Wedrowiec", new[] { "PN_Kons_NowyCzlowiek", "PN_Kons_Plotki" } },
        { "PN_Akcja_Dzikus", new[] { "PN_Kons_NowyCzlowiek" } },
        { "PN_Akcja_Uchodzcy", new[] { "PN_Kons_NowyCzlowiek", "PN_Kons_Plotki" } },
        { "PN_Akcja_Zrzut", new[] { "PN_Kons_Wrak" } },
        { "PN_Akcja_Meteoryt", new[] { "PN_Kons_Wrak" } },
        { "PN_Akcja_Burza", new[] { "PN_Kons_ZnakNaNiebie" } },
        { "PN_Akcja_Emanator", new[] { "PN_Kons_ZnakNaNiebie" } },
        { "PN_Akcja_Okup", new string[0] },
        { "PN_Akcja_Ambrozja", new string[0] },
        // Krok 9, K1 (decyzje autora K1-i, E0-8): nowe konsekwencje wedlug rodzin zdarzen.
        { "PN_Akcja_Mroz", new[] { "PN_Kons_Pogoda" } },
        { "PN_Akcja_Upal", new[] { "PN_Kons_Pogoda" } },
        { "PN_Akcja_Opad", new[] { "PN_Kons_Pogoda", "PN_Kons_ZnakNaNiebie" } },
        { "PN_Akcja_ZimaWulkaniczna", new[] { "PN_Kons_Pogoda" } },
        { "PN_Akcja_SzalStada", new[] { "PN_Kons_SladWalki", "PN_Kons_Trofea" } },
        { "PN_Akcja_Alfabobry", new[] { "PN_Kons_Zwierzyna", "PN_Kons_Straty" } },
        { "PN_Akcja_Hodowlane", new[] { "PN_Kons_Inwentarz" } },
        { "PN_Akcja_Migracja", new[] { "PN_Kons_Zwierzyna" } },
        { "PN_Akcja_Oswojenie", new[] { "PN_Kons_Inwentarz" } },
        { "PN_Akcja_Thrumbo", new[] { "PN_Kons_Zwierzyna" } },
        { "PN_Akcja_Zaraza", new[] { "PN_Kons_Straty" } },
        { "PN_Akcja_Zwarcie", new[] { "PN_Kons_Straty" } },
        { "PN_Akcja_FalaPsychiczna", new[] { "PN_Kons_Psychika" } },
        { "PN_Akcja_KojacaFala", new[] { "PN_Kons_Psychika" } },
        { "PN_Akcja_Defoliator", new[] { "PN_Kons_ZnakNaNiebie", "PN_Kons_Straty" } },
        // Krok 9, K2 (decyzje autora K2-b, K2-d, K2-h): rodzina "anomalia" pisze Groza; zdarzenia ukryte - nic.
        { "PN_Akcja_KlasterMaszyn", new[] { "PN_Kons_ZnakNaNiebie" } },
        { "PN_Akcja_ZrodloKlopotow", new[] { "PN_Kons_Plotki" } },
        { "PN_Akcja_Abazja", new[] { "PN_Kons_NowyCzlowiek", "PN_Kons_Plotki" } },
        { "PN_Akcja_RojTrupow", new[] { "PN_Kons_Groza" } },
        { "PN_Akcja_RojZwierzat", new[] { "PN_Kons_Groza" } },
        { "PN_Akcja_GarstkaTrupow", new[] { "PN_Kons_Groza" } },
        { "PN_Akcja_SzturmTrupow", new[] { "PN_Kons_SladWalki", "PN_Kons_Groza" } },
        { "PN_Akcja_Miesobestie", new[] { "PN_Kons_SladWalki", "PN_Kons_Groza" } },
        { "PN_Akcja_Kolcarze", new[] { "PN_Kons_SladWalki", "PN_Kons_Groza" } },
        { "PN_Akcja_Pozeracze", new[] { "PN_Kons_SladWalki", "PN_Kons_Groza" } },
        { "PN_Akcja_PozeraczeZWody", new[] { "PN_Kons_SladWalki", "PN_Kons_Groza" } },
        { "PN_Akcja_Chimery", new[] { "PN_Kons_Groza" } },
        { "PN_Akcja_Ghul", new[] { "PN_Kons_SladWalki", "PN_Kons_Groza" } },
        { "PN_Akcja_PiesnNienawisci", new[] { "PN_Kons_Groza" } },
        { "PN_Akcja_RytualKultu", new[] { "PN_Kons_Groza" } },
        { "PN_Akcja_Oczyslepy", new string[0] },
        { "PN_Akcja_Wrzaski", new[] { "PN_Kons_Groza" } },
        { "PN_Akcja_Zjawa", new string[0] },
        { "PN_Akcja_Kregoslup", new[] { "PN_Kons_Groza" } },
        { "PN_Akcja_Wszczep", new string[0] },
        { "PN_Akcja_BramaOtchlani", new[] { "PN_Kons_Groza" } },
        { "PN_Akcja_SerceZMiesa", new[] { "PN_Kons_Groza" } },
        { "PN_Akcja_Nocisfera", new[] { "PN_Kons_Groza" } },
        { "PN_Akcja_ObeliskA", new[] { "PN_Kons_Groza", "PN_Kons_ZnakNaNiebie" } },
        { "PN_Akcja_ObeliskD", new[] { "PN_Kons_Groza", "PN_Kons_ZnakNaNiebie" } },
        { "PN_Akcja_ObeliskM", new[] { "PN_Kons_Groza", "PN_Kons_ZnakNaNiebie" } },
        { "PN_Akcja_LadunekC", new[] { "PN_Kons_Plotki" } },
        { "PN_Akcja_LadunekS", new[] { "PN_Kons_Plotki" } },
        { "PN_Akcja_LadunekZ", new[] { "PN_Kons_Plotki" } },
        { "PN_Akcja_KrwawyDeszcz", new[] { "PN_Kons_Pogoda", "PN_Kons_Groza" } },
        { "PN_Akcja_Calun", new[] { "PN_Kons_Groza" } },
        { "PN_Akcja_Ciemnosc", new[] { "PN_Kons_Groza" } },
        { "PN_Akcja_Przybysz", new[] { "PN_Kons_NowyCzlowiek" } },
        { "PN_Akcja_PrzybyszM", new[] { "PN_Kons_NowyCzlowiek" } },
    };

    /// <summary>
    /// SPECYFIKACJA kontraktu kluczy faktow: kto PISZE klucz czytany przez luki. Decyzja autora nr 2
    /// z S5 ("Trofea tez pisze walka.byla") nie miala straznika - usuniecie wpisu dawalo 1129/0.
    /// </summary>
    public static readonly Dictionary<string, string[]> PisarzeKluczy = new Dictionary<string, string[]>
    {
        { "walka.byla", new[] { "PN_Kons_SladWalki", "PN_Kons_Trofea" } },
        { "niebo.znak", new[] { "PN_Kons_ZnakNaNiebie" } },
        { "wiesci.zrodlo", new[] { "PN_Kons_Plotki" } },
        // Krok 9, K1: czytaja je nowe luki (Kaprysy pogody, Ruch w dziczy, Szepty, Chude dni).
        { "pogoda.zla", new[] { "PN_Kons_Pogoda" } },
        { "zwierzeta.przybyly", new[] { "PN_Kons_Zwierzyna", "PN_Kons_Inwentarz" } },
        { "dobytek.straty", new[] { "PN_Kons_Straty" } },
        { "psychika.fala", new[] { "PN_Kons_Psychika" } },
        // Krok 9, K2 (decyzja autora K2-d): czyta go luk "Cos sie budzi".
        { "anomalia.byla", new[] { "PN_Kons_Groza" } },
    };

    /// <summary>
    /// TEST 0 - lista plikow klockow == Defs/Blocks/*.xml. Wolane NA STARCIE walidatora, tuz po
    /// wczytaniu: brak pliku wywraca pozniejsze testy (First() na klocku z brakujacego pliku), zanim
    /// ta asercja zdazylaby zadzialac - mutacja X8 byla wykrywana tylko awaria (przeglad S6).
    /// </summary>
    public static void ListaPlikowKlockow(string[] plikiKlockow)
    {
        T.Section("TEST 0 - lista plikow klockow walidatora == Defs/Blocks/*.xml (gra czyta wszystkie)");
        T.Ok("STRAZNIK: lista plikow klockow nie jest pusta", plikiKlockow != null && plikiKlockow.Length > 0, "");
        if (plikiKlockow == null || plikiKlockow.Length == 0)
        {
            return;
        }
        string katalogKlockow = Path.GetDirectoryName(plikiKlockow[0]);
        var naDysku = Directory.GetFiles(katalogKlockow, "*.xml").Select(Path.GetFullPath).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var naLiscie = plikiKlockow.Select(Path.GetFullPath).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        T.Ok("lista plikow klockow w Program.cs == pliki Defs/Blocks/*.xml (nowy plik musi trafic do testow)",
             naDysku.SequenceEqual(naLiscie, StringComparer.OrdinalIgnoreCase),
             "na dysku: " + string.Join(",", naDysku.Select(Path.GetFileName)) + " | na liscie: " + string.Join(",", naLiscie.Select(Path.GetFileName)));
    }

    /// <summary>
    /// TEST 1c - kontrakt tresci kroku 6 (przeglad S6): tabela konsekwencji, klucze faktow, pliki.
    /// Wszystko na PRAWDZIWYCH Defach - to sa bledy tresci, ktore gra przyjelaby bez slowa.
    /// </summary>
    /// <summary>
    /// Wagi cech stylu gracza klockow AKCJI (krok 7, decyzja autora nr 20), kolejnosc W/G/E/R.
    /// Nowa akcja bez wpisu to blad (jak w DozwoloneKonsekwencje) - "czego zdarzenie dotyczy" jest decyzja.
    /// </summary>
    public static readonly Dictionary<string, float[]> WagiStyluAkcji = new Dictionary<string, float[]>
    {
        { "PN_Akcja_Napad", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_Szal", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_Rojenie", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_Amok", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_Burza", new[] { 0f, 1f, 0f, 0.5f } },
        { "PN_Akcja_Emanator", new[] { 1f, 0f, 0f, 0f } },
        { "PN_Akcja_Okup", new[] { 0f, 0f, 1f, 0f } },
        { "PN_Akcja_Zrzut", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_Ambrozja", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_Meteoryt", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_Wedrowiec", new[] { 0f, 0f, 1f, 0f } },
        { "PN_Akcja_Uchodzcy", new[] { 0f, 0f, 1f, 0f } },
        { "PN_Akcja_Dzikus", new[] { 0f, 0f, 1f, 0f } },
        // Krok 9, K1 (tabela z Docs/K1_PROPOZYCJA.md, zatwierdzona K1-i).
        { "PN_Akcja_Mroz", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_Upal", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_Opad", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_ZimaWulkaniczna", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_SzalStada", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_Alfabobry", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_Hodowlane", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_Migracja", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_Oswojenie", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_Thrumbo", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_Zaraza", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_Zwarcie", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_Defoliator", new[] { 1f, 0f, 0f, 0f } },
        // Decyzja autora K1-h: bez wag (flaga styleNeutral w XML) - nastroj nie jest zadna z czterech cech.
        { "PN_Akcja_FalaPsychiczna", new[] { 0f, 0f, 0f, 0f } },
        { "PN_Akcja_KojacaFala", new[] { 0f, 0f, 0f, 0f } },
        // Krok 9, K2 (tabele z Docs/K2_PROPOZYCJA.md sekcja 5, zatwierdzone K2-h).
        { "PN_Akcja_KlasterMaszyn", new[] { 1f, 0f, 0f, 0f } },
        { "PN_Akcja_ZrodloKlopotow", new[] { 1f, 0f, 0f, 0f } },
        { "PN_Akcja_Abazja", new[] { 0f, 0f, 1f, 0f } },
        { "PN_Akcja_RojTrupow", new[] { 0.5f, 0f, 0f, 0.5f } },
        { "PN_Akcja_RojZwierzat", new[] { 0.5f, 0f, 0f, 0.5f } },
        { "PN_Akcja_GarstkaTrupow", new[] { 0.5f, 0f, 0f, 0.5f } },
        { "PN_Akcja_SzturmTrupow", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_Miesobestie", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_Kolcarze", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_Pozeracze", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_PozeraczeZWody", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_Chimery", new[] { 1f, 0f, 0f, 0f } },
        { "PN_Akcja_Ghul", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_PiesnNienawisci", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_RytualKultu", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_Oczyslepy", new[] { 1f, 0f, 0f, 0f } },
        { "PN_Akcja_Wrzaski", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_Zjawa", new[] { 1f, 0f, 0f, 0f } },
        { "PN_Akcja_Kregoslup", new[] { 0f, 0f, 0f, 1f } },
        { "PN_Akcja_BramaOtchlani", new[] { 1f, 0f, 0f, 0f } },
        { "PN_Akcja_SerceZMiesa", new[] { 1f, 0f, 0f, 0f } },
        { "PN_Akcja_Nocisfera", new[] { 1f, 0f, 0f, 0f } },
        { "PN_Akcja_ObeliskA", new[] { 1f, 0f, 0f, 0f } },
        { "PN_Akcja_ObeliskD", new[] { 1f, 0f, 0f, 0f } },
        { "PN_Akcja_ObeliskM", new[] { 1f, 0f, 0f, 0f } },
        { "PN_Akcja_LadunekC", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_LadunekS", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_LadunekZ", new[] { 0f, 1f, 0f, 0f } },
        { "PN_Akcja_KrwawyDeszcz", new[] { 0f, 0f, 0f, 1f } },
        { "PN_Akcja_Calun", new[] { 0f, 0f, 0f, 1f } },
        { "PN_Akcja_Ciemnosc", new[] { 1f, 0f, 0f, 0.5f } },
        { "PN_Akcja_Przybysz", new[] { 0f, 0f, 1f, 0f } },
        { "PN_Akcja_PrzybyszM", new[] { 0f, 0f, 1f, 0f } },
        // Wszczep (zdarzenie ukryte): bez wag - gracz nie wie o zdarzeniu, wiec nie ma na co zareagowac.
        { "PN_Akcja_Wszczep", new[] { 0f, 0f, 0f, 0f } },
    };

    /// <summary>Akcje swiadomie bez wag stylu (decyzje autora K1-h i K2-h) - jedyne, ktorym wolno miec sume wag 0.</summary>
    public static readonly string[] BezStylu = { "PN_Akcja_FalaPsychiczna", "PN_Akcja_KojacaFala", "PN_Akcja_Wszczep" };

    public static void KontraktTresci(List<Block> blocks, CompatibilityGraph graph, XmlConfig cfg, string[] plikiKlockow)
    {
        T.Section("TEST 1c - kontrakt tresci kroku 6: akcja -> konsekwencje, klucze faktow, lista plikow");

        var akcje = blocks.Where(b => b.Type == BlockType.Action).Select(b => b.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var konsekwencje = blocks.Where(b => b.Type == BlockType.Consequence).ToList();
        T.Ok("STRAZNIK: katalog ma akcje i konsekwencje", akcje.Count > 0 && konsekwencje.Count > 0,
             akcje.Count + " / " + konsekwencje.Count);
        T.Ok("kazda akcja katalogu ma wpis w DozwoloneKonsekwencje (nowa akcja = decyzja, nie domyslna zgoda)",
             akcje.All(a => DozwoloneKonsekwencje.ContainsKey(a)),
             string.Join(",", akcje.Where(a => !DozwoloneKonsekwencje.ContainsKey(a))));
        T.Ok("kazdy wpis DozwoloneKonsekwencje wskazuje istniejaca akcje",
             DozwoloneKonsekwencje.Keys.All(a => akcje.Contains(a)),
             string.Join(",", DozwoloneKonsekwencje.Keys.Where(a => !akcje.Contains(a))));
        foreach (string a in akcje.Where(x => DozwoloneKonsekwencje.ContainsKey(x)))
        {
            var zGrafu = konsekwencje.Where(k => graph.Allows(a, k.Id)).Select(k => k.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var zeSpec = DozwoloneKonsekwencje[a].OrderBy(x => x, StringComparer.Ordinal).ToList();
            T.Ok("konsekwencje zgodne z " + a + " == specyfikacja", zGrafu.SequenceEqual(zeSpec),
                 "graf: " + string.Join(",", zGrafu) + " | spec: " + string.Join(",", zeSpec));
        }
        T.Ok("BezKonsekwencji == akcje z pusta specyfikacja (dwie tabele mowia to samo)",
             BezKonsekwencji.OrderBy(x => x, StringComparer.Ordinal)
                 .SequenceEqual(DozwoloneKonsekwencje.Where(p => p.Value.Length == 0).Select(p => p.Key).OrderBy(x => x, StringComparer.Ordinal)), "");

        // --- kontrakt kluczy faktow: czytelnicy (warunki startu lukow) i pisarze (factsOnExecute)
        var czytane = new HashSet<string>(StringComparer.Ordinal);
        foreach (var luk in cfg.arcDefs)
        {
            foreach (var c in luk.startConditions ?? new List<NarrativeCondition>())
            {
                string k = c is Cond_Fakt ? ((Cond_Fakt)c).key : c is Cond_FaktOd ? ((Cond_FaktOd)c).key
                         : c is Cond_FaktLiczba ? ((Cond_FaktLiczba)c).key : null;
                if (k != null)
                {
                    czytane.Add(k);
                }
            }
        }
        T.Ok("STRAZNIK: luki czytaja fakty", czytane.Count > 0, "");
        T.Ok("klucze czytane przez luki == klucze specyfikacji pisarzy",
             czytane.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(PisarzeKluczy.Keys.OrderBy(x => x, StringComparer.Ordinal)),
             "czytane: " + string.Join(",", czytane.OrderBy(x => x, StringComparer.Ordinal)));
        foreach (var p in PisarzeKluczy)
        {
            var pisarze = blocks.Where(b => b.FactsOnExecute != null && b.FactsOnExecute.Any(w => w != null && w.key == p.Key))
                                .Select(b => b.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
            T.Ok("pisarze klucza " + p.Key + " == specyfikacja", pisarze.SequenceEqual(p.Value.OrderBy(x => x, StringComparer.Ordinal)),
                 "pisza: " + string.Join(",", pisarze));
        }

        // Pisarze jednego klucza musza sie zgadzac co do trybu i czasu zycia - inaczej ostatni wygrywa,
        // a o tym, ktory byl ostatni, decyduje losowanie w remisie konsekwencji (przeglad S6).
        var zapisy = blocks.Where(b => b.FactsOnExecute != null).SelectMany(b => b.FactsOnExecute.Where(w => w != null)).ToList();
        T.Ok("STRAZNIK: katalog ma deklaracje faktow", zapisy.Count > 0, "");
        foreach (var grupa in zapisy.GroupBy(w => w.key ?? string.Empty, StringComparer.Ordinal))
        {
            T.Ok("pisarze klucza " + grupa.Key + " zgodni co do trybu i czasu zycia",
                 grupa.Select(w => w.accumulate).Distinct().Count() == 1 && grupa.Select(w => w.lifespanDays).Distinct().Count() == 1,
                 string.Join(",", grupa.Select(w => (w.accumulate ? "Add" : "Set") + "/" + w.lifespanDays)));
        }
        T.Ok("kazdy czas zycia faktu w katalogu jest uzyteczny (0 albo >= 1 dnia)",
             zapisy.All(w => FactLedger.IsUsableLifespan(w.lifespanDays)),
             string.Join(",", zapisy.Where(w => !FactLedger.IsUsableLifespan(w.lifespanDays)).Select(w => w.key + "/" + w.lifespanDays)));

        // --- plik lukow == zawartosc Defs/Arcs (liste klockow sprawdza ListaPlikowKlockow na starcie).
        var lukiNaDysku = Directory.GetFiles(Path.GetDirectoryName(cfg.arcsPath), "*.xml").Select(Path.GetFullPath).ToList();
        T.Ok("plik lukow walidatora == jedyny plik Defs/Arcs/*.xml",
             lukiNaDysku.Count == 1 && string.Equals(lukiNaDysku[0], Path.GetFullPath(cfg.arcsPath), StringComparison.OrdinalIgnoreCase),
             string.Join(",", lukiNaDysku.Select(Path.GetFileName)));

        // --- krok 7: wagi stylu gracza tylko na akcjach, zgodne z tabela decyzji autora nr 20
        T.Ok("kazda akcja katalogu ma wpis w WagiStyluAkcji", akcje.All(a => WagiStyluAkcji.ContainsKey(a)),
             string.Join(",", akcje.Where(a => !WagiStyluAkcji.ContainsKey(a))));
        T.Ok("kazdy wpis WagiStyluAkcji wskazuje istniejaca akcje", WagiStyluAkcji.Keys.All(a => akcje.Contains(a)),
             string.Join(",", WagiStyluAkcji.Keys.Where(a => !akcje.Contains(a))));
        foreach (Block b in blocks.Where(x => x.Type == BlockType.Action && WagiStyluAkcji.ContainsKey(x.Id)))
        {
            float[] spec = WagiStyluAkcji[b.Id];
            bool zgodne = b.StyleWeights != null && b.StyleWeights.walka == spec[0] && b.StyleWeights.gospodarka == spec[1]
                          && b.StyleWeights.ekspansja == spec[2] && b.StyleWeights.reaktywnosc == spec[3];
            bool neutralna = BezStylu.Contains(b.Id);
            T.Ok("wagi stylu " + b.Id + " == specyfikacja", zgodne && (neutralna ? b.StyleWeights.Total() == 0f : b.StyleWeights.Total() > 0f),
                 b.StyleWeights == null ? "null" : b.StyleWeights.Describe());
        }
        // Flaga styleNeutral w XML == tabela BezStylu: akcja bez wag to decyzja zapisana w danych, nie przeoczenie.
        var neutralneXml = blocks.Where(x => x.StyleNeutral).Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        T.EqS("flaga styleNeutral == tabela BezStylu (decyzja K1-h)", string.Join(",", neutralneXml),
              string.Join(",", BezStylu.OrderBy(x => x, StringComparer.Ordinal)));
        T.Ok("styleNeutral tylko na akcjach", blocks.All(x => !x.StyleNeutral || x.Type == BlockType.Action), "");
        var zWagami = blocks.Where(x => x.Type != BlockType.Action && x.StyleWeights != null && x.StyleWeights.Total() != 0f).ToList();
        T.Ok("wagi stylu deklaruje WYLACZNIE klocek akcji", zWagami.Count == 0, string.Join(",", zWagami.Select(x => x.Id)));
        var zeStylem = blocks.Where(x => (x.Conditions ?? new List<NarrativeCondition>()).Concat(x.Preferences ?? new List<NarrativeCondition>())
                                          .Any(c => c is Cond_StylMocnaStrona)).ToList();
        T.Ok("warunek stylu gracza nie wystepuje w warunkach ani preferencjach klockow (tylko w lukach)", zeStylem.Count == 0,
             string.Join(",", zeStylem.Select(x => x.Id)));
    }

    /// <summary>
    /// Akcje, dla ktorych katalog nie ma ANI JEDNEJ zgodnej konsekwencji - ich szosty segment
    /// sygnatury zostaje pusty (pusty slot opcjonalny wylacznie jako WYMUSZONY).
    /// Czesc specyfikacji katalogu, trzymana obok tabeli rozbicia.
    /// </summary>
    // Krok 9, K2-b: zdarzenia ukryte nie maja konsekwencji (nie pisza faktow).
    public static readonly string[] BezKonsekwencji = { "PN_Akcja_Okup", "PN_Akcja_Ambrozja", "PN_Akcja_Oczyslepy", "PN_Akcja_Zjawa",
                                                        "PN_Akcja_Wszczep" };

    /// <summary>Calkowita przestrzen kombinacji - SUMA specyfikacji, nie osobny literal.</summary>
    public static int Lacznie
    {
        get { return Oczekiwane.Values.Sum(); }
    }

    /// <summary>Liczba klockow akcji - LICZNOSC specyfikacji, nie osobny literal.</summary>
    public static int Akcji
    {
        get { return Oczekiwane.Count; }
    }

    /// <summary>
    /// Liczba wszystkich klockow katalogu. Jedyna liczba, ktorej NIE da sie wyprowadzic
    /// z tabeli rozbicia (tamta zna wylacznie sloty akcji), wiec zostaje literalem -
    /// ale jednym, nazwanym i w tym samym miejscu co reszta specyfikacji.
    /// </summary>
    // Krok 9, K1: 30 + 3 aktorow (Maszyny, Hodowlane, Los) + 15 akcji toru Core + 5 konsekwencji = 53.
    // Krok 9, K2: + 3 akcje Royalty + 31 akcji Anomaly + 2 aktorow (Otchlan, Kult) + 1 konsekwencja (Groza) = 90
    // (katalog wczytany ze WSZYSTKIMI DLC; bez DLC walidator wraca do 53 - TEST 20).
    public const int OczekiwanychKlockow = 90;

    /// <summary>
    /// Liczba wariantow najwiekszej akcji. Uzywana przez test granicy enumeracja/probkowanie,
    /// zeby "max = N" i "max = N-1" same szly za katalogiem.
    /// </summary>
    private static int NajwiekszaAkcja
    {
        get { return Oczekiwane.Values.Max(); }
    }

    private static string NazwaNajwiekszej
    {
        get { return Oczekiwane.OrderByDescending(p => p.Value).ThenBy(p => p.Key).First().Key; }
    }

    public static void Run(List<Block> blocks, CompatibilityGraph graph, EventComposer composer, int budzetZXml)
    {
        // ---------------------------------------------------------------- TEST 8
        T.Section("TEST 8 - REGRESJA KOMPOZYCJI (TryCompose nadal " + Lacznie + " kombinacji)");
        var przezTryCompose = new HashSet<string>();
        var tagi = new List<string> { null, "militarny", "spoleczny", "zasoby", "pogoda", "psychika", "szantaz", "dzicz" };
        foreach (var tag in tagi)
        {
            // Krok 9, K2: 3000 -> 12000 prob. Przy 408 kombinacjach 3000 ziaren gubilo 5 najrzadszych (wariant akcji
            // z 16 wariantami ma szanse ok. 1/62 * 1/16 na probe bez tagu, czyli ok. 3 trafienia na 3000 - e^-3 = 5% braku
            // na kombinacje). 12000 daje ok. 12 trafien i szanse braku e^-12 na kombinacje.
            for (int s = 0; s < 12000; s++)
            {
                ComposedEvent e = composer.TryCompose(new EventRecipe { RequiredActionTag = tag }, new SeededRandom(s), null);
                if (e != null) przezTryCompose.Add(e.Signature);
            }
        }
        T.EqI("TryCompose daje " + Lacznie + " unikalnych sygnatur", przezTryCompose.Count, Lacznie);
        T.EqI("katalog: " + OczekiwanychKlockow + " klockow", blocks.Count, OczekiwanychKlockow);
        // 117 = stan po S5; +1 = zakaz Wrak x Uchodzcy (S6); +2 = zakazy Dzikie x Rojenie i Plotki x Dzikus
        // (przeglad S10 kroku 8). Literal zostaje (wyprowadzenie z tych samych danych, ktore laduje graf, byloby
        // tautologia przy loaderze gubiacym krawedzie), ale jest rozpisany.
        // KROK 9, K1: +368. Kazda nowa akcja z listy onlyWith zakazuje wszystkich aktorow i konsekwencji spoza listy,
        // plus wyzwalacz Bogactwo w incompatibleWith. Po K1 katalog ma 8 aktorow i 11 konsekwencji (19 klockow
        // objetych listami), wiec akcja z a aktorami i k konsekwencjami na liscie dostaje 19 - a - k zakazow z listy
        // i 1 zakaz wyzwalacza: 20 - a - k. Suma po 15 akcjach (a + k z tabeli DozwoloneKonsekwencje i list aktorow):
        //   Mroz 18, Upal 18, Opad 17, Zima 18, SzalStada 17, Alfabobry 17, Hodowlane 18, Migracja 18, Oswojenie 18,
        //   Thrumbo 18, Zaraza 17, Zwarcie 18, FalaPsychiczna 18, KojacaFala 18, Defoliator 16 = 264.
        // Do tego nowe klocki zmieniaja listy STARYCH akcji: 3 nowych aktorow i 5 nowych konsekwencji jest poza
        // listami 13 starych akcji (13 x 8 = 104). 264 + 104 = 368.
        // KROK 9, K2: +702 +84. Po K2 katalog (wszystkie DLC) ma 10 aktorow i 12 konsekwencji (22 klocki objete listami),
        // wiec nowa akcja z a aktorami i k konsekwencjami dostaje 22 - a - k zakazow z listy i 1 zakaz wyzwalacza:
        // 23 - a - k. Suma po 34 akcjach: 34 x 23 - suma a - suma k = 782 - 38 - 42 = 702, gdzie suma a = 38 (31 akcji
        // z jednym aktorem, Wrzaski i trzy obeliski z dwoma) i suma k = 42 (z tabeli DozwoloneKonsekwencje).
        // 28 akcji sprzed K2 dostaje po 3 zakazy nowych klockow (Otchlan, Kult, Groza): 84. 488 + 702 + 84 = 1274.
        T.EqI("graf: 117 + 1 + 2 + 368 + 702 + 84 zabronionych krawedzi", graph.ForbiddenEdgeCount, 117 + 1 + 2 + 368 + 702 + 84);
        T.EqI("klockow akcji: " + Akcji, blocks.Count(b => b.Type == BlockType.Action), Akcji);
        // Liczba segmentow sygnatury bierze sie z LICZBY TYPOW SLOTOW, a nie z przepisanej liczby:
        // krok 6 dolozyl slot Consequence, wiec segmentow jest szesc. Pusty jest DOKLADNIE ten
        // segment, dla ktorego katalog nie ma ani jednego klocka - pusty slot opcjonalny powstaje
        // wylacznie jako WYMUSZONY (AllowDeliberateEmptyOptional=false).
        var typySlotow = Enum.GetValues(typeof(BlockType)).Cast<BlockType>().ToList();
        int segmentow = typySlotow.Count;
        bool ksztaltOk = przezTryCompose.All(s => s.Split('|').Length == segmentow);
        T.Ok("sygnatura ma tyle segmentow, ile jest typow slotow", ksztaltOk, "segmentow " + segmentow);

        // Sloty WYMAGANE nie moga byc puste NIGDY - pusty slot wymagany zabija cala galaz,
        // wiec jego obecnosc w sygnaturze znaczylaby, ze enumeracja przepuscila kandydata
        // niemozliwego do zlozenia. Indeksy segmentow ida za SignatureOrder.
        bool wymaganePelne = przezTryCompose.All(s =>
        {
            string[] p = s.Split('|');
            return p[1] != "-" && p[2] != "-" && p[3] != "-";
        });
        T.Ok("sloty wymagane (aktor, akcja, cel) nigdy nie sa puste", wymaganePelne, null);

        // Slot konsekwencji jest pusty DOKLADNIE dla akcji, ktore nie maja ani jednej zgodnej
        // konsekwencji - bo pusty slot opcjonalny powstaje wylacznie jako WYMUSZONY. To jest
        // wyprowadzenie ze specyfikacji katalogu, a nie przepisana liczba.
        // Straznik dlugosci: sygnatura krotsza niz 6 segmentow ma zapalic asercje ksztaltu (nizej),
        // a nie wywrocic przebiegu wyjatkiem (przeglad S6: mutacja B20).
        var pusteFaktyczne = przezTryCompose
            .Where(s => s.Split('|').Length > 5 && s.Split('|')[5] == "-")
            .Select(s => s.Split('|')[2])
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        var pusteOczekiwane = BezKonsekwencji.OrderBy(x => x, StringComparer.Ordinal).ToList();
        T.Ok("STRAZNIK: specyfikacja wymienia akcje bez konsekwencji (inaczej test jest pusty)",
             pusteOczekiwane.Count > 0, string.Join(",", pusteOczekiwane));
        T.Ok("slot konsekwencji pusty DOKLADNIE dla akcji bez zgodnej konsekwencji",
             pusteFaktyczne.SequenceEqual(pusteOczekiwane),
             "faktyczne: " + string.Join(",", pusteFaktyczne) + " | oczekiwane: " + string.Join(",", pusteOczekiwane));
        T.Ok("kazda pozostala akcja ma slot konsekwencji WYPELNIONY",
             przezTryCompose.All(s => s.Split('|')[5] != "-" || BezKonsekwencji.Contains(s.Split('|')[2])), null);

        // KONTRAKT KLOCKA KONSEKWENCJI (krok 6).
        var konsekwencje = blocks.Where(b => b.Type == BlockType.Consequence).ToList();
        T.Ok("STRAZNIK: katalog ma klocki konsekwencji", konsekwencje.Count > 0, "konsekwencji " + konsekwencje.Count);
        // Intensywnosc sumuje sie ze WSZYSTKICH slotow i tlumaczy na punkty zagrozenia, wiec slad
        // po zdarzeniu o niezerowym wkladzie po cichu przesuwalby trudnosc i sito punktow.
        T.Ok("kazdy klocek konsekwencji ma ZEROWY wklad intensywnosci (Normal)",
             konsekwencje.All(b => b.Intensity == IntensityLevel.Normal),
             string.Join(",", konsekwencje.Where(b => b.Intensity != IntensityLevel.Normal)
                                          .Select(b => b.Id + "=" + b.Intensity)));
        T.Ok("kazdy klocek konsekwencji deklaruje co najmniej jeden fakt (inaczej slot jest pusta ozdoba)",
             konsekwencje.All(b => b.FactsOnExecute != null && b.FactsOnExecute.Count > 0),
             string.Join(",", konsekwencje.Where(b => b.FactsOnExecute == null || b.FactsOnExecute.Count == 0)
                                          .Select(b => b.Id)));
        var zleKlucze = konsekwencje.SelectMany(b => b.FactsOnExecute.Select(w => new { b.Id, w.key }))
                                    .Where(x => !FactLedger.IsValidKey(x.key))
                                    .Select(x => x.Id + ":" + (x.key ?? "null"))
                                    .ToList();
        T.Ok("klucze faktow w katalogu przechodza alfabet FactLedger.IsValidKey", zleKlucze.Count == 0,
             string.Join(",", zleKlucze));
        // Osie deklaruje WYLACZNIE klocek akcji (umowa danych kroku 2) - konsekwencja ich nie wnosi.
        T.Ok("klocki konsekwencji nie wnosza wlasnego tematu",
             konsekwencje.All(b => b.Theme == Theme.Natural), "domyslny temat to Natural");

        // ---------------------------------------------------------------- TEST 2
        T.Section("TEST 2 - ENUMERACJA WARIANTOW: rozbicie, brak duplikatow, legalnosc grafu");
        List<Block> akcje = composer.AvailableActions(null, new EventRecipe());
        T.EqI("AvailableActions(snapshot=null) zwraca " + Akcji + " akcji", akcje.Count, Akcji);

        int suma = 0;
        var wszystkieSygnatury = new List<string>();
        bool wszystkieWyczerpane = true, zeroLosowan = true;
        foreach (Block a in akcje)
        {
            VariantEnumerationStats st;
            List<ComposedEvent> v = composer.EnumerateVariants(a, null, 1000, null, out st);
            int oczek;
            if (!Oczekiwane.TryGetValue(a.Id, out oczek)) oczek = -1;
            T.EqI("  N(" + a.Id + ")", v.Count, oczek);
            wszystkieWyczerpane = wszystkieWyczerpane && st.Exhausted && st.VariantsSeen == st.Returned;
            zeroLosowan = zeroLosowan && st.RandomDraws == 0;
            suma += v.Count;
            wszystkieSygnatury.AddRange(v.Select(e => e.Signature));
        }
        T.EqI("suma wariantow po akcjach == " + Lacznie, suma, Lacznie);
        T.Ok("kazdy przelot Exhausted == true i VariantsSeen == Returned", wszystkieWyczerpane, null);
        T.Ok("kazdy przelot RandomDraws == 0", zeroLosowan, null);
        T.EqI("brak duplikatow sygnatur w calej przestrzeni", wszystkieSygnatury.Distinct().Count(), Lacznie);

        // Granica enumeracja/probkowanie (najczestsze miejsce na blad o jeden).
        int N = NajwiekszaAkcja;
        Block najwieksza = akcje.First(a => a.Id == NazwaNajwiekszej);
        VariantEnumerationStats sN, sN1;
        List<ComposedEvent> vN = composer.EnumerateVariants(najwieksza, null, N, new SeededRandom(7), out sN);
        List<ComposedEvent> vN1 = composer.EnumerateVariants(najwieksza, null, N - 1, new SeededRandom(7), out sN1);
        T.Ok(NazwaNajwiekszej + " max=" + N + " -> " + N + "/" + N + ", Exhausted, 0 losowan",
             vN.Count == N && sN.VariantsSeen == N && sN.Exhausted && sN.RandomDraws == 0,
             "ret=" + vN.Count + " seen=" + sN.VariantsSeen + " exh=" + sN.Exhausted + " draws=" + sN.RandomDraws);
        T.Ok(NazwaNajwiekszej + " max=" + (N - 1) + " -> " + (N - 1) + "/" + N + ", NIE Exhausted, 1 losowanie",
             vN1.Count == N - 1 && sN1.VariantsSeen == N && !sN1.Exhausted && sN1.RandomDraws == 1,
             "ret=" + vN1.Count + " seen=" + sN1.VariantsSeen + " exh=" + sN1.Exhausted + " draws=" + sN1.RandomDraws);

        // Legalnosc grafu i ksztalt kandydata na calym zbiorze.
        var gen = new CandidateGenerator(composer);
        CandidateSet pelny = gen.Generate(new EventRecipe(), null, new SeededRandom(1), budzetZXml);
        int zlamania = 0, zleSloty = 0;
        foreach (ComposedEvent e in pelny.Candidates)
        {
            for (int i = 0; i < e.Blocks.Count; i++)
            {
                for (int j = i + 1; j < e.Blocks.Count; j++)
                {
                    if (!graph.Allows(e.Blocks[i].Id, e.Blocks[j].Id)) zlamania++;
                }
            }
            if (e.Blocks.Count(b => b.Type == BlockType.Action) != 1) zleSloty++;
            if (e.Blocks.Count(b => b.Type == BlockType.Actor) != 1) zleSloty++;
            if (e.Blocks.Count(b => b.Type == BlockType.Target) != 1) zleSloty++;
        }
        T.EqI("par klockow lamiacych graf", zlamania, 0);
        T.EqI("kandydatow o zlej liczbie slotow Action/Actor/Target", zleSloty, 0);
        T.EqI("unikalnych sygnatur w CandidateSet", pelny.Candidates.Select(c => c.Signature).Distinct().Count(), Lacznie);

        // ---------------------------------------------------------------- TEST 1
        // Krok 9 (K0): budzety testu wyprowadzane z KATALOGU, a nie wpisane (dawniej 400 i 40 przy 13 akcjach) -
        // pelne pokrycie toru zmieni liczbe akcji z 13 na 62, a literaly przestalyby znaczyc to, co znaczyly.
        //   bPelny = Akcji * maksimum wariantow - najmniejszy budzet generowania BEZ LOSOWAN (K = maksimum; pelny
        //            zestaw zachodzi juz od budzetu = liczba wariantow - rozroznienie nizej, wlasnosci (1) i (2));
        //   bMaly  = 3 * Akcji + Akcji / 2      - probkowanie: K = 3 i reszta dla polowy akcji.
        int maxWariantow = gen.Generate(new EventRecipe(), null, new SeededRandom(7), 1000000).PerAction.Max(p => p.VariantsSeen);
        int bPelny = Akcji * maxWariantow;
        int bMaly = 3 * Akcji + Akcji / 2;
        T.Section("TEST 1 - BUDZET OCEN: pelny (" + bPelny + ") -> pelna enumeracja, maly (" + bMaly + ") -> probkowanie");
        var licznik400 = new CountingRandom(1);
        CandidateSet b400 = gen.Generate(new EventRecipe(), null, licznik400, bPelny);
        T.EqI("budzet pelny: liczba kandydatow", b400.Candidates.Count, Lacznie);
        T.Ok("budzet pelny: wyczerpano == true", b400.Exhausted, "Exhausted=" + b400.Exhausted);
        T.EqI("budzet pelny: ActionCount", b400.ActionCount, Akcji);
        T.EqI("budzet pelny: PerActionQuota K = maksimum wariantow", b400.PerActionQuota, maxWariantow);
        T.EqI("budzet pelny: TotalVariants", b400.TotalVariants, Lacznie);
        T.Ok("budzet pelny: BudgetExceeded == false", !b400.BudgetExceeded, null);
        T.Ok("budzet pelny: Truncated == false", !b400.Truncated, null);
        T.EqI("budzet pelny: ZERO wywolan rng.Next", licznik400.NextCalls, 0);
        T.EqI("budzet pelny: ZERO wywolan rng.Pick", licznik400.PickCalls, 0);
        T.EqI("budzet pelny: PerAction.Count == ActionCount", b400.PerAction.Count, b400.ActionCount);
        T.EqI("budzet pelny: suma Returned == Candidates.Count", b400.PerAction.Sum(p => p.Returned), b400.Candidates.Count);
        T.EqI("budzet pelny: suma VariantsSeen == TotalVariants", b400.PerAction.Sum(p => p.VariantsSeen), b400.TotalVariants);
        // DWIE ROZNE WLASNOSCI (sprostowanie przy K0, 2026-09-25 - pierwsza wersja tej asercji je mylila):
        //  (1) ZERO LOSOWAN przy generowaniu zachodzi dokladnie od K = maksimum wariantow: przy K o jeden mniejszym
        //      przebieg 1 losuje probke najwiekszej akcji (reservoir), nawet jesli przebieg 2 potem ja uzupelni;
        //  (2) PELNY ZESTAW kandydatow zachodzi juz od budzetu = liczba wszystkich wariantow, bo przebieg 2
        //      rozdziela niewykorzystana reszte (BudgetSplitter) - ponizej tego progu zestaw jest niepelny.
        var licznikMniej = new CountingRandom(1);
        CandidateSet bJedenMniej = gen.Generate(new EventRecipe(), null, licznikMniej, bPelny - Akcji);
        T.Ok("(1) przy K = maksimum - 1 generowanie juz losuje (zero losowan wymaga K >= maksimum)",
             licznikMniej.NextCalls + licznikMniej.PickCalls > 0, "Next=" + licznikMniej.NextCalls + " Pick=" + licznikMniej.PickCalls);
        CandidateSet bRowno = gen.Generate(new EventRecipe(), null, new SeededRandom(1), Lacznie);
        CandidateSet bBrakJednego = gen.Generate(new EventRecipe(), null, new SeededRandom(1), Lacznie - 1);
        T.Ok("(2) budzet = liczba wariantow: pelny zestaw (reszta rozdzielona w przebiegu 2)",
             bRowno.Exhausted && bRowno.Candidates.Count == Lacznie, "n=" + bRowno.Candidates.Count);
        T.Ok("(2) budzet o jeden mniejszy: zestaw niepelny", !bBrakJednego.Exhausted && bBrakJednego.Candidates.Count == Lacznie - 1,
             "n=" + bBrakJednego.Candidates.Count);

        // WLASNOSC "RANKING NIE ZUZYWA ANI JEDNEGO LOSOWANIA" ZALEZY OD KONFIGURACJI, nie od literalu
        // 400 powyzej. Trzyma sie dokladnie dopoki K = budzet / liczba_akcji jest >= maksimum
        // wariantow na akcje. Krok 6 dolozyl slot Consequence, wiec przestrzen rosnie razem z trescia
        // i ta granica da sie przekroczyc DOSYPKA XML, bez jednej linii kodu - dlatego jest tu
        // asercja liczaca maksimum z PRAWDZIWEGO katalogu, a nie komentarz.
        CandidateSet bezLimitu = gen.Generate(new EventRecipe(), null, new SeededRandom(7), 1000000);
        int maxNaAkcje = bezLimitu.PerAction.Max(p => p.VariantsSeen);
        int kZXml = budzetZXml / Akcji;
        T.Ok("STRAZNIK: katalog ma akcje z wariantami (inaczej asercja budzetu jest pusta)",
             maxNaAkcje > 0 && bezLimitu.PerAction.Count > 0, "max na akcje " + maxNaAkcje);
        T.Ok("budzet z XML: K >= maksimum wariantow na akcje (warunek generowania bez losowan)",
             kZXml >= maxNaAkcje,
             "budzet " + budzetZXml + " / akcji " + Akcji + " = K " + kZXml + ", max na akcje " + maxNaAkcje);
        var licznikKonf = new CountingRandom(3);
        CandidateSet bKonf = gen.Generate(new EventRecipe(), null, licznikKonf, budzetZXml);
        T.EqI("budzet z XML: ZERO wywolan rng.Next", licznikKonf.NextCalls, 0);
        T.EqI("budzet z XML: ZERO wywolan rng.Pick", licznikKonf.PickCalls, 0);
        T.Ok("budzet z XML: wyczerpano == true", bKonf.Exhausted, "Exhausted=" + bKonf.Exhausted);

        // POMIAR KOSZTU GENEROWANIA (krok 6: przestrzen urosla z 80 do ponad 130 wariantow).
        // To jest pomiar OFFLINE na net8.0, nie w grze (Mono Unity bywa kilka razy wolniejsze) -
        // dlatego nie ma tu asercji na konkretna liczbe, tylko granica sanitarna o dwa rzedy
        // wielkosci luzniejsza od zmierzonej, lapiaca wylacznie patologiczna regresje (np. enumeracja
        // wykladnicza). Liczbe do dokumentacji bierze sie z wypisanego wiersza, z data.
        var zegar = System.Diagnostics.Stopwatch.StartNew();
        const int powtorzen = 200;
        int sumaKandydatow = 0;
        for (int i = 0; i < powtorzen; i++)
        {
            sumaKandydatow += gen.Generate(new EventRecipe(), null, new SeededRandom(i), budzetZXml).Candidates.Count;
        }
        zegar.Stop();
        double msNaTure = zegar.Elapsed.TotalMilliseconds / powtorzen;
        Console.WriteLine("  [INFO] generowanie + ocena kontekstowa: "
                          + msNaTure.ToString("0.000", CultureInfo.InvariantCulture) + " ms na ture ("
                          + (sumaKandydatow / powtorzen).ToString(CultureInfo.InvariantCulture)
                          + " kandydatow, net8.0 offline, " + powtorzen + " powtorzen)");
        T.Ok("granica sanitarna kosztu generowania: < 50 ms na ture (offline)", msNaTure < 50.0,
             msNaTure.ToString("0.000", CultureInfo.InvariantCulture) + " ms");

        CandidateSet b40 = gen.Generate(new EventRecipe(), null, new SeededRandom(1), bMaly);
        T.Ok("budzet maly: kandydatow <= budzet", b40.Candidates.Count <= bMaly, "kandydatow=" + b40.Candidates.Count);
        T.EqI("budzet maly: kandydatow == budzet (SPEC: K=" + (bMaly / Akcji) + " x " + Akcji
              + " + " + (bMaly % Akcji) + " z reszty)", b40.Candidates.Count, bMaly);
        T.Ok("budzet maly: wyczerpano == false", !b40.Exhausted, "Exhausted=" + b40.Exhausted);
        T.EqI("budzet maly: PerActionQuota K = floor(budzet/" + Akcji + ") = 3", b40.PerActionQuota, 3);
        T.EqI("budzet maly: TotalVariants nadal " + Lacznie, b40.TotalVariants, Lacznie);
        T.EqI("budzet maly: brak duplikatow (przebieg 2 PODMIENIA, nie doklej)",
              b40.Candidates.Select(c => c.Signature).Distinct().Count(), b40.Candidates.Count);
        // Reszta z dzielenia idzie do PIERWSZYCH ordynalnie akcji. Zbior uprzywilejowanych
        // WYPROWADZAMY z tabeli specyfikacji, zamiast wpisywac nazwy z reki - inaczej kazda
        // dosypka katalogu wymagalaby recznej korekty tej listy, a pomylka w niej wygladalaby
        // jak blad water-fillingu.
        int kwota40 = bMaly / Akcji;
        int nadwyzka40 = bMaly % Akcji;
        var uprzywilejowane = new HashSet<string>(
            Oczekiwane.Keys.OrderBy(x => x, StringComparer.Ordinal).Take(nadwyzka40));
        bool rozdzialOk = b40.PerAction.All(
            p => p.Returned == (uprzywilejowane.Contains(p.ActionId) ? kwota40 + 1 : kwota40));
        T.Ok("budzet maly: " + nadwyzka40 + " pierwszych akcji ordynalnie dostaje po " + (kwota40 + 1)
             + ", reszta po " + kwota40, rozdzialOk,
             string.Join(", ", b40.PerAction.Select(p => p.ActionId.Replace("PN_Akcja_", "") + "=" + p.Returned)));

        // Rozproszenie proby - lapie "pierwsze K" i blad o jeden w reservoir.
        var licznikiWariantow = new Dictionary<string, int>();
        for (int seed = 0; seed < 200; seed++)
        {
            CandidateSet cs = gen.Generate(new EventRecipe(), null, new SeededRandom(seed), bMaly);
            foreach (ComposedEvent e in cs.Candidates)
            {
                if (e.ActionBlockId != NazwaNajwiekszej) continue;
                int n;
                licznikiWariantow.TryGetValue(e.Signature, out n);
                licznikiWariantow[e.Signature] = n + 1;
            }
        }
        int roznych = licznikiWariantow.Count;
        int minL = roznych == 0 ? 0 : licznikiWariantow.Values.Min();
        int maxL = roznych == 0 ? 0 : licznikiWariantow.Values.Max();
        T.EqI("rozproszenie: kazdy z " + NajwiekszaAkcja + " wariantow " + NazwaNajwiekszej
              + " wystapil (200 ziaren, K=" + kwota40 + ")", roznych, NajwiekszaAkcja);
        // Wartosc oczekiwana = 200 ziaren * K / liczba wariantow (przy 16 wariantach i K = 3: 37,5); pasmo jak dawniej
        // 10..90 przy 37,5, czyli wzglednie 0,27..2,4 wartosci oczekiwanej.
        double oczekiwana = 200.0 * kwota40 / NajwiekszaAkcja;
        T.Ok("rozproszenie: licznosci w pasmie 0,27..2,4 wartosci oczekiwanej (" + oczekiwana.ToString("0.0", CultureInfo.InvariantCulture) + ")",
             minL >= 0.27 * oczekiwana && maxL <= 2.4 * oczekiwana, "min=" + minL + " max=" + maxL);

        // Skrajne przypadki budzetu.
        CandidateSet b5 = gen.Generate(new EventRecipe(), null, new SeededRandom(1), 5);
        T.Ok("budzet 5 (m>B): K wymuszone na 1, " + Akcji + " kandydatow, BudgetExceeded",
             b5.PerActionQuota == 1 && b5.Candidates.Count == Akcji && b5.BudgetExceeded && !b5.Exhausted,
             "K=" + b5.PerActionQuota + " n=" + b5.Candidates.Count + " przekr=" + b5.BudgetExceeded);
        CandidateSet bBrak = gen.Generate(new EventRecipe { RequiredActionTag = "tag-ktorego-nie-ma" }, null, new SeededRandom(1), budzetZXml);
        T.Ok("tag nieistniejacy: 0 kandydatow, ActionCount 0, Exhausted true",
             bBrak.Candidates.Count == 0 && bBrak.ActionCount == 0 && bBrak.Exhausted, bBrak.Trace);

        T.Ok("budzet z XML == 1600 (krok 9, K0: K >= maksimum wariantow przy 62 akcjach pelnego pokrycia)",
             budzetZXml == 1600, "XML candidateBudget=" + budzetZXml);
        T.EqI("stala awaryjna CandidateGenerator.DefaultBudget == budzet z XML", CandidateGenerator.DefaultBudget, budzetZXml);

        // Niezaleznosc od kolejnosci katalogu (sortowanie ordynalne w konstruktorze).
        var rndPerm = new Random(4242);
        string wzorzec = string.Join(";", pelny.Candidates.Select(c => c.Signature));
        bool permOk = true;
        for (int p = 0; p < 20; p++)
        {
            List<Block> shuffled = blocks.OrderBy(x => rndPerm.Next()).ToList();
            var comp2 = new EventComposer(shuffled, graph);
            var gen2 = new CandidateGenerator(comp2);
            CandidateSet cs = gen2.Generate(new EventRecipe(), null, new SeededRandom(1), budzetZXml);
            if (string.Join(";", cs.Candidates.Select(c => c.Signature)) != wzorzec) permOk = false;
        }
        T.Ok("20 permutacji katalogu daje identyczna liste sygnatur", permOk, null);

        // ============================================================================
        //  NASYCENIE SKALI INTENSYWNOSCI (klamrowanie)
        // ============================================================================
        //
        // Piec slotow wnosi wklad na skale o pieciu poziomach, wiec suma siega -3..+3, a skala
        // konczy sie na -2 i +2. Klamrowanie jest POPRAWNE - ponizej VeryLow nie ma gdzie zejsc -
        // ale bylo NIEWIDOCZNE: ComposedEvent niosl tylko wynik po klamrze. Teraz niesie tez
        // surowa sume, i te asercje pilnuja, ze niesie ja PRAWDZIWA.
        //
        // CELOWO NIE MA TU LITERALU "ile kandydatow jest przycietych". Ta liczba zmienia sie
        // przy kazdej dosypce katalogu, a asercja na nia byla by kopia katalogu, nie wyprowadzeniem.
        // Pilnujemy WLASNOSCI (suma zgadza sie z wkladami, wynik zgadza sie z klamrowana suma),
        // a liczbe DRUKUJEMY.
        var wszystkieKand = gen.Generate(new EventRecipe(), null, new SeededRandom(3), 4000).Candidates;
        int zlaSuma = 0, zlaKlamra = 0, przycietych = 0;
        int minSuma = int.MaxValue, maxSuma = int.MinValue;
        foreach (ComposedEvent e in wszystkieKand)
        {
            int oczekiwanaSuma = e.Blocks.Sum(b => (int)b.Intensity);
            if (e.IntensityRawSum != oczekiwanaSuma) zlaSuma++;

            int poKlamrze = Math.Max((int)IntensityLevel.VeryLow,
                                Math.Min((int)IntensityLevel.VeryHigh, e.IntensityRawSum));
            if ((int)e.Intensity != poKlamrze) zlaKlamra++;

            if (e.IntensityClamped) przycietych++;
            if (e.IntensityRawSum < minSuma) minSuma = e.IntensityRawSum;
            if (e.IntensityRawSum > maxSuma) maxSuma = e.IntensityRawSum;
        }
        T.EqI("IntensityRawSum == suma wkladow WSZYSTKICH slotow kandydata", zlaSuma, 0);
        T.EqI("Intensity == klamrowana IntensityRawSum", zlaKlamra, 0);

        // STRAZNIK PUSTEGO TESTU: gdyby katalog przestal przekraczac zakres, obie asercje wyzej
        // przechodzilyby trywialnie, a pole IntensityClamped stalo by sie martwym kodem bez
        // jednego przebiegu, ktory je zapala. Wtedy ten straznik ma zgasnac JAWNIE.
        T.Ok("STRAZNIK: katalog faktycznie przekracza zakres skali (inaczej test jest pusty)",
             przycietych > 0,
             "przycietych " + przycietych + " z " + wszystkieKand.Count
             + " (" + (100.0 * przycietych / wszystkieKand.Count).ToString("0.0", CultureInfo.InvariantCulture)
             + "%), surowe sumy w zakresie " + minSuma + ".." + maxSuma);
        T.Ok("IntensityClamped zgadza sie z zakresem surowej sumy",
             wszystkieKand.All(e => e.IntensityClamped == (e.IntensityRawSum < (int)IntensityLevel.VeryLow
                                                        || e.IntensityRawSum > (int)IntensityLevel.VeryHigh)),
             null);

        // ============================================================================
        //  UMOWA DANYCH: osie NALEZA DO KLOCKA AKCJI
        // ============================================================================
        //
        // EventComposer kopiuje Theme/Valence/Scale wylacznie z akcji. Deklaracja tych pol na
        // innym slocie jest martwa, ale wyglada na dzialajaca - autor tresci ustawi valence
        // na aktorze i nie dostanie niczego ani w zachowaniu, ani w logu. Katalog zostal z nich
        // oczyszczony; ta asercja pilnuje, zeby nie wrocily po cichu przy dosypce.
        var zOsiami = blocks
            .Where(b => b.Type != BlockType.Action)
            .Where(b => b.Theme != Theme.Natural || b.Valence != Valence.Neutral || b.Scale != EventScale.Moderate)
            .Select(b => b.Id + "(" + b.Theme + "/" + b.Valence + "/" + b.Scale + ")")
            .ToList();
        T.EqI("umowa danych: zaden klocek POZA akcja nie deklaruje osi klasyfikacji",
              zOsiami.Count, 0);
        if (zOsiami.Count > 0)
        {
            Console.WriteLine("      naruszenia: " + string.Join(", ", zOsiami));
        }

        // DRUGI KIERUNEK: kazdy kandydat dziedziczy osie po swoim klocku akcji.
        T.EqI("umowa danych: KAZDY kandydat dziedziczy osie po swoim klocku akcji",
              wszystkieKand.Count(e =>
              {
                  Block akcjaK = e.Blocks.First(b => b.Type == BlockType.Action);
                  return e.Theme != akcjaK.Theme || e.Valence != akcjaK.Valence || e.Scale != akcjaK.Scale;
              }), 0);

        // ⚠ SPROSTOWANIE WLASNEGO KOMENTARZA (przeglad adwersarialny).
        // Napisalem tu wczesniej, ze asercja "dziedziczy osie" chroni przed katalogiem, w ktorym
        // "osi nie ma nigdzie". To bylo FALSZYWE i zostalo obalone WYKONANIEM: po usunieciu
        // WSZYSTKICH 39 deklaracji osi (takze z klockow akcji) walidator dawal nadal 0 bledow.
        // Mechanizm: bez deklaracji akcja dostaje inicjalizatory (Natural/Neutral/Moderate),
        // kandydat kopiuje je z akcji, wiec obie strony sa rowne Z DEFINICJI i obie asercje
        // przechodza naraz.
        //
        // Wlasciwym zabezpieczeniem jest ZROZNICOWANIE: katalog, ktory realnie uzywa osi, musi
        // pokazywac na nich WIECEJ NIZ JEDNA wartosc. Inaczej czynniki freshness (po Theme)
        // i dramaticContrast (po Valence x Scale) nie maja czego rozrozniac, a ich wyniki
        // w ewaluacji byly by stale - co wygladaloby jak slaby mechanizm, a bylo by pustym katalogiem.
        var akcjeKat = blocks.Where(b => b.Type == BlockType.Action).ToList();
        int roznychThemes = akcjeKat.Select(b => b.Theme).Distinct().Count();
        int roznychValence = akcjeKat.Select(b => b.Valence).Distinct().Count();
        int roznychScale = akcjeKat.Select(b => b.Scale).Distinct().Count();
        T.Ok("umowa danych: osie akcji sa ZROZNICOWANE (katalog ich faktycznie uzywa)",
             roznychThemes > 1 && roznychValence > 1 && roznychScale > 1,
             "roznych wartosci - Theme: " + roznychThemes + ", Valence: " + roznychValence
             + ", Scale: " + roznychScale);

        // I trzeci kierunek, ktory dopiero zamyka luke: kandydaci tez maja pokazywac to
        // zroznicowanie. Asercja wyzej patrzy na KATALOG, ta na to, co z niego realnie wychodzi.
        T.Ok("umowa danych: zroznicowanie osi dociera do KANDYDATOW, nie tylko do katalogu",
             wszystkieKand.Select(e => e.Theme).Distinct().Count() > 1
             && wszystkieKand.Select(e => e.Valence).Distinct().Count() > 1
             && wszystkieKand.Select(e => e.Scale).Distinct().Count() > 1,
             "kandydaci - Theme: " + wszystkieKand.Select(e => e.Theme).Distinct().Count()
             + ", Valence: " + wszystkieKand.Select(e => e.Valence).Distinct().Count()
             + ", Scale: " + wszystkieKand.Select(e => e.Scale).Distinct().Count());

        // ------------------------------------------------- BudgetSplitter jednostkowo
        T.Section("BUDGETSPLITTER (jednostkowo, SPEC 1)");
        Wektor("Distribute(4, [5]x12)", BudgetSplitter.Distribute(4, Enumerable.Repeat(5, 12).ToArray()),
               new[] { 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0 });
        Wektor("Distribute(10, [2,50,50])", BudgetSplitter.Distribute(10, new[] { 2, 50, 50 }), new[] { 2, 4, 4 });
        Wektor("Distribute(100, [10,10,10])", BudgetSplitter.Distribute(100, new[] { 10, 10, 10 }), new[] { 10, 10, 10 });
        Wektor("Distribute(0, [7,7])", BudgetSplitter.Distribute(0, new[] { 7, 7 }), new[] { 0, 0 });
        Wektor("Distribute(5, [])", BudgetSplitter.Distribute(5, new int[0]), new int[0]);

        var rnd = new Random(20240831);
        int zlamane = 0;
        for (int i = 0; i < 200; i++)
        {
            int n = rnd.Next(1, 15);
            var d = new int[n];
            for (int k = 0; k < n; k++) d[k] = rnd.Next(0, 30);
            int rem = rnd.Next(0, 200);
            int[] wynik = BudgetSplitter.Distribute(rem, d);
            if (wynik.Sum() != Math.Min(rem, d.Sum())) zlamane++;
            for (int k = 0; k < n; k++) if (wynik[k] < 0 || wynik[k] > d[k]) zlamane++;
        }
        T.EqI("200 losowych przypadkow: niezmienniki sumy i zakresu", zlamane, 0);
    }

    private static void Wektor(string name, int[] got, int[] exp)
    {
        bool ok = got.Length == exp.Length;
        if (ok)
        {
            for (int i = 0; i < got.Length; i++) if (got[i] != exp[i]) ok = false;
        }
        T.Ok(name, ok, "oczekiwano [" + string.Join(",", exp) + "], otrzymano [" + string.Join(",", got) + "]");
    }
}
