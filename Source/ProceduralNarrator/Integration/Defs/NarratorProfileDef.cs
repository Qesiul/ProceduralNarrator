using System.Collections.Generic;
using System.Globalization;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Tension;
using Verse;

namespace ProceduralNarrator.Integration.Defs
{
    /// <summary>
    /// Deklaratywna definicja OSOBOWOSCI narratora (wzorzec Strategia, sekcja 4 koncepcji).
    /// Warstwa DANYCH: dolozenie czwartego profilu to dopisanie Defa w XML, bez linijki kodu.
    ///
    /// UWAGA: wezel XML musi uzywac PELNEJ nazwy typu z namespace'em, czyli
    /// &lt;ProceduralNarrator.Integration.Defs.NarratorProfileDef&gt;. Sama nazwa klasy jest
    /// po cichu ignorowana przez DirectXmlLoader - patrz sekcja 2 CLAUDE.md.
    ///
    /// PROFIL NIE JEST STORYTELLERDEFEM I TO JEST ISTOTA SPRAWY. Gdyby byl, staly by sie
    /// pozycja w menu wyboru narratora - czyli dokladnie tym modelem, ktory ta praca krytykuje
    /// (osobowosc Cassandry, Phoebe i Randy'ego to trzy pozycje na liscie i siedem liczb
    /// roznicy). Profil jest stanem WEWNETRZNYM systemu: losowanym raz na rozgrywke,
    /// utrwalanym w zapisie i nieujawnianym graczowi. W menu zostaje jedna pozycja - Ariadne.
    ///
    /// Efekt uboczny tej decyzji jest praktyczny: profile nie duplikuja listy 17 przejetych
    /// compow Cassandry ani portretow, wiec plik XML jest krotki, a nie dluzszy.
    /// </summary>
    public class NarratorProfileDef : Def
    {
        /// <summary>
        /// Waga w losowaniu profilu na starcie rozgrywki. Wieksza = czesciej wybierany.
        /// Zero wylacza profil BEZ usuwania go z katalogu - przydatne przy strojeniu
        /// i przy seriach kontrolnych, gdzie chcemy zawezic pule.
        /// </summary>
        public float selectionWeight = 1f;

        /// <summary>
        /// Wagi czynnikow zdarzeniowych. Nazwy wezlow dzieci sa identyczne jak w bloku
        /// &lt;weights&gt; StorytellerDefa - to ten sam typ i ten sam niezmiennik nazw.
        /// </summary>
        public ScoringWeights weights = new ScoringWeights();

        /// <summary>Ksztalt krzywej napiecia.</summary>
        public TensionParams tension = new TensionParams();

        /// <summary>
        /// Przepisuje Def na typ rdzenia. KOPIA, nie referencja: Def zyje w DefDatabase przez
        /// caly proces gry i jest wspoldzielony miedzy rozgrywkami, wiec przekazanie referencji
        /// pozwoliloby jednej rozgrywce po cichu przestroic profil innej. Sanitize() dziala
        /// wtedy na kopii i nie mutuje danych z pliku - dotyczy to takze audytu startowego
        /// (PNStartup.AuditProfiles), ktory do drugiego przegladu etapu 4 sanityzowal sam Def.
        /// Kompletnosc Clone (wszystkie pola publiczne, takze dodane w przyszlosci) walidator
        /// sprawdza refleksja.
        /// </summary>
        public NarratorProfile ToProfile()
        {
            return new NarratorProfile
            {
                Id = defName,
                Label = string.IsNullOrEmpty(label) ? defName : label,
                SelectionWeight = selectionWeight,
                Weights = (weights ?? new ScoringWeights()).Clone(),
                Tension = (tension ?? TensionParams.Default()).Clone()
            };
        }

        /// <summary>
        /// Waniliowy hook walidacji Defow - komunikaty laduja w Player.log przy starcie gry,
        /// zanim ktokolwiek zaczyna rozgrywke.
        /// </summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors())
            {
                yield return e;
            }

            if (selectionWeight < 0f)
            {
                yield return "selectionWeight jest ujemny ("
                             + selectionWeight.ToString("0.###", CultureInfo.InvariantCulture)
                             + ") - losowanie profilu traktuje go jak zero";
            }

            if (weights == null || weights.Total() <= 0f)
            {
                // Profil z zerowa suma wag dalby KAZDEMU kandydatowi uzytecznosc 0, wiec
                // narrator wybieralby wylacznie przez softmax po samych zerach, czyli czysto
                // losowo. Dziala, ale nie jest juz utility AI - i nikt by tego nie zauwazyl.
                yield return "suma wag scoringu wynosi zero - narrator z tym profilem "
                             + "wybieralby kandydatow losowo, bez oceny";
            }
        }
    }
}
