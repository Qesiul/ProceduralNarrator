using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Integration.Defs;
using ProceduralNarrator.Integration.Storyteller;
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
    ///
    /// Od kroku 3 sprawdzamy tu druga awarie o dokladnie tym samym objawie: nierozpoznany
    /// wezel konfiguracji w StorytellerDefie. Gdy &lt;weights&gt; albo &lt;pass&gt; sie nie
    /// zdeserializuje, obiekt powstanie z wartosciami domyslnymi i NIKT sie nie dowie, ze
    /// kalibracja z XML nie zadzialala - narrator bedzie dzialal, tylko nie tak, jak napisano
    /// w pliku. Dlatego wypisujemy EFEKTYWNE wartosci wszystkich parametrow decyzyjnych.
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
                // stracilby filtrowanie kontekstowe, zachowujac sie pozornie poprawnie.
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

                // Klocek AKCJI bez preferencji nie wnosi NIC do dopasowania kontekstowego, a
                // ContextEvaluator zwraca 1.0, gdy cala pula preferencji kandydata jest pusta
                // ("brak informacji nie jest kara" - decyzja projektowa kroku 2). Zderzenie tych
                // dwoch regul daje kandydata z bezwarunkowym SUFITEM na osi o najwyzszej wadze,
                // czyli darmowa premie okolo +0.44 uzytecznosci, ktorej zaden trafny kontekstowo
                // kandydat nie przebije. Zmierzone przed naprawa: 2 z 84 kandydatow mialy
                // contextFit = 1.0000 w KAZDYM kontekscie, przez co RaidEnemy wypadal poza pasmo
                // near-best i nie mogl wygrac przy ZADNYM ziarnie.
                //
                // To wada DANYCH, nie kodu, wiec pilnuje jej diagnostyka, a nie asercja.
                var bezPreferencji = blocks
                    .Where(b => b.blockType == BlockType.Action)
                    .Where(b => b.preferences == null || b.preferences.Count == 0)
                    .Select(b => b.defName)
                    .ToList();

                if (bezPreferencji.Count > 0)
                {
                    PNLog.Warn("Klocki akcji BEZ preferencji (" + bezPreferencji.Count + "): "
                               + string.Join(", ", bezPreferencji.ToArray())
                               + ". Nie wnosza nic do contextFit, a kandydat zlozony z samych takich "
                               + "klockow dostaje bezwarunkowe contextFit=1.0 i wypycha z pasma "
                               + "kandydatow faktycznie trafnych. Dopisz im preferencje.");
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

            AuditDecisionConfig();

            // Naglowek formatu danych badawczych wypisujemy raz, przed jakakolwiek decyzja.
            // Dzieki temu skrypt agregujacy z kroku 8 czyta kolejnosc kolumn z tego samego pliku,
            // z ktorego czyta dane, zamiast miec ja zaszyta u siebie.
            PNLog.DataHeader();
        }

        /// <summary>
        /// Audyt konfiguracji warstwy decyzyjnej we WSZYSTKICH storytellerach, ktore uzywaja
        /// naszego kompomentu - nie tylko w PN_GenerativeNarrator, bo comp moze zostac wpiety
        /// w kolejny Def (np. druga "osobowosc" narratora w kroku 4) i wtedy tez ma byc sprawdzony.
        ///
        /// Wykrywamy tu klase awarii, ktora nie daje ZADNEGO objawu w runtime: nierozpoznany
        /// wezel XML zostawia pola na inicjalizatorach C#, wiec narrator dziala, tylko wedlug
        /// innych liczb niz te w pliku. Jedynym dowodem, ze XML zadzialal, jest wypisanie
        /// wartosci EFEKTYWNYCH - czyli tych, ktore faktycznie siedza w obiekcie.
        /// </summary>
        private static void AuditDecisionConfig()
        {
            List<StorytellerDef> storytellers = DefDatabase<StorytellerDef>.AllDefsListForReading;
            int znalezione = 0;

            foreach (StorytellerDef st in storytellers)
            {
                if (st.comps == null)
                {
                    continue;
                }

                foreach (StorytellerCompProperties comp in st.comps)
                {
                    var nasz = comp as StorytellerCompProperties_Generative;
                    if (nasz == null)
                    {
                        continue;
                    }

                    znalezione++;

                    // Sanitize NAJPIERW: log ma pokazywac wartosci, ktorych narrator faktycznie
                    // uzyje, a nie te sprzed korekty. Kazda korekta jest wypisywana osobno -
                    // cicha korekta jest gorsza od zlej wartosci, bo w XML widnieje jedna liczba,
                    // a system pracuje na innej.
                    string poprawki = nasz.Sanitize();
                    if (!string.IsNullOrEmpty(poprawki))
                    {
                        PNLog.Warn("Konfiguracja " + st.defName + " poprawiona: " + poprawki);
                    }

                    PNLog.Decision("Parametry decyzyjne (" + st.defName + "): " + nasz.DescribeEffective());
                }
            }

            if (znalezione == 0)
            {
                PNLog.Warn("Zaden StorytellerDef nie uzywa StorytellerCompProperties_Generative - "
                           + "warstwa decyzyjna nie zostanie nigdy uruchomiona. Sprawdz atrybut Class= "
                           + "w Defs/Storytellers/Storyteller_Generative.xml (wymagana PELNA nazwa typu).");
            }
        }
    }
}
