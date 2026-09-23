using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Arcs
{
    /// <summary>
    /// Czy zwiazana frakcja moze DZIS poprowadzic zdarzenie danej mocy (np. napad: punkty po mnozniku
    /// intensywnosci, pora dnia, temperatura przybycia). Implementuje Integration; rdzen widzi
    /// tylko odpowiedz. Brak implementacji (testy offline) = "tak".
    /// </summary>
    public interface IFactionUsability
    {
        bool UsableAt(string factionId, IntensityLevel intensity);
    }

    /// <summary>
    /// FOKUS LUKOW NA JEDNA TURE - wyjscie warstwy lukow dla warstwy decyzyjnej (koncepcja 5.4:
    /// "biezaca faza i oczekiwany typ"). Buduje go ArcDirector.BuildFocus z ksiegi mapy i intencji
    /// tury; UtilityScorer.ScoreAll nadaje przez niego kandydatom ArcValue.
    ///
    /// STATUS FAZY:
    ///   aktywna     - faza dojrzala I walencja, na ktora czeka, jest zgodna z intencja (steruje);
    ///   czeka       - dojrzala, ale niezgodna z intencja (krzywa decyduje KIEDY - decyzja autora nr 2);
    ///   niedojrzala - przed uplywem minDaysAfterPrevious.
    ///
    /// WARTOSC KANDYDATA (decyzja autora R4-2, binarna): 1, gdy wykonanie kandydata przesunelo by
    /// ktoras AKTYWNA faze - ten sam predykat co rozpoznanie, plus zgodnosc walencji KANDYDATA
    /// z intencja; inaczej 0. Wartosc = maksimum po lukach, wiec dwa luki czekajace na to samo
    /// zdarzenie nie daja premii podwojnej. Gdy zaden niezawetowany kandydat nie pasuje, luk sie
    /// WSTRZYMUJE: wszyscy dostaja -1 (pusta kolumna) - tura jest wtedy dokladnie tura v6.
    /// </summary>
    public sealed class ArcFocus
    {
        public const string StatusActive = "aktywna";
        public const string StatusWaiting = "czeka";
        public const string StatusUnripe = "niedojrzala";

        public sealed class Entry
        {
            public ArcInstance Arc;
            public ArcDefinition Def;
            public ArcPhase Phase;
            public bool Ripe;
            public bool Active;
            public string Status;
        }

        private readonly List<Entry> entries;
        private readonly IFactionUsability usability;
        private readonly Dictionary<string, string> bindings = new Dictionary<string, string>(StringComparer.Ordinal);

        public ArcFocus(Intent intent, List<Entry> entries, IFactionUsability usability)
        {
            Intent = intent;
            this.entries = entries ?? new List<Entry>();
            this.usability = usability;
        }

        public Intent Intent { get; private set; }

        public IReadOnlyList<Entry> Entries
        {
            get { return entries; }
        }

        /// <summary>Czy ktoras faza steruje w tej turze.</summary>
        public bool HasActivePhase
        {
            get { return entries.Any(e => e.Active); }
        }

        /// <summary>Po Apply: czy luk w tej turze w ogole cos zmienia (jest aktywna faza i dopasowany kandydat).</summary>
        public bool Applied { get; private set; }

        /// <summary>Po Apply: ilu niezawetowanych kandydatow dostalo wartosc 1.</summary>
        public int Matched { get; private set; }

        /// <summary>
        /// Wartosc kandydata: 1 albo 0, ze sladem. Zapamietuje, ktoremu kandydatowi nalezy ustawic
        /// frakcje (dopasowanie przez oczekiwanie sameFaction przy frakcji uzytecznej dla jego mocy).
        /// </summary>
        public float ValueFor(ComposedEvent e, out string why)
        {
            why = null;
            if (e == null)
            {
                why = "brak kandydata";
                return 0f;
            }
            if (!ArcIntentRules.Compatible(e.Valence, Intent))
            {
                why = "walencja " + e.Valence + " niezgodna z intencja " + Intent;
                return 0f;
            }

            ArcEventView widok = ArcEventView.FromCandidate(e, null);
            float wartosc = 0f;
            string frakcja = null;
            var trafienia = new List<string>();
            foreach (Entry en in entries)
            {
                if (!en.Active)
                {
                    continue;
                }
                string zwiazana = en.Arc.BoundFactionId;
                foreach (ArcExpectation x in en.Phase.expectations)
                {
                    if (x == null)
                    {
                        continue;
                    }
                    // Frakcja kandydata = ta, ktora zostanie mu USTAWIONA - tylko przy oczekiwaniu
                    // sameFaction i frakcji uzytecznej dla jego mocy. Przeszkoda chwilowa (punkty,
                    // temperatura) daje po prostu brak dopasowania, a nie wiazanie na sile.
                    bool wiaz = x.sameFaction && widok.CarriesFaction && !string.IsNullOrEmpty(zwiazana)
                                && (usability == null || usability.UsableAt(zwiazana, e.Intensity));
                    widok.FactionId = wiaz ? zwiazana : null;
                    string w;
                    if (x.Matches(widok, zwiazana, out w))
                    {
                        wartosc = 1f;
                        trafienia.Add(en.Arc.ArcId + ":" + en.Phase.id);
                        if (wiaz)
                        {
                            frakcja = zwiazana;
                        }
                        break;
                    }
                }
            }
            if (frakcja != null && e.Signature != null)
            {
                bindings[e.Signature] = frakcja;
            }
            why = wartosc > 0f ? "pasuje: " + string.Join(",", trafienia.ToArray()) : "nie pasuje do zadnej aktywnej fazy";
            return wartosc;
        }

        /// <summary>
        /// Nadaje ArcValue ocenionym kandydatom tury (wola UtilityScorer.ScoreAll PO wecie).
        /// Zawetowany dostaje -1: nie wchodzi do wyboru, wiec wartosc bylaby pomiarem bez znaczenia.
        /// Wstrzymanie: jesli nic nie pasuje albo zadna faza nie steruje, wszyscy maja -1.
        /// </summary>
        public void Apply(IList<ScoredCandidate> scored)
        {
            bindings.Clear();
            Applied = false;
            Matched = 0;
            if (scored == null)
            {
                return;
            }
            foreach (ScoredCandidate k in scored)
            {
                if (k != null)
                {
                    k.ArcValue = -1f;
                    k.ArcTrace = null;
                }
            }
            if (!HasActivePhase)
            {
                return;
            }
            foreach (ScoredCandidate k in scored)
            {
                if (k == null || k.IsPass || k.Vetoed || k.Event == null)
                {
                    continue;
                }
                string why;
                k.ArcValue = ValueFor(k.Event, out why);
                k.ArcTrace = why;
                if (k.ArcValue > 0f)
                {
                    Matched++;
                }
            }
            if (Matched == 0)
            {
                foreach (ScoredCandidate k in scored)
                {
                    if (k != null)
                    {
                        k.ArcValue = -1f;
                    }
                }
                bindings.Clear();
                return;
            }
            Applied = true;
        }

        /// <summary>Frakcja do ustawienia w IncidentParms tego kandydata albo null.</summary>
        public string FactionToBind(ComposedEvent e)
        {
            string f;
            return e != null && e.Signature != null && bindings.TryGetValue(e.Signature, out f) ? f : null;
        }

        /// <summary>Kolumna lukFazy: "luk:faza:status,..." (bez ';' i '=' - bezpieczne dla parsera).</summary>
        public string DataPhases()
        {
            if (entries.Count == 0)
            {
                return string.Empty;
            }
            var sb = new StringBuilder();
            foreach (Entry en in entries.OrderBy(x => x.Arc.Number))
            {
                if (sb.Length > 0)
                {
                    sb.Append(',');
                }
                sb.Append(en.Arc.ArcId).Append(':').Append(en.Phase.id).Append(':').Append(en.Status);
            }
            return sb.ToString();
        }

        /// <summary>Kolumna frakcjaLuku: frakcja zwiazana z aktywnym lukiem albo pusto (najwyzej jedna - walidacja katalogu).</summary>
        public string BoundFaction()
        {
            Entry z = entries.FirstOrDefault(x => !string.IsNullOrEmpty(x.Arc.BoundFactionId));
            return z == null ? string.Empty : z.Arc.BoundFactionId;
        }
    }
}
