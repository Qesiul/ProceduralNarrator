using System.Globalization;

namespace ProceduralNarrator.Core.Blackboard
{
    /// <summary>
    /// Deklaracja faktu, ktory klocek zostawia po sobie po POTWIERDZONYM wykonaniu zdarzenia.
    ///
    /// To jest warstwa DANYCH, nie kodu: nowy rodzaj sladu dopisuje sie w XML klocka, bez dotykania
    /// logiki decyzyjnej. Nazwy pol sa te same, co wezly XML w NarrativeBlockDef - rozjazd pilnuje
    /// audyt refleksyjny przy starcie gry.
    ///
    /// ZAPIS NASTEPUJE PO WYKONANIU, NIE PO DECYZJI. Narrator moze wybrac zdarzenie, ktorego gra
    /// ostatecznie nie odpali; fakt "napad odparty" po nieodpalonym napadzie bylby klamstwem
    /// w pamieci, a na klamstwie stanelyby kolejne warunki.
    /// </summary>
    public sealed class FactWrite
    {
        /// <summary>Klucz faktu. Musi przejsc FactLedger.IsValidKey - inaczej audyt startowy krzyczy.</summary>
        public string key;

        /// <summary>Wartosc do ustawienia albo doliczenia.</summary>
        public float value = 1f;

        /// <summary>true = dolicz do istniejacej wartosci (licznik), false = ustaw (flaga albo stan).</summary>
        public bool accumulate;

        /// <summary>Ile dni fakt obowiazuje; wartosc <= 0 znaczy "bez wygasania".</summary>
        public float lifespanDays;

        /// <summary>Stosuje deklaracje do ksiegi. Zwraca false, gdy klucz jest niepoprawny.</summary>
        public bool ApplyTo(FactLedger ledger, float nowDay)
        {
            if (ledger == null)
            {
                return false;
            }
            return accumulate
                ? ledger.Add(key, value, nowDay, lifespanDays)
                : ledger.Set(key, value, nowDay, lifespanDays);
        }

        public override string ToString()
        {
            return (key ?? "?") + (accumulate ? "+=" : "=")
                   + value.ToString("0.##", CultureInfo.InvariantCulture)
                   + (lifespanDays > 0f ? "/" + lifespanDays.ToString("0.##", CultureInfo.InvariantCulture) + "d" : "");
        }
    }
}
