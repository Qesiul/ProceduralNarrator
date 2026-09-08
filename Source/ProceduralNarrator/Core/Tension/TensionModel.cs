using System;
using System.Globalization;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Tension
{
    /// <summary>
    /// Odczyt napiecia z jednej tury, ze sladem rozbicia na czlony.
    /// Slad jest wymagany tak samo jak przy scoringu - patrz sekcja 8 CLAUDE.md.
    /// </summary>
    public class TensionReading
    {
        /// <summary>Napiecie wypadkowe, [0,1].</summary>
        public float Tension;

        /// <summary>Czlon narracyjny po zaniku czasowym, [0,1].</summary>
        public float Narrative;

        /// <summary>Czlon sytuacyjny, [0,1].</summary>
        public float Situational;

        /// <summary>Surowy ladunek rytmu Rc z Factor_DramaticContrast, [-1,1]. Do diagnostyki.</summary>
        public float RhythmCharge;

        /// <summary>Mnoznik zaniku w ciszy, [0,1]. 1.0 = zdarzenie wlasnie bylo.</summary>
        public float SilenceDecay;

        /// <summary>Czy sumy wag byly zdegenerowane (obie zerowe) - wtedy Tension = 0.</summary>
        public bool WeightsDegenerate;

        public string Trace;
    }

    /// <summary>
    /// KRZYWA DRAMATURGICZNA - miara napiecia rozgrywki (sekcja 5.5 koncepcji).
    ///
    /// Napiecie jest POZIOMEM, a nie odlegloscia - i to jest cala roznica wobec czynnika
    /// Factor_DramaticContrast, ktory z tego samego rytmu korzysta RELACYJNIE (liczy
    /// |ladunek_kandydata - Rc|, wiec rytm -0.9 i +0.9 daja ten sam kontrast dla kandydata
    /// odleglego o tyle samo). Kontrast pyta "czy to bedzie odmiana", napiecie pyta
    /// "jak zle jest teraz". Te dwa pytania sa ortogonalne i dlatego oba czynniki moga
    /// istniec obok siebie bez liczenia tego samego dwa razy.
    ///
    /// RYTM LICZYMY WOLAJAC ComputeRhythm, A NIE PISZAC DRUGIEJ SREDNIEJ WYKLADNICZEJ.
    /// Kanoniczne mapowanie walencji i skali na liczby (ValenceValue / ScaleValue / Charge)
    /// zyje w Factor_DramaticContrast i jest tam jedyne. Druga kopia rozjechalaby sie przy
    /// pierwszej zmianie osi i nikt by tego nie zauwazyl, bo oba wyniki nadal wygladalyby
    /// sensownie.
    ///
    /// TRZY MECHANIZMY, KTORYCH TA KLASA CELOWO NIE DUBLUJE:
    ///   Factor_PassRestraint.Density - mierzy LICZBE zdarzen w oknie czasowym; napiecie
    ///     mierzy ich CIEZAR. Rozne wielkosci, ta sama historia.
    ///   Factor_Freshness - mierzy TOZSAMOSC (ten sam temat, ta sama akcja), nie ciezar.
    ///   Factor_DramaticContrast - patrz wyzej, poziom kontra odleglosc.
    /// </summary>
    public class TensionModel
    {
        private readonly TensionParams parameters;
        private readonly Factor_DramaticContrast rhythmSource;

        public TensionModel(TensionParams parameters, ContrastTuning contrastTuning)
        {
            this.parameters = parameters ?? TensionParams.Default();

            // Wlasna instancja czynnika kontrastu SLUZY WYLACZNIE do policzenia rytmu.
            // Dostaje to samo strojenie co czynnik uzywany w scoringu, zeby oba widzialy
            // identyczny rytm - inaczej "napiecie" i "kontrast" opisywalyby dwie rozne
            // historie tej samej kolonii.
            rhythmSource = new Factor_DramaticContrast(contrastTuning);
        }

        public TensionParams Parameters
        {
            get { return parameters; }
        }

        /// <summary>
        /// Liczy napiecie. Funkcja CZYSTA: zero stanu miedzy wywolaniami, zero losowosci,
        /// zero czasu rzeczywistego - ten sam kontrakt co IScoringFactor.
        /// </summary>
        public TensionReading Compute(EventHistory history, WorldSnapshot snapshot, float gameDay)
        {
            var reading = new TensionReading();

            // ---- czlon narracyjny: jak ciezkie bylo to, co narrator ostatnio wysylal ----
            RhythmPoint rytm = rhythmSource.ComputeRhythm(history);
            reading.RhythmCharge = rytm.Charge;

            // clamp01(-Rc): rytm dodatni (same dobre rzeczy) daje napiecie ZERO, a nie ujemne.
            // Ujemne napiecie nie ma sensu - "lepiej niz dobrze" to nadal brak napiecia.
            float surowaNarracja = Curves.Clamp01(-rytm.Charge);

            // Zanik w ciszy. Bez niego Breathe zatrzasnalby sie na stale - patrz TensionParams.
            float dniOdZdarzenia = DaysSinceNewest(history, gameDay);
            reading.SilenceDecay = Curves.HalfLifeDecay(dniOdZdarzenia, parameters.halfLifeDays);
            reading.Narrative = Curves.Clamp01(surowaNarracja * reading.SilenceDecay);

            // ---- czlon sytuacyjny: czy zle dzieje sie TERAZ ----
            reading.Situational = Situational(snapshot, parameters);

            // ---- zlozenie ----
            float wN = NonNegative(parameters.narrativeWeight);
            float wS = NonNegative(parameters.situationalWeight);
            float suma = wN + wS;

            if (suma <= 1e-6f)
            {
                // Obie wagi zerowe. Zwracamy 0, a nie 0.5: napiecie zerowe znaczy "narrator
                // nie ma podstaw sadzic, ze jest zle", co przy braku jakiegokolwiek pomiaru
                // jest uczciwsze niz udawanie sredniego napiecia. Flaga idzie do sladu.
                reading.WeightsDegenerate = true;
                reading.Tension = 0f;
            }
            else
            {
                reading.Tension = Curves.Clamp01((wN * reading.Narrative + wS * reading.Situational) / suma);
            }

            reading.Trace = "napiecie=" + reading.Tension.ToString("0.000", CultureInfo.InvariantCulture)
                            + " [narr=" + reading.Narrative.ToString("0.000", CultureInfo.InvariantCulture)
                            + " Rc=" + reading.RhythmCharge.ToString("0.00", CultureInfo.InvariantCulture)
                            + " cisza=" + reading.SilenceDecay.ToString("0.00", CultureInfo.InvariantCulture)
                            + " (" + dniOdZdarzenia.ToString("0.0", CultureInfo.InvariantCulture) + "d)"
                            + " | syt=" + reading.Situational.ToString("0.000", CultureInfo.InvariantCulture)
                            + " powalonych=" + (snapshot == null ? 0 : snapshot.DownedColonistCount)
                            + " zagrozenie=" + (snapshot == null ? DangerLevel.None : snapshot.Danger)
                            + "]" + (reading.WeightsDegenerate ? " WAGI ZDEGENEROWANE" : string.Empty);

            return reading;
        }

        /// <summary>
        /// Czlon sytuacyjny: MAKSIMUM z dwoch sygnalow, nie ich suma.
        ///
        /// Suma liczylaby dwa razy dokladnie ten przypadek, ktory wystepuje najczesciej:
        /// napad jednoczesnie powala kolonistow I podnosi DangerRating. Maksimum mowi
        /// "jest tak zle, jak najgorszy z sygnalow" i jest wobec tego odporne na korelacje
        /// miedzy nimi, ktorej i tak nie znamy.
        /// </summary>
        public static float Situational(WorldSnapshot snapshot, TensionParams p)
        {
            if (snapshot == null)
            {
                return 0f;
            }

            TensionParams par = p ?? TensionParams.Default();

            // Mianownik: ilu kolonistow musi lezec, zeby uznac sytuacje za skrajna.
            // Podloga 1 chroni przed dzieleniem przez zero w kolonii jednoosobowej
            // (i przed sytuacja, w ktorej snapshot nie zdazyl policzyc kolonistow).
            float progPowalonych = Math.Max(1f, par.downedFractionForMax * Math.Max(1, snapshot.ColonistCount));
            float powaleni = Curves.Ramp(snapshot.DownedColonistCount, 0f, progPowalonych);

            // DangerLevel None/Low/High -> 0 / 0.5 / 1.0
            float zagrozenie = Curves.Clamp01((int)snapshot.Danger / 2f);

            return Math.Max(powaleni, zagrozenie);
        }

        /// <summary>
        /// Dni gry od ostatniej EMISJI. Liczone z historii, a nie ze snapshotu, mimo ze
        /// WorldSnapshot.DaysSinceLastEvent podaje to samo: rdzen ma byc testowalny bez
        /// adaptera, a adapter jest jedyna warstwa, ktorej walidator offline nie widzi.
        ///
        /// Pusta historia -> zwracamy 0, czyli BRAK zaniku. Uzasadnienie: przy pustej historii
        /// rytm i tak jest zerowy, wiec czlon narracyjny wychodzi 0 niezaleznie od mnoznika.
        /// Zwrocenie duzej liczby dawaloby ten sam wynik dluzsza droga, ale slad pokazywalby
        /// mylacy "zanik po 300 dniach" w dniu trzecim rozgrywki.
        /// </summary>
        private static float DaysSinceNewest(EventHistory history, float gameDay)
        {
            if (history == null || history.Newest == null)
            {
                return 0f;
            }
            return Math.Max(0f, gameDay - history.Newest.GameDay);
        }

        private static float NonNegative(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f)
            {
                return 0f;
            }
            return v;
        }
    }
}
