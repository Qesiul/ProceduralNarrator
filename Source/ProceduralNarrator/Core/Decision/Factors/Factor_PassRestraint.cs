using System.Collections.Generic;
using System.Globalization;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Czynnik "powsciagliwosc": im gestszy byl ostatni ciag wydarzen, tym cenniejsza jest cisza.
    ///
    /// DLACZEGO GESTOSC, A NIE "ILE DNI OD OSTATNIEGO ZDARZENIA":
    /// gdyby powsciagliwosc rosla z czasem od ostatniego zdarzenia, rosla by RAZEM ze swiezoscia
    /// kandydatow zdarzeniowych i powstalo by sprzezenie DODATNIE - im dluzej cisza, tym bardziej
    /// oplaca sie milczec dalej. Tutaj jest odwrotnie: gestosc zanika wykladniczo, wiec kazda tura
    /// bez zdarzenia OSLABIA PASS, podczas gdy swiezosc zdarzen rosnie. Sprzezenie jest ujemne
    /// i samostabilizujace. To wlasnosc konstrukcyjna, nie przypadek, i pilnuje jej test
    /// antysprzezenia (na tej samej historii U_pass maleje w czasie, a swiezosc rosnie).
    ///
    /// KSZTALT: gestosc D to suma wkladow wszystkich pamietanych zdarzen, kazde wazone zanikiem
    /// polowicznym 0.5^(wiek/halfLifeDays). Potem liniowa rampa D na przedziale
    /// [densityFloor, densitySaturation] daje wynik w 0..1.
    ///
    /// WYPROWADZENIE DOMYSLNYCH PROGOW (liczby sa policzone, nie zgadniete):
    ///   halfLifeDays = 2 * mtbDays = 2 * 2.5 = 5.0  (pamiec o dwa typowe odstepy miedzy zdarzeniami)
    ///   w stanie ustalonym procesu Poissona o intensywnosci 1/mtbDays:
    ///     E[D]  = halfLifeDays / (mtbDays * ln 2) = 5 / (2.5 * 0.6931) = 2.886
    ///     sd[D] = sqrt(E[D] / 2) = 1.201
    ///   densityFloor      = 0.52 * E[D] = 1.5   (ponizej sredniej cisza nic nie warta)
    ///   densitySaturation = 1.56 * E[D] = 4.5   (mniej wiecej kwantyl 0.89 rozkladu D)
    ///
    /// REGULA STROJENIA - JEDNO POKRETLO (do zapisania w pracy):
    /// sufit uzytecznosci PASS wynosi 1.0, wiec PASS MOZE przebic kazdego kandydata, takze idealnego.
    /// To jest swiadome: PASS konkuruje na rownych prawach, a nie jako opcja awaryjna. Gdy zmierzony
    /// udzial PASS przekroczy 12% (wtedy mtb_eff = 2.5/(1-q) > 2.85 dnia i sypie sie argument
    /// o rownym budzecie zdarzen wobec Cassandry), PODNOSI SIE densitySaturation - przesuwa to moment
    /// osiagniecia sufitu. NIE WOLNO w tym celu ruszac weightBaseline ani obnizac weightRestraint:
    /// obie te zmiany podnosza PODLOGE uzytecznosci PASS (udzial w_b / sumW rosnie) i daja efekt
    /// DOKLADNIE ODWROTNY do zamierzonego.
    /// </summary>
    public class Factor_PassRestraint : IScoringFactor
    {
        /// <summary>
        /// Nazwa czynnika == nazwa wagi w PassScoringParams == nazwa wezla XML. Stala jest brana
        /// wprost z PassScoringParams, zeby literowka nie mogla rozjechac tych trzech miejsc -
        /// rozjazd oznaczalby ciche wyzerowanie wagi i martwy czynnik, bez zadnego bledu.
        /// </summary>
        public const string FactorName = PassScoringParams.RestraintFactorName;

        private readonly PassScoringParams p;

        public Factor_PassRestraint(PassScoringParams parameters)
        {
            // null oznacza nieudana deserializacje bloku <pass> z XML. Podstawiamy domysly zamiast
            // rzucac wyjatkiem - wyjatek w MakeIntervalIncidents zabija cala ture narratora.
            p = parameters ?? PassScoringParams.Defaults();
        }

        public string Name
        {
            get { return FactorName; }
        }

        public float Evaluate(ScoredCandidate candidate, DecisionContext context, out string explanation)
        {
            // Straznik kontraktu. Czynniki PASS i czynniki zdarzeniowe to DWIE ROZLACZNE tablice
            // i UtilityScorer nigdy ich nie miesza; straznik istnieje po to, zeby ewentualna pomylka
            // w okablowaniu wyszla w tescie i w sladzie, a nie objawila sie dopiero w grze.
            if (candidate != null && !candidate.IsPass)
            {
                explanation = "BLAD: czynnik PASS na kandydacie-zdarzeniu";
                return 0f;
            }

            if (context == null)
            {
                explanation = "brak kontekstu decyzji - gestosc 0";
                return 0f;
            }

            float density = Density(context.History, context.GameDay, p.halfLifeDays);
            float restraint = Restraint(density, p.densityFloor, p.densitySaturation);

            int wpisow = context.History == null ? 0 : context.History.Count;
            explanation = "gestosc " + Fmt(density)
                          + " z " + wpisow.ToString(CultureInfo.InvariantCulture) + " wpisow"
                          + ", rampa " + Fmt(p.densityFloor) + ".." + Fmt(p.densitySaturation)
                          + ", polowicznosc " + Fmt(p.halfLifeDays) + " dnia";
            return restraint;
        }

        /// <summary>
        /// Gestosc ostatnich wydarzen: suma zanikow polowicznych po CALEJ pamietanej historii.
        /// Publiczna, bo siega po nia takze log decyzji (NarratorDecision.RecentDensity) i testy.
        ///
        /// BRAK OKNA CZASOWEGO jest celowy: okno wprowadzaloby nieciaglosc uzytecznosci w chwili
        /// wypadniecia wpisu poza horyzont. Zanik zalatwia to gladko - wpis sprzed 30 dni wnosi
        /// 0.5^6 = 0.016, sprzed 60 dni 0.0002 - a pojemnosc bufora (24) trzyma koszt na poziomie
        /// najwyzej 24 potegowan na ture.
        ///
        /// Bufor NIE ZAWIERA wpisow PASS (decyzje PASS licza sie skalarami EventHistory), wiec nie
        /// trzeba ich tu odsiewac - cisza z zalozenia nie zageszcza narracji.
        /// </summary>
        public static float Density(EventHistory history, float nowGameDay, float halfLifeDays)
        {
            if (history == null)
            {
                return 0f;
            }

            IReadOnlyList<EventHistoryEntry> wpisy = history.Entries;
            if (wpisy == null || wpisy.Count == 0)
            {
                return 0f;
            }

            // Suma jest przemienna, wiec kolejnosc wpisow nie ma znaczenia - bufor cykliczny moze
            // byc rozspojony i wynik i tak bedzie ten sam. Akumulacja w double, zeby przy 24 wpisach
            // blad zaokraglenia nie ruszyl trzeciego miejsca po przecinku w punktach odniesienia.
            double suma = 0.0;
            for (int i = 0; i < wpisy.Count; i++)
            {
                EventHistoryEntry wpis = wpisy[i];
                if (wpis == null)
                {
                    continue;
                }

                // Curves.HalfLifeDecay klamruje ujemna delte do zera, wiec cofniety czas gry
                // (wczytanie starszego zapisu, tryb deweloperski) daje wklad 1.0 zamiast NaN.
                // To najbardziej zachowawczy wynik - nadmiar ciszy zamiast awarii.
                suma += Curves.HalfLifeDecay(nowGameDay - wpis.GameDay, halfLifeDays);
            }

            return (float)suma;
        }

        /// <summary>
        /// Liniowa rampa gestosci na przedzial 0..1.
        /// Nie delegujemy do Curves.Ramp, bo Ramp obsluguje wylacznie zdegenerowany przypadek
        /// from == to, a przy saturation mniejszym od floor dalby rampe ODWROCONA (ujemny
        /// mianownik). Tutaj saturation &lt;= floor ma znaczyc "prog skokowy", bo tak da sie
        /// swiadomie skonfigurowac ostrego narratora; PassScoringParams.Sanitize odnotowuje to raz.
        /// </summary>
        public static float Restraint(float density, float floor, float saturation)
        {
            if (float.IsNaN(density) || float.IsInfinity(density))
            {
                return 0f;
            }

            if (saturation <= floor)
            {
                return density >= saturation ? 1f : 0f;
            }

            return Curves.Clamp01((density - floor) / (saturation - floor));
        }

        private static string Fmt(float v)
        {
            // InvariantCulture bezwyjatkowo: przy locale pl-PL przecinek dziesietny rozwalilby
            // parsowanie sladu w skryptach ewaluacyjnych kroku 8.
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
