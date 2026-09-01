using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProceduralNarrator.Core.Decision;

namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Etap, na ktorym kandydat wypadl z gry o wybor. Kazdy odrzucony dostaje DOKLADNIE JEDEN
    /// powod - to dlatego etapy polityki wyboru sa rozlaczne i uporzadkowane (weto, prog jakosci,
    /// pasmo near-best, odmowa silnika gry).
    ///
    /// Rozroznienie nie jest kosmetyka logu: "bez sensu tutaj" (Veto), "za slaby" (QualityCutoff),
    /// "dobry, ale byl lepszy" (NearBestBand) i "gra nie pozwolila odpalic" (EngineRefused)
    /// to cztery rozne zjawiska, a metryki z sekcji 12 koncepcji licza je osobno.
    /// </summary>
    public enum RejectionStage
    {
        None,
        Veto,
        QualityCutoff,
        NearBestBand,
        EngineRefused
    }

    /// <summary>
    /// Kandydat po ocenie: uzytecznosc, pelny slad rozbicia na czynniki i los w polityce wyboru.
    /// Reprezentuje ZAROWNO zlozone wydarzenie, jak i pseudo-kandydata PASS - bo PASS jest
    /// pelnoprawna decyzja narratora i musi konkurowac na tej samej liscie, z tym samym sladem.
    ///
    /// Obiekt jest budowany etapami i celowo ma pola publiczne, a nie konstruktor:
    ///  1. UtilityScorer tworzy SKORUPE (Event, IsPass, SortKey) - czynniki dostaja ja jako
    ///     argument, wiec musi istniec ZANIM cokolwiek zostanie policzone,
    ///  2. UtilityScorer dopelnia Factors, RawUtility, Utility, Vetoed i flagi diagnostyczne,
    ///  3. SelectionPolicy dopisuje Rejected i SelectionProbability.
    /// </summary>
    public class ScoredCandidate
    {
        /// <summary>
        /// Zlozone wydarzenie. null WTEDY I TYLKO WTEDY, gdy IsPass - czynniki zdarzeniowe
        /// moga wiec czytac Event.* bez sprawdzania, o ile UtilityScorer nie miesza tablic.
        /// </summary>
        public ComposedEvent Event;

        /// <summary>Czy to pseudo-kandydat "cisza" (swiadomy brak wydarzenia w tej turze).</summary>
        public bool IsPass;

        /// <summary>
        /// Klucz tozsamosci kandydata, unikalny w obrebie tury: ComposedEvent.Signature
        /// albo "PASS". Sluzy do deterministycznego rozstrzygania remisow w sortowaniu
        /// (porownanie ORDYNALNE, nie kulturowe) oraz do sklejania logow z kolejnych tur.
        /// Jedno zrodlo prawdy - nie ma drugiego formatu klucza w projekcie.
        /// </summary>
        public string SortKey;

        /// <summary>
        /// Uzytecznosc PRZED wetem. Zostaje nietknieta nawet dla kandydata zawetowanego,
        /// bo tylko ona pokazuje, ile kandydat bylby wart, gdyby pasowal do kontekstu -
        /// bez tej liczby nie da sie wykazac w pracy, ze weto faktycznie cos odsiewa.
        /// </summary>
        public float RawUtility;

        /// <summary>Uzytecznosc uzywana przez polityke wyboru: Vetoed ? 0 : RawUtility.</summary>
        public float Utility;

        /// <summary>
        /// Weto spojnosci (contextFit ponizej progu). Dla PASS ZAWSZE false: weto jest
        /// zdefiniowane wylacznie na dopasowaniu kontekstowym, a cisza go nie ma -
        /// to nie zwolnienie z weta, tylko brak przedmiotu weta.
        /// </summary>
        public bool Vetoed;

        /// <summary>Czytelny powod weta razem z wartoscia sprzed weta. null gdy brak weta.</summary>
        public string VetoReason;

        /// <summary>Suma wag &lt;= 1e-6 - normalizacja niemozliwa, RawUtility zbite do 0.</summary>
        public bool WeightsDegenerate;

        /// <summary>Ktorys czynnik zwrocil NaN albo nieskonczonosc (szczegoly w sladzie).</summary>
        public bool HadInvalidFactor;

        /// <summary>
        /// Slad scoringu: po jednym wierszu na czynnik, w KOLEJNOSCI REJESTRACJI.
        /// Kolejnosc jest czescia formatu danych badawczych - jej zmiana zmienia kolumny logu.
        /// </summary>
        public List<FactorScore> Factors = new List<FactorScore>();

        /// <summary>Etap, na ktorym kandydat odpadl. Wypelnia wylacznie SelectionPolicy.</summary>
        public RejectionStage Rejected = RejectionStage.None;

        /// <summary>
        /// Prawdopodobienstwo wylosowania w softmaksie, odtworzone z FAKTYCZNIE uzytych progow
        /// calkowitych (a nie z rozkladu sprzed kwantyzacji). 0 dla kandydatow odrzuconych.
        /// </summary>
        public float SelectionProbability;

        /// <summary>
        /// Krotka etykieta do logu: "PASS" albo defName incydentu z klocka akcji.
        /// Odporna na stan niespojny (Event == null przy IsPass == false), bo log ma dzialac
        /// takze wtedy, gdy cos poszlo nie tak - to wlasnie wtedy jest najbardziej potrzebny.
        /// </summary>
        public string Label
        {
            get
            {
                if (IsPass)
                {
                    return "PASS";
                }
                if (Event == null)
                {
                    return "?BRAK-ZDARZENIA";
                }
                return string.IsNullOrEmpty(Event.ActionPayload) ? "?BRAK-PAYLOAD" : Event.ActionPayload;
            }
        }

        /// <summary>
        /// Surowa wartosc czynnika o podanej nazwie, prosto ze sladu. Zwraca -1f, gdy czynnika
        /// nie ma - wartosc spoza dziedziny [0,1] jest tu celowa: "nie mierzono" musi dac sie
        /// odroznic od "zmierzono zero". Zero jest wartoscia, brak pomiaru nia nie jest.
        ///
        /// Porownanie ordynalne, bo nazwy czynnikow sa identyfikatorami technicznymi,
        /// a porownanie kulturowe potrafi zalezec od ustawien systemu (dziura w determinizmie).
        /// </summary>
        public float FactorValue(string name)
        {
            if (Factors == null || string.IsNullOrEmpty(name))
            {
                return -1f;
            }
            for (int i = 0; i < Factors.Count; i++)
            {
                FactorScore fs = Factors[i];
                if (fs != null && string.CompareOrdinal(fs.Name, name) == 0)
                {
                    return fs.Value;
                }
            }
            return -1f;
        }

        /// <summary>
        /// Jedna linia rankingu do logu czytelnego, np.
        /// "#1 WandererJoin u=0.812 raw=0.812 p=0.341 klucz=... | contextFit=0.75 ...".
        ///
        /// Wypisujemy ZAROWNO Utility, JAK I RawUtility, bo dla kandydata zawetowanego
        /// te dwie liczby rozjezdzaja sie i wlasnie ta roznica jest interesujaca.
        /// Nazwa parametru pochodzi z zamrozonego kontraktu kroku 3 i nie jest tlumaczona.
        /// </summary>
        public string ToRankingLine(int pozycja)
        {
            var sb = new StringBuilder();
            sb.Append('#').Append(pozycja.ToString(CultureInfo.InvariantCulture))
              .Append(' ').Append(Label)
              .Append(" u=").Append(Utility.ToString("0.000", CultureInfo.InvariantCulture))
              .Append(" raw=").Append(RawUtility.ToString("0.000", CultureInfo.InvariantCulture))
              .Append(" p=").Append(SelectionProbability.ToString("0.000", CultureInfo.InvariantCulture));

            if (Vetoed)
            {
                sb.Append(" WETO");
                if (!string.IsNullOrEmpty(VetoReason))
                {
                    sb.Append('(').Append(VetoReason).Append(')');
                }
            }
            if (Rejected != RejectionStage.None)
            {
                sb.Append(" odrzucony=").Append(Rejected);
            }
            if (WeightsDegenerate)
            {
                sb.Append(" [WAGI ZDEGENEROWANE]");
            }
            if (HadInvalidFactor)
            {
                sb.Append(" [CZYNNIK NIEPOPRAWNY]");
            }
            if (!string.IsNullOrEmpty(SortKey))
            {
                sb.Append(" klucz=").Append(SortKey);
            }

            if (Factors != null && Factors.Count > 0)
            {
                sb.Append(" | ");
                for (int i = 0; i < Factors.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append("; ");
                    }
                    sb.Append(Factors[i] == null ? "?" : Factors[i].ToString());
                }
            }
            return sb.ToString();
        }

        /// <summary>Skrot do podgladu w debuggerze i do komunikatow bledu - bez sladu.</summary>
        public override string ToString()
        {
            return Label + " u=" + Utility.ToString("0.000", CultureInfo.InvariantCulture)
                   + (Vetoed ? " (zawetowany)" : string.Empty);
        }
    }
}
