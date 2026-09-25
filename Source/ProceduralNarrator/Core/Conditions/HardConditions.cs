using System;
using System.Globalization;
using ProceduralNarrator.Core.Blackboard;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.PlayerModel;

namespace ProceduralNarrator.Core.Conditions
{
    // ================== WARUNKI TWARDE - bramki spojnosci ==================
    // Uzywane w <conditions>. Jesli ktorykolwiek nie jest spelniony, klocek
    // nie moze w ogole wejsc do kompozycji.

    /// <summary>
    /// Wymaga odpowiednio duzego stropu gorskiego w poblizu kolonii (np. infestacja).
    /// Prog, a nie flaga - pojedyncza skala w rogu mapy to nie jest gorska baza.
    /// </summary>
    public class Cond_MountainRoof : NarrativeCondition
    {
        public int minCells = 40;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.MountainRoofCellsNearColony >= minCells;
        }

        public override string Describe()
        {
            return "gorski strop >= " + minCells + " komorek";
        }
    }

    /// <summary>Wymaga istnienia niepokonanej frakcji wrogiej graczowi (np. napad).</summary>
    public class Cond_HostileFaction : NarrativeCondition
    {
        public bool required = true;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.HasHostileFaction == required;
        }

        public override string Describe()
        {
            return required ? "wymaga wrogiej frakcji" : "wymaga braku wrogiej frakcji";
        }
    }

    /// <summary>Ogranicza klocek do przedzialu liczby kolonistow.</summary>
    public class Cond_Colonists : NarrativeCondition
    {
        public int min = 0;
        public int max = 9999;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.ColonistCount >= min && s.ColonistCount <= max;
        }

        public override string Describe()
        {
            return "kolonistow " + min + "-" + max;
        }
    }

    /// <summary>
    /// Wymaga, zeby gra przyznawala zdarzeniom co najmniej tyle punktow zagrozenia.
    ///
    /// SITO ZGRUBNE, NIE ODWZOROWANIE DOKLADNE - i ta roznica jest istotna.
    ///
    /// Bazowy IncidentWorker.CanFireNow odrzuca kandydata przy parms.points &lt; def.minThreatPoints,
    /// ale porownuje punkty JUZ PRZEMNOZONE przez intensywnosc gotowej kompozycji
    /// (IncidentParmsBuilder mnozy je przez IntensityTable.PointsFactor). Ten warunek dziala
    /// natomiast na poziomie KLOCKA, czyli przy doborze slotow - a koncowa intensywnosc sumuje
    /// wklady wszystkich piecu slotow i jest znana dopiero po zlozeniu kandydata. Warunek
    /// z definicji jej nie zna.
    ///
    /// DLATEGO PROG JEST PODZIELONY PRZEZ MAKSYMALNY MNOZNIK:
    ///     prog_klocka = minThreatPoints / IntensityTable.MaxPointsFactor
    ///
    /// Dzieki temu sito odrzuca WYLACZNIE te przypadki, w ktorych zaden wariant nie mialby
    /// szans - czyli nie produkuje falszywych negatywow. Wersja z progiem rownym wartosci
    /// z Defa je produkowala: przy bazie 350 i kompozycji VeryHigh gra widzi 472 i przepuszcza,
    /// a warunek blokowal, bo 350 &lt; 400. Taka strata jest NIEWIDOCZNA W ZADNYM LOGU, bo
    /// kandydat w ogole nie powstaje.
    ///
    /// Sprawdzenie DOKLADNE - z koncowa intensywnoscia - robi warstwa integracji na gotowych
    /// kandydatach, przed scoringiem (IncidentParmsBuilder.OdfiltrujNieosiagalne). Podzial jest
    /// celowy: tanie sito odcina hurtem przy kompozycji, drogie sprawdzenie dotyka tylko tego,
    /// co juz powstalo.
    ///
    /// Relacji miedzy progiem w XML a wartoscia z Defa pilnuje audyt startowy.
    ///
    /// UWAGA: to NIE jest to samo co Pref_WealthRelative. Ten warunek mowi "gra pozwala na
    /// zdarzenie tej klasy", a preferencja mowi "takie zdarzenie tu pasuje". Punkty zagrozenia
    /// rosna z bogactwem same z siebie, wiec uzycie tego warunku do strojenia trudnosci
    /// liczyloby bogactwo dwa razy - patrz regula w sekcji o bogactwie w CLAUDE.md.
    /// </summary>
    public class Cond_MinThreatPoints : NarrativeCondition
    {
        public float min = 0f;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.ThreatPoints >= min;
        }

        public override string Describe()
        {
            return "punkty zagrozenia >= " + min.ToString("0", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Ogranicza klocek do przedzialu bogactwa WZGLEDNEGO (krotnosc normy na dany dzien).
    /// min = 2.0 znaczy "kolonia dwa razy bogatsza, niz gra sie spodziewa".
    /// </summary>
    public class Cond_WealthRelative : NarrativeCondition
    {
        public float min = 0f;
        public float max = float.MaxValue;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.WealthRelative >= min && s.WealthRelative <= max;
        }

        public override string Describe()
        {
            return "bogactwo wzgl >= " + min.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Nie wpuszcza klocka przed uplywem N dni gry.</summary>
    public class Cond_MinDaysPassed : NarrativeCondition
    {
        public int min = 0;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.DaysPassed >= min;
        }

        public override string Describe()
        {
            return "od dnia " + min;
        }
    }

    /// <summary>Wymaga obecnosci dzikich zwierzat (np. szal zwierzat).</summary>
    /// <summary>
    /// Wymaga, by ktos z kolonii byl przetrzymywany przez wroga frakcje.
    ///
    /// REGULA PROJEKTU: fragment tekstu, ktory STWIERDZA sprawdzalny fakt o stanie swiata, musi
    /// miec ten fakt jako warunek TWARDY. PN_Akcja_Okup pisze "zada okupu za porwanego czlonka
    /// kolonii" - bez tego warunku narrator mogl zazadac okupu za nikogo, dokladnie tak, jak
    /// wczesniej pisal "wszystko rozgrywa sie po zmroku" w biale poludnie.
    /// </summary>
    public class Cond_KidnappedColonist : NarrativeCondition
    {
        public int min = 1;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.KidnappedColonistCount >= min;
        }

        public override string Describe()
        {
            return "porwanych kolonistow >= " + min;
        }
    }

    /// <summary>
    /// Wymaga zasilanej konsoli lacznosci.
    ///
    /// Nie pilnuje prawdziwosci tekstu, tylko WYKONALNOSCI: waniliowy IncidentWorker_RansomDemand
    /// odrzuca zdarzenie bez konsoli. Ten sam wzorzec co Cond_MountainRoof - warunek twardy
    /// odwzorowuje realne wymaganie workera, zeby nie produkowac kandydatow skazanych na odmowe.
    /// </summary>
    public class Cond_PoweredCommsConsole : NarrativeCondition
    {
        public bool required = true;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.HasPoweredCommsConsole == required;
        }

        public override string Describe()
        {
            return required ? "zasilana konsola lacznosci" : "brak konsoli lacznosci";
        }
    }

    /// <summary>
    /// Czy na mapie jest zwierze, ktore moze oszalec POJEDYNCZO.
    ///
    /// Osobny warunek obok Cond_WildAnimals, bo waniliowy IncidentWorker_AnimalInsanitySingle
    /// ma znacznie wezszy predykat niz "dzikie zwierze": odrzuca mutanty, zwierzeta zbyt silne,
    /// stojace w mgle, powalone i juz oszalale. Rdzen tych regul nie zna i znac nie moze -
    /// liczba przychodzi gotowa w snapshocie, tak samo jak przy stropie gorskim.
    ///
    /// Warunek jest z zalozenia NIE LUZNIEJSZY od workera. Odwrotny kierunek bylby bezpieczny
    /// tylko pozornie: kandydat, ktorego silnik odrzuci, marnuje runde petli wyboru, a czynnik
    /// swiezosci premiuje go dalej, bo akcja odrzucona nie trafia do historii.
    /// </summary>
    public class Cond_MaddenableAnimals : NarrativeCondition
    {
        public int min = 1;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.MaddenableAnimalCount >= min;
        }

        public override string Describe()
        {
            return "zwierzat zdolnych do amoku >= " + min;
        }
    }

    public class Cond_WildAnimals : NarrativeCondition
    {
        public int min = 1;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.WildAnimalCount >= min;
        }

        public override string Describe()
        {
            return "dzikich zwierzat >= " + min;
        }
    }

    /// <summary>
    /// Wymaga okreslonej pory doby.
    ///
    /// REGULA PROJEKTU: fragment tekstu, ktory STWIERDZA sprawdzalny fakt o stanie swiata,
    /// musi miec ten fakt jako warunek TWARDY, a nie preferencje. Preferencja mowi "to tu
    /// pasuje", nie "to jest prawda". Zmierzone w grze: klocek PN_Mod_Noc mial sama preferencje,
    /// wiec softmax wybral go w biale poludnie i narrator napisal "Wszystko rozgrywa sie
    /// po zmroku" przy noc=False. Kompozycja byla formalnie poprawna, a tekst falszywy.
    /// </summary>
    public class Cond_Night : NarrativeCondition
    {
        public bool wantNight = true;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.IsNight == wantNight;
        }

        public override string Describe()
        {
            return wantNight ? "wymaga nocy" : "wymaga dnia";
        }
    }

    /// <summary>
    /// Pora roku znosna dla ludzi (krok 8, dlug 6) - odwzorowanie warunku z
    /// IncidentWorker_WildManWandersIn.CanFireNowSub (map.mapTemperature.SeasonAcceptableFor(ThingDefOf.Human),
    /// dekompilacja 1.5.4063), jak Cond_MountainRoof odwzorowuje wymog Rojenia. Bez niego akcja przegrywala
    /// sezonowo w silniku, kosztujac pytanie do gry w kazdej turze i nigdy nie wygrywajac.
    /// </summary>
    public class Cond_SeasonAcceptableForHumans : NarrativeCondition
    {
        public bool wantAcceptable = true;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.SeasonAcceptableForHumans == wantAcceptable;
        }

        public override string Describe()
        {
            return wantAcceptable ? "pora roku znosna dla ludzi" : "pora roku nieznosna dla ludzi";
        }
    }

    /// <summary>
    /// Skazone powietrze (przeglad S10, dlug 6): domyslnie WYMAGA czystego powietrza - odwzorowanie odmow
    /// IncidentWorker_WildManWandersIn.CanFireNowSub przy opadzie toksycznym i toksycznej mgle.
    /// </summary>
    public class Cond_ToxicAir : NarrativeCondition
    {
        public bool wantToxic = false;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.ToxicAirActive == wantToxic;
        }

        public override string Describe()
        {
            return wantToxic ? "powietrze skazone" : "powietrze czyste";
        }
    }

    /// <summary>
    /// Wymaga, by od ostatniego WYDARZENIA narratora uplynelo co najmniej tyle dni gry.
    /// Decyzje PASS sie nie licza - cisza nie przerywa spokoju, tylko go przedluza.
    ///
    /// Domyslne 1.5 dnia przy mtbDays = 2.5 odsiewa nastepstwa "tuz po sobie", a zostawia
    /// typowe odstepy. Wartosc do przestrojenia razem z tempem w kroku 4.
    /// </summary>
    public class Cond_CalmPeriod : NarrativeCondition
    {
        public float minDays = 1.5f;

        public override bool IsMet(WorldSnapshot s)
        {
            return s.DaysSinceLastEvent >= minDays;
        }

        public override string Describe()
        {
            return "spokoj >= " + minDays.ToString("0.##", CultureInfo.InvariantCulture) + " dnia";
        }
    }

    // =====================================================================================
    //  KROK 6 - warunki czytajace PAMIEC NARRATORA (blackboard) przez postac kanoniczna
    //  w WorldSnapshot. Zadna z tych klas nie zna ani ksiegi faktow, ani ksiegi lukow:
    //  widza dokladnie to, co zamrozil snapshot na poczatku tury.
    // =====================================================================================

    /// <summary>
    /// Wymaga, by fakt o danym kluczu obowiazywal (albo NIE obowiazywal, przy required = false).
    ///
    /// Obecnosc, a nie wartosc: zero jest poprawna wartoscia faktu, wiec sprawdzanie "czy jest"
    /// porownaniem z zerem odpowiadaloby falszywie dla licznika, ktory wlasnie wyzerowano.
    /// </summary>
    /// <summary>
    /// STYL GRACZA (krok 7, decyzja autora nr 14): cecha jest MOCNA STRONA gracza (profil wzgledny,
    /// c &gt;= prog). Dozwolony WYLACZNIE w startConditions lukow - w warunkach klocka zmienialby liczbe
    /// dostepnych akcji m, a przez to K = B/m i pule bramy PASS (pilnuja walidator i audyt startowy).
    ///
    /// Cecha jako TEKST, nie enum: parser Defow przy blednej wartosci enuma podstawia cicho wartosc
    /// domyslna (Walka). Nieznana cecha to blad katalogu (ArcCatalog.Build odrzuca caly luk).
    /// </summary>
    public class Cond_StylMocnaStrona : NarrativeCondition
    {
        public string dimension;
        public bool required = true;

        public override bool IsMet(WorldSnapshot s)
        {
            StyleDimension d;
            if (!StyleDimensions.TryParse(dimension, out d))
            {
                return false;
            }
            bool jest = !string.IsNullOrEmpty(s.StyleStrongSides)
                        && s.StyleStrongSides.IndexOf(";" + dimension + ";", StringComparison.Ordinal) >= 0;
            return jest == required;
        }

        public override string Describe()
        {
            return (required ? "mocna strona gracza: " : "nie mocna strona gracza: ") + (dimension ?? "?");
        }
    }

    public class Cond_Fakt : NarrativeCondition
    {
        public string key;
        public bool required = true;

        public override bool IsMet(WorldSnapshot s)
        {
            return NarratorBlackboard.FactPresentIn(s.Facts, key) == required;
        }

        public override string Describe()
        {
            return (required ? "fakt " : "brak faktu ") + (key ?? "?");
        }
    }

    /// <summary>
    /// Wymaga, by fakt obowiazywal i byl starszy niz zadana liczba dni ("rany zdazyly ostygnac").
    /// Brak faktu NIE spelnia warunku - "nigdy sie nie zdarzylo" to nie to samo co "zdarzylo sie dawno".
    /// </summary>
    public class Cond_FaktOd : NarrativeCondition
    {
        public string key;
        public float minDays = 1f;

        public override bool IsMet(WorldSnapshot s)
        {
            float wiek = NarratorBlackboard.FactAgeFrom(s.Facts, key);
            // Tolerancja granicy jak w Fact.IsActive: w interwale, w ktorym fakt osiaga minDays,
            // warunek jest spelniony niezaleznie od szumu float32 w wieku z postaci kanonicznej.
            return !float.IsNaN(wiek) && wiek >= minDays - FactLedger.BoundaryToleranceDays;
        }

        public override string Describe()
        {
            return "fakt " + (key ?? "?") + " starszy niz "
                   + minDays.ToString("0.##", CultureInfo.InvariantCulture) + " dnia";
        }
    }

    /// <summary>
    /// Wymaga, by wartosc obowiazujacego faktu miescila sie w przedziale (licznik: "co najmniej
    /// dwa razy", "nie wiecej niz piec razy"). Brak faktu nie spelnia warunku.
    /// </summary>
    public class Cond_FaktLiczba : NarrativeCondition
    {
        public string key;
        public float min;
        public float max = float.MaxValue;

        public override bool IsMet(WorldSnapshot s)
        {
            float v = NarratorBlackboard.FactValueFrom(s.Facts, key);
            return !float.IsNaN(v) && v >= min && v <= max;
        }

        public override string Describe()
        {
            return "fakt " + (key ?? "?") + " w [" + min.ToString("0.##", CultureInfo.InvariantCulture)
                   + ", " + (max >= float.MaxValue ? "inf" : max.ToString("0.##", CultureInfo.InvariantCulture)) + "]";
        }
    }

    /// <summary>
    /// Wymaga, by watek (instancja luku) byl teraz otwarty - albo NIE byl, przy required = false.
    /// Realizuje przyklad wnioskowania z sekcji 5.2 koncepcji: "watek ruiny wciaz otwarty".
    /// </summary>
    public class Cond_WatekOtwarty : NarrativeCondition
    {
        public string arc;
        public bool required = true;

        public override bool IsMet(WorldSnapshot s)
        {
            return NarratorBlackboard.ThreadHasStatus(s.Threads, NarratorBlackboard.StatusOpen, arc) == required;
        }

        public override string Describe()
        {
            return (required ? "watek otwarty " : "watek nieotwarty ") + (arc ?? "?");
        }
    }

    /// <summary>
    /// Wymaga, by watek byl juz kiedys zamkniety - opcjonalnie z konkretnym wynikiem
    /// (ArcDirector.OutcomeResolved / OutcomeFaded). Pusty outcome znaczy "dowolny wynik".
    ///
    /// Pamiec zamkniec jest OGRANICZONA (ArcLedger.MaxClosed), wiec "nie bylo zamkniete" znaczy
    /// scisle "nie ma tego w pamieci watkow", a nie "nigdy w tej rozgrywce". Fakt licznikowy jest
    /// wlasciwym narzedziem, gdy potrzebna jest pamiec bez horyzontu.
    /// </summary>
    public class Cond_WatekZamkniety : NarrativeCondition
    {
        public string arc;
        public string outcome;

        public override bool IsMet(WorldSnapshot s)
        {
            string wynik = NarratorBlackboard.ThreadTail(s.Threads, NarratorBlackboard.StatusClosed, arc);
            if (wynik == null)
            {
                return false;
            }
            return string.IsNullOrEmpty(outcome) || string.Equals(wynik, outcome, StringComparison.Ordinal);
        }

        public override string Describe()
        {
            return "watek zamkniety " + (arc ?? "?") + (string.IsNullOrEmpty(outcome) ? "" : " jako " + outcome);
        }
    }

    /// <summary>
    /// Wymaga, by od ostatniego zdarzenia o danym TEMACIE uplynelo co najmniej tyle TUR (decyzji
    /// narratora, nie dni). Realizuje drugi przyklad wnioskowania z sekcji 5.2: "3 tury bez
    /// zdarzenia militarnego".
    ///
    /// beyondHorizonCounts rozstrzyga przypadek "tematu nie ma w buforze pamieci" JAWNIE, zamiast
    /// chowac go pod nieskonczonoscia: bufor ma 24 wpisy, wiec temat nieuzywany od 24 emisji wyglada
    /// dokladnie tak samo jak nigdy nieuzyty, a to sa dwa rozne zdania o swiecie.
    /// </summary>
    public class Cond_TurBezTematu : NarrativeCondition
    {
        public Theme theme;
        public int min = 3;
        public bool beyondHorizonCounts = true;

        public override bool IsMet(WorldSnapshot s)
        {
            int tur = NarratorBlackboard.TurnsSinceThemeFrom(s.TurnsSinceThemes, theme);
            if (tur == NarratorBlackboard.BeyondHorizon)
            {
                return beyondHorizonCounts;
            }
            return tur >= min;
        }

        public override string Describe()
        {
            return "bez tematu " + theme + " od >= " + min.ToString(CultureInfo.InvariantCulture) + " tur"
                   + (beyondHorizonCounts ? "" : " (poza horyzontem nie liczy sie)");
        }
    }
}
