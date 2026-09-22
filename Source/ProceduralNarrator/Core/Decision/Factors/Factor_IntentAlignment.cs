using System;
using System.Globalization;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Czynnik "zgodnosc z intencja narratora" po stronie ZDARZEN.
    ///
    /// Od kroku 4 czynnik jest PELNY: krzywa dramaturgiczna wyznacza intencje i docelowa
    /// intensywnosc, a ten czynnik mierzy, jak blisko kandydat jest tego, czego narrator chce.
    /// Do kroku 3 wlacznie byl zaslepka zwracajaca stale 0.5 przy wadze 0 - wpieta juz wtedy
    /// po to, zeby zestaw i kolejnosc kolumn logu badawczego byly identyczne przed i po
    /// wlaczeniu krzywej, wiec serie rozgrywek daja sie porownac wprost.
    ///
    /// DWA SKLADNIKI, BO INTENCJA MOWI O DWOCH RZECZACH NARAZ:
    ///   LADUNEK  - czy zdarzenie ma byc dobre czy zle i jak duze (walencja razy skala),
    ///   MOC      - jak mocna ma byc dawka (IntensityLevel, ktory idzie na punkty zagrozenia).
    /// Sam ladunek nie wystarcza, bo dwa zdarzenia o tej samej walencji i skali moga miec
    /// rozna intensywnosc; sama moc nie wystarcza, bo nie ma znaku.
    ///
    /// LADUNEK LICZYMY PRZEZ Factor_DramaticContrast.Charge, A NIE WLASNYM MAPOWANIEM.
    /// Kanoniczne przelozenie walencji i skali na liczby zyje tam i jest w projekcie jedyne.
    /// Druga kopia rozjechalaby sie przy pierwszej zmianie osi, a oba wyniki nadal wygladalyby
    /// sensownie - czyli awaria niewidoczna w zadnym logu.
    ///
    /// UWAGA NA BLIZNIAKA: identycznie nazwany czynnik istnieje w ROZLACZNEJ przestrzeni
    /// czynnikow PASS (zgodnosc CISZY z intencja) i ma wlasna wage w PassScoringParams.
    /// To dwa rozne pytania o niezaleznej kalibracji - nie wolno ich unifikowac.
    /// </summary>
    public class Factor_IntentAlignment : IScoringFactor
    {
        /// <summary>
        /// Nazwa wyprowadzona z nazwy pola wagi (nie literal), zeby niezmiennika
        /// "nazwa czynnika == nazwa pola wagi == nazwa wezla XML" pilnowal kompilator.
        /// Wiaze czynnik z waga ScoringWeights.intentAlignment, a NIE z jego imiennikiem
        /// z przestrzeni PASS.
        /// </summary>
        public const string FactorName = nameof(ScoringWeights.intentAlignment);

        /// <summary>Umowna wartosc "brak informacji", wspolna dla calej warstwy decyzyjnej.</summary>
        public const float NeutralValue = 0.5f;

        /// <summary>Udzial skladnika LADUNKU w wyniku. Znak wazy wiecej niz dawka.</summary>
        public const float ChargeWeight = 0.70f;

        /// <summary>Udzial skladnika MOCY.</summary>
        public const float MagnitudeWeight = 0.30f;

        /// <summary>
        /// Pelna rozpietosc ladunku: Charge nalezy do [-1, 1], wiec najwieksza mozliwa
        /// odleglosc miedzy kandydatem a zyczeniem wynosi 2.
        /// </summary>
        private const float ChargeSpan = 2f;

        /// <summary>
        /// Pelna rozpietosc skali IntensityLevel: od VeryLow (-2) do VeryHigh (+2).
        /// </summary>
        private const float IntensitySpan = 4f;

        public string Name
        {
            get { return FactorName; }
        }

        /// <summary>
        /// Ladunek, ktorego narrator sobie zyczy przy danej intencji.
        ///
        /// WARTOSCI ODPOWIADAJA REALNIE OSIAGALNEMU ZBIOROWI, a nie teoretycznemu [-1, 1].
        /// Dzisiejszy katalog (13 akcji) produkuje piec ladunkow: -1.00 (negatywne wielkie),
        /// -0.60 (negatywne srednie), -0.25 (negatywne drobne - PN_Akcja_Amok), 0.00 (neutralne
        /// drobne) i +0.25 (pozytywne drobne). Zyczenie +1.00 przy Breathe byloby wiec
        /// nieosiagalne dla KAZDEGO kandydata i cala intencja "daj oddech" sprowadzalaby sie
        /// do stalego przesuniecia wyniku w dol - czyli do niczego, bo softmax jest
        /// niewrazliwy na stala addytywna. Skrajne wartosci Escalate i Breathe walidator
        /// porownuje z min/max ladunku wyprowadzonym z WCZYTANEGO katalogu akcji.
        ///
        /// Hold celuje w -0.30, a nie w 0.00: katalog jest przechylony ku zdarzeniom negatywnym
        /// i "utrzymaj poziom" ma znaczyc utrzymanie TEGO rozkladu, a nie dryf ku rzadkim
        /// zdarzeniom neutralnym. -0.30 to srednia ladunku katalogu (-0.31 po akcjach, -0.30 po
        /// kandydatach), a NIE srodek przedzialu [-1.00, +0.25] (to byloby -0.375). Najblizsza
        /// klasa to dzis Negative/Minor (-0.25, dopasowanie 0.975) - PN_Akcja_Amok dodano
        /// wlasnie po to, zeby rozbic remis 0.850 przy Hold (Blocks_Extended.xml). Walidator
        /// pilnuje porzadku Escalate &lt; Hold &lt; Breathe; sama wartosc Hold jest kalibracja.
        ///
        /// Tablica, a nie wzor - swiadomie, tak samo jak w Factor_PassIntent.FitFor.
        /// Wzor sugerowalby, ze intencje leza na jednej osi liczbowej w ustalonych odstepach,
        /// a one leza na osi POJECIOWEJ i ich odstepy sa kalibracja, nie geometria.
        /// </summary>
        public static float DesiredCharge(Intent intent)
        {
            switch (intent)
            {
                case Intent.Escalate:
                    return -1.00f;
                case Intent.Breathe:
                    return 0.25f;
                case Intent.Hold:
                    return -0.30f;
                case Intent.Pass:
                    // Intencja zarezerwowana - IntentSelector jej nie zwraca (patrz tam).
                    // Gdyby jednak dotarla tu z zapisu w starszym formacie albo z testu,
                    // traktujemy ja jak Breathe: obie znacza "nie dokladaj ciezaru".
                    return 0.25f;
                default:
                    // Nowa wartosc enuma dodana bez aktualizacji tej tablicy. Neutralny
                    // srodek realnego zakresu jest jedyna odpowiedzia, ktora nie klamie.
                    return -0.30f;
            }
        }

        /// <summary>
        /// Mierzy, jak blisko kandydat jest tego, czego chce krzywa dramaturgiczna.
        ///
        /// Funkcja CZYSTA: zero stanu, zero losowosci, wynik w [0,1]. Kandydat PASS nie ma
        /// zlozonego zdarzenia, wiec dostaje wartosc neutralna - jego wlasna zgodnosc
        /// z intencja jest liczona osobno, w przestrzeni PASS, przez Factor_PassIntent.
        /// </summary>
        public float Evaluate(ScoredCandidate candidate, DecisionContext context, out string explanation)
        {
            Intent intent = context == null ? Intent.Hold : context.Intent;

            ComposedEvent zdarzenie = candidate == null ? null : candidate.Event;
            if (zdarzenie == null)
            {
                explanation = "brak zlozonego zdarzenia (kandydat PASS albo pusty) - wartosc neutralna 0.50";
                return NeutralValue;
            }

            float zadanyLadunek = DesiredCharge(intent);
            float ladunekKandydata = Factor_DramaticContrast.Charge(zdarzenie.Valence, zdarzenie.Scale);
            float zgodnoscLadunku = 1f - Math.Abs(ladunekKandydata - zadanyLadunek) / ChargeSpan;

            float zadanaMoc = context == null ? 0f : context.TargetIntensity;
            float mocKandydata = (int)zdarzenie.Intensity;
            float zgodnoscMocy = 1f - Math.Abs(mocKandydata - zadanaMoc) / IntensitySpan;

            float wynik = Curves.Clamp01(ChargeWeight * Curves.Clamp01(zgodnoscLadunku)
                                         + MagnitudeWeight * Curves.Clamp01(zgodnoscMocy));

            explanation = "intencja=" + intent
                          + " ladunek " + ladunekKandydata.ToString("0.00", CultureInfo.InvariantCulture)
                          + " wobec " + zadanyLadunek.ToString("0.00", CultureInfo.InvariantCulture)
                          + " (zgodnosc " + zgodnoscLadunku.ToString("0.00", CultureInfo.InvariantCulture) + ")"
                          + ", moc " + mocKandydata.ToString("0", CultureInfo.InvariantCulture)
                          + " wobec " + zadanaMoc.ToString("0.00", CultureInfo.InvariantCulture)
                          + " (zgodnosc " + zgodnoscMocy.ToString("0.00", CultureInfo.InvariantCulture) + ")";

            return wynik;
        }
    }
}
