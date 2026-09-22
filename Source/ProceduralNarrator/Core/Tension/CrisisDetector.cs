using System;
using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Tension
{
    /// <summary>
    /// Parametry predykatu KRYZYSU SKRAJNEGO. Wspolna MASZYNERIA narratora, a nie czesc profilu -
    /// i to jest decyzja projektowa, nie wygoda.
    ///
    /// Profil opisuje OSOBOWOSC (wagi narracyjne, ksztalt krzywej). Kryzys skrajny jest regula
    /// BEZPIECZENSTWA: kazda osobowosc ma go rozpoznawac tak samo. Gdyby prog siedzial w profilu,
    /// porownanie trzech profili mieszaloby "regula wlaczona/wylaczona" z osobowoscia, a roznicy
    /// w wynikach nie dalo by sie przypisac zadnej z tych dwoch rzeczy.
    ///
    /// Z tego samego powodu predykat NIE czyta TensionParams.downedFractionForMax: dzis zaden
    /// profil go nie ustawia, ale pierwsze strojenie po cichu uzaleznialoby kryzys od profilu.
    ///
    /// Nazwy pol camelCase = nazwy wezlow XML (blok &lt;crisis&gt; w StorytellerCompProperties_Generative).
    /// </summary>
    public class CrisisParams
    {
        /// <summary>
        /// Jaki ULAMEK kolonistow obecnych na mapie musi lezec powalony ostro, zeby uznac kryzys
        /// za skrajny. 0.5 = polowa kolonii (decyzja autora). Porownanie jest NIEOSTRE od dolu:
        /// 2 z 4 to juz kryzys, 1 z 3 jeszcze nie.
        /// </summary>
        public float downedFraction = 0.5f;

        /// <summary>
        /// Podloga liczby powalonych. Chroni przed "kryzysem" przy zerze powalonych, gdyby na mapie
        /// nie bylo nikogo (0 &gt;= 0.5 * 0). Sanitize wymusza co najmniej 1.
        /// </summary>
        public int minDowned = 1;

        /// <summary>Wylacznik reguly - do serii kontrolnych porownujacych narratora z regula i bez.</summary>
        public bool enabled = true;

        public static CrisisParams Default()
        {
            return new CrisisParams();
        }

        public CrisisParams Clone()
        {
            return new CrisisParams { downedFraction = downedFraction, minDowned = minDowned, enabled = enabled };
        }

        /// <summary>Klamruje wartosci i ZWRACA opis poprawek (pusty, gdy nic nie poprawiono).</summary>
        public string Sanitize()
        {
            var sb = new StringBuilder();
            if (float.IsNaN(downedFraction) || float.IsInfinity(downedFraction))
            {
                Note(sb, "downedFraction NaN/Inf -> 0.5");
                downedFraction = 0.5f;
            }
            else if (downedFraction < 0.01f)
            {
                Note(sb, "downedFraction " + downedFraction.ToString("0.###", CultureInfo.InvariantCulture) + " -> 0.01");
                downedFraction = 0.01f;
            }
            else if (downedFraction > 1f)
            {
                Note(sb, "downedFraction " + downedFraction.ToString("0.###", CultureInfo.InvariantCulture) + " -> 1");
                downedFraction = 1f;
            }

            if (minDowned < 1)
            {
                Note(sb, "minDowned " + minDowned.ToString(CultureInfo.InvariantCulture) + " -> 1");
                minDowned = 1;
            }
            return sb.ToString();
        }

        private static void Note(StringBuilder sb, string t)
        {
            if (sb.Length > 0)
            {
                sb.Append("; ");
            }
            sb.Append(t);
        }

        public override string ToString()
        {
            return "kryzys: " + (enabled ? "wlaczony" : "WYLACZONY")
                   + " ulamekPowalonych=" + downedFraction.ToString("0.##", CultureInfo.InvariantCulture)
                   + " minPowalonych=" + minDowned.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Wynik predykatu kryzysu skrajnego dla jednej tury, ze sladem.</summary>
    public class CrisisReading
    {
        public bool Extreme;
        public int Downed;
        public int Colonists;

        /// <summary>Calkowity prog liczby powalonych - kryzys, gdy Downed &gt;= Threshold.</summary>
        public int Threshold;

        public string Trace;
    }

    /// <summary>
    /// JAWNY SYGNAL KRYZYSU SKRAJNEGO - decyzja autora po przegladzie etapu 4.
    ///
    /// PROBLEM, KTORY TO ZAMYKA. Intencja Breathe powstaje z napiecia, a napiecie jest SREDNIA
    /// WAZONA czlonu narracyjnego i sytuacyjnego. Przy pustej albo lagodnej historii czlon
    /// narracyjny rozcienczal sygnal kryzysu: polowa kolonii powalona dawala u profilu
    /// napastliwego napiecie najwyzej wS/(wN+wS) = 0.40 &lt; calmBelow = 0.45, czyli ESCALATE
    /// w kazdym stanie swiata. Tabela "kolonia w tarapatach -&gt; Breathe" z CLAUDE.md byla
    /// falszywa dla dwoch z trzech profili.
    ///
    /// ROZWIAZANIE. Predykat liczony RAZ na ture, w maszynerii wspolnej dla wszystkich profili.
    /// Gdy zachodzi, IntentSelector.ApplyCrisis wymusza Breathe i ogranicza docelowa moc do
    /// co najwyzej 0 (min z mocy profilu, nie nadpisanie - patrz tam), a straznik serii ciszy
    /// zostaje zawieszony (SelectionPolicy.IsStreakWaivedByCrisis). Napiecie NIE jest zmieniane:
    /// do logu idzie surowe, zeby dalo sie zobaczyc, ile regula zmienila wobec samej krzywej.
    ///
    /// CZEGO REGULA NIE ROBI - SWIADOMIE. Nie zakazuje zdarzen negatywnych w kryzysie. Zakaz
    /// odbieralby opcje w KAZDEJ turze kryzysu, takze gdy brama chce dzialac, i wymagalby albo
    /// warunku twardego mieszajacego "prawde tekstu" z polityka tempa, albo drugiego weta
    /// (sprzecznego z decyzja "weto ma tylko contextFit"). Breathe i tak przesuwa preferencje
    /// ku zdarzeniom lagodnym i ku ciszy - tak samo, jak robi to poza kryzysem.
    ///
    /// NIEZALEZNOSC OD ZAGROZENIA (decyzja autora). Predykat nie wymaga DangerRating: kryzys
    /// niebojowy (zaraza w stadium skrajnym - szok bolowy; pozar - oparzenia do opatrzenia) tez
    /// kladzie kolonie. Powalenie przez sama swiadomosc bez ran (zatrucie toksynami, udar
    /// cieplny, hipotermia) NIE jest liczone - patrz ograniczenia w WorldSnapshotBuilder.
    /// Samo DangerRating = High przy zerze powalonych NIE jest kryzysem skrajnym - to zwykly
    /// napad, i od tego jest krzywa.
    /// </summary>
    public static class CrisisDetector
    {
        public static CrisisReading Evaluate(WorldSnapshot snapshot, CrisisParams parameters)
        {
            CrisisParams p = parameters ?? CrisisParams.Default();
            var r = new CrisisReading();

            if (snapshot == null)
            {
                r.Trace = "kryzys: brak stanu swiata";
                return r;
            }

            r.Downed = Math.Max(0, snapshot.AcuteDownedCount);
            r.Colonists = Math.Max(0, snapshot.ColonistsOnMap);

            // PROG CALKOWITY, liczony z odjeciem epsilona przed zaokragleniem w gore. Porownanie
            // floatow "powalonych >= ulamek * kolonistow" zawodzi na granicy: 0.6f * 25 to
            // w arytmetyce float 15.000001, wiec 15 z 25 nie byloby "60%" (sprawdzone przegladem
            // wszystkich ulamkow 0.01-0.99 i kolonii do 40 osob - to jedyny taki przypadek, ale
            // istnieje). Prog calkowity jest przy tym czytelny w logu ("2/3 &gt;= prog 2").
            // Zgodnosc z Ramp w TensionModel.Situational ("kryzys <=> nasycenie czlonu powalonych")
            // zachodzi przy ulamku 0.5 (dzisiejszy stan: downedFraction == downedFractionForMax
            // == 0.5, asercja TEST 10h). Situational liczy bez epsilona, wiec przy innych rownych
            // ulamkach rozjezdza sie w pulapce floatow powyzej (f=0.6, 25 kolonistow, 15 powalonych:
            // kryzys, a czlon 0.99999994) - roznica 6e-8 napiecia, bez znaczenia praktycznego.
            int progUlamkowy = (int)Math.Ceiling(p.downedFraction * r.Colonists - 1e-4);
            r.Threshold = Math.Max(Math.Max(1, p.minDowned), progUlamkowy);

            r.Extreme = p.enabled && r.Downed >= r.Threshold;

            r.Trace = (r.Extreme ? "KRYZYS SKRAJNY: " : "kryzys: nie; ")
                      + "powalonych " + r.Downed.ToString(CultureInfo.InvariantCulture)
                      + "/" + r.Colonists.ToString(CultureInfo.InvariantCulture)
                      + (r.Downed >= r.Threshold ? " >= " : " < ") + "prog "
                      + r.Threshold.ToString(CultureInfo.InvariantCulture)
                      + (p.enabled ? string.Empty : " (regula WYLACZONA)");
            return r;
        }
    }
}
