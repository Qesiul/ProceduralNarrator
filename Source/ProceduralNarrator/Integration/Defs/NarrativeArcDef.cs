using System.Collections.Generic;
using System.Globalization;
using ProceduralNarrator.Core.Arcs;
using ProceduralNarrator.Core.Conditions;
using Verse;

namespace ProceduralNarrator.Integration.Defs
{
    /// <summary>
    /// Deklaratywna definicja LUKU narracyjnego (krok 5, koncepcja 5.4) - warstwa DANYCH.
    /// Nowy luk to nowy Def w Defs/Arcs, bez linijki kodu decyzyjnego.
    ///
    /// UWAGA: wezel XML musi uzywac PELNEJ nazwy typu z namespace'em, czyli
    /// &lt;ProceduralNarrator.Integration.Defs.NarrativeArcDef&gt;. Sama nazwa klasy jest po cichu
    /// ignorowana przez DirectXmlLoader - ta sama pulapka co przy klockach i profilach.
    ///
    /// Pola maja TE SAME nazwy co ArcDefinition (typ rdzenia) - walidator czyta XML prosto do
    /// ArcDefinition, a PNStartup.AuditArcs porownuje oba typy refleksja. Fazy, oczekiwania
    /// i straznicy to typy z Core, deserializowane bezposrednio (jak warunki klockow).
    /// </summary>
    public class NarrativeArcDef : Def
    {
        public int priority;
        public string exclusionGroup;
        public float cooldownDays = 30f;
        public string successor;
        public List<NarrativeCondition> startConditions = new List<NarrativeCondition>();
        public List<ArcPhase> phases = new List<ArcPhase>();
        public List<ArcTransition> transitions = new List<ArcTransition>();

        /// <summary>
        /// Przepisuje Def na typ rdzenia. Obiekty faz sa wspoldzielone (rdzen ich nie mutuje),
        /// listy - kopiowane, zeby dopisanie do listy po stronie rdzenia nie zmienialo Defa.
        /// </summary>
        public ArcDefinition ToDefinition()
        {
            return new ArcDefinition
            {
                defName = defName,
                label = string.IsNullOrEmpty(label) ? defName : label,
                priority = priority,
                exclusionGroup = exclusionGroup,
                cooldownDays = cooldownDays,
                successor = successor,
                startConditions = startConditions == null ? new List<NarrativeCondition>() : new List<NarrativeCondition>(startConditions),
                phases = phases == null ? new List<ArcPhase>() : new List<ArcPhase>(phases),
                transitions = transitions == null ? new List<ArcTransition>() : new List<ArcTransition>(transitions)
            };
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors())
            {
                yield return e;
            }
            if (phases == null || phases.Count < 2)
            {
                yield return "luk ma mniej niz 2 fazy (" + (phases == null ? 0 : phases.Count).ToString(CultureInfo.InvariantCulture)
                             + ") - pelna walidacja w PNStartup.AuditArcs";
            }
        }
    }

    /// <summary>Adapter danych: NarrativeArcDef (typ gry) -> ArcCatalog (typ rdzenia, po walidacji).</summary>
    public static class ArcCatalogLoader
    {
        public static ArcCatalog Load(out List<string> problems)
        {
            var defs = new List<ArcDefinition>();
            foreach (NarrativeArcDef d in DefDatabase<NarrativeArcDef>.AllDefsListForReading)
            {
                defs.Add(d.ToDefinition());
            }
            return ArcCatalog.Build(defs, out problems);
        }
    }
}
