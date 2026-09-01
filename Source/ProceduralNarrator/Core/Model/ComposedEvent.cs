using System.Collections.Generic;

namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Wydarzenie zlozone z klockow - wyjscie warstwy kompozycji, wejscie warstwy decyzyjnej.
    /// </summary>
    public class ComposedEvent
    {
        /// <summary>Uporzadkowany zbior klockow, z ktorych powstalo wydarzenie.</summary>
        public List<Block> Blocks = new List<Block>();

        /// <summary>Zagregowany ladunek klocka akcji (dla integracji: defName incydentu).</summary>
        public string ActionPayload;

        /// <summary>
        /// Block.Id klocka akcji - ustawiane w EventComposer.Assemble z tego samego obiektu,
        /// z ktorego bierze sie ActionPayload.
        ///
        /// Osobne pole, a nie wyszukiwanie klocka akcji w Blocks przy kazdej ocenie, z dwoch
        /// powodow. Pierwszy jest wydajnosciowy: klucz jest czytany dla kazdego kandydata
        /// w kazdej decyzji. Drugi jest powazniejszy - indeks slotu akcji w liscie Blocks
        /// PRZESUWA SIE, gdy slot opcjonalny (wyzwalacz albo modyfikator) jest pusty, wiec
        /// odczyt "po pozycji" jest cichym bledem czekajacym na okazje.
        ///
        /// To jest KLUCZ OSI AKCJI w czynniku swiezosci i musi byc uzywany po obu stronach:
        /// przy zapisie do historii i przy porownaniu. Zapisanie tu defName incydentu, a
        /// porownywanie z Block.Id (albo odwrotnie) daje czynnik dzialajacy w polowie -
        /// os akcji zwraca wtedy zawsze 1.0 i nikt tego nie zauwaza.
        /// </summary>
        public string ActionBlockId;

        /// <summary>
        /// JEDYNY klucz kandydata: sortowanie kanoniczne, deduplikacja, rozstrzyganie remisow
        /// w polityce wyboru i identyfikacja w logu ewaluacyjnym.
        ///
        /// Format: trigId|actorId|actionId|targetId|modId, klocek nieobecny zapisany jako "-".
        /// Sloty ida w kolejnosci NARRACYJNEJ, a nie w kolejnosci enumeracji wariantow, wiec
        /// sygnatura czyta sie jak zdanie. Jest roznowartosciowa, bo dwie rozne kombinacje
        /// klockow roznia sie co najmniej jednym identyfikatorem.
        ///
        /// Dlaczego identyfikatory klockow, a nie payload akcji: payload nie musi byc
        /// roznowartosciowy (dwa klocki akcji moga kiedys wskazac ten sam IncidentDef),
        /// wiec klucz oparty o niego przestalby rozrozniac kandydatow dokladnie w dniu,
        /// w ktorym katalog urosnie. Payload trafia do logu osobnym polem.
        ///
        /// UWAGA NA KROK 6: dolozenie slotu Consequence dopisze SZOSTY segment, czyli zmieni
        /// format klucza. Logi ewaluacyjne zebrane wczesniej przestana byc porownywalne
        /// po kluczu - to swiadomy koszt do odnotowania przy zamykaniu kroku 6.
        /// </summary>
        public string Signature;

        /// <summary>Osie odziedziczone po klocku akcji - on definiuje charakter wydarzenia.</summary>
        public Theme Theme;
        public Valence Valence;
        public EventScale Scale;

        /// <summary>Wypadkowa sila zdarzenia. Integration tlumaczy ja na punkty incydentu.</summary>
        public IntensityLevel Intensity = IntensityLevel.Normal;

        /// <summary>
        /// Dopasowanie kontekstowe 0..1 - jak bardzo to wydarzenie pasuje tu i teraz.
        /// Liczone na GOTOWYM kandydacie (decyzja projektowa), zasila scoring w kroku 3.
        /// </summary>
        public float ContextFit = 1f;

        /// <summary>Zlozony tekst narracyjny.</summary>
        public string Description;

        /// <summary>Slad kompozycji - do logu decyzji i ewaluacji (sekcja 12).</summary>
        public string Trace;

        /// <summary>Rozbicie contextFit na skladniki - wymagany slad scoringu.</summary>
        public string FitTrace;
    }
}
