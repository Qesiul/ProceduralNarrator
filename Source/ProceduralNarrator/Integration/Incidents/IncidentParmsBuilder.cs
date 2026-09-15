using System.Collections.Generic;
using ProceduralNarrator.Core.Model;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration.Incidents
{
    /// <summary>
    /// SZEW MIEDZY ZNACZENIEM A WYKONANIEM: tlumaczy zlozone wydarzenie na parametry, z ktorymi
    /// gra je odpali. Jedyne miejsce, w ktorym decyzje kompozycji zamieniaja sie w mechanike.
    ///
    /// Do tej pory to tlumaczenie bylo dwiema linijkami wtopionymi w petle wyboru. Wydzielenie
    /// go daje trzy rzeczy: nazwane miejsce dla mapowania, punkt wpiecia dla przyszlych
    /// tlumaczen per rodzina incydentow, oraz - najwazniejsze - miejsce, w ktorym mozna zapisac,
    /// CZEGO SILNIK NIE POZWALA ZROBIC.
    ///
    /// ================================================================================
    ///  CO Z IncidentParms JEST REALNIE HONOROWANE PRZEZ NASZ KATALOG
    ///  (zmierzone dekompilacja Assembly-CSharp 1.5.4063, nie zalozone)
    /// ================================================================================
    ///
    /// UWAGA - METODA POMIARU MA ZNACZENIE - pierwsza wersja tej tabeli byla BLEDNA.
    /// Grepowala typy LISCIOWE po zgadnietych nazwach, bez sprawdzenia, czy typ sie w ogole
    /// rozwiazal, i bez przejscia po dziedziczeniu. Pusty wynik ilspycmd daje `grep -c` rowne
    /// zero, NIEODROZNIALNE od "pole nieuzywane" - ta sama rodzina cichej awarii, ktora ten
    /// projekt tropi w kodzie. Konkretnie: typ IncidentWorker_ManhunterPack NIE ISTNIEJE
    /// (ManhunterPack uzywa IncidentWorker_AggressiveAnimals), a WandererJoin i RefugeePodCrash
    /// uzywaja IncidentWorker_GiveQuest, nie workerow o swoich nazwach.
    /// Ponizsze liczby pochodza z przejscia LANCUCHEM DZIEDZICZENIA i szukania WYWOLAN,
    /// a nie definicji w klasie bazowej.
    ///
    ///   points                12/12  - docieraja do kazdego workera i bramkuja CanFireNow przez
    ///                                  min/maxThreatPoints. UWAGA: "docieraja" to NIE to samo co
    ///                                  "skaluja efekt". Zweryfikowane: Infestation skaluje
    ///                                  (points/220f), a ResourcePodCrash, MeteoriteImpact
    ///                                  i Flashstorm NIE uzywaja parms.points ani razu -
    ///                                  ResourcePodCrash wola ThingSetMaker bez parametrow.
    ///                                  Pozostalych nie sprawdzano.
    ///
    ///   customLetterText       8/12  - honoruje je IncidentWorker.SendIncidentLetter, wolane
    ///                                  przez (zweryfikowane po lancuchu):
    ///                                    ResourcePodCrash, AmbrosiaSprout, Flashstorm,
    ///                                    Infestation, WildManWandersIn, CrashedShipPart,
    ///                                    ManhunterPack (IncidentWorker_AggressiveAnimals),
    ///                                    RaidEnemy (przez bazowy IncidentWorker_Raid).
    ///                                  IGNORUJA (buduja list same albo robi to quest):
    ///                                    MeteoriteImpact, RansomDemand,
    ///                                    WandererJoin i RefugeePodCrash - oba IncidentWorker_GiveQuest,
    ///                                    gdzie list tworzy zadanie, nie incydent.
    ///
    ///   spawnCenter          CZESC  - NIE jest bezczynne, wbrew pierwszej wersji tej tabeli.
    ///                                  Czytaja je bezposrednio IncidentWorker_AggressiveAnimals
    ///                                  (ManhunterPack) i IncidentWorker_RaidEnemy. Napady korzystaja
    ///                                  z niego dodatkowo POSREDNIO, przez wykonawcow sposobu
    ///                                  przybycia: PawnsArrivalModeWorker_EdgeWalkIn, _EdgeDrop
    ///                                  i _CenterDrop. Infestation ma wlasne pole polozenia -
    ///                                  infestationLocOverride.
    ///                                  Sterowanie polozeniem jest wiec OTWARTYM kierunkiem
    ///                                  rozwoju dla czesci katalogu, a nie granica API.
    ///
    ///   ilosc zasobow            --  - TAKIEGO POLA NIE MA. IncidentWorker_ResourcePodCrash
    ///                                  dobiera zawartosc sam (ThingSetMakerDefOf.ResourcePod).
    ///                                  "Mniejsza dostawa" wymagalaby wlasnego IncidentWorkera
    ///                                  albo Harmony'ego - to jedyny punkt, w ktorym pierwsza
    ///                                  wersja tabeli byla trafna.
    ///
    /// ================================================================================
    ///  ZASADA, KTORA TA KLASA MA CHRONIC
    /// ================================================================================
    ///
    /// ZADNEGO `switch` PO defName PAYLOADU. Sekcja 15 CLAUDE.md: "dodawanie tresci ma isc
    /// w Defy/XML, nie w kod decyzyjny; jesli musisz dotknac logiki, zeby dodac tresc - projekt
    /// danych jest zly". Rozgalezienie po nazwie incydentu znaczyloby, ze kazdy nowy klocek
    /// akcji wymaga zmiany w C#. Gdy dojda tlumaczenia per rodzina (raid: raidArrivalMode,
    /// infestacja: infestationLocOverride), maja byc wybierane po DEKLARACJI KLOCKA, a nie
    /// po nazwie incydentu - tak samo jak IntensityLevel wybiera mnoznik przez IntensityTable.
    /// </summary>
    public static class IncidentParmsBuilder
    {
        /// <summary>
        /// Dopisuje do gotowych parametrow wszystko, co wnosi kompozycja.
        ///
        /// Parametry bazowe przychodza z zewnatrz (StorytellerComp.GenerateParms), bo tamta
        /// metoda jest chroniona i nalezy do compa - a ta klasa ma zostac bezstanowa i mozliwie
        /// niezalezna. Mutujemy przekazany obiekt i zwracamy go dla wygody wolajacego.
        /// </summary>
        public static IncidentParms Apply(IncidentParms parms, ComposedEvent zdarzenie,
                                          bool uzyjZlozonegoListu)
        {
            if (parms == null || zdarzenie == null)
            {
                return parms;
            }

            // INTENSYWNOSC -> PUNKTY. Jedyne tlumaczenie, ktore dziala dla calego katalogu.
            // Zakres mnoznika (0.70-1.35) jest celowo wezszy od waniliowego pointsFactorFromAdaptDays
            // (0.40-2.00): intensywnosc klocka ma modulowac dawke, a nie przejmowac sterowanie
            // trudnoscia, ktora i tak rosnie z bogactwem przez PointsPerWealthCurve.
            parms.points *= IntensityTable.PointsFactor(zdarzenie.Intensity);

            if (uzyjZlozonegoListu && !string.IsNullOrEmpty(zdarzenie.Description))
            {
                // Zlozony opis zamiast waniliowego listu. Dziala dla 8 z 12 naszych incydentow -
                // patrz tabela w komentarzu klasy. Etykiety NIE podmieniamy: nie skladamy
                // krotkiego tytulu, a waniliowy jest trafny, wiec customLetterLabel zostaje pusty
                // i gra uzyje swojego.
                //
                // Bezpieczenstwo tekstu jest zapewnione WCZESNIEJ, przez regule "fakt w tekscie
                // = warunek twardy": kazdy fragment, ktory stwierdza sprawdzalny fakt o swiecie,
                // ma ten fakt jako conditions (Cond_Night, Cond_CalmPeriod, Cond_KidnappedColonist,
                // Cond_HostileFaction...), a fragmenty, ktorych nie dalo sie zabezpieczyc, zostaly
                // przepisane tak, by nic nie stwierdzaly. Bez tamtej rundy wlaczenie tego
                // przelacznika kazaloby narratorowi klamac graczowi prosto w twarz.
                parms.customLetterText = zdarzenie.Description;
            }

            return parms;
        }

        /// <summary>
        /// DOKLADNE sito kandydatow: usuwa te, ktorych gra i tak nie przepusci przez prog
        /// punktow zagrozenia. Wolane MIEDZY generowaniem a scoringiem.
        ///
        /// DLACZEGO TU, A NIE W WARUNKU TWARDYM. Bazowy IncidentWorker.CanFireNow porownuje
        /// z def.minThreatPoints punkty JUZ PRZEMNOZONE przez intensywnosc gotowej kompozycji.
        /// Warunek twardy dziala na poziomie klocka, wiec koncowej intensywnosci nie zna -
        /// ona sumuje wklady wszystkich piecu slotow i jest znana dopiero po zlozeniu.
        /// Cond_MinThreatPoints jest wiec sitem ZGRUBNYM (prog podzielony przez maksymalny
        /// mnoznik, zeby nie produkowac falszywych negatywow), a dokladnosc jest tutaj.
        ///
        /// DLACZEGO PRZED SCORINGIEM, A NIE PRZY ODPALANIU. Kandydat nieosiagalny, ktory wejdzie
        /// do puli, moze wygrac runde - i wtedy CanFireNow go odrzuci, runda przepada, a czynnik
        /// swiezosci premiuje go dalej, bo akcja odrzucona nie trafia do historii. To jest
        /// dokladnie petla, ktora zjadala 64% rund przez emanatora. Usuwajac go przed scoringiem,
        /// nie dajemy jej powstac.
        ///
        /// SLAD W DANYCH: kandydaci usunieci tutaj nie trafiaja do puli ocenianej, wiec
        /// w linii [PN-DATA] zachodzi
        ///     wygenerowanych - kandydatow == liczba odfiltrowanych jako nieosiagalne
        /// Nie potrzeba wiec nowej kolumny ani podbicia wersji formatu; roznica jest
        /// samoopisujaca, bo zaden inny mechanizm nie usuwa kandydatow miedzy tymi dwoma
        /// pomiarami (weto i progi jakosci tylko OZNACZAJA, nie usuwaja).
        ///
        /// ZWRACA NOWA LISTE I JEST TO NAPRAWA BLEDU, NIE STYL. Pierwsza wersja usuwala
        /// kandydatow W MIEJSCU, a wolajacy podawal jej CandidateSet.Candidates - czyli liste,
        /// z ktorej logger czyta kolumne "wygenerowanych". Skutek: obie strony relacji powyzej
        /// kurczyly sie razem i roznica wychodzila ZAWSZE ZERO, niezaleznie od tego, ilu
        /// kandydatow faktycznie usunieto. Zmierzone przy bazie 450 punktow: wygenerowano 84,
        /// usunieto 1, a log raportowal 83 i 83.
        ///
        /// Usterka byla samozacierajaca w najgorszy mozliwy sposob: jedyny slad po dzialaniu
        /// sita znikal dokladnie dlatego, ze sito zadzialalo. Naprawa przez komentarz "wolajacy
        /// ma podac kopie" bylaby tu za slaba - to ten sam rodzaj zabezpieczenia, ktore ten
        /// projekt konsekwentnie zastepuje konstrukcja (patrz: prog czytany z obiektow warunkow
        /// zamiast z literalu). Metoda jest wiec funkcja czysta i wolajacy NIE MA JAK jej zepsuc.
        /// </summary>
        public static List<ComposedEvent> OdfiltrujNieosiagalne(List<ComposedEvent> kandydaci,
                                                                float punktyBazowe,
                                                                out int usunietych)
        {
            usunietych = 0;
            if (kandydaci == null)
            {
                return new List<ComposedEvent>();
            }

            var przepuszczeni = new List<ComposedEvent>(kandydaci.Count);
            for (int i = 0; i < kandydaci.Count; i++)
            {
                ComposedEvent e = kandydaci[i];
                if (e == null || string.IsNullOrEmpty(e.ActionPayload))
                {
                    przepuszczeni.Add(e);
                    continue;
                }

                IncidentDef inc = DefDatabase<IncidentDef>.GetNamedSilentFail(e.ActionPayload);
                if (inc == null || inc.minThreatPoints <= 0f)
                {
                    // Brak progu w Defie albo nierozwiazany payload - nie nasza sprawa.
                    // Payloadu pilnuje audyt startowy, a odmowe zglosi akceptor.
                    przepuszczeni.Add(e);
                    continue;
                }

                float punktyKoncowe = punktyBazowe * IntensityTable.PointsFactor(e.Intensity);
                if (punktyKoncowe < inc.minThreatPoints)
                {
                    usunietych++;
                    continue;
                }

                przepuszczeni.Add(e);
            }

            return przepuszczeni;
        }
    }
}
