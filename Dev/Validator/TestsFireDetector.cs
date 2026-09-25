using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Evaluation;

/// <summary>
/// TEST 17 - detektor odpalen incydentow dla [PN-FIRED] (krok 8, decyzja autora K8-2).
///
/// Wyprowadzenie: gra wpisuje do lastFireTicks[incydent] tick odpalenia. Odpalenie "nowe" to wpis
/// o ticku wiekszym niz poprzedni przeglad celu i nie wiekszym niz biezacy tick. Po wczytaniu zapisu
/// przegladem odniesienia jest tick wczytania - wpisy sprzed zapisu nie wracaja.
/// Przeglad S10: cel widziany pierwszy raz PO pierwszym przegladzie zaczyna od chwili zobaczenia (gra kopiuje
/// StoryState miedzy karawana a mapa tymczasowa), a wpisy z przyszlosci sa fantomami, nie odpaleniami.
/// </summary>
static class TestsFireDetector
{
    static List<KeyValuePair<string, int>> W(params (string inc, int tick)[] wpisy)
    {
        return wpisy.Select(w => new KeyValuePair<string, int>(w.inc, w.tick)).ToList();
    }

    static string Opis(List<DetectedFire> l)
    {
        return string.Join(",", l.Select(f => f.Target + "/" + f.Incident + "@" + f.Tick));
    }

    public static void Run()
    {
        T.Section("TEST 17 - detektor odpalen incydentow ([PN-FIRED], krok 8)");

        // 17a. Start po wczytaniu w ticku 1000: wpisy do 1000 (wlacznie) sa z przeszlosci.
        var d = new FireDetector(1000);
        var wynik = new List<DetectedFire>();
        d.Scan("map:3", W(("RaidEnemy", 400), ("Flashstorm", 1000), ("ResourcePodCrash", 1001)), 1001, wynik);
        T.EqS("17a po wczytaniu: tylko odpalenie po ticku startu", Opis(wynik), "map:3/ResourcePodCrash@1001");

        // 17b. Kolejny przeglad: tylko wpisy nowsze niz poprzedni przeglad (1001).
        wynik.Clear();
        d.Scan("map:3", W(("RaidEnemy", 400), ("Flashstorm", 1000), ("ResourcePodCrash", 1001), ("ManhunterPack", 1002)), 1002, wynik);
        T.EqS("17b drugi przeglad: bez powtorek", Opis(wynik), "map:3/ManhunterPack@1002");
        wynik.Clear();
        d.Scan("map:3", W(("ManhunterPack", 1002)), 1003, wynik);
        T.EqI("17b przeglad bez nowych odpalen = pusto", wynik.Count, 0);
        T.EqI("17b znacznik celu = tick przegladu", d.LastScan("map:3"), 1003);

        // 17c. (przeglad S10) Wpis z przyszlosci to FANTOM (waniliowe "Future incidents" wpisuje ticki
        // symulacji): nie jest odpaleniem ani teraz, ani gdy zegar do niego dojdzie. Scan zwraca liczbe NOWYCH
        // fantomow (obserwator ostrzega raz), a prawdziwe odpalenie nadpisujace fantom inna wartoscia - wykryte.
        wynik.Clear();
        int f1 = d.Scan("map:3", W(("Infestation", 1500)), 1004, wynik);
        T.EqI("17c wpis z przyszlosci pominiety", wynik.Count, 0);
        T.EqI("17c nowy fantom policzony", f1, 1);
        T.EqI("17c fantom zapamietany", d.Phantoms, 1);
        int f2 = d.Scan("map:3", W(("Infestation", 1500)), 1250, wynik);
        T.EqI("17c ten sam fantom nie jest liczony drugi raz", f2, 0);
        d.Scan("map:3", W(("Infestation", 1500)), 1500, wynik);
        T.EqI("17c zegar doszedl do fantomu - to NIE jest odpalenie", wynik.Count, 0);
        T.EqI("17c fantom zapomniany po dojsciu zegara", d.Phantoms, 0);
        int f3 = d.Scan("map:3", W(("Infestation", 2000)), 1600, wynik);
        T.EqI("17c drugi fantom", f3, 1);
        d.Scan("map:3", W(("Infestation", 1700)), 1700, wynik);
        T.EqS("17c gra nadpisala fantom prawdziwym odpaleniem - wykryte", Opis(wynik), "map:3/Infestation@1700");
        T.EqI("17c po nadpisaniu fantomu nie ma", d.Phantoms, 0);
        wynik.Clear();
        int fInny = d.Scan("map:4", W(("Infestation", 2000)), 1700, wynik);
        T.EqI("17c fantom jest per cel (ta sama wartosc na innym celu to nowy fantom)", fInny, 1);

        // 17d. Wiele odpalen naraz: porzadek (tick, incydent) niezalezny od kolejnosci w slowniku.
        var d2 = new FireDetector(0);
        wynik.Clear();
        d2.Scan("world", W(("Zeta", 20), ("Alfa", 20), ("Beta", 10)), 30, wynik);
        T.EqS("17d porzadek wykrytych: tick, potem incydent", Opis(wynik), "world/Beta@10,world/Alfa@20,world/Zeta@20");

        // 17e. Zegar cofniety (np. eksperyment przywracajacy stan): znacznik sie nie cofa, powtorek brak.
        wynik.Clear();
        d2.Scan("world", W(("Beta", 10), ("Alfa", 20), ("Zeta", 20)), 15, wynik);
        T.EqI("17e cofniety zegar - bez powtorek", wynik.Count, 0);
        T.EqI("17e znacznik celu nie cofa sie", d2.LastScan("world"), 30);

        // 17f. (przeglad S10) Cele z PIERWSZEGO przegladu licza od startu detektora (wczytanie); cel widziany
        // pierwszy raz pozniej (nowa karawana) liczy od chwili zobaczenia - jego wpisy z przeszlosci to kopia
        // cudzego StoryState, nie odpalenia. Kolejne przeglady tego celu wykrywaja juz normalnie.
        var d3 = new FireDetector(100);
        wynik.Clear();
        d3.Scan("map:1", W(("A", 150)), 150, wynik);
        d3.Scan("world", W(("B", 120)), 150, wynik);
        T.EqS("17f pierwszy przeglad: kazdy cel od startu detektora", Opis(wynik), "map:1/A@150,world/B@120");
        wynik.Clear();
        d3.Scan("caravan:7", W(("RaidEnemy", 3000), ("CaravanAmbush", 5000)), 5000, wynik);
        T.EqI("17f nowy cel po pierwszym przegladzie: wpisy do chwili zobaczenia pominiete", wynik.Count, 0);
        d3.Scan("caravan:7", W(("RaidEnemy", 3000), ("CaravanAmbush", 5100)), 5250, wynik);
        T.EqS("17f kolejny przeglad nowego celu wykrywa normalnie", Opis(wynik), "caravan:7/CaravanAmbush@5100");
        T.EqI("17f trzy znane cele", d3.KnownTargets, 3);
        T.EqI("17f cel nieznany = tick startu (LastScan)", d3.LastScan("map:99"), 100);

        // 17h. (przeglad S10, F1) Mapa tymczasowa zalozona z karawany dostaje KOPIE StoryState karawany
        // (RetainedCaravanData / Notify_CaravanFormed): zasadzka z 7990 byla juz zalogowana na karawanie, na nowej
        // mapie nie moze wrocic jako drugie odpalenie. Prawdziwe odpalenie na nowej mapie - wykryte.
        wynik.Clear();
        d3.Scan("caravan:7", W(("CaravanAmbush", 7990)), 8000, wynik);
        T.EqS("17h zasadzka wykryta na karawanie", Opis(wynik), "caravan:7/CaravanAmbush@7990");
        wynik.Clear();
        d3.Scan("map:12", W(("RaidEnemy", 3000), ("CaravanAmbush", 7990)), 8250, wynik);
        T.EqI("17h kopia StoryState na nowej mapie - bez fantomowych odpalen", wynik.Count, 0);
        d3.Scan("map:12", W(("RaidEnemy", 8300), ("CaravanAmbush", 7990)), 8500, wynik);
        T.EqS("17h prawdziwe odpalenie na nowej mapie wykryte", Opis(wynik), "map:12/RaidEnemy@8300");

        // 17g. Bezpieczenstwo wejscia.
        wynik.Clear();
        d3.Scan(null, W(("X", 9999)), 9999, wynik);
        d3.Scan("map:1", null, 9999, wynik);
        d3.Scan("map:1", W((null, 9999)), 9999, wynik);
        T.EqI("17g null cel/slownik/incydent - bez wykryc i bez wyjatku", wynik.Count, 0);
    }
}
