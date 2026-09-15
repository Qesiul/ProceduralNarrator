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

            PNLog.Decision("Powiazania klockow akcji: " + sprawnych.ToString(CultureInfo.InvariantCulture)
                           + " z " + akcje.Count.ToString(CultureInfo.InvariantCulture)
                           + " sprawnych (payload istnieje, kategoria w naszym torze, cel dopuszcza mape kolonii).");
        }

    }
}
