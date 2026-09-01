using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

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

        // Ponizsze dwie metody sa CIENKIMI DELEGATAMI do Core/Util/Curves.
        //
        // Dlaczego delegaty, a nie usuniecie: krok 3 dokladal czynniki scoringu, ktore
        // potrzebuja tych samych krzywych, ale nie dziedzicza po NarrativeCondition
        // (te metody sa protected). Alternatywa - prywatna kopia Clamp01 w kazdym czynniku -
        // dala by kilka wersji tej samej funkcji o roznym zachowaniu na wartosciach
        // zdegenerowanych, czyli cichy rozjazd wynikow. Jedno zrodlo, jedno zachowanie.
        //
        // Dlaczego delegaty, a nie zamiana wywolan w warunkach: dziesiatki warunkow
        // twardych i miekkich wolaja Clamp01/Ramp bez kwalifikatora. Delegat zostawia
        // je bez zmian, wiec migracja nie ma prawa niczego popsuc w juz zweryfikowanej
        // w grze warstwie kompozycji.
        //
        // Jedyna zmiana zachowania wobec wersji sprzed migracji: NaN jest teraz zbijany
        // do zera zamiast przechodzic dalej. Dla kazdego skonczonego wejscia wynik jest
        // bit w bit ten sam, a NaN i tak nie mial prawa wystapic w zadnym istniejacym
        // warunku - liczy sie tylko z wartosci ze snapshotu, ktore sa skonczone.

        protected static float Clamp01(float v)
        {
            return Curves.Clamp01(v);
        }

        /// <summary>Rampa liniowa: from -> 0, to -> 1 (dziala tez malejaco, gdy from > to).</summary>
        protected static float Ramp(float value, float from, float to)
        {
            return Curves.Ramp(value, from, to);
        }
    }
}
