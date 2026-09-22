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

        /// <summary>
        /// Czlon narracyjny, [0,1]: clamp01(NegativeLoad - PositiveRelief). Kazdy wpis historii
        /// starzeje sie tu WLASNYM wiekiem - patrz Factor_DramaticContrast.ComputeAgedLoad.
        /// </summary>
        public float Narrative;

        /// <summary>Czlon sytuacyjny, [0,1].</summary>
        public float Situational;

        /// <summary>Ciezar zdarzen negatywnych po zaniku czasowym kazdego wpisu, [0,1].</summary>
        public float NegativeLoad;

        /// <summary>Ulga ze zdarzen pozytywnych po zaniku czasowym kazdego wpisu, [0,1].</summary>
        public float PositiveRelief;

        /// <summary>Srednia wazona zanikow wpisow, [0,1]. 1.0 = cala historia z tej chwili.</summary>
        public float MeanDecay;

        /// <summary>Ile wpisow weszlo do czlonu narracyjnego.</summary>
        public int Entries;

        /// <summary>Dni gry od najnowszego wpisu. Wylacznie do sladu - NIE jest juz mnoznikiem.</summary>
        public float NewestAgeDays;

        /// <summary>
        /// Rytm Rc z Factor_DramaticContrast, [-1,1] - czyli historia BEZ zaniku czasowego,
        /// dokladnie taka, jaka widzi czynnik kontrastu. WYLACZNIE DIAGNOSTYKA, nie jest wejsciem
        /// czlonu narracyjnego. Trzymamy go w sladzie, bo roznica miedzy Rc a obciazeniem
        /// z zanikiem jest jedynym miejscem, w ktorym widac, ile napiecia "wystyglo" z czasem.
        /// </summary>
        public float RhythmCharge;

        /// <summary>Czy sumy wag byly zdegenerowane (obie zerowe) - wtedy Tension = 0.</summary>
        public bool WeightsDegenerate;

        public string Trace;
    }

    /// <summary>
    /// KRZYWA DRAMATURGICZNA - miara napiecia rozgrywki (sekcja 5.5 koncepcji).
    ///
    /// Napiecie jest POZIOMEM, a nie odlegloscia - i to jest cala roznica wobec czynnika
    /// Factor_DramaticContrast, ktory z tej samej historii korzysta RELACYJNIE (liczy
    /// |ladunek_kandydata - Rc|, wiec rytm -0.9 i +0.9 daja ten sam kontrast dla kandydata
    /// odleglego o tyle samo). Kontrast pyta "czy to bedzie odmiana", napiecie pyta
    /// "jak zle jest teraz". Te dwa pytania sa ortogonalne i dlatego oba czynniki moga
    /// istniec obok siebie bez liczenia tego samego dwa razy.
    ///
    /// MAPOWANIE OSI I WAGI KOLEJNOSCI BIERZEMY Z Factor_DramaticContrast, A NIE PISZEMY DRUGICH.
    /// Kanoniczne mapowanie walencji i skali na liczby (ValenceValue / ScaleValue / Charge),
    /// okno i lambda zyja tam i sa jedyne. Druga kopia rozjechalaby sie przy pierwszej zmianie
    /// osi i nikt by tego nie zauwazyl, bo oba wyniki nadal wygladalyby sensownie.
    ///
    /// CZLON NARRACYJNY (od polerowania etapu 4):
    ///     N = clamp01( - suma_i w_i * c_i * 2^(-wiek_i / polokres) / suma_i w_i )
    /// gdzie w_i = lambda^i po oknie historii (najnowszy wpis na indeksie 0), c_i = ladunek
    /// wpisu, a wiek_i liczony z JEGO WLASNEJ daty. Pusta historia daje N = 0.
    ///
    /// Poprzednio: N = clamp01(-Rc) * 2^(-wiek NAJNOWSZEGO wpisu / polokres). Mnoznik wspolny
    /// dla calej sumy mial wade wykryta przegladem: po dlugiej ciszy dopisanie zdarzenia
    /// POZYTYWNEGO zerowalo wiek najnowszego wpisu, wiec mnoznik wracal do 1 i "ozywial" ciezar
    /// napadow sprzed kilkudziesieciu dni. Skutek dla intencji zalezal od profilu: powsciagliwy
    /// przechodzil z Escalate na Hold na kilka dni, domyslny tylko w ok. 0.6 dnia po prezencie,
    /// napastliwy nigdy (TEST 10g(c) sprawdza to na profilu powsciagliwym).
    ///
    /// ZASIEG ZMIANY. Przy wspolnym wieku wszystkich wpisow obie formuly sa TOZSAME, ale w grze
    /// wieki wpisow roznia sie praktycznie zawsze, wiec zmiana dotyczy niemal kazdej tury.
    /// Zmierzone Monte Carlo w przegladzie (tempo 2.5 dnia, czlon sytuacyjny 0): srednie napiecie
    /// spada o ok. 30-40%, udzial Hold u powsciagliwego z ok. 24% do ok. 12%. Progi profili NIE
    /// zostaly przestrojone - swiadomie; patrz CLAUDE.md i naglowek Profiles_Core.xml.
    ///
    /// Precedens w kodzie: Factor_PassRestraint.Density od poczatku stosuje TEN SAM WZOR zaniku
    /// per wpis. Stala jest jednak rowna tylko dla profilu zrownowazonego: gestosc bierze
    /// halfLifeDays z bloku &lt;pass&gt; (5 dni), napiecie - z profilu (8 / 5 / 3).
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

            // Wlasna instancja czynnika kontrastu SLUZY WYLACZNIE do liczenia obciazenia historii
            // (ComputeAgedLoad) i - do sladu - rytmu Rc. Dostaje to samo strojenie co czynnik
            // uzywany w scoringu, zeby OKNO, WAGI KOLEJNOSCI i MAPOWANIE OSI byly wspolne.
            //
            // ROZJAZD SWIADOMY, NIE PRZEOCZENIE. Do polerowania etapu 4 stalo tu, ze napiecie
            // i kontrast "widza identyczny rytm". Juz tak nie jest: napiecie widzi historie
            // z zanikiem KAZDEGO wpisu, a kontrast - rytm BEZ zaniku, z bramka aktualnosci
            // liczona z wieku NAJNOWSZEGO wpisu (ComputeTrust). Rozjazd jest decyzja zakresu:
            // przeglad etapu 4 ograniczyl poprawke do czlonu historycznego napiecia i zabronil
            // ruszac czynnik kontrastu. Zostaje przez to otwarty dlug tej samej klasy
            // w ComputeTrust (prezent po dlugiej ciszy przywraca zaufanie do rytmu sprzed
            // tygodni) - opisany w CLAUDE.md, do rozstrzygniecia przez autora.
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

            // ---- czlon narracyjny: jak ciezkie bylo to, co narrator wysylal - z uwzglednieniem,
            //      JAK DAWNO wyslal kazda z tych rzeczy ----
            AgedLoad obciazenie = rhythmSource.ComputeAgedLoad(history, gameDay, parameters.halfLifeDays);
            reading.NegativeLoad = obciazenie.Negative;
            reading.PositiveRelief = obciazenie.Positive;
            reading.MeanDecay = obciazenie.MeanDecay;
            reading.Entries = obciazenie.Entries;

            // clamp01: przewaga ulgi daje napiecie ZERO, a nie ujemne. "Lepiej niz dobrze" to nadal
            // brak napiecia. Pusta historia daje 0 - brak informacji nie jest napieciem.
            //
            // Petla sprzezenia zwrotnego Breathe (narrator milczy -> napiecie musi opadac) jest
            // domykana przez zanik KAZDEGO wpisu: bez nowych wpisow cala suma polowi sie dokladnie
            // co polokres. Asercja w TEST 10b walidatora.
            reading.Narrative = Curves.Clamp01(reading.NegativeLoad - reading.PositiveRelief);

            // Diagnostyka: rytm bez zaniku (widziany przez kontrast) i wiek najnowszego wpisu.
            reading.RhythmCharge = rhythmSource.ComputeRhythm(history).Charge;
            reading.NewestAgeDays = DaysSinceNewest(history, gameDay);

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

            reading.Trace = "napiecie=" + F3(reading.Tension)
                            + " [narr=" + F3(reading.Narrative)
                            + " = obciazenie " + F3(reading.NegativeLoad)
                            + " - ulga " + F3(reading.PositiveRelief)
                            + " (sr.zanik " + reading.MeanDecay.ToString("0.00", CultureInfo.InvariantCulture)
                            + ", n=" + reading.Entries.ToString(CultureInfo.InvariantCulture)
                            + ", najnowszy " + reading.NewestAgeDays.ToString("0.0", CultureInfo.InvariantCulture) + "d)"
                            + " Rc=" + reading.RhythmCharge.ToString("0.00", CultureInfo.InvariantCulture)
                            + " | syt=" + F3(reading.Situational)
                            + " powalonych=" + (snapshot == null ? 0 : snapshot.AcuteDownedCount)
                            + "/" + (snapshot == null ? 0 : snapshot.ColonistsOnMap)
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
        ///
        /// LICZNIK I MIANOWNIK Z TEJ SAMEJ LISTY: WorldSnapshot.AcuteDownedCount i ColonistsOnMap
        /// licza tych samych pionkow (kolonisci obecni na mapie, bez niemowlat). Wczesniej mianownik
        /// bral ColonistCount - z pionkami w kriokomorach, noszonymi i w transporterach - a licznik
        /// tylko obecnych, wiec ulamek byl zanizony dokladnie wtedy, gdy czesc kolonii byla poza
        /// gra. Ten sam mianownik czyta CrisisDetector, wiec przy rownych ulamkach kryzys skrajny
        /// zachodzi dokladnie wtedy, gdy ten czlon osiaga nasycenie po stronie powalonych.
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
            float progPowalonych = Math.Max(1f, par.downedFractionForMax * Math.Max(1, snapshot.ColonistsOnMap));
            float powaleni = Curves.Ramp(snapshot.AcuteDownedCount, 0f, progPowalonych);

            // DangerLevel None/Low/High -> 0 / 0.5 / 1.0
            float zagrozenie = Curves.Clamp01((int)snapshot.Danger / 2f);

            return Math.Max(powaleni, zagrozenie);
        }

        /// <summary>
        /// Dni gry od najnowszego wpisu - WYLACZNIE do sladu. Do polerowania etapu 4 byl to
        /// mnoznik calego czlonu narracyjnego; dzis kazdy wpis starzeje sie wlasnym wiekiem.
        /// Pusta historia -> 0, zeby slad nie pokazywal mylacego "300 dni" w trzecim dniu gry.
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

        private static string F3(float v)
        {
            return v.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }
}
