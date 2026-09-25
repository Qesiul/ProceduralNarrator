using System.Collections.Generic;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Integration.Arcs;
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
    ///                                  OD KROKU 8 NIEUZYWANE (decyzja autora K8-4): opis dopisujemy
    ///                                  do listu PO wykonaniu (ChoiceLetter.Text ma publiczny setter),
    ///                                  co dziala dla 13/13 i zostawia waniliowe informacje
    ///                                  mechaniczne - patrz LetterAnnotator.
    ///
    ///   spawnCenter         1/12 wprost
    ///                              - NIE jest bezczynne, ale tez NIE czyta go zaden worker napadu.
    ///                                DRUGIE SPROSTOWANIE, tym razem poprzedniej wersji TEGO komentarza:
    ///                                pisala ona, ze spawnCenter czyta bezposrednio
    ///                                IncidentWorker_RaidEnemy. Nieprawda. Zliczone w calym lancuchu
    ///                                (RaidEnemy -> Raid -> PawnsArrive -> IncidentWorker, wszystkie
    ///                                cztery typy rozwiazane): pole pada DOKLADNIE RAZ i jest to linia
    ///                                logu debugowego pod if (DebugSettings.logRaidInfo). To ta sama
    ///                                pomylka co przy points: "pole wystepuje w typie" wzieto za
    ///                                "typ czyta to pole".
    ///
    ///                                Wprost czyta je tylko IncidentWorker_AggressiveAnimals
    ///                                (ManhunterPack). Napady honoruja je WYLACZNIE POSREDNIO, przez
    ///                                wykonawce sposobu przybycia - i tylko przez trzy z nich
    ///                                (EdgeWalkIn, EdgeDrop, CenterDrop oslaniaja wlasne wyliczenie
    ///                                straznikiem if (!parms.spawnCenter.IsValid)). Pozostale ignoruja
    ///                                albo NADPISUJA; przy strategii ImmediateAttack pula ma siedem
    ///                                trybow i szansa na uszanowanie naszej komorki wynosi 40-52%
    ///                                zaleznie od punktow. Infestation ma wlasne pole:
    ///                                infestationLocOverride.
    ///
    ///                                MAPOWANIA SLOTU Target NA TO POLE NIE ROBIMY - i jest to decyzja
    ///                                po rozpoznaniu, nie zaniechanie. Przypiecie raidArrivalMode
    ///                                (jedyna droga do determinizmu) zmienia SILE napadu przez
    ///                                pointsFactorCurve, omija bramke technologiczna i odblokowuje
    ///                                strategie, ktorych wanilia nie wybierze. Do tego EdgeWalkIn
    ///                                spawnuje pionki dokladnie w podanej komorce BEZ sprawdzenia,
    ///                                ze jest brzegowa - "serce osady" wstawiloby napastnikow do
    ///                                srodka bazy. Pelne wyprowadzenie w CLAUDE.md, sekcja o slocie
    ///                                Target.
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
        /// Dopisuje do gotowych parametrow wszystko, co wnosi kompozycja - i NIC poza tym, co niesie
        /// specyfikacja wykonania (krok 8, dlug 9). Metoda nie widzi ani zdarzenia, ani tekstu, wiec
        /// nie da sie w niej zmienic parametru, ktorego nie ma w kluczu: ExecutionSpec.Key i to
        /// tlumaczenie powstaja z TEGO SAMEGO obiektu (walidator TEST 15 pilnuje, ze kazde pole specu
        /// zmienia klucz).
        ///
        /// Parametry bazowe przychodza z zewnatrz (StorytellerComp.GenerateParms), bo tamta
        /// metoda jest chroniona i nalezy do compa - a ta klasa ma zostac bezstanowa i mozliwie
        /// niezalezna. Mutujemy przekazany obiekt i zwracamy go dla wygody wolajacego.
        ///
        /// FRAKCJA SPRAWCY (krok 5, ciaglosc frakcji luku Wendeta): ustawiana wylacznie dla
        /// kandydata, ktorego luk dopasowal przez oczekiwanie sameFaction i tylko przy frakcji,
        /// ktora przechodzi PELNY waniliowy filtr zrodla napadu (FactionBinding) - bo ustawiona
        /// frakcja omija sprawdzenie kandydatow w PawnsArrive.CanFireNowSub. Rozwiazywana tutaj
        /// z identyfikatora w specyfikacji, a nie podawana z boku.
        /// </summary>
        public static IncidentParms Apply(IncidentParms parms, ExecutionSpec spec)
        {
            if (parms == null || spec == null)
            {
                return parms;
            }

            if (spec.FactionId != null)
            {
                Faction frakcja = ArcObservationBuilder.ResolveFaction(spec.FactionId);
                if (frakcja != null)
                {
                    parms.faction = frakcja;
                }
            }

            // INTENSYWNOSC -> PUNKTY. Jedyne tlumaczenie, ktore dziala dla calego katalogu.
            // Zakres mnoznika (0.70-1.35) jest celowo wezszy od waniliowego pointsFactorFromAdaptDays
            // (0.40-2.00): intensywnosc klocka ma modulowac dawke, a nie przejmowac sterowanie
            // trudnoscia, ktora i tak rosnie z bogactwem przez PointsPerWealthCurve.
            parms.points *= IntensityTable.PointsFactor(spec.Intensity);

            // Tekstu listu tu NIE MA (krok 8, decyzja K8-4): opis zlozony z klockow dopisuje
            // LetterAnnotator do listu gry PO wykonaniu - customLetterText honorowalo 9 z 13 naszych
            // incydentow i zastepowalo waniliowe informacje mechaniczne.
            return parms;
        }

        /// <summary>
        /// ODCISK PARAMETROW WYKONANIA - dwa zdarzenia o tym samym kluczu trafiaja do gry
        /// z IncidentParms nieodroznialnymi z punktu widzenia CanFireNow.
        ///
        /// Od kroku 8 to cienka nakladka na ExecutionSpec.Key (jedno zrodlo prawdy z Apply).
        /// Apply rozniicuje dzis dokladnie dwie rzeczy, obie w specyfikacji:
        ///   - parms.points   (przez IntensityTable.PointsFactor(Intensity))  -> ISTOTNE
        ///   - parms.faction  (wiazanie frakcji luku)                         -> ISTOTNE
        /// Punkty wchodza, bo od nich wykonalnosc REALNIE zalezy:
        /// bramki min/maxThreatPoints leza przed cache'em w kazdym payloadzie z progiem, a z 3 z 13
        /// payloadow, ktore czytaja parms.points w CanFireNowSub, wynik zmienia sie w JEDNYM -
        /// ManhunterPack (TryFindAggressiveAnimalKind(points)). WandererJoin i RefugeePodCrash
        /// przekazuja punkty do questScriptDef.CanRun, ale TestRunInt ich rootow zwraca true.
        ///
        /// Klucz jest budowany z INTENSYWNOSCI, a nie z policzonych punktow, i jest to celowe:
        /// punkty bazowe sa wspolne dla calej tury, wiec mnoznik intensywnosci wyznacza je
        /// jednoznacznie, a klucz da sie policzyc bez budowania IncidentParms dla kazdego
        /// z osiemdziesieciu kandydatow.
        ///
        /// GDY DOJDZIE NOWE POLE (raidArrivalMode, infestationLocOverride, spawnCenter przy
        /// domykaniu slotu Target) - dopisuje sie je do ExecutionSpec (Core/Model), a nie tutaj.
        /// Apply nie ma innego wejscia, a TEST 15 zapali sie, dopoki nowe pole nie zmienia klucza.
        ///
        /// Frakcja (krok 5): parms.faction zmienia sciezke CanFireNowSub napadu (z frakcja "true" od
        /// razu, bez niej - sprawdzenie kandydatow), wiec dwa warianty rozniace sie tylko wiazaniem
        /// frakcji NIE sa dla gry nieodroznialne. Bez frakcji klucz jest identyczny jak przed
        /// krokiem 5 - dane i odlozenia v6 zostaja porownywalne.
        /// </summary>
        public static string ExecutionKey(ComposedEvent zdarzenie, string frakcjaId)
        {
            ExecutionSpec spec = ExecutionSpec.From(zdarzenie, frakcjaId);
            return spec == null ? null : spec.Key;
        }

        /// <summary>
        /// DOKLADNE sito kandydatow: usuwa te, ktorych gra i tak nie przepusci - przez prog
        /// punktow zagrozenia ALBO przez filtr trudnosci dla ThreatBig (patrz BigThreatsAllowedNow).
        /// Wolane MIEDZY generowaniem a scoringiem.
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
        ///     wygenerowanych - kandydatow == liczba odfiltrowanych (obie przyczyny RAZEM)
        /// Zaden inny mechanizm nie usuwa kandydatow miedzy tymi dwoma pomiarami (weto i progi
        /// jakosci tylko OZNACZAJA, nie usuwaja). Podzialu na przyczyny w [PN-DATA] NIE MA - niesie
        /// go wylacznie log czytelny (osobne linie "Filtr trudnosci" i "Sito punktow zagrozenia").
        /// Uwaga: przy wylaczonych duzych zagrozeniach sito punktow jest dzis NIEOSIAGALNE, bo oba
        /// payloady z dodatnim minThreatPoints (Infestation, PsychicEmanatorShipPartCrash) sa
        /// ThreatBig i filtr trudnosci odcina je wczesniej.
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
        /// <param name="duzeZagrozeniaDozwolone">
        /// Wynik BigThreatsAllowedNow() - liczony RAZ na ture przez wolajacego. Gdy false,
        /// odpadaja wszystkie kandydaty kategorii ThreatBig (patrz nizej).
        /// </param>
        /// <param name="usunietychTrudnosc">
        /// Ile z usunietych odpadlo przez filtr trudnosci (a nie przez prog punktow). Zawiera sie
        /// w "usunietych" - relacja wygenerowanych - kandydatow == usunietych obowiazuje dalej.
        /// </param>
        public static List<ComposedEvent> OdfiltrujNieosiagalne(List<ComposedEvent> kandydaci,
                                                                float punktyBazowe,
                                                                bool duzeZagrozeniaDozwolone,
                                                                out int usunietych,
                                                                out int usunietychTrudnosc)
        {
            usunietych = 0;
            usunietychTrudnosc = 0;
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

                // FILTR TRUDNOSCI - odwzorowanie tego, co Storyteller.MakeIncidentsForInterval
                // robi Z WYNIKIEM compa. Patrz BigThreatsAllowedNow.
                if (inc != null && !duzeZagrozeniaDozwolone && inc.category == IncidentCategoryDefOf.ThreatBig)
                {
                    usunietych++;
                    usunietychTrudnosc++;
                    continue;
                }

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

        /// <summary>
        /// Czy gra PRZEPUSCI teraz incydent kategorii ThreatBig zwrocony przez comp narratora.
        ///
        /// Zdekompilowane Storyteller.MakeIncidentsForInterval filtruje wynik compa PO FAKCIE:
        ///     foreach (FiringIncident fi in comp.MakeIntervalIncidents(target))
        ///         if ((difficulty.allowBigThreats || fi.def.category != ThreatBig)
        ///             &amp;&amp; (!AnomalyActive || TicksGame - metalHellClosedTick &gt;= 300000
        ///                 || fi.def.category != ThreatBig))
        ///             yield return fi;
        /// Nasz comp zapisuje zdarzenie do historii PRZED yield return (musi - patrz komentarz
        /// przy RecordEvent).
        ///
        /// DWIE CZESCI PREDYKATU, DWA ROZNE SKUTKI (sprostowanie po przegladzie - pierwsze wydanie
        /// tego komentarza twierdzilo, ze na Peaceful narrator zapisywal napady, ktorych nie bylo;
        /// to NIEPRAWDA):
        ///   - allowBigThreats: bazowy IncidentWorker.CanFireNow sam odrzuca ThreatBig przy
        ///     allowBigThreats=false (bramka PRZED cache'em CanFireNowSub), wiec kandydat konczyl
        ///     jako odmowa silnika, a nie wpis do historii. Tu filtr jest OPTYMALIZACJA i zmiana
        ///     ksiegowania: ThreatBig nie zuzywa pytan fazy 0, nie zasila odmowSilnika/odmowCzola,
        ///     a brama od razu widzi czolo wykonalne.
        ///   - okno 300000 tickow po zamknieciu metalowego piekla (Anomaly): CanFireNow tego NIE
        ///     sprawdza. Tu byl REALNY blad - narrator zapisywal do pamieci i do [PN-DATA] napad,
        ///     ktory gra po cichu wyrzucala.
        /// Warunek jest odwzorowaniem wanilii 1:1, a nie wlasna polityka.
        /// </summary>
        public static bool BigThreatsAllowedNow()
        {
            if (Find.Storyteller == null || Find.Storyteller.difficulty == null)
            {
                return true;
            }
            if (!Find.Storyteller.difficulty.allowBigThreats)
            {
                return false;
            }
            if (ModsConfig.AnomalyActive && Find.Anomaly != null && Find.TickManager != null
                && Find.TickManager.TicksGame - Find.Anomaly.metalHellClosedTick < 300000)
            {
                return false;
            }
            return true;
        }
    }
}
