using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ProceduralNarrator.Integration.Incidents
{
    /// <summary>
    /// ZLOZONY OPIS W LISCIE GRY (krok 8, decyzja autora K8-4) - bez Harmony i bez wlasnych workerow.
    ///
    /// Mechanizm: migawka listow PRZED naszym yield (LetterSnapshot.Take), a po wznowieniu iteratora -
    /// czyli po Storyteller.TryFire w tym samym ticku - wszystko, co przybylo, pochodzi z naszego
    /// incydentu (miedzy yield a wznowieniem gra wykonuje wylacznie TryFire tego jednego zdarzenia).
    /// Do pierwszego nowego ChoiceLetter dopisujemy opis NA POCZATKU; waniliowy tekst zostaje pod nim,
    /// z informacjami mechanicznymi (kwota okupu, kto dolacza, jakie zwierzeta).
    ///
    /// Zmierzone dekompilacja 1.5.4063: ChoiceLetter.Text ma publiczny setter; listy 12 z 13 naszych
    /// incydentow trafiaja na stos synchronicznie w TryExecute - takze listy zadan Wedrowca i Uchodzcow
    /// (QuestNode_Root_WandererJoin.RunInt wola SendLetter w trakcie generowania zadania). Meteoryt trzyma
    /// list w publicznym polu Skyfaller.impactLetter do chwili uderzenia - tam dopisujemy "odroczony".
    ///
    /// Od kroku 8 customLetterText NIE jest uzywane: honorowalo je 9 z 13 incydentow i zastepowalo caly
    /// tekst gry, razem z informacjami, ktorych gracz potrzebuje do decyzji.
    /// </summary>
    internal static class LetterAnnotator
    {
        /// <summary>Opis dopisany do listu, ktory juz jest na stosie.</summary>
        public const string Dopisany = "dopisany";

        /// <summary>Opis dopisany do listu czekajacego na uderzenie spadajacego obiektu.</summary>
        public const string Odroczony = "odroczony";

        /// <summary>Zdarzenie wykonane, ale nie przyszedl zaden list, do ktorego da sie dopisac.</summary>
        public const string Brak = "brak";

        /// <summary>Dopisywanie wylaczone w konfiguracji (useComposedLetter=false).</summary>
        public const string Wylaczony = "wylaczony";

        /// <summary>Zdarzenie niewykonane - listu nie ma czego opisywac.</summary>
        public const string Niewykonane = "niewykonane";

        /// <summary>Symulator: listow nie ma, tekst liczony tylko do sladu wariantow.</summary>
        public const string Symulacja = "symulacja";

        /// <summary>Sciezka spozniona: list gracz juz widzial, nie dopisujemy.</summary>
        public const string Pozno = "pozno";

        /// <summary>Pusty opis (klocki bez tekstu).</summary>
        public const string PustyOpis = "pustyOpis";

        /// <summary>
        /// Dopisuje opis do listu z naszego incydentu. Zwraca status do [PN-EXEC] list=.
        /// </summary>
        public static string Annotate(LetterSnapshot przed, Map map, string opis, out int nowychListow)
        {
            nowychListow = 0;
            if (string.IsNullOrEmpty(opis))
            {
                return PustyOpis;
            }
            if (przed == null || Find.LetterStack == null)
            {
                return Brak;
            }

            ChoiceLetter cel = null;
            List<Letter> listy = Find.LetterStack.LettersListForReading;
            for (int i = 0; i < listy.Count; i++)
            {
                Letter l = listy[i];
                if (l == null || przed.Letters.Contains(l))
                {
                    continue;
                }
                nowychListow++;
                if (cel == null)
                {
                    cel = l as ChoiceLetter;
                }
            }
            if (cel != null)
            {
                Dopisz(cel, opis);
                return Dopisany;
            }

            // List odroczony (meteoryt): nowy spadajacy obiekt z listem na uderzenie.
            if (map != null && map.listerThings != null)
            {
                List<Thing> posiadacze = map.listerThings.ThingsInGroup(ThingRequestGroup.ThingHolder);
                for (int i = 0; i < posiadacze.Count; i++)
                {
                    Skyfaller s = posiadacze[i] as Skyfaller;
                    if (s == null || s.impactLetter == null || przed.ImpactLetters.Contains(s.impactLetter))
                    {
                        continue;
                    }
                    ChoiceLetter odroczony = s.impactLetter as ChoiceLetter;
                    if (odroczony != null)
                    {
                        nowychListow++;
                        Dopisz(odroczony, opis);
                        return Odroczony;
                    }
                }
            }
            return Brak;
        }

        private static void Dopisz(ChoiceLetter list, string opis)
        {
            // Setter robi CapitalizeFirst - nasz opis zaczyna sie wielka litera, wiec nic nie zmienia.
            list.Text = opis + "\n\n" + list.Text;
        }
    }

    /// <summary>
    /// Migawka listow przed naszym yield: listy na stosie i listy czekajace w spadajacych obiektach mapy.
    /// </summary>
    internal sealed class LetterSnapshot
    {
        public readonly HashSet<Letter> Letters = new HashSet<Letter>();
        public readonly HashSet<Letter> ImpactLetters = new HashSet<Letter>();

        public static LetterSnapshot Take(Map map)
        {
            var m = new LetterSnapshot();
            if (Find.LetterStack != null)
            {
                List<Letter> listy = Find.LetterStack.LettersListForReading;
                for (int i = 0; i < listy.Count; i++)
                {
                    if (listy[i] != null)
                    {
                        m.Letters.Add(listy[i]);
                    }
                }
            }
            if (map != null && map.listerThings != null)
            {
                List<Thing> posiadacze = map.listerThings.ThingsInGroup(ThingRequestGroup.ThingHolder);
                for (int i = 0; i < posiadacze.Count; i++)
                {
                    Skyfaller s = posiadacze[i] as Skyfaller;
                    if (s != null && s.impactLetter != null)
                    {
                        m.ImpactLetters.Add(s.impactLetter);
                    }
                }
            }
            return m;
        }
    }
}
