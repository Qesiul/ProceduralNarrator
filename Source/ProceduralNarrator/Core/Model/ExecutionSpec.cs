using System.Globalization;

namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// SPECYFIKACJA WYKONANIA - wszystko, co nasza kompozycja zmienia w parametrach incydentu.
    /// Jedno zrodlo prawdy dla DWOCH konsumentow (krok 8, dlug 9):
    ///   - klucza wykonania (Key) - rdzen wie z niego, ktore warianty obejmuje jedno pytanie do gry,
    ///     a ktore trzeba odlozyc, bo cache CanFireNowSub nie pozwoli ich sprawdzic;
    ///   - IncidentParmsBuilder.Apply w warstwie integracji - przyjmuje WYLACZNIE ten obiekt.
    ///
    /// DLACZEGO OBIEKT, A NIE KOMENTARZ. Przed krokiem 8 Apply i ExecutionKey laczylo tylko sasiedztwo
    /// w pliku i komentarz "musi sie zmieniac razem". Nowe pole dopisane do Apply bez klucza nie dawalo
    /// bledu kompilacji ani wyjatku - narrator po cichu wspoldzielil potwierdzenie miedzy wariantami,
    /// ktore ida do gry roznie. Teraz Apply nie ma skad wziac niczego, czego nie ma w specyfikacji,
    /// a walidator (TEST 15) sprawdza refleksja, ze KAZDE pole tej klasy zmienia klucz.
    ///
    /// Format klucza jest identyczny z uzywanym od kroku 5 (payload|moc[|Ffrakcja]) - dane i odlozenia
    /// z wczesniejszych wersji zostaja porownywalne.
    /// </summary>
    public sealed class ExecutionSpec
    {
        /// <summary>defName incydentu (payload klocka akcji).</summary>
        public readonly string Payload;

        /// <summary>Moc koncowa kompozycji - przez IntensityTable wyznacza mnoznik punktow.</summary>
        public readonly IntensityLevel Intensity;

        /// <summary>
        /// Frakcja sprawcy wiazana przez luk (krok 5, ciaglosc Wendety) albo null. Zmienia sciezke
        /// CanFireNowSub napadu, wiec wchodzi do klucza.
        /// </summary>
        public readonly string FactionId;

        private ExecutionSpec(string payload, IntensityLevel intensity, string factionId)
        {
            Payload = payload;
            Intensity = intensity;
            FactionId = string.IsNullOrEmpty(factionId) ? null : factionId;
        }

        /// <summary>Specyfikacja zdarzenia albo null dla braku zdarzenia.</summary>
        public static ExecutionSpec From(ComposedEvent zdarzenie, string factionId)
        {
            if (zdarzenie == null)
            {
                return null;
            }
            return new ExecutionSpec(zdarzenie.ActionPayload, zdarzenie.Intensity, factionId);
        }

        /// <summary>
        /// Klucz wykonania: dwa zdarzenia o tym samym kluczu trafiaja do gry z parametrami
        /// nieodroznialnymi z punktu widzenia CanFireNow. Tekst listu do klucza nie wchodzi - od
        /// kroku 8 w ogole nie jest parametrem incydentu (list dopisywany po wykonaniu).
        /// </summary>
        public string Key
        {
            get
            {
                return (Payload ?? "?") + "|"
                       + ((int)Intensity).ToString(CultureInfo.InvariantCulture)
                       + (FactionId == null ? string.Empty : "|F" + FactionId);
            }
        }
    }
}
