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
        public int deliberateSilenceStreak;
        public List<string> lines = new List<string>();

        /// <summary>
        /// Ksiega lukow narracyjnych tej mapy (krok 5, wersja pamieci 2): linie ArcLedger.ToPersistableLines.
        /// Zapis w wersji 1 nie ma tego wezla - wraca null, straznik nizej robi z tego pusta liste,
        /// czyli luki startuja od zera (nie jest to utrata danych, bo w wersji 1 lukow nie bylo).
        /// </summary>
        public List<string> arcLines = new List<string>();

        /// <summary>
        /// Ksiega faktow blackboardu tej mapy (krok 6, wersja pamieci 3): linie FactLedger.ToPersistableLines,
        /// razem z kolejka faktow czekajacych na rozstrzygniecie. OSOBNY wezel "fakty" - zapis sprzed
        /// wersji 3 go nie ma, wraca null, straznik nizej robi z tego pusta liste.
        /// </summary>
        public List<string> factLines = new List<string>();

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

            // NOWY WEZEL, a nie zmiana nazwy pola pod starym - i to jest cala migracja.
            //
            // Licznik zmienil ZNACZENIE: liczyl kazda ture bez zdarzenia, a liczy wylacznie
            // cisze SWIADOMA (PassReason.Competitive). Czytanie starej wartosci pod nowa
            // semantyka daloby liczbe zawyzona o wszystkie ciszne techniczne - i to najmocniej
            // w zapisach z koloni, w ktorych gra duzo odmawia, czyli dokladnie tam, gdzie
            // straznik serii najbardziej szkodzi.
            //
            // Stary wezel "seriaPass" zostaje w zapisach nieczytany i wygasa naturalnie.
            // Zapis sprzed tej wersji wczytuje sie wiec z seria ZEROWA, co jest bezpiecznym
            // kierunkiem bledu: straznik nigdy nie wygasi PASS-a bez pokrycia w danych,
            // a licznik odbuduje sie po kilku turach.
            Scribe_Values.Look(ref deliberateSilenceStreak, "ciszaSwiadoma", 0, true);

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

            Scribe_Collections.Look(ref arcLines, "luki", LookMode.Value);
            if (arcLines == null)
            {
                arcLines = new List<string>();
            }

            Scribe_Collections.Look(ref factLines, "fakty", LookMode.Value);
            if (factLines == null)
            {
                factLines = new List<string>();
            }
        }
    }
}
