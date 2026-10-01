using System;
using System.Collections.Generic;
using System.Text;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Composition
{
    /// <summary>
    /// LUSTRO SPRAWDZEN GRY (krok 9, etap K0; decyzja autora E0-2) - czesc rdzeniowa.
    ///
    /// Gra przed odpaleniem incydentu sprawdza warunki niezalezne od parametrow: odstep od poprzedniego razu
    /// (minRefireDays), najwczesniejszy dzien, biom, poziom zagrozenia Anomaly, warunki gry blokujace incydenty,
    /// ustawienia trudnosci i inne (IncidentWorker.CanFireNow przed pamiecia podreczna). Warstwa integracji
    /// odczytuje je BEZ pytania gry i bez losowania, a wynik trafia do WorldSnapshot.EngineBlockedPayloads.
    /// Tu akcje z zablokowanym payloadem wypadaja PRZED generowaniem kandydatow.
    ///
    /// Po co: odrzucona akcja nie trafia do historii, wiec jej swiezosc zostaje maksymalna, a faza 0 i rundy
    /// dziela budzet 8 pytan do gry. Przy 62 akcjach (31 Anomaly zablokowanych poziomem monolitu, wiele
    /// minRefireDays) czolo rankingu byloby seryjnie odrzucane - cisza techniczna zamiast decyzji. Decyzja
    /// bramy PASS sie nie zmienia (faza 0 i tak usuwala odrzucone czolo przed brama), spada zuzycie pytan.
    ///
    /// Postac kanoniczna: ";Payload1;Payload2;" (posortowane ordynalnie, bez powtorzen), pusta = nic
    /// nie zablokowane. Sprawdzanie po pelnym tokenie z separatorami - "Raid" nie trafia w "RaidEnemy".
    /// </summary>
    public static class EngineMirror
    {
        /// <summary>Postac kanoniczna zbioru zablokowanych payloadow (pusta, gdy zbior pusty).</summary>
        public static string Canonical(IEnumerable<string> payloads)
        {
            if (payloads == null)
            {
                return string.Empty;
            }
            var zbior = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string p in payloads)
            {
                if (!string.IsNullOrEmpty(p) && p.IndexOf(';') < 0)
                {
                    zbior.Add(p);
                }
            }
            if (zbior.Count == 0)
            {
                return string.Empty;
            }
            var sb = new StringBuilder(";");
            foreach (string p in zbior)
            {
                sb.Append(p).Append(';');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Kolumna "lustroOdcina" (v11, etap L): odciete payloady po przecinku, "-" gdy lustro dzialalo i nic nie odcielo,
        /// pusto gdy lustro nie dzialalo (brak pomiaru).
        /// </summary>
        public static string DataColumn(WorldSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.EngineMirrorActive)
            {
                return string.Empty;
            }
            string c = snapshot.EngineBlockedPayloads;
            if (string.IsNullOrEmpty(c) || c == ";")
            {
                return "-";
            }
            return c.Trim(';').Replace(';', ',');
        }

        /// <summary>Czy gra i tak odrzucilaby ten payload (brak snapshotu = nic nie wiadomo = nie blokujemy).</summary>
        public static bool IsBlocked(WorldSnapshot snapshot, string payload)
        {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.EngineBlockedPayloads) || string.IsNullOrEmpty(payload))
            {
                return false;
            }
            return snapshot.EngineBlockedPayloads.IndexOf(";" + payload + ";", StringComparison.Ordinal) >= 0;
        }
    }
}
