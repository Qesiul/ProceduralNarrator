using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Conditions
{
    /// <summary>
    /// Predykat na stanie gry (sekcja 7 koncepcji: "warunki - kiedy klocek jest dostepny").
    ///
    /// Dwa zastosowania, celowo rozdzielone:
    ///   TWARDE (lista conditions)  -> IsMet() decyduje, czy klocka W OGOLE wolno uzyc.
    ///                                 Odpowiada za SPOJNOSC: infestacja bez stropu gorskiego
    ///                                 nie ma prawa powstac.
    ///   MIEKKIE (lista preferences) -> Fit() zwraca 0..1 i mowi, JAK BARDZO klocek pasuje
    ///                                 tu i teraz. Odpowiada za KONTEKSTOWOSC i zasila scoring.
    ///
    /// Klasa zyje w Core, wiec operuje wylacznie na WorldSnapshot - zero API gry.
    /// Nowy typ warunku nie wymaga zmian w logice decyzyjnej ani w kompozycji.
    /// </summary>
    public abstract class NarrativeCondition
    {
        /// <summary>Waga tego warunku przy liczeniu dopasowania kontekstowego kandydata.</summary>
        public float weight = 1f;

        /// <summary>Twarda bramka. Domyslnie warunek miekki niczego nie blokuje.</summary>
        public virtual bool IsMet(WorldSnapshot s)
        {
            return true;
        }

        /// <summary>Miekkie dopasowanie 0..1. Domyslnie sprowadza sie do bramki.</summary>
        public virtual float Fit(WorldSnapshot s)
        {
            return IsMet(s) ? 1f : 0f;
        }

        /// <summary>Opis do sladu decyzji w logu.</summary>
        public virtual string Describe()
        {
            return GetType().Name;
        }

        protected static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>Rampa liniowa: from -> 0, to -> 1 (dziala tez malejaco, gdy from > to).</summary>
        protected static float Ramp(float value, float from, float to)
        {
            if (from == to)
            {
                return value >= to ? 1f : 0f;
            }
            return Clamp01((value - from) / (to - from));
        }
    }
}
