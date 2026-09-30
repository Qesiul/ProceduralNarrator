using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Postac kanoniczna zbiorow i slownikow liczb w plaskim WorldSnapshot (krok 9, K1): ";A;B;" (zbior, sortowanie
    /// ordynalne) i ";A=1;B=0;" (klucz -> liczba). Jedna postac dla wszystkich pol tego rodzaju, zeby zamrozony snapshot
    /// dalo sie porownac, skopiowac i wypisac bez kolekcji. Klucz z ';' albo '=' jest pomijany (nie da sie go zapisac).
    /// </summary>
    public static class CanonicalSet
    {
        public static string Of(IEnumerable<string> keys)
        {
            if (keys == null)
            {
                return string.Empty;
            }
            var zbior = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string k in keys)
            {
                if (IsValidKey(k))
                {
                    zbior.Add(k);
                }
            }
            if (zbior.Count == 0)
            {
                return string.Empty;
            }
            var sb = new StringBuilder(";");
            foreach (string k in zbior)
            {
                sb.Append(k).Append(';');
            }
            return sb.ToString();
        }

        public static bool Contains(string canonical, string key)
        {
            if (string.IsNullOrEmpty(canonical) || !IsValidKey(key))
            {
                return false;
            }
            return canonical.IndexOf(";" + key + ";", StringComparison.Ordinal) >= 0;
        }

        public static string OfCounts(IDictionary<string, int> counts)
        {
            if (counts == null)
            {
                return string.Empty;
            }
            var posortowane = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var p in counts)
            {
                if (IsValidKey(p.Key))
                {
                    posortowane[p.Key] = p.Value;
                }
            }
            if (posortowane.Count == 0)
            {
                return string.Empty;
            }
            var sb = new StringBuilder(";");
            foreach (var p in posortowane)
            {
                sb.Append(p.Key).Append('=').Append(p.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
            }
            return sb.ToString();
        }

        /// <summary>Liczba zapisana pod kluczem; brak klucza = 0 (snapshot nie liczyl albo rzeczy nie ma).</summary>
        public static int Count(string canonical, string key)
        {
            if (string.IsNullOrEmpty(canonical) || !IsValidKey(key))
            {
                return 0;
            }
            string szukany = ";" + key + "=";
            int i = canonical.IndexOf(szukany, StringComparison.Ordinal);
            if (i < 0)
            {
                return 0;
            }
            int start = i + szukany.Length;
            int koniec = canonical.IndexOf(';', start);
            int wartosc;
            return koniec > start && int.TryParse(canonical.Substring(start, koniec - start), NumberStyles.Integer,
                                                  CultureInfo.InvariantCulture, out wartosc)
                ? wartosc : 0;
        }

        public static bool IsValidKey(string key)
        {
            return !string.IsNullOrEmpty(key) && key.IndexOf(';') < 0 && key.IndexOf('=') < 0;
        }
    }
}
