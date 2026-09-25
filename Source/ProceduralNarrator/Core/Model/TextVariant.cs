using System.Collections.Generic;
using ProceduralNarrator.Core.Conditions;

namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// WARIANT TEKSTU KLOCKA (krok 8, decyzja autora K8-6; koncepcja 5.7 i model danych:
    /// "warianty_tekstu: mapa kontekst -> opis, np. wariant frakcyjny").
    ///
    /// Wariant zastepuje textFragment klocka w liscie gracza, gdy jego warunki zachodza. Warunki sa
    /// czterech rodzajow i kazdy odpowiada innemu zrodlu prawdy:
    ///   - conditions         - swiat z POCZATKU TURY (ten sam WorldSnapshot, ktory widziala decyzja);
    ///                          tylko warunki TWARDE (klasy nadpisujace IsMet), bo tekst stwierdza fakt;
    ///   - min/maxIntensity   - moc KONCOWA zlozonego zdarzenia (suma wkladow slotow, klamrowana);
    ///   - requiresPointsScaling - akcja zdarzenia NAPRAWDE skaluje sie punktami zagrozenia
    ///                          (Block.ScalesWithPoints klocka akcji; audyt startowy porownuje z
    ///                          IncidentDef.pointsScaleable). Bez tego "mniejszy rozmach" przy incydencie,
    ///                          ktory punktow nie czyta, nie mialby w grze zadnego desygnatu;
    ///   - requiresFaction    - tekst z {FRAKCJA}; wybierany tylko wtedy, gdy PO wykonaniu wiadomo, jaka
    ///                          frakcje wybrala gra (parms.faction). Nazwa pochodzi z gry, wiec zdanie
    ///                          jest prawdziwe z konstrukcji.
    ///
    /// Wariant BEZ zadnego warunku jest ZAMIENNIKIEM tekstu bazowego (roznorodnosc bez kontekstu).
    /// Zasady wyboru i walidacji - TextComposer.
    ///
    /// Pola malymi literami, bo wypelnia je parser Defow gry (DirectXmlToObject) i parser walidatora.
    /// </summary>
    public sealed class TextVariant
    {
        /// <summary>Identyfikator unikalny w obrebie klocka; trafia do sladu (kolumna warianty=).</summary>
        public string id;

        /// <summary>Warunki twarde na swiecie z poczatku tury. Wszystkie musza zachodzic.</summary>
        public List<NarrativeCondition> conditions = new List<NarrativeCondition>();

        /// <summary>Dolna granica mocy koncowej zdarzenia (wlacznie).</summary>
        public IntensityLevel minIntensity = IntensityLevel.VeryLow;

        /// <summary>Gorna granica mocy koncowej zdarzenia (wlacznie).</summary>
        public IntensityLevel maxIntensity = IntensityLevel.VeryHigh;

        /// <summary>Tylko przy akcji skalujacej sie punktami zagrozenia.</summary>
        public bool requiresPointsScaling;

        /// <summary>Tylko przy frakcji znanej po wykonaniu; tekst musi zawierac {FRAKCJA}.</summary>
        public bool requiresFaction;

        /// <summary>Tresc wariantu (ASCII, decyzja autora K8-7).</summary>
        public string text;

        /// <summary>
        /// Czy wariant ma JAKIKOLWIEK warunek. Wariant z warunkiem jest "dopasowany do sytuacji"
        /// i ma pierwszenstwo przed tekstem bazowym i zamiennikami.
        /// </summary>
        public bool IsSpecific
        {
            get
            {
                return (conditions != null && conditions.Count > 0)
                       || minIntensity != IntensityLevel.VeryLow
                       || maxIntensity != IntensityLevel.VeryHigh
                       || requiresPointsScaling
                       || requiresFaction;
            }
        }

        /// <summary>
        /// Czy wariant pasuje. Brak snapshotu = warunki swiata NIE zachodza (wariant ze swiatem nie moze
        /// stwierdzac czegos, czego nikt nie sprawdzil).
        /// </summary>
        public bool Matches(WorldSnapshot snapshot, IntensityLevel mocKoncowa, bool akcjaSkalujeSie, string nazwaFrakcji)
        {
            if (conditions != null && conditions.Count > 0)
            {
                if (snapshot == null)
                {
                    return false;
                }
                for (int i = 0; i < conditions.Count; i++)
                {
                    if (conditions[i] == null || !conditions[i].IsMet(snapshot))
                    {
                        return false;
                    }
                }
            }
            if (mocKoncowa < minIntensity || mocKoncowa > maxIntensity)
            {
                return false;
            }
            if (requiresPointsScaling && !akcjaSkalujeSie)
            {
                return false;
            }
            if (requiresFaction && string.IsNullOrEmpty(nazwaFrakcji))
            {
                return false;
            }
            return true;
        }
    }
}
