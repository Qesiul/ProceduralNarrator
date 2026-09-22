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
