using System.Collections.Generic;
using ProceduralNarrator.Core.Conditions;

namespace ProceduralNarrator.Core.Arcs
{
    /// <summary>
    /// Definicja luku narracyjnego w rdzeniu - automat skonczony z fazami, przejsciami i warunkami
    /// startu (koncepcja 5.4; katalog lukow jako DANE, sekcja 15).
    ///
    /// Pola camelCase maja TE SAME nazwy co wezly XML i pola NarrativeArcDef. To nie wygoda:
    /// walidator offline czyta Defs/Arcs/*.xml prosto do tego typu, a audyt startowy w grze
    /// porownuje refleksja pola obu typow - rozjazd nazw bylby cichym brakiem pola po jednej stronie.
    /// Obiekty faz, oczekiwan i straznikow sa wspoldzielone z Defem (jak warunki klockow) i rdzen
    /// ich NIE mutuje.
    /// </summary>
    public class ArcDefinition
    {
        public string defName;
        public string label;

        /// <summary>Kolejnosc otwierania, gdy jedno zdarzenie pasuje do zasiewu kilku lukow (wiekszy pierwszy).</summary>
        public int priority;

        /// <summary>Luki z jednej grupy wykluczaja sie (najwyzej jeden aktywny). Pusty = bez grupy.</summary>
        public string exclusionGroup;

        /// <summary>Ile dni po zamknieciu ten sam luk nie moze otworzyc sie ponownie.</summary>
        public float cooldownDays = 30f;

        /// <summary>
        /// Luk-nastepnik (splatanie, decyzja autora nr 4). Otwiera sie przy zamknieciu tego luku
        /// jako "rozwiazany" - od razu na swojej drugiej fazie, bo jego zasiewem jest to zamkniecie.
        /// Warunki startu, sloty, grupy i odstep obowiazuja jak przy zwyklym otwarciu.
        /// </summary>
        public string successor;

        /// <summary>Warunki startu na stanie swiata (te same klasy Cond_* co klocki).</summary>
        public List<NarrativeCondition> startConditions = new List<NarrativeCondition>();

        public List<ArcPhase> phases = new List<ArcPhase>();

        /// <summary>Przejscia calego luku, sprawdzane w kazdej fazie PRZED przejsciami fazy. Tylko zamykajace.</summary>
        public List<ArcTransition> transitions = new List<ArcTransition>();

        public string Id
        {
            get { return defName; }
        }

        public ArcPhase Seed
        {
            get { return phases != null && phases.Count > 0 ? phases[0] : null; }
        }

        public int IndexOf(string phaseId)
        {
            if (phases == null || phaseId == null)
            {
                return -1;
            }
            for (int i = 0; i < phases.Count; i++)
            {
                if (phases[i] != null && string.Equals(phases[i].id, phaseId, System.StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        public ArcPhase PhaseById(string phaseId)
        {
            int i = IndexOf(phaseId);
            return i < 0 ? null : phases[i];
        }

        /// <summary>Faza Rozwiazania - zawsze ostatnia (ArcCatalog.Validate).</summary>
        public ArcPhase Resolution
        {
            get { return phases != null && phases.Count > 0 ? phases[phases.Count - 1] : null; }
        }

        /// <summary>Czy ktoras faza luku wiaze frakcje.</summary>
        public bool BindsFaction
        {
            get
            {
                if (phases == null)
                {
                    return false;
                }
                for (int i = 0; i < phases.Count; i++)
                {
                    if (phases[i] != null && phases[i].bindsFaction)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public bool StartConditionsMet(Model.WorldSnapshot snapshot, out string failed)
        {
            failed = null;
            if (startConditions == null || snapshot == null)
            {
                return true;
            }
            for (int i = 0; i < startConditions.Count; i++)
            {
                NarrativeCondition c = startConditions[i];
                if (c != null && !c.IsMet(snapshot))
                {
                    failed = c.Describe();
                    return false;
                }
            }
            return true;
        }

        public override string ToString()
        {
            return defName;
        }
    }
}
