using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LudeonTK;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Integration.Defs;
using ProceduralNarrator.Integration.Persistence;
using ProceduralNarrator.Integration.Storyteller;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration.Experiments
{
    /// <summary>
    /// WLASNY SYMULATOR PRZYSZLYCH INCYDENTOW - akcja debugowa "PN: test przyszlych incydentow".
    ///
    /// DLACZEGO NIE WANILIOWY StorytellerUtility.DebugGetFutureIncidents. Przeglad etapu 4
    /// (dekompilacja + uruchomienie na prawdziwych klasach) wykazal, ze wanilia:
    ///   - "przywraca" StoryState, trzymajac REFERENCJE do obiektu, ktory sama czysci - stan
    ///     przepada (lastFireTicks, lastThreatBigTick), wiec FiredTooRecently przestaje chronic
    ///     przed ponownym odpaleniem;
    ///   - nie przywraca obserwatorow map (bogactwo zamrozone na dlugosc symulacji), licznika
    ///     numThreatBigs ani cache'u workerow;
    ///   - nie daje haka na ziarno, wiec trzech profili nie da sie porownac na tym samym
    ///     harmonogramie MTB;
    ///   - przechodzi przez nasz comp, ktory ZAPISUJE decyzje do trwalej pamieci.
    /// Od tej wersji comp odrzuca takie wywolania (straznik zegara), a do testow sluzy ta akcja.
    ///
    /// CO ROBI. Ta sama petla co wanilia - DebugSetTicksGame + Find.Storyteller
    /// .MakeIncidentsForInterval(comp, [cel]) - wiec waniliowe filtry (minDaysPassed,
    /// allowBigThreats, metalowe pieklo) nadal dzialaja. Do tego:
    ///   - zrzut i przywrocenie stanu gry (GameStateCheckpoint) w finally, takze po wyjatku;
    ///   - pamiec narratora PODMIENIONA na glebokie kopie - prawdziwej nic nie dotyka;
    ///   - Rand.PushState(ziarno(tick, cel)) na KAZDY interwal, wiec wszystkie ramiona widza
    ///     identyczny harmonogram bramki MTB, a losowosc nie przecieka miedzy interwalami;
    ///   - ramiona = profile przy IDENTYCZNYM wejsciu; na koncu RAMIE KONTROLNE (powtorzenie
    ///     pierwszego) jako kanarek izolacji: kazda roznica wobec ramienia 1 znaczy, ze jakis
    ///     stan nie zostal przywrocony;
    ///   - dane: wiersze [PN-DATA] w PN_decyzje.log z tryb=symulacja i kolumna eksperyment,
    ///     log czytelny w PN_symulacje.log (nie w Verse.Log - limit 1000 wiadomosci).
    ///
    /// CZEGO NIE GWARANTUJE - swiadomie. Swiat jest ZAMROZONY: nie ma TryExecute ani rozwoju
    /// kolonii, a artefakt dryfu WealthRelative (norma rosnie z dniem, bogactwo stoi) zostaje.
    /// Wynik porownuje POLITYKE na stanie statycznym, nie przebieg rozgrywki - do tego potrzebna
    /// jest prawdziwa gra. Ograniczenia zrzutu - patrz GameStateCheckpoint.
    ///
    /// UWAGA: akcja NIE COFA szkod wyrzadzonych wczesniej przez waniliowe narzedzie
    /// (wyzerowany StoryState, zamrozone obserwatory). Jedyna pelna naprawa po nim to wczytanie
    /// zapisu sprzed jego uzycia.
    /// </summary>
    public static class FutureIncidentsExperiment
    {
        private const int TicksPerInterval = 1000;
        private const int IntervalsPerDay = 60;

        private static bool running;

        [DebugAction("Procedural Narrator", "PN: test przyszlych incydentow", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TestPrzyszlychIncydentow()
        {
            var opcje = new List<DebugMenuOption>
            {
                new DebugMenuOption("biezacy profil + ramie kontrolne, 30 dni", DebugMenuOptionMode.Action,
                                    delegate { Uruchom(30, false); }),
                new DebugMenuOption("biezacy profil + ramie kontrolne, 100 dni", DebugMenuOptionMode.Action,
                                    delegate { Uruchom(100, false); }),
                new DebugMenuOption("WSZYSTKIE profile + ramie kontrolne, 100 dni", DebugMenuOptionMode.Action,
                                    delegate { Uruchom(100, true); })
            };
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opcje));
        }

        /// <summary>Wynik jednego ramienia - do porownania z ramieniem kontrolnym i do raportu.</summary>
        private sealed class Ramie
        {
            public string Etykieta;
            public string Profil;
            public readonly List<string> Wypalone = new List<string>();
            public readonly Dictionary<string, int> PoIncydencie = new Dictionary<string, int>();
            public int Decyzji;
            public int Interwalow;
            public string OdciskPamieci = string.Empty;
            public bool Kompletne;

            /// <summary>
            /// Ramie "bez lukow" (krok 5): ten sam profil co ramie 1, warstwa lukow nieobecna.
            /// Luk nie zmienia decyzji "dzialac czy milczec" w turze, ale zmienia historie, a przez
            /// nia gestosc, napiecie i intencje kolejnych tur - roznica ramion 1 i B mierzy ten
            /// efekt DRUGIEGO RZEDU (tempo, udzial PASS).
            /// </summary>
            public bool BezLukow;
        }

        private static void Uruchom(int dni, bool wszystkieProfile)
        {
            if (running)
            {
                PNLog.Error("Eksperyment juz trwa - wywolanie pominiete.");
                return;
            }
            if (Current.Game == null || Find.Storyteller == null)
            {
                PNLog.Error("Brak gry albo narratora - eksperyment niemozliwy.");
                return;
            }

            StorytellerComp_Generative comp = Find.Storyteller.storytellerComps
                                                  .OfType<StorytellerComp_Generative>().FirstOrDefault();
            if (comp == null)
            {
                PNLog.Error("Biezacy narrator nie zawiera StorytellerComp_Generative - wybierz 'Ariadne "
                            + "Generative'. Eksperyment dotyczy wylacznie naszego toru.");
                return;
            }

            NarratorMemoryComponent pamiec = Current.Game.GetComponent<NarratorMemoryComponent>();
            if (pamiec == null)
            {
                PNLog.Error("Brak NarratorMemoryComponent - eksperyment niemozliwy (nie ma czego podmienic).");
                return;
            }

            // Ramiona: profil(e) + powtorzenie pierwszego jako kontrola izolacji.
            var profile = new List<string>();
            if (wszystkieProfile)
            {
                List<NarratorProfileDef> defs = NarratorProfileCatalog.AllDefs();
                for (int i = 0; defs != null && i < defs.Count; i++)
                {
                    profile.Add(defs[i].defName);
                }
            }
            if (profile.Count == 0)
            {
                profile.Add(pamiec.ProfileId);
            }

            // Kopia listy celow: AllIncidentTargets to lista statyczna, czyszczona przy kazdym
            // odczycie (Storyteller.cs) - trzymanie referencji pod petla byloby zakladem.
            var cele = new List<IIncidentTarget>(Find.Storyteller.AllIncidentTargets);

            // Kanarek pamieci gracza (S6): odcisk PRZED eksperymentem porownywany z odciskiem po
            // przywroceniu. Z konstrukcji (ramiona dostaja kopie, oryginal wraca referencja) to prawie
            // tautologia - ma zapalic sie dopiero, gdy ktos kiedys zmieni te konstrukcje.
            string odciskGryPrzed = OdciskPamieci(pamiec);
            // Bezpieczniki warstw sa polami compa, ktorego uzywa i symulator, i gra (przeglad S6).
            StorytellerComp_Generative.Bezpieczniki bezpiecznikiGry = comp.OdczytajBezpieczniki();
            var wylaczeniaWarstw = new List<string>();

            string problem;
            GameStateCheckpoint cp = GameStateCheckpoint.Capture(comp, pamiec, cele, out problem);
            if (cp == null)
            {
                PNLog.Error("Eksperyment NIE wystartowal (stan gry nietkniety): " + problem);
                return;
            }

            running = true;
            int t0 = cp.Ticks0;
            string runId = pamiec.RunId ?? string.Empty;
            int ziarnoBazowe = Gen.HashCombineInt(GenText.StableStringHash(runId), t0);
            // Id UNIKALNE: dwa uruchomienia przy tym samym ticku (pauza) dawalyby inaczej te sama
            // kolumne "eksperyment" i identyczne wiersze. Guid nie rusza Verse.Rand, a ziarno
            // - od ktorego zalezy odtwarzalnosc - zostaje bez zmian.
            string idEksperymentu = "exp" + t0.ToString(CultureInfo.InvariantCulture) + "-"
                                    + Guid.NewGuid().ToString("N").Substring(0, 6);

            // Pierwszy tick: NASTEPNA wielokrotnosc 1000 - ta sama siatka, na ktorej wola compy
            // prawdziwy Storyteller.StorytellerTick. Tick biezacy zostaje prawdziwej turze.
            int pierwszyTick = (t0 / TicksPerInterval + 1) * TicksPerInterval;
            int interwalow = dni * IntervalsPerDay;

            var ramiona = new List<Ramie>();
            for (int i = 0; i < profile.Count; i++)
            {
                ramiona.Add(new Ramie { Etykieta = (i + 1).ToString(CultureInfo.InvariantCulture) + "-" + profile[i],
                                        Profil = profile[i] });
            }
            // Ramie "bez lukow" PRZED kontrolnym: kanarek izolacji porownuje ramie 1 z OSTATNIM.
            ramiona.Add(new Ramie { Etykieta = "B-" + profile[0], Profil = profile[0], BezLukow = true });
            ramiona.Add(new Ramie { Etykieta = "K-" + profile[0], Profil = profile[0] });

            var bledyIzolacji = new List<string>();
            string przerwanie = null;

            PNLog.BeginExperiment(idEksperymentu,
                "runId=" + runId + "; t0=" + t0.ToString(CultureInfo.InvariantCulture)
                + "; pierwszyTick=" + pierwszyTick.ToString(CultureInfo.InvariantCulture)
                + "; dni=" + dni.ToString(CultureInfo.InvariantCulture)
                + "; ziarno=" + ziarnoBazowe.ToString(CultureInfo.InvariantCulture)
                + "; ramiona=" + string.Join(",", ramiona.Select(r => r.Etykieta).ToArray()));

            var jedenCel = new List<IIncidentTarget>(1);
            try
            {
                foreach (Ramie ramie in ramiona)
                {
                    int odrzuconych = cp.ResetForArm(ramie.Profil);
                    if (odrzuconych > 0)
                    {
                        bledyIzolacji.Add(ramie.Etykieta + ": odbudowa pamieci odrzucila "
                                          + odrzuconych.ToString(CultureInfo.InvariantCulture) + " linii");
                    }
                    // Kazde ramie startuje z bezpiecznikami gry - wyjatek w poprzednim ramieniu nie moze
                    // wylaczyc warstwy w nastepnym (inaczej "kontrola=ROZNA" bez widocznej przyczyny).
                    comp.UstawBezpieczniki(bezpiecznikiGry);
                    int decyzjiPrzed = SumaDecyzji(pamiec);
                    comp.ArcsDisabledForArm = ramie.BezLukow;
                    PNLog.BeginArm(ramie.Etykieta, "profil=" + ramie.Profil + (ramie.BezLukow ? "; luki=wylaczone" : string.Empty));

                    for (int j = 0; j < interwalow; j++)
                    {
                        int tick = pierwszyTick + j * TicksPerInterval;
                        Find.TickManager.DebugSetTicksGame(tick);

                        for (int c = 0; c < cele.Count; c++)
                        {
                            IIncidentTarget cel = cele[c];
                            if (cel == null)
                            {
                                continue;
                            }
                            jedenCel.Clear();
                            jedenCel.Add(cel);

                            // Ziarno per (tick, cel): kazde ramie widzi TEN SAM harmonogram bramki MTB,
                            // a losowosc silnika wewnatrz interwalu nie przecieka do nastepnych.
                            Rand.PushState(Gen.HashCombineInt(Gen.HashCombineInt(ziarnoBazowe, tick),
                                                              cel.ConstantRandSeed));
                            try
                            {
                                foreach (FiringIncident fi in Find.Storyteller.MakeIncidentsForInterval(comp, jedenCel))
                                {
                                    if (fi == null || fi.def == null)
                                    {
                                        continue;
                                    }
                                    ramie.Wypalone.Add(tick.ToString(CultureInfo.InvariantCulture) + "@"
                                                       + cel.ConstantRandSeed.ToString(CultureInfo.InvariantCulture)
                                                       + ":" + fi.def.defName + "/"
                                                       + (fi.parms == null ? "?" : fi.parms.points.ToString("0", CultureInfo.InvariantCulture)));
                                    int ile;
                                    ramie.PoIncydencie.TryGetValue(fi.def.defName, out ile);
                                    ramie.PoIncydencie[fi.def.defName] = ile + 1;

                                    // Refire w obrebie symulacji: bez tego FiredTooRecently nie chronilby
                                    // przed ponownym odpaleniem tego samego incydentu. StoryState jest
                                    // przywracany na koncu, wiec prawdziwa gra tego nie zobaczy.
                                    if (fi.parms != null && fi.parms.target != null && fi.parms.target.StoryState != null)
                                    {
                                        fi.parms.target.StoryState.Notify_IncidentFired(fi);
                                    }
                                }
                            }
                            finally
                            {
                                Rand.PopState();
                            }
                        }
                        ramie.Interwalow++;
                    }

                    ramie.Decyzji = SumaDecyzji(pamiec) - decyzjiPrzed;
                    ramie.OdciskPamieci = OdciskPamieci(pamiec);
                    ramie.Kompletne = true;
                    StorytellerComp_Generative.Bezpieczniki poRamieniu = comp.OdczytajBezpieczniki();
                    if (poRamieniu.Luki != bezpiecznikiGry.Luki || poRamieniu.Fakty != bezpiecznikiGry.Fakty)
                    {
                        wylaczeniaWarstw.Add(ramie.Etykieta + ":" + (poRamieniu.Luki != bezpiecznikiGry.Luki ? "luki" : "")
                                             + (poRamieniu.Fakty != bezpiecznikiGry.Fakty ? "fakty" : ""));
                    }
                }
            }
            catch (Exception e)
            {
                przerwanie = e.GetType().Name + ": " + e.Message;
                PNLog.Error("Eksperyment PRZERWANY wyjatkiem - stan gry zostanie przywrocony.\n" + e);
            }
            finally
            {
                comp.ArcsDisabledForArm = false;
                List<string> bledyPrzywracania = cp.RestoreAll();
                comp.UstawBezpieczniki(bezpiecznikiGry);
                running = false;
                string pamiecGry = string.Equals(odciskGryPrzed, OdciskPamieci(pamiec), StringComparison.Ordinal)
                    ? "zgodna" : "ROZNA";

                // Kanarek izolacji: ramie kontrolne musi byc identyczne z ramieniem 1.
                Ramie pierwsze = ramiona[0];
                Ramie kontrolne = ramiona[ramiona.Count - 1];
                bool kontrolaPoliczona = pierwsze.Kompletne && kontrolne.Kompletne;
                bool kontrolaZgodna = kontrolaPoliczona
                                      && pierwsze.Wypalone.SequenceEqual(kontrolne.Wypalone)
                                      && pierwsze.Decyzji == kontrolne.Decyzji
                                      && string.Equals(pierwsze.OdciskPamieci, kontrolne.OdciskPamieci, StringComparison.Ordinal);

                // TRZY stany kontroli, nie dwa: "brak" (ktores ramie przerwane - nie ma czego
                // porownac) nie moze zlac sie z "ROZNA" (przeciek stanu miedzy ramionami).
                string status = przerwanie != null ? "przerwany" : "kompletny";
                PNLog.EndExperiment("status=" + status
                                    + "; kontrola=" + (!kontrolaPoliczona ? "brak" : (kontrolaZgodna ? "zgodna" : "ROZNA"))
                                    + "; bledyPrzywracania=" + bledyPrzywracania.Count.ToString(CultureInfo.InvariantCulture)
                                    + "; bledyIzolacji=" + bledyIzolacji.Count.ToString(CultureInfo.InvariantCulture)
                                    + "; pamiecGry=" + pamiecGry
                                    + "; wylaczeniaWarstw=" + (wylaczeniaWarstw.Count == 0 ? "-" : string.Join(",", wylaczeniaWarstw.ToArray())));

                // Raport do Verse.Log - PO EndExperiment, wiec idzie do zwyklego logu.
                PNLog.Decision(Raport(idEksperymentu, dni, ramiona, kontrolaPoliczona, kontrolaZgodna, przerwanie,
                                      bledyPrzywracania.Count));

                for (int i = 0; i < bledyPrzywracania.Count; i++)
                {
                    PNLog.Error("Eksperyment: nie udalo sie przywrocic stanu - " + bledyPrzywracania[i]
                                + ". NIE ZAPISUJ gry; wczytaj zapis sprzed eksperymentu.");
                }
                for (int i = 0; i < bledyIzolacji.Count; i++)
                {
                    PNLog.Error("Eksperyment: blad izolacji ramienia - " + bledyIzolacji[i]);
                }
                if (wylaczeniaWarstw.Count > 0)
                {
                    PNLog.Error("Eksperyment: wyjatek WYLACZYL warstwe w ramieniu (" + string.Join(", ", wylaczeniaWarstw.ToArray())
                                + ") - przyczyna jest wyzej w logu z przedrostkiem [PN][SYM]. Bezpieczniki gry przywrocono, "
                                + "ale wyniki tego ramienia i kontrola izolacji sa niewiarygodne.");
                }
                if (pamiecGry != "zgodna")
                {
                    PNLog.Error("Eksperyment: pamiec narratora PO przywroceniu rozni sie od pamieci PRZED eksperymentem - "
                                + "przeciek symulacji do prawdziwej gry. NIE ZAPISUJ gry; wczytaj zapis sprzed eksperymentu.");
                }
                if (przerwanie == null && !kontrolaZgodna)
                {
                    PNLog.Error("Eksperyment: RAMIE KONTROLNE rozni sie od ramienia 1 przy identycznym wejsciu - "
                                + "jakis stan gry nie zostal przywrocony miedzy ramionami. Wyniki porownania "
                                + "profili z tego eksperymentu sa NIEWIARYGODNE.");
                }
            }
        }

        private static int SumaDecyzji(NarratorMemoryComponent pamiec)
        {
            int suma = 0;
            foreach (KeyValuePair<int, EventHistory> para in pamiec.All)
            {
                if (para.Value != null)
                {
                    suma += para.Value.DecisionCount;
                }
            }
            return suma;
        }

        /// <summary>
        /// Odcisk koncowej pamieci ramienia: licznik decyzji, seria i PELNA tresc bufora kazdej
        /// mapy - czyli wszystko, co idzie do zapisu gry. Porownanie odciskow ramienia 1
        /// i kontrolnego jest mocniejsze niz porownanie samej listy wypalonych incydentow,
        /// bo obejmuje tez decyzje PASS (licznik) i ksiegowanie serii ciszy.
        /// </summary>
        private static string OdciskPamieci(NarratorMemoryComponent pamiec)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<int, EventHistory> para in pamiec.All.OrderBy(p => p.Key))
            {
                if (para.Value == null)
                {
                    continue;
                }
                sb.Append(para.Key.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(para.Value.DecisionCount.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(para.Value.DeliberateSilenceStreak.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(string.Join("#", para.Value.ToPersistableLines().ToArray())).Append('\n');
            }
            // Ksiegi lukow (krok 5) - stan watkow tez musi byc identyczny w ramieniu 1 i kontrolnym.
            foreach (KeyValuePair<int, ArcLedger> para in pamiec.AllLedgers.OrderBy(p => p.Key))
            {
                if (para.Value == null)
                {
                    continue;
                }
                sb.Append("L").Append(para.Key.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(string.Join("#", para.Value.ToPersistableLines().ToArray())).Append('\n');
            }
            // Ksiegi faktow (krok 6) razem z kolejka - ramie kontrolne musi zostawic w pamieci
            // dokladnie te same slady co ramie 1, inaczej kanarek izolacji jest slepy na fakty.
            foreach (KeyValuePair<int, Core.Blackboard.FactLedger> para in pamiec.AllFacts.OrderBy(p => p.Key))
            {
                if (para.Value == null)
                {
                    continue;
                }
                sb.Append("F").Append(para.Key.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(string.Join("#", para.Value.ToPersistableLines().ToArray())).Append('\n');
            }
            return sb.ToString();
        }

        private static string Raport(string id, int dni, List<Ramie> ramiona, bool kontrolaPoliczona,
                                     bool kontrolaZgodna, string przerwanie, int bledowPrzywracania)
        {
            var sb = new StringBuilder(1024);
            sb.Append("EKSPERYMENT ").Append(id).Append(": ").Append(dni.ToString(CultureInfo.InvariantCulture))
              .Append(" dni, ramion ").Append(ramiona.Count.ToString(CultureInfo.InvariantCulture))
              .Append(przerwanie == null ? string.Empty : " | PRZERWANY: " + przerwanie)
              .Append(" | ramie kontrolne: ")
              .Append(!kontrolaPoliczona
                      ? "NIEPOLICZONE (ramie przerwane)"
                      : (kontrolaZgodna
                         ? "zgodne z ramieniem 1 - nie wykryto przecieku stanu wplywajacego na decyzje narratora"
                         : "ROZNE - wyniki niewiarygodne"));
            foreach (Ramie r in ramiona)
            {
                sb.AppendLine();
                sb.Append("  ").Append(r.Etykieta).Append(": decyzji=").Append(r.Decyzji.ToString(CultureInfo.InvariantCulture))
                  .Append(" zdarzen=").Append(r.Wypalone.Count.ToString(CultureInfo.InvariantCulture))
                  .Append(" PASS=").Append((r.Decyzji - r.Wypalone.Count).ToString(CultureInfo.InvariantCulture))
                  .Append(" interwalow=").Append(r.Interwalow.ToString(CultureInfo.InvariantCulture))
                  .Append(r.Kompletne ? string.Empty : " (NIEKOMPLETNE)")
                  .Append(r.BezLukow ? " [BEZ LUKOW]" : string.Empty)
                  .Append(" | ");
                sb.Append(string.Join(", ", r.PoIncydencie.OrderByDescending(p => p.Value)
                                              .Select(p => p.Key + " " + p.Value.ToString(CultureInfo.InvariantCulture))
                                              .ToArray()));
            }
            sb.AppendLine();
            sb.Append("  Dane: ").Append(PNLog.DataFilePath).Append(" (tryb=symulacja, eksperyment=").Append(id)
              .Append("/...), log czytelny: ").Append(PNLog.SimFilePath)
              .Append(bledowPrzywracania == 0
                      ? ". Przywrocono wszystkie elementy z listy GameStateCheckpoint (w tym pamiec narratora); "
                        + "stan spoza tej listy (inne mody, przechodnie cache'e wanilii) nie jest weryfikowany."
                      : ". UWAGA: " + bledowPrzywracania.ToString(CultureInfo.InvariantCulture)
                        + " krok(ow) przywracania zawiodlo - NIE zapisuj gry, wczytaj zapis sprzed eksperymentu.");
            return sb.ToString();
        }
    }
}
