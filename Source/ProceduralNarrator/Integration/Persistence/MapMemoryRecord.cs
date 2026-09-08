using System.Collections.Generic;
using Verse;

namespace ProceduralNarrator.Integration.Persistence
{
    /// <summary>
    /// Zapisywalna postac pamieci narratora DLA JEDNEJ MAPY. Czysty pojemnik na dane:
    /// trzy pola, zadnej logiki poza serializacja.
    ///
    /// Po co osobna klasa, skoro rdzen ma juz EventHistory: rdzen NIE MOZE znac API gry
    /// (twarda regula z sekcji 9 CLAUDE.md - zaden using Verse w Core/). EventHistory
    /// wystawia wiec kontrakt neutralny - ToPersistableLines() i RestoreFromLines() operuja
    /// na List of string - a ta klasa jest jedynym miejscem, w ktorym ten kontrakt spotyka
    /// sie ze Scribe'em. Dzieki temu caly krok 6 zamyka sie w warstwie integracji i rdzen
    /// nie zmienia sie ani o linie.
    ///
    /// KONSTRUKTOR BEZPARAMETROWY JEST WYMAGANY. Slownikowe przeciazenie
    /// Scribe_Collections.Look nie przekazuje ctorArgs w dol (wola listowa wersje Look bez
    /// nich), wiec ScribeExtractor.CreateInstance ma do dyspozycji wylacznie ctor bez
    /// argumentow. Domyslny wystarczy - nie deklarujemy zadnego innego, zeby nikt go
    /// przypadkiem nie usunal dodajac konstruktor z parametrami.
    /// </summary>
    public class MapMemoryRecord : IExposable
    {
        public int decisionCount;
        public int consecutivePassCount;
        public List<string> lines = new List<string>();

        public void ExposeData()
        {
            // forceSave JEST KONIECZNE przy obu licznikach.
            //
            // Scribe_Values.Look POMIJA zapis, gdy wartosc rowna sie domyslnej (zweryfikowane
            // dekompilacja: "if (!forceSave && value.Equals(defaultValue)) return;"). Zero jest
            // tu jednak stanem ZNACZACYM - swieza kolonia, ktora nie podjela jeszcze decyzji -
            // a nie brakiem danych. Bez forceSave wezel nie powstalby w XML, a przy wczytaniu
            // nie dalo by sie odroznic "zero decyzji" od "pole nie zostalo zapisane".
            Scribe_Values.Look(ref decisionCount, "decyzji", 0, true);
            Scribe_Values.Look(ref consecutivePassCount, "seriaPass", 0, true);

            Scribe_Collections.Look(ref lines, "wpisy", LookMode.Value);

            // Straznik bezwarunkowy, nie tylko w PostLoadInit jak robi to wanilia w StoryState.
            // Powod: pusta albo nieobecna kolekcja wraca ze Scribe'a jako null i NADPISUJE
            // inicjalizator pola. Wanilia moze sobie pozwolic na straznik w PostLoadInit, bo jej
            // obiekty zawsze przechodza pelna sciezke deserializacji; nasz rekord moze trafic
            // do zapisu, w ktorym go nie bylo, i wtedy tamta faza nigdy nie nadejdzie.
            if (lines == null)
            {
                lines = new List<string>();
            }
        }
    }
}
