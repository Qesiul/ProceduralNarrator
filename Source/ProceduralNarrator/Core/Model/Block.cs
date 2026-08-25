using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Conditions;

namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Elementarny klocek wydarzenia. Struktura czysto danowa - rdzen nie wie,
    /// czym jest Payload; interpretuje go dopiero warstwa integracji.
    /// </summary>
    public class Block
    {
        public string Id;
        public BlockType Type;

        // ---- osie klasyfikacji (typowane, bo czyta je scoring) ----
        public Theme Theme = Theme.Natural;
        public Valence Valence = Valence.Neutral;
        public EventScale Scale = EventScale.Moderate;

        /// <summary>Wolne tagi na klimat i warianty. Scoring ich NIE uzywa.</summary>
        public HashSet<string> Tags = new HashSet<string>();

        /// <summary>Ladunek dla warstwy integracji (dla klocka akcji: defName incydentu).</summary>
        public string Payload;

        /// <summary>Intencja co do sily zdarzenia. Przeklada sie na punkty w Integration.</summary>
        public IntensityLevel Intensity = IntensityLevel.Normal;

        /// <summary>Fragment opisu narracyjnego wnoszony przez ten klocek.</summary>
        public string TextFragment;

        /// <summary>Bramki spojnosci - klocek jest niedostepny, gdy ktorakolwiek nie przejdzie.</summary>
        public List<NarrativeCondition> Conditions = new List<NarrativeCondition>();

        /// <summary>Preferencje kontekstowe - nie blokuja, zasilaja contextFit.</summary>
        public List<NarrativeCondition> Preferences = new List<NarrativeCondition>();

        public bool HasTag(string tag)
        {
            return tag == null || Tags.Contains(tag);
        }

        /// <summary>Czy wszystkie twarde warunki sa spelnione w danym stanie swiata.</summary>
        public bool IsAvailable(WorldSnapshot snapshot)
        {
            // Brak snapshotu = brak filtrowania. Pozwala testowac sama kompozycje.
            if (snapshot == null)
            {
                return true;
            }
            return Conditions.All(c => c.IsMet(snapshot));
        }

        /// <summary>Pierwszy niespelniony warunek - do sladu decyzji w logu.</summary>
        public string FirstUnmetCondition(WorldSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }
            NarrativeCondition failed = Conditions.FirstOrDefault(c => !c.IsMet(snapshot));
            return failed != null ? failed.Describe() : null;
        }

        public override string ToString()
        {
            return Type + ":" + Id;
        }
    }
}
