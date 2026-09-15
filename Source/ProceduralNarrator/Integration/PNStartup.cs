using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Tension;
using ProceduralNarrator.Integration.Defs;
using ProceduralNarrator.Integration.Storyteller;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration
{
    /// <summary>
    /// Diagnostyka startowa. Uruchamia sie zaraz po zaladowaniu Defow, wiec od razu
    /// widac w Player.log, czy warstwa danych w ogole wstala - bez czekania na
    /// pierwsze wydarzenie.
    ///
    /// Powod: pusty katalog klockow daje PASS przy kazdej decyzji, co z zewnatrz
    /// wyglada jak "narrator dziala, tylko nic nie robi". Ten log rozstrzyga to w 5 sekund.
    ///
    /// Od kroku 3 sprawdzamy tu druga awarie o dokladnie tym samym objawie: nierozpoznany
    /// wezel konfiguracji w StorytellerDefie. Gdy &lt;weights&gt; albo &lt;pass&gt; sie nie
    /// zdeserializuje, obiekt powstanie z wartosciami domyslnymi i NIKT sie nie dowie, ze
    /// kalibracja z XML nie zadzialala - narrator bedzie dzialal, tylko nie tak, jak napisano
    /// w pliku. Dlatego wypisujemy EFEKTYWNE wartosci wszystkich parametrow decyzyjnych.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PNStartup
    {
        static PNStartup()
        {
            List<NarrativeBlockDef> blocks = DefDatabase<NarrativeBlockDef>.AllDefsListForReading;

            if (blocks.NullOrEmpty())
            {
                PNLog.Error(
                    "KATALOG KLOCKOW PUSTY - zero NarrativeBlockDef w DefDatabase. "
                    + "Narrator bedzie zwracal PASS przy kazdej decyzji. Sprawdz: "
                    + "(1) czy istnieje Defs/Blocks/Blocks_Core.xml; "
                    + "(2) czy wezly XML uzywaja PELNEJ nazwy typu, czyli "
                    + "<ProceduralNarrator.Integration.Defs.NarrativeBlockDef> - sama "
                    + "<NarrativeBlockDef> NIE zadziala, bo GenTypes rozwiazuje nazwy "
                    + "typow przez Assembly.GetType, ktore wymaga namespace'u; "
                    + "(3) czy gra zostala URUCHOMIONA PONOWNIE po zmianie plikow "
                    + "- RimWorld czyta Defy i DLL wylacznie przy starcie procesu.");
            }
            else
            {
                var wgTypu = new StringBuilder();
                foreach (BlockType t in System.Enum.GetValues(typeof(BlockType)))
                {
                    int n = blocks.Count(b => b.blockType == t);
                    if (n > 0)
                    {
                        if (wgTypu.Length > 0)
                        {
                            wgTypu.Append(", ");
                        }
                        wgTypu.Append(t).Append('=').Append(n);
                    }
                }

                int krawedzie = blocks.Sum(b => b.incompatibleWith != null ? b.incompatibleWith.Count : 0);
                PNLog.Decision("START OK - " + blocks.Count + " klockow (" + wgTypu + "), "
                               + krawedzie + " zabronionych krawedzi.");

                // Obiekty warunkow to typy polimorficzne z Core, wskazywane w XML przez Class=.
                // Gdyby ktorys sie nie rozwiazal, RimWorld pominalby go PO CICHU i narrator
                // stracilby filtrowanie kontekstowe, zachowujac sie pozornie poprawnie.
                // Dlatego liczymy je jawnie i porownujemy z oczekiwaniem.
                int twarde = blocks.Sum(b => b.conditions != null ? b.conditions.Count : 0);
                int miekkie = blocks.Sum(b => b.preferences != null ? b.preferences.Count : 0);
                PNLog.Decision("Warunki: " + twarde + " twardych, " + miekkie + " miekkich.");

                if (twarde == 0 && miekkie == 0)
                {
                    PNLog.Error("Zero warunkow mimo ich obecnosci w XML - sprawdz, czy atrybuty "
                                + "Class= uzywaja PELNYCH nazw typow z namespace'em "
                                + "(ProceduralNarrator.Core.Conditions.*).");
                }

                // Klocek AKCJI bez preferencji nie wnosi NIC do dopasowania kontekstowego, a
                // ContextEvaluator zwraca 1.0, gdy cala pula preferencji kandydata jest pusta
                // ("brak informacji nie jest kara" - decyzja projektowa kroku 2). Zderzenie tych
                // dwoch regul daje kandydata z bezwarunkowym SUFITEM na osi o najwyzszej wadze,
                // czyli darmowa premie okolo +0.44 uzytecznosci, ktorej zaden trafny kontekstowo
                // kandydat nie przebije. Zmierzone przed naprawa: 2 z 84 kandydatow mialy
                // contextFit = 1.0000 w KAZDYM kontekscie, przez co RaidEnemy wypadal poza pasmo
                // near-best i nie mogl wygrac przy ZADNYM ziarnie.
                //
                // To wada DANYCH, nie kodu, wiec pilnuje jej diagnostyka, a nie asercja.
                var bezPreferencji = blocks
                    .Where(b => b.blockType == BlockType.Action)
                    .Where(b => b.preferences == null || b.preferences.Count == 0)
                    .Select(b => b.defName)
                    .ToList();

                if (bezPreferencji.Count > 0)
                {
                    PNLog.Warn("Klocki akcji BEZ preferencji (" + bezPreferencji.Count + "): "
                               + string.Join(", ", bezPreferencji.ToArray())
                               + ". Nie wnosza nic do contextFit, a kandydat zlozony z samych takich "
                               + "klockow dostaje bezwarunkowe contextFit=1.0 i wypycha z pasma "
                               + "kandydatow faktycznie trafnych. Dopisz im preferencje.");
                }

                // Wiszaca referencja w incompatibleWith po cichu oslabia graf, wiec zglaszamy.
                var znane = new HashSet<string>(blocks.Select(b => b.defName));
                foreach (NarrativeBlockDef b in blocks)
                {
                    if (b.incompatibleWith == null)
                    {
                        continue;
                    }
                    foreach (string other in b.incompatibleWith.Where(o => !znane.Contains(o)))
                    {
                        PNLog.Warn("Klocek " + b.defName + " odwoluje sie do nieistniejacego " + other);
                    }
                }
            }

            StorytellerDef narrator = DefDatabase<StorytellerDef>.GetNamedSilentFail("PN_GenerativeNarrator");
            PNLog.Decision(narrator != null
                ? "Narrator zarejestrowany w menu jako \"" + narrator.label + "\"."
                : "UWAGA: StorytellerDef PN_GenerativeNarrator NIE zaladowal sie.");

            AuditActionPayloads();
            AuditIntensityEffect();
            AuditBlockAxes();
            AuditDecisionConfig();
            AuditProfiles();

            // Naglowek formatu danych badawczych wypisujemy raz, przed jakakolwiek decyzja.
            // Dzieki temu skrypt agregujacy z kroku 8 czyta kolejnosc kolumn z tego samego pliku,
            // z ktorego czyta dane, zamiast miec ja zaszyta u siebie.
            PNLog.DataHeader();
        }

        /// <summary>
        /// Audyt konfiguracji warstwy decyzyjnej we WSZYSTKICH storytellerach, ktore uzywaja
        /// naszego kompomentu - nie tylko w PN_GenerativeNarrator, bo comp moze zostac wpiety
        /// w kolejny Def (np. druga "osobowosc" narratora w kroku 4) i wtedy tez ma byc sprawdzony.
        ///
        /// Wykrywamy tu klase awarii, ktora nie daje ZADNEGO objawu w runtime: nierozpoznany
        /// wezel XML zostawia pola na inicjalizatorach C#, wiec narrator dziala, tylko wedlug
        /// innych liczb niz te w pliku. Jedynym dowodem, ze XML zadzialal, jest wypisanie
        /// wartosci EFEKTYWNYCH - czyli tych, ktore faktycznie siedza w obiekcie.
        /// </summary>
        private static void AuditDecisionConfig()
        {
            List<StorytellerDef> storytellers = DefDatabase<StorytellerDef>.AllDefsListForReading;
            int znalezione = 0;

            foreach (StorytellerDef st in storytellers)
            {
                if (st.comps == null)
                {
                    continue;
                }

                foreach (StorytellerCompProperties comp in st.comps)
                {
                    var nasz = comp as StorytellerCompProperties_Generative;
                    if (nasz == null)
                    {
                        continue;
                    }

                    znalezione++;

                    // Sanitize NAJPIERW: log ma pokazywac wartosci, ktorych narrator faktycznie
                    // uzyje, a nie te sprzed korekty. Kazda korekta jest wypisywana osobno -
                    // cicha korekta jest gorsza od zlej wartosci, bo w XML widnieje jedna liczba,
                    // a system pracuje na innej.
                    string poprawki = nasz.Sanitize();
                    if (!string.IsNullOrEmpty(poprawki))
                    {
                        PNLog.Warn("Konfiguracja " + st.defName + " poprawiona: " + poprawki);
                    }

                    // KANAREK. Pusty configStamp znaczy, ze blok <li Class=...Generative> nie wzial
                    // udzialu w deserializacji i WSZYSTKIE parametry decyzyjne siedza na
                    // inicjalizatorach C#. Poniewaz te sa dzis co do jednego rowne wartosciom
                    // z XML, jest to awaria NIEWIDOCZNA w samych liczbach - narrator dziala,
                    // tylko kalibracja z pliku nie ma zadnego wplywu. Stad osobny, glosny blad.
                    if (string.IsNullOrEmpty(nasz.configStamp))
                    {
                        PNLog.Error("configStamp PUSTY w " + st.defName + " - blok konfiguracyjny "
                                    + "<li Class=\"...StorytellerCompProperties_Generative\"> nie zostal "
                                    + "wczytany z XML, a narrator pracuje na wartosciach domyslnych z kodu. "
                                    + "Sprawdz Defs/Storytellers/Storyteller_Generative.xml oraz czy gra "
                                    + "zostala URUCHOMIONA PONOWNIE po zmianie plikow.");
                    }

                    PNLog.Decision("Parametry decyzyjne (" + st.defName + "): " + nasz.DescribeEffective());
                }
            }

            if (znalezione == 0)
            {
                PNLog.Warn("Zaden StorytellerDef nie uzywa StorytellerCompProperties_Generative - "
                           + "warstwa decyzyjna nie zostanie nigdy uruchomiona. Sprawdz atrybut Class= "
                           + "w Defs/Storytellers/Storyteller_Generative.xml (wymagana PELNA nazwa typu).");
            }
        }

        /// <summary>
        /// Audyt katalogu OSOBOWOSCI narratora (krok 4).
        ///
        /// Pusty katalog jest awaria CICHA tej samej rodziny co pusty katalog klockow: narrator
        /// dziala dalej, tylko na profilu awaryjnym z wartosciami domyslnymi z kodu - czyli
        /// wszystkie rozgrywki dostaja te sama osobowosc, a wzorzec Strategia po prostu nie
        /// zachodzi. Z zewnatrz wyglada to jak poprawnie dzialajacy narrator.
        ///
        /// Najczestsza przyczyna jest zawsze ta sama: wezel XML bez PELNEJ nazwy typu.
        /// </summary>
        private static void AuditProfiles()
        {
            List<NarratorProfileDef> profile = DefDatabase<NarratorProfileDef>.AllDefsListForReading;

            if (profile.NullOrEmpty())
            {
                PNLog.Error(
                    "KATALOG PROFILI NARRATORA PUSTY - zero NarratorProfileDef w DefDatabase. "
                    + "Kazda rozgrywka dostanie ten sam profil awaryjny, wiec wzorzec Strategia "
                    + "nie zadziala, a narrator bedzie wygladal na sprawny. Sprawdz: "
                    + "(1) czy istnieje Defs/Storytellers/Profiles_Core.xml; "
                    + "(2) czy wezly uzywaja PELNEJ nazwy typu, czyli "
                    + "<ProceduralNarrator.Integration.Defs.NarratorProfileDef> - sama "
                    + "<NarratorProfileDef> NIE zadziala; "
                    + "(3) czy gra zostala uruchomiona PONOWNIE po zmianie plikow.");
                return;
            }

            PNLog.Decision("Profile narratora: " + NarratorProfileCatalog.DescribeCatalog()
                           + ". Profil jest LOSOWANY na starcie rozgrywki i nie jest wybierany "
                           + "przez gracza - w menu narratorow zostaje jedna pozycja.");

            foreach (NarratorProfileDef def in profile)
            {
                string poprawki = (def.tension ?? TensionParams.Default()).Sanitize();
                if (!string.IsNullOrEmpty(poprawki))
                {
                    PNLog.Warn("Profil " + def.defName + " - poprawiono parametry krzywej: " + poprawki);
                }
                PNLog.Decision("  " + def.ToProfile());
            }
        }


        /// <summary>
        /// Audyt POWIAZAN klockow akcji z incydentami gry (payload -> IncidentDef).
        ///
        /// DLACZEGO NA STARCIE, SKORO KOD DECYZYJNY I TAK TO SPRAWDZA. Sprawdza, ale dopiero
        /// przy uzyciu - a "uzycie" znaczy tutaj "klocek wygral runde softmaksu". Zle podlaczony
        /// klocek nie jest wiec wykrywany POZNO, tylko PROBABILISTYCZNIE: przy dwunastu akcjach
        /// i losowym wyborze moze przejsc niezauwazony przez dziesiatki decyzji, a przy kazdej
        /// wygranej rundzie po cichu zjada jedna runde petli wyboru. To ta sama rodzina strat
        /// co zmierzone juz marnowanie rund na akcje, ktorych silnik nigdy nie przepuszcza.
        /// Audyt startowy zamienia awarie losowa w deterministyczna i widoczna, zanim zacznie
        /// sie rozgrywka.
        ///
        /// Trzy kontrole, kazda wykrywa inna klase bledu w danych:
        ///
        ///   1. ISTNIENIE - literowka w defName incydentu. Klocek jest wtedy calkowicie martwy.
        ///
        ///   2. KATEGORIA - czy incydent nalezy do jednego z TRZECH torow, ktore podmieniamy
        ///      (ThreatBig, ThreatSmall, Misc). To NIE jest czystosc dla czystosci: nasz
        ///      StorytellerDef przejmuje 17 waniliowych compow bez zmian, wiec klocek
        ///      wskazujacy na chorobe albo questa strzelalby w tor, ktory obsluguje ROWNIEZ
        ///      wanilia - i po cichu psul argument o eksperymencie kontrolowanym, na ktorym
        ///      stoi caly rozdzial o ewaluacji porownawczej. Bladu tej klasy nie widac
        ///      w zachowaniu gry, tylko w interpretacji danych.
        ///
        ///   3. TAG CELU - czy incydent w ogole dopuszcza mape kolonii. Sprawdzamy pole
        ///      targetTags bezposrednio, bo IncidentDef.TargetAllowed wymaga zywego
        ///      IIncidentTarget, ktorego na starcie nie ma. Bez tego tagu CanFireNow odrzuci
        ///      kandydata przy KAZDEJ probie, czyli klocek jest martwy mimo poprawnego defName.
        /// </summary>
        private static void AuditActionPayloads()
        {
            List<NarrativeBlockDef> akcje = DefDatabase<NarrativeBlockDef>.AllDefsListForReading
                .Where(b => b.blockType == BlockType.Action)
                .ToList();

            if (akcje.Count == 0)
            {
                // Brak akcji jest juz zglaszany przy pustym katalogu; tutaj tylko nie udajemy,
                // ze audyt cokolwiek sprawdzil.
                return;
            }

            // Trzy tory, ktore nasz StorytellerDef podmienia u Cassandry. Poza nimi zaczyna sie
            // teren waniliowy, ktory przejmujemy bez zmian.
            var naszeKategorie = new List<IncidentCategoryDef>
            {
                IncidentCategoryDefOf.ThreatBig,
                IncidentCategoryDefOf.ThreatSmall,
                IncidentCategoryDefOf.Misc
            };

            int sprawnych = 0;

            foreach (NarrativeBlockDef b in akcje)
            {
                if (string.IsNullOrEmpty(b.payload))
                {
                    PNLog.Error("Klocek akcji " + b.defName + " nie ma pola <payload> - nie wskazuje "
                                + "na zaden incydent gry i nigdy nie wyprodukuje wydarzenia.");
                    continue;
                }

                IncidentDef inc = DefDatabase<IncidentDef>.GetNamedSilentFail(b.payload);
                if (inc == null)
                {
                    PNLog.Error("Klocek akcji " + b.defName + " wskazuje na NIEISTNIEJACY IncidentDef \""
                                + b.payload + "\". Klocek jest martwy: przejdzie kompozycje i scoring, "
                                + "a odpadnie dopiero przy probie odpalenia, marnujac runde petli wyboru. "
                                + "Sprawdz pisownie defName w Defs/Blocks/.");
                    continue;
                }

                bool ok = true;

                if (inc.category == null || !naszeKategorie.Contains(inc.category))
                {
                    ok = false;
                    PNLog.Warn("Klocek akcji " + b.defName + " -> " + inc.defName + " ma kategorie "
                               + (inc.category == null ? "BRAK" : inc.category.defName)
                               + ", spoza naszych trzech torow (ThreatBig, ThreatSmall, Misc). "
                               + "Nasz narrator przejmuje 17 waniliowych compow bez zmian, wiec ten "
                               + "incydent moze byc odpalany ROWNIEZ przez wanilie - co psuje "
                               + "kontrole eksperymentu w ewaluacji porownawczej z Cassandra.");
                }

                if (inc.targetTags == null || !inc.targetTags.Contains(IncidentTargetTagDefOf.Map_PlayerHome))
                {
                    ok = false;
                    PNLog.Error("Klocek akcji " + b.defName + " -> " + inc.defName + " NIE dopuszcza "
                                + "mapy kolonii (brak tagu Map_PlayerHome w targetTags). CanFireNow "
                                + "odrzuci go przy kazdej probie, wiec klocek jest martwy mimo "
                                + "poprawnego defName.");
                }

                // CZWARTA KONTROLA, dopisana po pomiarze w grze: prog punktow zagrozenia.
                //
                // Bazowy IncidentWorker.CanFireNow odrzuca kandydata przy
                // parms.points < def.minThreatPoints. Klocek bez odpowiadajacego warunku
                // twardego jest wtedy odrzucany W KOLKO: akcja odrzucona nie trafia do historii,
                // wiec jej swiezosc zostaje na maksimum, wiec wraca na czolo rankingu.
                // Zmierzone: 44 z 56 odmow silnika w przebiegu 100-dniowym pochodzilo z jednego
                // takiego klocka. Ta kontrola zamienia te strate w komunikat przy starcie.
                //
                // Porownujemy rowniez WARTOSC progu, nie tylko jego obecnosc - warunek ma byc
                // odwzorowaniem liczby z waniliowego Defa, a nie osobna kalibracja, ktora
                // z czasem rozjedzie sie z gra przy jej aktualizacji.
                if (inc.minThreatPoints > 0f)
                {
                    Cond_MinThreatPoints prog = b.conditions == null
                        ? null
                        : b.conditions.OfType<Cond_MinThreatPoints>().FirstOrDefault();

                    if (prog == null)
                    {
                        ok = false;
                        PNLog.Warn("Klocek akcji " + b.defName + " -> " + inc.defName + " wymaga "
                                   + inc.minThreatPoints.ToString("0", CultureInfo.InvariantCulture)
                                   + " punktow zagrozenia (minThreatPoints), ale NIE MA warunku "
                                   + "Cond_MinThreatPoints. Kompozycja bedzie produkowac kandydatow, "
                                   + "ktorych silnik odrzuci, a czynnik swiezosci bedzie je premiowal "
                                   + "w nieskonczonosc - bo akcja odrzucona nie trafia do historii.");
                    }
                    else
                    {
                        // RELACJA, NIE ROWNOSC - i to jest sedno poprawki.
                        //
                        // Gra porownuje z minThreatPoints punkty JUZ PRZEMNOZONE przez
                        // intensywnosc gotowej kompozycji. Warunek twardy dziala na poziomie
                        // klocka i tej intensywnosci nie zna, wiec zeby nie odrzucal kandydatow,
                        // ktorych gra by przepuscila, jego prog musi byc podzielony przez
                        // MAKSYMALNY mnoznik. Poprzednia wersja tego audytu wymagala ROWNOSCI
                        // i tym samym utrwalalaby blad, ktory mial wykrywac.
                        float oczekiwany = inc.minThreatPoints / IntensityTable.MaxPointsFactor;

                        if (prog.min > oczekiwany + 0.5f)
                        {
                            ok = false;
                            PNLog.Warn("Klocek akcji " + b.defName + " -> " + inc.defName
                                       + ": Cond_MinThreatPoints ma prog "
                                       + prog.min.ToString("0", CultureInfo.InvariantCulture)
                                       + ", a najwyzszy BEZPIECZNY to "
                                       + oczekiwany.ToString("0", CultureInfo.InvariantCulture)
                                       + " (= " + inc.minThreatPoints.ToString("0", CultureInfo.InvariantCulture)
                                       + " / " + IntensityTable.MaxPointsFactor.ToString("0.00", CultureInfo.InvariantCulture)
                                       + "). Zbyt wysoki prog blokuje kandydatow, ktorych gra by "
                                       + "przepuscila przy wysokiej intensywnosci - a tej straty "
                                       + "NIE WIDAC W ZADNYM LOGU, bo kandydat nie powstaje.");
                        }
                        else if (prog.min < oczekiwany - 5f)
                        {
                            PNLog.Warn("Klocek akcji " + b.defName + " -> " + inc.defName
                                       + ": Cond_MinThreatPoints ma prog "
                                       + prog.min.ToString("0", CultureInfo.InvariantCulture)
                                       + ", czyli wyraznie nizszy niz bezpieczne maksimum "
                                       + oczekiwany.ToString("0", CultureInfo.InvariantCulture)
                                       + ". Sito jest luzniejsze, niz moglo by byc - wiecej kandydatow "
                                       + "dojdzie do dokladnego filtra przed scoringiem. Nie jest to "
                                       + "blad poprawnosci, tylko zmarnowana praca kompozycji.");
                        }
                    }
                }

                if (ok)
                {
                    sprawnych++;
                }
            }

            // CZTERY kryteria, nie trzy - lista musi zgadzac sie z tym, co faktycznie zeruje `ok`.
            // Poprzednia wersja wymieniala trzy, chociaz czwarta kontrola (prog punktow zagrozenia)
            // rowniez odejmuje klocek od "sprawnych". Czytajacy log widzialby wtedy spadek licznika
            // bez podanej przyczyny.
            PNLog.Decision("Powiazania klockow akcji: " + sprawnych.ToString(CultureInfo.InvariantCulture)
                           + " z " + akcje.Count.ToString(CultureInfo.InvariantCulture)
                           + " sprawnych (payload istnieje, kategoria w naszym torze, cel dopuszcza "
                           + "mape kolonii, prog punktow zgadza sie z waniliowym Defem).");
        }

        /// <summary>
        /// STRAZNIK UMOWY DANYCH: osie naleza do klocka AKCJI i tylko do niego.
        ///
        /// EventComposer kopiuje Theme, Valence i Scale WYLACZNIE z klocka akcji - reszta
        /// slotow wnosi warunki, preferencje, tekst i intensywnosc. Deklaracja osi gdzie
        /// indziej jest wiec martwa, ale wyglada na dzialajaca: autor tresci ustawi
        /// valence=Negative na aktorze, bedzie oczekiwal ciezszego zdarzenia i nie dostanie
        /// niczego ani w zachowaniu, ani w logu.
        ///
        /// Katalog zostal z tych deklaracji oczyszczony (33 usuniete pola na 11 klockach),
        /// wiec ta kontrola milczy - i taka ma byc. Jej wartosc jest w tym, ze deklaracja
        /// NIE MOZE juz wrocic po cichu przy dosypce katalogu.
        ///
        /// Wykrywamy po WARTOSCI, a nie po fakcie deklaracji: DirectXmlToObject nie zostawia
        /// sladu po tym, czy wezel byl w pliku, wiec "zadeklarowane jako domyslne" jest
        /// nieodroznialne od "niezadeklarowane". Kontrola po wartosci jest slabsza o dokladnie
        /// ten jeden przypadek i jest to jej znany limit, a nie przeoczenie.
        /// </summary>
        private static void AuditBlockAxes()
        {
            List<NarrativeBlockDef> inneNizAkcje = DefDatabase<NarrativeBlockDef>.AllDefsListForReading
                .Where(b => b.blockType != BlockType.Action)
                .ToList();

            // PUNKT ODNIESIENIA WYPROWADZONY, NIE PRZEPISANY. Wlasciwym wzorcem nie sa literaly
            // Theme.Natural / Valence.Neutral / EventScale.Moderate, tylko to, co deserializacja
            // zostawia przy BRAKU wezla - czyli inicjalizatory samego NarrativeBlockDef. Literal
            // bylby TRZECIA kopia tej samej trojki (obok Block.cs i NarrativeBlockDef.cs) i przy
            // zmianie ktoregokolwiek inicjalizatora audyt zaczalby zglaszac naruszenie dla
            // WSZYSTKICH klockow, mimo ze zaden nie ma wezla w XML. Swiezy egzemplarz kosztuje
            // jedna alokacje raz na uruchomienie gry i usuwa te klase rozjazdu z konstrukcji.
            var domyslny = new NarrativeBlockDef();

            var naruszenia = new List<string>();
            foreach (NarrativeBlockDef b in inneNizAkcje)
            {
                var pola = new List<string>();
                if (b.theme != domyslny.theme)
                {
                    pola.Add("theme=" + b.theme);
                }
                if (b.valence != domyslny.valence)
                {
                    pola.Add("valence=" + b.valence);
                }
                if (b.scale != domyslny.scale)
                {
                    pola.Add("scale=" + b.scale);
                }
                if (pola.Count > 0)
                {
                    naruszenia.Add(b.defName + " (" + string.Join(", ", pola.ToArray()) + ")");
                }
            }

            if (naruszenia.Count > 0)
            {
                PNLog.Warn("Umowa danych zlamana: " + naruszenia.Count.ToString(CultureInfo.InvariantCulture)
                           + " klockow NIE BEDACYCH akcja deklaruje osie klasyfikacji: "
                           + string.Join("; ", naruszenia.ToArray())
                           + ". Te pola czyta WYLACZNIE klocek akcji (EventComposer), wiec tutaj "
                           + "nie robia nic - a wygladaja, jakby robily. Usun je z XML albo "
                           + "przenies zamierzony efekt na klocek akcji.");
            }
        }

        /// <summary>
        /// CZY INTENSYWNOSC KOMPOZYCJI W OGOLE COS ROBI DLA DANEJ AKCJI.
        ///
        /// PROBLEM, KTORY TO UJAWNIA. Piec slotow wnosi swoja intensywnosc, ContextEvaluator
        /// sumuje ja w IntensityLevel, IncidentParmsBuilder mnozy przez nia punkty zagrozenia -
        /// i dla czesci katalogu ten caly lancuch nie daje ZADNEGO obserwowalnego skutku.
        ///
        /// CZEGO TA KONTROLA NIE WIE - i dlaczego jej komunikat jest ostrozny.
        /// Czytamy DWIE DEKLARACJE z Defa: pointsScaleable oraz minThreatPoints. Brak obu nie
        /// DOWODZI, ze worker nie uzywa punktow - dowodzi tylko, ze Def tego nie deklaruje.
        /// pointsScaleable nie ma zreszta zadnego czytelnika w silniku (sprawdzone w pietnastu
        /// typach potoku incydentow), wiec jest deklaracja autora wanilii, a nie zachowaniem
        /// gry - nic nie wymusza, zeby byla aktualna.
        ///
        /// I TO NIE JEST OBAWA TEORETYCZNA: rozjazd wystepuje w naszym wlasnym katalogu.
        /// WandererJoin i RefugeePodCrash maja pointsScaleable = false, a ich worker
        /// (IncidentWorker_GiveQuest) PRZEKAZUJE parms.points do generatora zadania. Czy zmienia
        /// to TRESC zadania - nie zostalo zbadane, wiec ani "wplywa", ani "nie wplywa" nie jest
        /// dzis twierdzeniem uprawnionym.
        ///
        /// Dlatego komunikat mowi "brak deklaracji ... wymaga sprawdzenia kodu incydentu",
        /// a nie "bez wplywu na wykonanie". Kontrola wskazuje MIEJSCA DO SPRAWDZENIA, nie
        /// wydaje werdyktu.
        ///
        /// Niezaleznie od niej zmierzono DEKOMPILACJA, ze siedem workerow nie czyta parms.points
        /// ani razu (ResourcePodCrash, AmbrosiaSprout, MeteoriteImpact, Flashstorm,
        /// WildManWandersIn, RansomDemand, AnimalInsanitySingle). Ten pomiar jest MOCNIEJSZY,
        /// ale rowniez WEZSZY: dotyczy konkretnego katalogu w konkretnej wersji gry i nie
        /// przeniesie sie sam na nowe klocki. Kontrola startowa przeniesie sie.
        ///
        /// DLACZEGO TO NIE JEST BLAD I DLACZEGO NIE USTAWIA `ok = false`. Klocek modyfikatora,
        /// ktory wnosi wylacznie opis, jest w pelni uprawniony - narracja nie musi miec
        /// pokrycia mechanicznego, musi byc PRAWDZIWA. Zakazanie takich par kosztowaloby
        /// kandydatow bez zadnego powodu narracyjnego i podmienialo warunek "tekst ma byc
        /// prawdziwy" na znacznie ostrzejszy "kazda kombinacja ma byc mechanicznie znaczaca".
        ///
        /// SPROSTOWANIE WLASNEGO SFORMULOWANIA (poprawka uzytkownika): pierwsza wersja tego
        /// audytu mowila o intensywnosci "bezczynnej". To bylo ZA MOCNE i mylace. Brak wplywu
        /// na WYKONANIE nie znaczy braku wplywu na WYBOR: modyfikator wnosi dalej preferencje
        /// do contextFit oraz wlasna intensywnosc do intentAlignment, wiec realnie przesuwa
        /// ranking kandydatow. Bezczynny jest wylacznie kanal "punkty zagrozenia -> przebieg
        /// zdarzenia", i tylko o nim mowi ta kontrola.
        ///
        /// CZEMU WIEC TO LOGUJEMY. Bo bez tego autor tresci nie ma jak sie dowiedziec, ktory
        /// modyfikator faktycznie dziala przy ktorej akcji - a od tego zalezy, czy jego TEKST
        /// moze cokolwiek obiecywac. Zmierzone przed ta kontrola: 18 z 76 kandydatow nioslo
        /// zdanie "Rozmach jest mniejszy, niz moglby byc" przy akcji, ktorej rozmachu nie da
        /// sie zmienic. To ta sama rodzina co "Wszystko rozgrywa sie po zmroku" w bialy dzien,
        /// tylko pietro nizej: klamstwo dotyczy mechaniki, a nie stanu swiata, wiec regula
        /// "fakt w tekscie = warunek twardy" go nie lapie.
        ///
        /// ZRODLO PRAWDY JEST DANOWE, NIE ZASZYTE. Czytamy IncidentDef.pointsScaleable, czyli
        /// deklaracje autora wanilii, oraz minThreatPoints. Zadnego `switch` po defName -
        /// sekcja 15 CLAUDE.md zabrania rozgalezien po nazwie incydentu, bo kazdy nowy klocek
        /// wymagalby wtedy zmiany w C#. Dzieki temu kontrola przezyje rozrost katalogu sama.
        ///
        /// ZASTRZEZENIE, ktore trzeba znac przy czytaniu wyniku: pointsScaleable jest
        /// deklaracja, a nie zachowaniem silnika - w pietnastu sprawdzonych typach potoku
        /// incydentow nie znalazlem ani jednego CZYTELNIKA tego pola. Zgadza sie ono
        /// z niezalezna dekompilacja w 10 przypadkach na 12; rozbiezne sa WandererJoin
        /// i RefugeePodCrash, gdzie IncidentWorker_GiveQuest przekazuje parms.points do
        /// generatora zadania, mimo ze Def deklaruje false. Ta kontrola bedzie je wiec
        /// raportowac jako bezczynne, choc punkty maja tam posredni wplyw na tresc questa.
        /// </summary>
        private static void AuditIntensityEffect()
        {
            List<NarrativeBlockDef> akcje = DefDatabase<NarrativeBlockDef>.AllDefsListForReading
                .Where(b => b.blockType == BlockType.Action && !string.IsNullOrEmpty(b.payload))
                .ToList();

            if (akcje.Count == 0)
            {
                return;
            }

            var bezczynne = new List<string>();
            int skalujace = 0;
            int bramkujace = 0;
            // MIANOWNIK LICZY ZBADANE, nie zadeklarowane. Petla pomija payloady, ktorych
            // DefDatabase nie rozwiazal, wiec uzycie akcje.Count dalo by komunikat "6 z 13",
            // gdy zbadano 12 - a roznica wygladalaby jak wlasciwosc katalogu, nie jak literowka
            // w jednym <payload>.
            int zbadanych = 0;

            foreach (NarrativeBlockDef b in akcje)
            {
                IncidentDef inc = DefDatabase<IncidentDef>.GetNamedSilentFail(b.payload);
                if (inc == null)
                {
                    // Nieistniejacy payload zglasza juz AuditActionPayloads; tu go pomijamy,
                    // zeby nie dublowac tego samego bledu drugim komunikatem.
                    continue;
                }
                zbadanych++;

                bool skaluje = inc.pointsScaleable;
                bool bramkuje = inc.minThreatPoints > 0f;

                if (skaluje)
                {
                    skalujace++;
                }
                if (bramkuje)
                {
                    bramkujace++;
                }
                if (!skaluje && !bramkuje)
                {
                    bezczynne.Add(b.defName + "->" + inc.defName);
                }
            }

            PNLog.Decision("Intensywnosc kompozycji: " + skalujace.ToString(CultureInfo.InvariantCulture)
                           + " akcji DEKLARUJE skalowanie punktami (pointsScaleable), "
                           + bramkujace.ToString(CultureInfo.InvariantCulture)
                           + " ma dodatni prog punktowy.");

            if (bezczynne.Count > 0)
            {
                // JEDNA linia zbiorcza, nie ostrzezenie na klocek. To jest wlasciwosc katalogu,
                // a nie usterka do naprawienia - zalew ostrzezen nauczylby ignorowac ten komunikat.
                PNLog.Decision("Brak deklaracji skalowania punktami i dodatniego progu punktowego dla "
                               + bezczynne.Count.ToString(CultureInfo.InvariantCulture)
                               + " z " + zbadanych.ToString(CultureInfo.InvariantCulture)
                               + " zbadanych akcji: "
                               + string.Join(", ", bezczynne.ToArray())
                               + ". Wplyw intensywnosci na WYKONANIE wymaga tam sprawdzenia kodu "
                               + "incydentu - te dwa pola sa deklaracjami w Defie, a nie dowodem, "
                               + "ze worker nie uzywa parms.points. "
                               + "Niezaleznie od tego: nawet przy braku wplywu na wykonanie klocek "
                               + "modyfikatora NIE jest bez znaczenia - wnosi preferencje do "
                               + "contextFit i wlasna intensywnosc do intentAlignment, wiec zmienia, "
                               + "KTORY kandydat wygra. Ostrozny musi byc jego TEKST, a nie jego "
                               + "obecnosc w katalogu.");
            }
        }

    }
}
