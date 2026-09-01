using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Pelny wynik jednej decyzji narratora: zwyciezca, RANKING WSZYSTKICH ocenionych kandydatow
    /// wraz z zawetowanymi, liczniki etapow, powod ewentualnego PASS-a i slad polityki.
    ///
    /// RANKING MUSI ZAWIERAC WSZYSTKICH, LACZNIE Z ODRZUCONYMI. Gdyby zostawal w nim sam zwyciezca,
    /// znikalby MIANOWNIK metryk z rozdzialu o ewaluacji: nie da sie policzyc, ile razy zadzialalo
    /// weto, jak szeroka byla stawka ani ile kandydatow odpadlo na ktorym etapie. Zawetowani maja
    /// zachowane RawUtility, wiec widac takze, ile byliby warci, gdyby nie weto - bez tego nie da
    /// sie w pracy pokazac, ze weto faktycznie cos odsiewa.
    /// Niezmiennik: Ranking.Count == CountScored + 1 (kandydaci realni plus pseudo-kandydat PASS).
    ///
    /// LINIA MASZYNOWA. ToDataFragment() jest JEDNYM miejscem budujacym czesc decyzyjna linii
    /// [PN-DATA]. Zestaw i KOLEJNOSC kolumn sa STALE - wypelnienie jest warunkowe. Kolumny
    /// czynnikow zdarzeniowych dla decyzji PASS zostaja PUSTE, a nie zerowe: zero jest pomiarem,
    /// pustka jest brakiem pomiaru. Wpisanie 0.5 albo 0 wstrzykneloby do danych badawczych pomiar,
    /// ktorego nie bylo (histogram dostalby sztuczny pik o wysokosci rownej udzialowi PASS).
    /// W Pandas pusta kolumna to NaN i sama wypada z agregacji.
    /// Fragment NIE zawiera znacznika czasu ani pola wersjaLogu - doklada je warstwa integracji,
    /// ktora sklada calosc linii i jest wlascicielem jej wersjonowania.
    /// </summary>
    public class NarratorDecision
    {
        /// <summary>Domyslna liczba pozycji rankingu wypisywanych do czytelnego logu.</summary>
        public const int DefaultRankingLines = 10;

        // Kolumny czynnikow PASS dostaja prefiks "pass", bo nazwa "intentAlignment" wystepuje
        // w OBU rozlacznych przestrzeniach wag (ScoringWeights i PassScoringParams) i bez prefiksu
        // w jednej linii pojawilyby sie dwie kolumny o tej samej nazwie.
        private const string ColPassRestraint = "passRestraint";
        private const string ColPassBaseline = "passBaseline";
        private const string ColPassIntent = "passIntent";

        public ScoredCandidate Winner;

        /// <summary>Wszyscy ocenieni (takze zawetowani i odrzuceni) plus PASS, w porzadku decyzji.</summary>
        public List<ScoredCandidate> Ranking = new List<ScoredCandidate>();

        /// <summary>Pseudo-kandydat PASS tej tury - takze wtedy, gdy przegral.</summary>
        public ScoredCandidate PassCandidate;

        /// <summary>
        /// Maksimum uzytecznosci po kandydatach REALNYCH, ktorzy przeszli prog. PASS wylaczony.
        /// Od wprowadzenia bramy dwuetapowej jest to takze REFERENCJA, przeciwko ktorej konkuruje
        /// cisza - patrz SelectionPolicy, etap A.
        /// </summary>
        public float BestUtility;

        /// <summary>nearBestFraction * BestUtility.</summary>
        public float BandThreshold;

        /// <summary>
        /// Prawdopodobienstwo, z jakim BRAMA "czy w ogole dzialac" wybrala cisze w tej turze.
        /// Liczone z dwuelementowego softmaksu {BestUtility, PassUtility} przy gateTemperature.
        ///
        /// To jest GLOWNA metryka sterowania tempem i wprost material na wykres w pracy: pokazuje
        /// sklonnosc narratora do milczenia NIEZALEZNIE od tego, co faktycznie wylosowal, wiec
        /// daje sie usrednic po turach zamiast czekac na rzadkie zdarzenie wygranej PASS.
        /// W wersji jednoetapowej takiej wielkosci nie dalo sie podac, bo udzial ciszy zalezal
        /// od licznosci puli i trzeba go bylo rekonstruowac z calego rozkladu.
        ///
        /// Rowne 1 przy pustej puli zdarzen (cisza z braku materialu) i 0 przy dzialajacym
        /// strazniku serii - w obu przypadkach brama nie losuje.
        /// </summary>
        public float GatePassProbability;

        /// <summary>
        /// Uzytecznosc PASS-a - wypelniana ZAWSZE, takze gdy wygralo zdarzenie. Bez tego nie da sie
        /// narysowac, jak blisko wygranej byla cisza, a to jest wprost material na wykres w pracy.
        /// </summary>
        public float PassUtility;

        /// <summary>
        /// Gestosc ostatnich zdarzen - wielkosc TURY, wspolna dla wszystkich kandydatow.
        /// Wypelnia warstwa integracji przez AttachTurnContext(scorer.LastPassDensity, ...),
        /// bo polityka wyboru celowo nie widzi historii ani kontekstu.
        /// </summary>
        public float RecentDensity;

        /// <summary>Dlugosc biezacej serii decyzji PASS. Wypelnia integracja przez AttachTurnContext.</summary>
        public int PassStreak;

        /// <summary>Czy straznik serii wykluczyl PASS z losowania w tej turze.</summary>
        public bool PassSuppressedByStreak;

        public int CountScored;
        public int CountVetoed;
        public int CountBelowCutoff;
        public int CountBelowBand;
        /// <summary>
        /// Licznosc puli etapu B, czyli SAMYCH zdarzen w pasmie. PASS-a juz w niej nie ma -
        /// rozstrzyga sie w bramie - wiec od wersji 2 formatu logu kolumna "wSoftmaksie" znaczy
        /// "ile zdarzen konkurowalo miedzy soba", a nie "ile opcji lacznie z cisza".
        /// </summary>
        public int CountInSoftmax;

        /// <summary>
        /// Liczba pobran z generatora. Z konstrukcji rowna DWUKROTNOSCI liczby rund (brama plus
        /// wybor zdarzenia), nigdy rozmiarowi puli - takze wtedy, gdy ktorys etap byl zdegenerowany
        /// i jego losowanie zostalo zuzyte na pusto. Patrz SelectionPolicy.BurnDraw.
        /// </summary>
        public int RandomDraws;

        public PassReason PassReason = PassReason.None;

        public string PolicyTrace;

        public bool IsPass
        {
            get { return Winner != null && Winner.IsPass; }
        }

        /// <summary>
        /// Niezmiennik rankingu. Sprawdzalny w tescie i w grze; jego zlamanie oznacza, ze ktos
        /// obcial ranking i metryki ewaluacji straca mianownik.
        /// </summary>
        public bool RankingIsComplete
        {
            get { return Ranking != null && Ranking.Count == CountScored + 1; }
        }

        /// <summary>
        /// Doklada wielkosci TURY, ktorych polityka wyboru nie zna, bo nie widzi kontekstu decyzji.
        /// Wywolywane przez warstwe integracji zaraz po Select.
        /// </summary>
        public void AttachTurnContext(float recentDensity, int passStreak)
        {
            RecentDensity = recentDensity;
            PassStreak = passStreak;
        }

        /// <summary>
        /// Czesc decyzyjna linii maszynowej [PN-DATA]. Separator pol to "; ", separator nazwy
        /// i wartosci to "=", wiec parser w Pythonie to split(';') + split('=').
        /// KAZDA liczba przez CultureInfo.InvariantCulture - przy locale pl-PL przecinek dziesietny
        /// wywroci float() po stronie Pythona i cala seria bedzie nie do odczytania.
        /// </summary>
        public string ToDataFragment()
        {
            var sb = new StringBuilder(512);

            bool pass = IsPass;
            ScoredCandidate w = Winner;

            Append(sb, "decyzja", pass ? "PASS" : "Zdarzenie");
            Append(sb, "wybor", w == null ? string.Empty : w.Label);
            Append(sb, "klucz", w == null || w.SortKey == null ? string.Empty : w.SortKey);
            Append(sb, "wynik", w == null ? string.Empty : Fmt(w.Utility));
            Append(sb, "p", w == null ? string.Empty : Fmt(w.SelectionProbability));

            Append(sb, "best", Fmt(BestUtility));
            Append(sb, "pasmo", Fmt(BandThreshold));

            Append(sb, "kandydatow", Int(CountScored));
            Append(sb, "zawetowanych", Int(CountVetoed));
            Append(sb, "odrzuconeCutoff", Int(CountBelowCutoff));
            Append(sb, "odrzuconePasmo", Int(CountBelowBand));
            Append(sb, "wSoftmaksie", Int(CountInSoftmax));
            Append(sb, "losowan", Int(RandomDraws));

            // powodPass jest PUSTE, gdy wygralo zdarzenie. Symbol enuma, nie polskie zdanie:
            // metryka "ile razy narrator swiadomie milczal" zliczana regexem po tekscie bylaby
            // krucha na literowke i na dryf sformulowan miedzy wersjami.
            Append(sb, "powodPass", PassReason == PassReason.None ? string.Empty : PassReason.ToString());
            Append(sb, "passWynik", Fmt(PassUtility));
            Append(sb, "pBrama", Fmt(GatePassProbability));
            Append(sb, "passStlumiony", PassSuppressedByStreak ? "true" : "false");
            Append(sb, "gestosc", Fmt(RecentDensity));
            Append(sb, "seriaPass", Int(PassStreak));

            // Czynniki ZDARZENIOWE zwyciezcy - puste dla decyzji PASS (brak pomiaru, nie zero).
            AppendFactor(sb, pass ? null : w, nameof(ScoringWeights.contextFit));
            AppendFactor(sb, pass ? null : w, nameof(ScoringWeights.freshness));
            AppendFactor(sb, pass ? null : w, nameof(ScoringWeights.dramaticContrast));
            AppendFactor(sb, pass ? null : w, nameof(ScoringWeights.intentAlignment));

            // Czynniki PASS - wypelniane w KAZDEJ turze, takze gdy wygralo zdarzenie, bo PASS jest
            // oceniany zawsze, a to sa wielkosci opisujace stan tury.
            ScoredCandidate p = FindPass();
            AppendFactorAs(sb, p, PassScoringParams.RestraintFactorName, ColPassRestraint);
            AppendFactorAs(sb, p, PassScoringParams.BaselineFactorName, ColPassBaseline);
            AppendFactorAs(sb, p, PassScoringParams.IntentAlignmentFactorName, ColPassIntent);

            return sb.ToString();
        }

        /// <summary>
        /// Czytelny ranking do PNLog. Wypisuje najpierw slad polityki, potem kolejne pozycje
        /// rankingu ze sladem rozbicia na czynniki, a na koncu informacje o obcieciu - obciecie
        /// dotyczy WYLACZNIE logu czytelnego, dane badawcze ida osobna linia i nie sa obcinane.
        /// </summary>
        public IEnumerable<string> ToRankingLines(int max = DefaultRankingLines)
        {
            if (!string.IsNullOrEmpty(PolicyTrace))
            {
                yield return "polityka: " + PolicyTrace;
            }

            if (Ranking == null || Ranking.Count == 0)
            {
                yield return "ranking: PUSTY (blad - PASS powinien byc w nim zawsze)";
                yield break;
            }

            int limit = max <= 0 ? Ranking.Count : max;
            int wypisanych = 0;
            for (int i = 0; i < Ranking.Count && wypisanych < limit; i++)
            {
                ScoredCandidate k = Ranking[i];
                if (k == null)
                {
                    continue;
                }
                wypisanych++;
                yield return k.ToRankingLine(i + 1);
            }

            if (Ranking.Count > wypisanych)
            {
                yield return "... i " + (Ranking.Count - wypisanych).ToString(CultureInfo.InvariantCulture)
                             + " dalszych pozycji rankingu (obcieto tylko log czytelny)";
            }

            // Zawetowani maja Utility = 0, wiec ZAWSZE sortuja sie na koniec rankingu i nigdy
            // nie mieszcza sie w obcietym czole. Bez tej linii nie da sie zobaczyc, KTORY kandydat
            // zostal odrzucony jako niespojny z kontekstem ani dlaczego - a to jest wlasnie ta
            // informacja, ktora tlumaczy nieoczywiste decyzje narratora. Linia maszynowa ma tylko
            // LICZNIK zawetowanych, wiec bez tego trop ginie calkowicie.
            // Wypisujemy zwiezle i z limitem, bo to log czytelny, a nie dane badawcze.
            string zawetowani = OpiszZawetowanych(6);
            if (zawetowani != null)
            {
                yield return zawetowani;
            }
        }

        /// <summary>
        /// Jedna linia z zawetowanymi kandydatami i ich surowa uzytecznoscia. Zwraca null,
        /// gdy nikogo nie zawetowano - pusta linia w logu tylko rozpraszalaby uwage.
        /// </summary>
        private string OpiszZawetowanych(int limit)
        {
            if (Ranking == null)
            {
                return null;
            }

            var sb = new StringBuilder();
            int pokazanych = 0;
            int wszystkich = 0;

            for (int i = 0; i < Ranking.Count; i++)
            {
                ScoredCandidate k = Ranking[i];
                if (k == null || !k.Vetoed)
                {
                    continue;
                }
                wszystkich++;
                if (pokazanych >= limit)
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(k.Label)
                  .Append(" (raw=")
                  .Append(k.RawUtility.ToString("0.00", CultureInfo.InvariantCulture))
                  .Append(')');
                pokazanych++;
            }

            if (wszystkich == 0)
            {
                return null;
            }
            if (wszystkich > pokazanych)
            {
                sb.Append(" i ").Append((wszystkich - pokazanych).ToString(CultureInfo.InvariantCulture))
                  .Append(" innych");
            }
            return "zawetowani (" + wszystkich.ToString(CultureInfo.InvariantCulture) + "): " + sb;
        }

        public override string ToString()
        {
            return (IsPass ? "PASS(" + PassReason + ")" : "Zdarzenie(" + (Winner == null ? "?" : Winner.Label) + ")")
                   + " wynik=" + (Winner == null ? "?" : Fmt(Winner.Utility))
                   + " passWynik=" + Fmt(PassUtility)
                   + " pBrama=" + Fmt(GatePassProbability)
                   + " kandydatow=" + Int(CountScored);
        }

        private ScoredCandidate FindPass()
        {
            if (PassCandidate != null)
            {
                return PassCandidate;
            }
            if (Winner != null && Winner.IsPass)
            {
                return Winner;
            }
            if (Ranking != null)
            {
                for (int i = 0; i < Ranking.Count; i++)
                {
                    if (Ranking[i] != null && Ranking[i].IsPass)
                    {
                        return Ranking[i];
                    }
                }
            }
            return null;
        }

        private static void AppendFactor(StringBuilder sb, ScoredCandidate candidate, string factorName)
        {
            AppendFactorAs(sb, candidate, factorName, factorName);
        }

        private static void AppendFactorAs(StringBuilder sb, ScoredCandidate candidate, string factorName, string columnName)
        {
            // FactorValue zwraca -1 dla czynnika NIEZAREJESTROWANEGO. To tez jest brak pomiaru,
            // wiec kolumna zostaje pusta - inaczej wylaczenie czynnika w XML wygladaloby w danych
            // jak zmierzone zero.
            float v = candidate == null ? -1f : candidate.FactorValue(factorName);
            Append(sb, columnName, v < 0f ? string.Empty : Fmt(v));
        }

        private static void Append(StringBuilder sb, string name, string value)
        {
            if (sb.Length > 0)
            {
                sb.Append("; ");
            }
            sb.Append(name).Append('=').Append(value ?? string.Empty);
        }

        private static string Fmt(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                // Wartosc niepoprawna raportujemy jako brak pomiaru, a nie jako 0 - inaczej
                // usterka scoringu wtopilaby sie w statystyke jako prawidlowy wynik zerowy.
                return string.Empty;
            }
            return v.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string Int(int v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }
    }
}
