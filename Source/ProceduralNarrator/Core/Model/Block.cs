using System.Collections.Generic;
using System.Linq;
using ProceduralNarrator.Core.Blackboard;
using ProceduralNarrator.Core.Conditions;
using ProceduralNarrator.Core.PlayerModel;

namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Elementarny klocek wydarzenia. Struktura czysto danowa - rdzen nie wie,
    /// czym jest Payload; interpretuje go dopiero warstwa integracji.
    /// </summary>
    public class Block
    {
        public string Id;
        public BlockType Type;

        // ================================================================================
        //  UMOWA DANYCH: CO WNOSI KTORY SLOT
        // ================================================================================
        //
        //   Theme / Valence / Scale  -> WYLACZNIE klocek AKCJI. To on decyduje, CZYM zdarzenie
        //                               jest; EventComposer kopiuje te trzy pola z akcji i nie
        //                               oglada ich na zadnym innym slocie.
        //   warunki, preferencje,    -> KAZDY slot. Tedy pozostale klocki wplywaja na wybor
        //   tekst                       (przez contextFit) i na opis.
        //   Intensity                -> AGREGOWANA z calej kompozycji (suma wkladow pieciu slotow,
        //                               klamrowana do zakresu skali).
        //
        // DLACZEGO OSIE NIE SA AGREGOWANE - i dlaczego nie nalezy tego "naprawiac".
        // Sumowanie walencji albo glosowanie tematow oznaczaloby, ze neutralny aktor i pozytywny
        // wyzwalacz OSLABIAJA negatywny charakter napadu. Nie da sie tego sensownie wytlumaczyc
        // ani graczowi, ani w rozdziale o ewaluacji: noc, obrzeza kolonii czy opis sprawcy sa
        // OKOLICZNOSCIA zdarzenia, a nie jego istota. Przypadkowa zamiana napadu w zdarzenie
        // neutralne byla by regresja, nie wzbogaceniem.
        //
        // KONSEKWENCJA, ktora trzeba znac: wszystkie warianty jednej akcji maja identyczna
        // trojke osi, wiec freshness i dramaticContrast ich NIE ROZROZNIAJA. To nie jest wada.
        // Warianty roznia sie okolicznoscia i dawka, a nie charakterem narracyjnym - i dokladnie
        // te dwie rzeczy roznicuja je w scoringu (contextFit i intentAlignment przez intensywnosc).
        // Mocniejsze roznicowanie mialoby sens dopiero wtedy, gdyby wariant zmienial ZNACZENIE
        // zdarzenia, a nie jego oprawe.
        //
        // Pola zostaja na Block (a nie przenosza sie do osobnego typu akcji), bo katalog jest
        // jednorodny i tak go czyta DirectXmlToObject. Pilnuje tego audyt startowy
        // (PNStartup.AuditBlockAxes) plus asercja walidatora - deklaracja osi na klocku innym
        // niz akcja jest od teraz zglaszana, zamiast lezec cicho i mylic autora tresci.
        // ================================================================================
        public Theme Theme = Theme.Natural;
        public Valence Valence = Valence.Neutral;
        public EventScale Scale = EventScale.Moderate;

        /// <summary>Wolne tagi na klimat i warianty. Scoring ich NIE uzywa.</summary>
        public HashSet<string> Tags = new HashSet<string>();

        /// <summary>Ladunek dla warstwy integracji (dla klocka akcji: defName incydentu).</summary>
        public string Payload;

        /// <summary>Intencja co do sily zdarzenia. Przeklada sie na punkty w Integration.</summary>
        public IntensityLevel Intensity = IntensityLevel.Normal;

        /// <summary>
        /// Czy incydent tego klocka AKCJI przyjmuje frakcje sprawcy (krok 5, ciaglosc frakcji
        /// w lukach). Deklaracja danych, nie zgadywanie po defName: audyt startowy dopuszcza ja
        /// tylko wtedy, gdy worker incydentu jest typu IncidentWorker_RaidEnemy - jedynym z naszych
        /// trzynastu, ktory honoruje parms.faction (sprawdzone dekompilacja 1.5.4063).
        /// </summary>
        public bool CarriesFaction;

        /// <summary>Fragment opisu narracyjnego wnoszony przez ten klocek.</summary>
        public string TextFragment;

        /// <summary>
        /// Warianty tekstu zalezne od kontekstu (krok 8, decyzja autora K8-6). Czyta wylacznie
        /// TextComposer przy skladaniu listu gracza; scoring, graf i sygnatura kandydata ich nie widza.
        /// </summary>
        public List<TextVariant> TextVariants = new List<TextVariant>();

        /// <summary>
        /// Czy incydent tego klocka AKCJI skaluje sie punktami zagrozenia (krok 8). Deklaracja danych -
        /// audyt startowy porownuje ja z IncidentDef.pointsScaleable; walidator pilnuje, ze deklaruje ja
        /// wylacznie klocek akcji. Czyta wariant tekstu z requiresPointsScaling.
        /// </summary>
        public bool ScalesWithPoints;

        /// <summary>Bramki spojnosci - klocek jest niedostepny, gdy ktorakolwiek nie przejdzie.</summary>
        public List<NarrativeCondition> Conditions = new List<NarrativeCondition>();

        /// <summary>Preferencje kontekstowe - nie blokuja, zasilaja contextFit.</summary>
        public List<NarrativeCondition> Preferences = new List<NarrativeCondition>();

        /// <summary>
        /// Fakty, ktore klocek zostawia w pamieci po POTWIERDZONYM wykonaniu zdarzenia (krok 6).
        ///
        /// Pole nalezy do KAZDEGO typu klocka, nie tylko do konsekwencji - slot Consequence jest
        /// naturalnym nosnikiem sladu, ale nic nie stoi na przeszkodzie, zeby akcja albo modyfikator
        /// tez cos zapamietaly. Ograniczanie tego typem bylo by regula w kodzie tam, gdzie wystarczy
        /// dyscyplina w danych.
        /// </summary>
        public List<FactWrite> FactsOnExecute = new List<FactWrite>();

        /// <summary>
        /// Czego dotyczy zdarzenie w jezyku cech stylu gracza (krok 7, decyzja autora nr 20). Deklaruje
        /// WYLACZNIE klocek akcji - tak jak osie; u innych klockow zostaja zera (pilnuje walidator
        /// i audyt startowy). Czyta StyleFocus.
        /// </summary>
        public StyleWeights StyleWeights = new StyleWeights();

        public bool HasTag(string tag)
        {
            return tag == null || Tags.Contains(tag);
        }

        /// <summary>Czy wszystkie twarde warunki sa spelnione w danym stanie swiata.</summary>
        public bool IsAvailable(WorldSnapshot snapshot)
        {
            // Brak snapshotu = brak filtrowania. Pozwala testowac sama kompozycje.
            if (snapshot == null)
            {
                return true;
            }
            return Conditions.All(c => c.IsMet(snapshot));
        }

        /// <summary>Pierwszy niespelniony warunek - do sladu decyzji w logu.</summary>
        public string FirstUnmetCondition(WorldSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }
            NarrativeCondition failed = Conditions.FirstOrDefault(c => !c.IsMet(snapshot));
            return failed != null ? failed.Describe() : null;
        }

        public override string ToString()
        {
            return Type + ":" + Id;
        }
    }
}
