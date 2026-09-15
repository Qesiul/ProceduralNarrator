using System.Globalization;
using ProceduralNarrator.Core.Model;

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
}
