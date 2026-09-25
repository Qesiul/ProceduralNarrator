using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LudeonTK;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Integration.Defs;
using Verse;

namespace ProceduralNarrator.Integration.Persistence
{
    /// <summary>
    /// Akcje debugowe do pamieci narratora. Widoczne w trybie deweloperskim pod zakladka
    /// Actions, w kategorii "Procedural Narrator" (filtr okna debugowego: wpisac "PN:").
    ///
    /// UWAGA NA NAMESPACE: DebugActionAttribute i AllowedGameStates mieszkaja w 1.5
    /// w LudeonTK, a NIE w Verse - Ludeon przeniosl narzedzia debugowe do osobnego
    /// namespace'u. Zly using nie daje bledu kompilacji tylko wtedy, gdy przypadkiem istnieje
    /// inny pasujacy typ; przy braku atrybutu akcje po prostu NIE POJAWIAJA SIE w menu,
    /// bez zadnego komunikatu. To jest cicha awaria tej samej rodziny co nazwa typu Defa bez
    /// namespace'u opisana w CLAUDE.md.
    ///
    /// Metody musza byc STATYCZNE i bezargumentowe - DebugActionType.Action jest domyslny.
    /// </summary>
    public static class PNDebugActions
    {
        private static NarratorMemoryComponent Pamiec
        {
            get
            {
                return Current.Game == null ? null : Current.Game.GetComponent<NarratorMemoryComponent>();
            }
        }

        /// <summary>
        /// Wypisuje pelny stan pamieci narratora. Sluzy do weryfikacji cyklu save/load bez
        /// czekania na kolejna decyzje: po wczytaniu widac od razu, czy pamiec przetrwala,
        /// zamiast zgadywac z nastepnego wiersza [PN-DATA] za maksymalnie 1000 tickow.
        /// </summary>
        [DebugAction("Procedural Narrator", "PN: stan pamieci", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StanPamieci()
        {
            NarratorMemoryComponent pamiec = Pamiec;
            if (pamiec == null)
            {
                PNLog.Error("Brak NarratorMemoryComponent w biezacej grze - pamiec narratora NIE JEST trwala. "
                            + "Sprawdz w Player.log, czy nie ma wpisu 'Could not instantiate a GameComponent'.");
                return;
            }

            var sb = new StringBuilder(512);
            sb.Append("STAN PAMIECI NARRATORA | runId=").Append(pamiec.RunId)
              .Append(" | profil=").Append(string.IsNullOrEmpty(pamiec.ProfileId) ? "(brak)" : pamiec.ProfileId)
              .Append(string.Equals(pamiec.ProfileId, pamiec.EffectiveProfileId, System.StringComparison.Ordinal)
                          ? string.Empty
                          : " (FAKTYCZNIE: " + pamiec.EffectiveProfileId + ")")
              .Append(" | map z wlasna pamiecia: ")
              .Append(pamiec.MapCount.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.Append("  ").Append(pamiec.ActiveProfile().ToString());

            int biezaca = Find.CurrentMap != null ? Find.CurrentMap.uniqueID : -1;

            foreach (KeyValuePair<int, EventHistory> para in pamiec.All)
            {
                sb.AppendLine();
                sb.Append("  mapa ").Append(para.Key.ToString(CultureInfo.InvariantCulture));
                if (para.Key == biezaca)
                {
                    sb.Append(" (biezaca)");
                }
                sb.Append(": ").Append(para.Value == null ? "BRAK OBIEKTU" : para.Value.Summary());
            }

            if (pamiec.MapCount == 0)
            {
                sb.AppendLine();
                sb.Append("  (pusto - narrator nie podjal jeszcze zadnej decyzji na zadnej mapie)");
            }

            PNLog.Decision(sb.ToString());
        }

        /// <summary>
        /// Kasuje cala pamiec narratora.
        ///
        /// Pierwotnie sluzyla do odkrecania skutkow waniliowego symulatora
        /// DebugLogTestFutureIncidents, ktory przechodzil przez nasz kod decyzyjny i dopisywal do
        /// trwalej pamieci kilkadziesiat FIKCYJNYCH decyzji. Od polerowania etapu 4 comp odrzuca
        /// wywolania spoza zegara gry (straznik zegara w StorytellerComp_Generative), a do testow
        /// sluzy "PN: test przyszlych incydentow" ze zrzutem i przywroceniem stanu. Akcja zostaje
        /// dla zapisow skazonych PRZED ta zmiana - i tylko pamieci: stanu gry popsutego przez
        /// waniliowe narzedzie (wyzerowany StoryState, zamrozone obserwatory) nie naprawia.
        /// </summary>
        /// <summary>
        /// Stan lukow narracyjnych (krok 5) per mapa: otwarte instancje z faza, dniem wejscia,
        /// zwiazana frakcja i bazami strazniekow, dni ostatnich zamkniec, oczekujace wykonanie
        /// i licznik strat. Do sprawdzenia cyklu zapis -> wczytanie bez czekania na przejscie.
        /// </summary>
        [DebugAction("Procedural Narrator", "PN: stan lukow", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StanLukow()
        {
            NarratorMemoryComponent pamiec = Pamiec;
            if (pamiec == null)
            {
                PNLog.Error("Brak NarratorMemoryComponent - luki nie maja gdzie zyc.");
                return;
            }
            var sb = new StringBuilder(512);
            sb.Append("STAN LUKOW NARRACYJNYCH | runId=").Append(pamiec.RunId);
            int map = 0;
            foreach (KeyValuePair<int, Core.Arcs.ArcLedger> para in pamiec.AllLedgers)
            {
                map++;
                Core.Arcs.ArcLedger l = para.Value;
                sb.AppendLine();
                sb.Append("  mapa ").Append(para.Key.ToString(CultureInfo.InvariantCulture)).Append(": ");
                if (l == null)
                {
                    sb.Append("BRAK OBIEKTU");
                    continue;
                }
                sb.Append("aktywnych=").Append(l.Active.Count.ToString(CultureInfo.InvariantCulture))
                  .Append(" nastepnyNr=").Append(l.NextNumber.ToString(CultureInfo.InvariantCulture))
                  .Append(" stratKolonistow=").Append(l.ColonistLosses.ToString(CultureInfo.InvariantCulture))
                  .Append(" oczekujace=").Append(l.Pending == null ? "-" : l.Pending.IncidentDefName + "@" + l.Pending.Tick.ToString(CultureInfo.InvariantCulture));
                foreach (Core.Arcs.ArcInstance a in l.Active)
                {
                    sb.AppendLine();
                    sb.Append("    ").Append(a.ToString())
                      .Append(" wejscie=").Append(a.PhaseEnteredDay.ToString("0.00", CultureInfo.InvariantCulture))
                      .Append(" otwarcie=").Append(a.OpenedDay.ToString("0.00", CultureInfo.InvariantCulture))
                      .Append(" bazaStrat=").Append(a.BaseColonistLosses.ToString(CultureInfo.InvariantCulture))
                      .Append(" bazaPorwanych=").Append(a.BaseKidnapped.ToString(CultureInfo.InvariantCulture))
                      .Append(" szczytZagrozenia=").Append(a.PeakDanger.ToString());
                }
                foreach (KeyValuePair<string, float> z in l.LastCloseDay)
                {
                    sb.AppendLine();
                    sb.Append("    zamkniety ").Append(z.Key).Append(" w dniu ").Append(z.Value.ToString("0.00", CultureInfo.InvariantCulture));
                }
                // Slad po zamknietych watkach (krok 6): wynik i faza koncowa - model Thread z pracy.
                foreach (Core.Arcs.ArcClosure z in l.Closed)
                {
                    sb.AppendLine();
                    sb.Append("    watek ").Append(z.ArcId).Append('#').Append(z.Number.ToString(CultureInfo.InvariantCulture))
                      .Append(" zamkniety jako ").Append(z.Outcome).Append(" na fazie ").Append(z.FinalPhaseId)
                      .Append(" w dniu ").Append(z.Day.ToString("0.00", CultureInfo.InvariantCulture));
                }
            }
            if (map == 0)
            {
                sb.AppendLine();
                sb.Append("  (pusto - zaden luk jeszcze nie ruszyl)");
            }
            PNLog.Decision(sb.ToString());
        }

        /// <summary>
        /// Stan ksiegi faktow (krok 6) na kazdej mapie: wszystkie fakty z dniem ustawienia, czasem
        /// zycia i informacja, czy dzis obowiazuja, plus kolejka faktow czekajacych na rozstrzygniecie.
        /// Do sprawdzenia cyklu zapis -> wczytanie i tego, czy konsekwencje w ogole cos zapisuja.
        /// </summary>
        [DebugAction("Procedural Narrator", "PN: stan faktow", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StanFaktow()
        {
            NarratorMemoryComponent pamiec = Pamiec;
            if (pamiec == null)
            {
                PNLog.Error("Brak NarratorMemoryComponent - fakty nie maja gdzie zyc.");
                return;
            }
            float dzis = Find.TickManager == null ? 0f : Find.TickManager.TicksGame / 60000f;
            var sb = new StringBuilder(512);
            sb.Append("STAN FAKTOW BLACKBOARDU | runId=").Append(pamiec.RunId)
              .Append(" | dzien ").Append(dzis.ToString("0.00", CultureInfo.InvariantCulture));
            int map = 0;
            foreach (KeyValuePair<int, Core.Blackboard.FactLedger> para in pamiec.AllFacts)
            {
                map++;
                Core.Blackboard.FactLedger f = para.Value;
                sb.AppendLine();
                sb.Append("  mapa ").Append(para.Key.ToString(CultureInfo.InvariantCulture)).Append(": ");
                if (f == null)
                {
                    sb.Append("BRAK OBIEKTU");
                    continue;
                }
                sb.Append("faktow=").Append(f.Count.ToString(CultureInfo.InvariantCulture))
                  .Append(" obowiazujacych=").Append(f.Active(dzis).Count.ToString(CultureInfo.InvariantCulture))
                  .Append(" kolejka=").Append(f.Pending == null ? "-"
                      : f.Pending.IncidentDefName + "@" + f.Pending.Tick.ToString(CultureInfo.InvariantCulture)
                        + (f.Pending.Confirmed ? " (potwierdzona)" : " (czeka)"));
                foreach (Core.Blackboard.Fact fakt in f.AllSorted())
                {
                    sb.AppendLine();
                    sb.Append("    ").Append(fakt.Key).Append('=').Append(fakt.Value.ToString("0.##", CultureInfo.InvariantCulture))
                      .Append(" od dnia ").Append(fakt.SetDay.ToString("0.00", CultureInfo.InvariantCulture))
                      .Append(fakt.LifespanDays > 0f
                          ? ", zycie " + fakt.LifespanDays.ToString("0.##", CultureInfo.InvariantCulture) + " d"
                          : ", bez wygasania")
                      .Append(fakt.IsActive(dzis) ? " [OBOWIAZUJE]" : " [wygasl]");
                }
            }
            if (map == 0)
            {
                sb.AppendLine();
                sb.Append("  (pusto - zadne zdarzenie z konsekwencja jeszcze sie nie wykonalo)");
            }
            PNLog.Decision(sb.ToString());
        }

        /// <summary>
        /// Stan STYLU GRACZA (krok 7). Styl jest ukryty przed graczem (decyzja autora nr 16), wiec to
        /// jedyny podglad poza logami: cechy (Z), profil wzgledny (c), mocne strony, etykieta i druga
        /// z marginesem, kierunek d dla trzech rytmow przy biezacym profilu, pomiary z okna, dzien w toku
        /// i stan obserwacji (otwarte epizody, bazy rekordow, oferty i dzicy ludzie w toku). Do sprawdzenia
        /// zapisu-wczytania w srodku doby: dzien w toku, epizod i bazy maja przetrwac.
        /// </summary>
        [DebugAction("Procedural Narrator", "PN: styl gracza", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StylGracza()
        {
            NarratorMemoryComponent pamiec = Pamiec;
            if (pamiec == null)
            {
                PNLog.Error("Brak NarratorMemoryComponent - styl gracza nie ma gdzie zyc.");
                return;
            }
            Core.PlayerModel.PlayerStyleParams p = pamiec.StyleParams;
            Core.PlayerModel.PlayerStyleLedger l = pamiec.Style;
            Core.PlayerModel.StyleReading r = Core.PlayerModel.PlayerStyleModel.Evaluate(l, p);
            float o = pamiec.ActiveProfile().StyleOrientation;

            var sb = new StringBuilder(1400);
            sb.Append("STYL GRACZA | runId=").Append(pamiec.RunId)
              .Append(" | ").Append(p.enabled ? "wlaczony" : "WYLACZONY w XML")
              .Append(pamiec.StyleBroken ? " | BEZPIECZNIK SPALONY (obserwator rzucil wyjatek - patrz log)" : string.Empty)
              .Append(" | zainicjowany=").Append(l.Initialized ? "tak" : "nie")
              .Append(" | dzien w toku ").Append(l.CurrentDay.ToString(CultureInfo.InvariantCulture))
              .Append(" | kolejka ").Append(l.Days.Count.ToString(CultureInfo.InvariantCulture))
              .Append('/').Append(p.capacityDays.ToString(CultureInfo.InvariantCulture)).Append(" dni");
            if (l.Days.Count > 0)
            {
                sb.Append(" (").Append(l.Days[0].Day.ToString(CultureInfo.InvariantCulture)).Append("..")
                  .Append(l.Days[l.Days.Count - 1].Day.ToString(CultureInfo.InvariantCulture)).Append(')');
            }
            sb.Append(" | ").Append(r.Active
                ? "AKTYWNY"
                : "rozgrzewka: jeszcze " + (p.warmupDays - r.Days).ToString(CultureInfo.InvariantCulture) + " dni");

            for (int d = 0; d < Core.PlayerModel.StyleDimensions.Count; d++)
            {
                sb.AppendLine();
                sb.Append("  ").Append(Core.PlayerModel.StyleDimensions.Name((Core.PlayerModel.StyleDimension)d)).Append(": ");
                if (!r.Known[d])
                {
                    sb.Append("nieznana").Append(float.IsNaN(r.Z[d])
                        ? string.Empty
                        : " (Z z okna " + r.Z[d].ToString("0.000", CultureInfo.InvariantCulture) + ", styl nieaktywny)");
                    continue;
                }
                sb.Append("Z=").Append(r.Z[d].ToString("0.000", CultureInfo.InvariantCulture))
                  .Append(" c=").Append(r.C[d].ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture))
                  .Append(r.Strong[d] ? "  MOCNA STRONA" : string.Empty);
            }
            sb.AppendLine();
            sb.Append("  etykieta: ").Append(r.Active && !string.IsNullOrEmpty(r.Label) ? r.Label : "-");
            if (r.Active && !string.IsNullOrEmpty(r.SecondLabel))
            {
                sb.Append(" (druga: ").Append(r.SecondLabel).Append(", margines ")
                  .Append(r.Margin.ToString("0.000", CultureInfo.InvariantCulture)).Append(')');
            }
            sb.Append(" | mocne: ").Append(r.Active ? r.StrongData() : "-");
            sb.AppendLine();
            sb.Append("  kierunek d (orientacja profilu ").Append(o.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)).Append("): oddech ")
              .Append(Core.PlayerModel.StyleDirection.Compute(o, Intent.Breathe, p).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture))
              .Append(", utrzymanie ")
              .Append(Core.PlayerModel.StyleDirection.Compute(o, Intent.Hold, p).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture))
              .Append(", eskalacja ")
              .Append(Core.PlayerModel.StyleDirection.Compute(o, Intent.Escalate, p).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture))
              .Append(" (+ = gra na mocne strony, - = na slabe)");

            sb.AppendLine();
            sb.Append("  pomiary z okna:");
            for (int i = 0; i < Core.PlayerModel.StyleSignals.Count; i++)
            {
                sb.Append(' ').Append(Core.PlayerModel.StyleSignals.Name((Core.PlayerModel.StyleSignal)i)).Append('=')
                  .Append(r.SignalKnown[i]
                      ? r.SignalX[i].ToString("0.###", CultureInfo.InvariantCulture) + "/z" + r.SignalZ[i].ToString("0.00", CultureInfo.InvariantCulture)
                      : "?");
            }
            sb.AppendLine();
            sb.Append("  w oknie: epizodow zagrozen ").Append(r.EpisodesInWindow.ToString(CultureInfo.InvariantCulture))
              .Append(", ofert rozstrzygnietych ").Append(r.OffersInWindow.ToString(CultureInfo.InvariantCulture))
              .Append(", schwytanych ").Append(r.CapturesInWindow.ToString(CultureInfo.InvariantCulture));
            if (l.Partial != null)
            {
                sb.AppendLine();
                sb.Append("  dzien w toku (licznik/mianownik):");
                for (int i = 0; i < Core.PlayerModel.StyleSignals.Count; i++)
                {
                    sb.Append(' ').Append(Core.PlayerModel.StyleSignals.Name((Core.PlayerModel.StyleSignal)i)).Append('=')
                      .Append(l.Partial.Num[i].ToString(CultureInfo.InvariantCulture)).Append('/')
                      .Append(l.Partial.Den[i].ToString(CultureInfo.InvariantCulture));
                }
            }
            sb.AppendLine();
            sb.Append("  obserwacja: baz rekordow ").Append(l.Baselines.Count.ToString(CultureInfo.InvariantCulture))
              .Append(", otwartych epizodow ").Append(l.Episodes.Count.ToString(CultureInfo.InvariantCulture));
            foreach (KeyValuePair<int, Core.PlayerModel.ThreatEpisode> e in l.Episodes)
            {
                sb.Append(" [mapa ").Append(e.Key.ToString(CultureInfo.InvariantCulture))
                  .Append(" od ticku ").Append(e.Value.StartTick.ToString(CultureInfo.InvariantCulture))
                  .Append(", probek ").Append(e.Value.ThreatSamples.ToString(CultureInfo.InvariantCulture))
                  .Append(", cichych ").Append(e.Value.QuietSamples.ToString(CultureInfo.InvariantCulture)).Append(']');
            }
            sb.Append(", ofert w toku ").Append(l.PendingOffers.Count.ToString(CultureInfo.InvariantCulture))
              .Append(" (znak wodny ").Append(l.MaxOfferId.ToString(CultureInfo.InvariantCulture)).Append(')')
              .Append(", dzikich ludzi sledzonych ").Append(l.WildMen.Count.ToString(CultureInfo.InvariantCulture));
            foreach (KeyValuePair<int, int> w in l.WildMen)
            {
                sb.Append(' ').Append(w.Key.ToString(CultureInfo.InvariantCulture)).Append("@dzien")
                  .Append(w.Value.ToString(CultureInfo.InvariantCulture));
            }
            PNLog.Decision(sb.ToString());
        }

        /// <summary>
        /// Kasuje ksiege stylu gracza - obserwacja i rozgrzewka od nowa (do sprawdzania rozgrzewki).
        /// Pamieci narratora nie rusza; "PN: skasuj pamiec" z kolei nie rusza stylu.
        /// </summary>
        [DebugAction("Procedural Narrator", "PN: skasuj styl gracza", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SkasujStylGracza()
        {
            NarratorMemoryComponent pamiec = Pamiec;
            if (pamiec == null)
            {
                PNLog.Error("Brak NarratorMemoryComponent w biezacej grze - nie ma czego kasowac.");
                return;
            }
            pamiec.ClearStyle();
        }

        [DebugAction("Procedural Narrator", "PN: skasuj pamiec", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SkasujPamiec()
        {
            NarratorMemoryComponent pamiec = Pamiec;
            if (pamiec == null)
            {
                PNLog.Error("Brak NarratorMemoryComponent w biezacej grze - nie ma czego kasowac.");
                return;
            }

            pamiec.ClearAll();
        }

        /// <summary>
        /// Wymusza profil narratora - narzedzie do SERII KONTROLNYCH.
        ///
        /// Po co w ogole istnieje: od kroku 4 profil jest losowany na starcie rozgrywki
        /// i nieujawniany graczowi, co jest zamierzone, ale wprowadza do ewaluacji zmienna,
        /// ktorej sie nie kontroluje. Ta akcja pozwala przejsc ten sam zapis trzema profilami
        /// i porownac ciagi decyzji przy IDENTYCZNYM stanie swiata oraz identycznej historii -
        /// czyli izolowac osobowosc jako jedyna zmienna. To jest wlasciwa demonstracja wzorca
        /// Strategia, mocniejsza niz N rozgrywek, bo nie tonie w szumie.
        ///
        /// Pamiec zdarzen NIE jest przy tym kasowana - to celowe i konieczne, zeby oba profile
        /// widzialy te sama historie.
        /// </summary>
        [DebugAction("Procedural Narrator", "PN: wymus profil", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void WymusProfil()
        {
            NarratorMemoryComponent pamiec = Pamiec;
            if (pamiec == null)
            {
                PNLog.Error("Brak NarratorMemoryComponent w biezacej grze - nie ma gdzie zapisac profilu.");
                return;
            }

            List<NarratorProfileDef> profile = NarratorProfileCatalog.AllDefs();
            if (profile == null || profile.Count == 0)
            {
                PNLog.Error("Katalog profili narratora jest PUSTY. Sprawdz, czy "
                            + "Defs/Storytellers/Profiles_Core.xml uzywa PELNEJ nazwy typu "
                            + "<ProceduralNarrator.Integration.Defs.NarratorProfileDef> - sama nazwa "
                            + "klasy jest po cichu ignorowana przez DirectXmlLoader.");
                return;
            }

            var opcje = new List<DebugMenuOption>();
            for (int i = 0; i < profile.Count; i++)
            {
                NarratorProfileDef def = profile[i];
                string etykieta = def.defName + "  (" + def.label + ")"
                                  + (def.defName == pamiec.ProfileId ? "  <- biezacy" : string.Empty);
                opcje.Add(new DebugMenuOption(etykieta, DebugMenuOptionMode.Action,
                                              delegate { pamiec.ForceProfile(def.defName); }));
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opcje));
        }
    }
}
