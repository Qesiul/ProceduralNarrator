using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Integration.Defs;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration
{
    /// <summary>
    /// Diagnostyka startowa. Uruchamia sie zaraz po zaladowaniu Defow, wiec od razu
    /// widac w Player.log, czy warstwa danych w ogole wstala - bez czekania na
    /// pierwsze wydarzenie.
    ///
    /// Powod: pusty katalog klockow daje PASS przy kazdej decyzji, co z zewnatrz
    /// wyglada jak "narrator dziala, tylko nic nie robi". Ten log rozstrzyga to w 5 sekund.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PNStartup
    {
        static PNStartup()
        {
            List<NarrativeBlockDef> blocks = DefDatabase<NarrativeBlockDef>.AllDefsListForReading;

            if (blocks.NullOrEmpty())
            {
                PNLog.Error(
                    "KATALOG KLOCKOW PUSTY - zero NarrativeBlockDef w DefDatabase. "
                    + "Narrator bedzie zwracal PASS przy kazdej decyzji. Sprawdz: "
                    + "(1) czy istnieje Defs/Blocks/Blocks_Core.xml; "
                    + "(2) czy wezly XML uzywaja PELNEJ nazwy typu, czyli "
                    + "<ProceduralNarrator.Integration.Defs.NarrativeBlockDef> - sama "
                    + "<NarrativeBlockDef> NIE zadziala, bo GenTypes rozwiazuje nazwy "
                    + "typow przez Assembly.GetType, ktore wymaga namespace'u; "
                    + "(3) czy gra zostala URUCHOMIONA PONOWNIE po zmianie plikow "
                    + "- RimWorld czyta Defy i DLL wylacznie przy starcie procesu.");
            }
            else
            {
                var wgTypu = new StringBuilder();
                foreach (BlockType t in System.Enum.GetValues(typeof(BlockType)))
                {
                    int n = blocks.Count(b => b.blockType == t);
                    if (n > 0)
                    {
                        if (wgTypu.Length > 0)
                        {
                            wgTypu.Append(", ");
                        }
                        wgTypu.Append(t).Append('=').Append(n);
                    }
                }

                int krawedzie = blocks.Sum(b => b.incompatibleWith != null ? b.incompatibleWith.Count : 0);
                PNLog.Decision("START OK - " + blocks.Count + " klockow (" + wgTypu + "), "
                               + krawedzie + " zabronionych krawedzi.");

                // Obiekty warunkow to typy polimorficzne z Core, wskazywane w XML przez Class=.
                // Gdyby ktorys sie nie rozwiazal, RimWorld pominalby go PO CICHU i narrator
                // straciłby filtrowanie kontekstowe, zachowujac sie pozornie poprawnie.
                // Dlatego liczymy je jawnie i porownujemy z oczekiwaniem.
                int twarde = blocks.Sum(b => b.conditions != null ? b.conditions.Count : 0);
                int miekkie = blocks.Sum(b => b.preferences != null ? b.preferences.Count : 0);
                PNLog.Decision("Warunki: " + twarde + " twardych, " + miekkie + " miekkich.");

                if (twarde == 0 && miekkie == 0)
                {
                    PNLog.Error("Zero warunkow mimo ich obecnosci w XML - sprawdz, czy atrybuty "
                                + "Class= uzywaja PELNYCH nazw typow z namespace'em "
                                + "(ProceduralNarrator.Core.Conditions.*).");
                }

                // Wiszaca referencja w incompatibleWith po cichu oslabia graf, wiec zglaszamy.
                var znane = new HashSet<string>(blocks.Select(b => b.defName));
                foreach (NarrativeBlockDef b in blocks)
                {
                    if (b.incompatibleWith == null)
                    {
                        continue;
                    }
                    foreach (string other in b.incompatibleWith.Where(o => !znane.Contains(o)))
                    {
                        PNLog.Warn("Klocek " + b.defName + " odwoluje sie do nieistniejacego " + other);
                    }
                }
            }

            StorytellerDef narrator = DefDatabase<StorytellerDef>.GetNamedSilentFail("PN_GenerativeNarrator");
            PNLog.Decision(narrator != null
                ? "Narrator zarejestrowany w menu jako \"" + narrator.label + "\"."
                : "UWAGA: StorytellerDef PN_GenerativeNarrator NIE zaladowal sie.");
        }
    }
}
