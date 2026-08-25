using System.Collections.Generic;

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
        public HashSet<string> Tags = new HashSet<string>();

        /// <summary>Ladunek dla warstwy integracji (dla klocka akcji: defName incydentu).</summary>
        public string Payload;

        /// <summary>Wklad klocka w intensywnosc zlozonego wydarzenia (mnoznik).</summary>
        public float IntensityFactor = 1f;

        /// <summary>Fragment opisu narracyjnego wnoszony przez ten klocek.</summary>
        public string TextFragment;

        public bool HasTag(string tag)
        {
            return tag == null || Tags.Contains(tag);
        }

        public override string ToString()
        {
            return Type + ":" + Id;
        }
    }
}
