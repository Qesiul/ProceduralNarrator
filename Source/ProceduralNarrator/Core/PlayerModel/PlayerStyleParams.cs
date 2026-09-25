using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProceduralNarrator.Core.PlayerModel
{
    /// <summary>Parametry jednego pomiaru stylu (wezel &lt;li&gt; listy &lt;signals&gt;).</summary>
    public class StyleSignalParams
    {
        /// <summary>
        /// Nazwa pomiaru jako TEKST (StyleSignals.TryParse), nie enum: parser Defow przy blednej
        /// wartosci enuma podstawia cicho wartosc domyslna, a wtedy dwa wpisy mierzylyby Obrone.
        /// </summary>
        public string signal = string.Empty;

        /// <summary>Waga pomiaru w srednia cechy (decyzja autora nr 19: glowny 1, pomocniczy 0.5).</summary>
        public float weight = 1f;

        /// <summary>Wartosc przecietnej kolonii (z = 0.5). TYMCZASOWA - kalibracja w sesji koncowej.</summary>
        public float norm = 0.5f;

        /// <summary>Odchylenie od normy dajace z = 1 (albo 0). Musi byc dodatnie.</summary>
        public float spread = 0.5f;

        /// <summary>Minimalna suma mianownikow w oknie, zeby pomiar zdarzeniowy byl "znany".</summary>
        public int minEvents = 1;

        /// <summary>
        /// Minimalna suma LICZNIKOW w oknie, zeby pomiar zdarzeniowy byl "znany" (0 = bez warunku). Decyzja autora
        /// po przegladzie S8: Inicjatywa (norma 0) liczy sie dopiero, gdy w oknie byl atak - bez atakow Walka to sama
        /// Obrona z pelnym zakresem 0..1, a ataki moga ja tylko podniesc. Wczesniej brak atakow dawal stale z = 0,5
        /// o wadze 0,5, co sciskalo Walke do [0,17; 0,83].
        /// </summary>
        public int minNumerator;

        public StyleSignalParams Clone()
        {
            return new StyleSignalParams
            {
                signal = signal, weight = weight, norm = norm, spread = spread, minEvents = minEvents,
                minNumerator = minNumerator
            };
        }
    }

    /// <summary>
    /// Prototyp ARCHETYPU (decyzja autora nr 22): etykieta i wspolrzedne na wspolnej skali 0..1.
    /// Etykieta to najblizszy prototyp po znanych cechach; remis rozstrzyga kolejnosc w XML.
    /// </summary>
    public class StylePrototype
    {
        public string label = string.Empty;
        public float walka = 0.5f;
        public float gospodarka = 0.5f;
        public float ekspansja = 0.5f;
        public float reaktywnosc = 0.5f;

        public float Get(StyleDimension d)
        {
            switch (d)
            {
                case StyleDimension.Walka: return walka;
                case StyleDimension.Gospodarka: return gospodarka;
                case StyleDimension.Ekspansja: return ekspansja;
                default: return reaktywnosc;
            }
        }

        public void Set(StyleDimension d, float v)
        {
            switch (d)
            {
                case StyleDimension.Walka: walka = v; break;
                case StyleDimension.Gospodarka: gospodarka = v; break;
                case StyleDimension.Ekspansja: ekspansja = v; break;
                default: reaktywnosc = v; break;
            }
        }

        public StylePrototype Clone()
        {
            return new StylePrototype
            {
                label = label, walka = walka, gospodarka = gospodarka, ekspansja = ekspansja, reaktywnosc = reaktywnosc
            };
        }

        public string Describe()
        {
            return label + ":" + N(walka) + "/" + N(gospodarka) + "/" + N(ekspansja) + "/" + N(reaktywnosc);
        }

        private static string N(float v)
        {
            return v.ToString("0.0##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Parametry STYLU GRACZA (krok 7) - blok &lt;playerStyle&gt; w StorytellerCompProperties_Generative.
    /// WSPOLNA maszyneria, nie profil: profil narratora wnosi do stylu jedna liczbe - orientacje
    /// kierunku (NarratorProfile.StyleOrientation). Reszta jest wspolna, zeby porownanie osobowosci
    /// nie mieszalo osobowosci z tym, jak narrator MIERZY gracza.
    ///
    /// Inicjalizatory list sa PUSTE, a wartosci domyslne daje Default(): gdyby XML pominal liste,
    /// Sanitize() ja uzupelni i ZGLOSI - brak listy nie moze przejsc po cichu jako "dziala".
    /// Liczby skalarne maja inicjalizatory rowne XML (ta sama zasada co mtbDays w compie).
    /// </summary>
    public class PlayerStyleParams
    {
        public const int DefaultWarmupDays = 15;
        public const int DefaultCapacityDays = 60;

        public bool enabled = true;

        /// <summary>Rozgrzewka: ile dni w kolejce, zanim styl zacznie dzialac (decyzja autora nr 9).</summary>
        public int warmupDays = DefaultWarmupDays;

        /// <summary>Pojemnosc kolejki FIFO dni (decyzja autora nr 10: rok gry = 60 dni).</summary>
        public int capacityDays = DefaultCapacityDays;

        /// <summary>Skala profilu wzglednego: c = (Z - srednia znanych Z) / relativeScale, klamrowane do [-1,1].</summary>
        public float relativeScale = 0.25f;

        /// <summary>Mocna strona: c &gt;= prog. 0.4 * 0.25 = co najmniej 0.1 ponad wlasna srednia.</summary>
        public float strongSideThreshold = 0.4f;

        /// <summary>Waga osobowosci w kierunku d (decyzja autora nr 13: 30/70).</summary>
        public float orientationWeight = 0.3f;

        /// <summary>Waga rytmu krzywej w kierunku d (decyzja autora nr 13: 30/70).</summary>
        public float rhythmWeight = 0.7f;

        /// <summary>Co ile tickow obserwator sprawdza zagrozenie i pobor.</summary>
        public int threatSampleTicks = 250;

        /// <summary>Ile kolejnych cichych probek zamyka epizod zagrozenia (histereza).</summary>
        public int quietSamplesToClose = 4;

        /// <summary>Epizod krotszy niz tyle probek z zagrozeniem jest odrzucany (migotanie).</summary>
        public int minEpisodeSamples = 2;

        /// <summary>Limit czasu reakcji: pobor po tym czasie albo wcale daje szybkosc 0.</summary>
        public int reactionCapTicks = 2500;

        /// <summary>
        /// Epizod zagrozenia dluzszy niz tyle tickow jest zamykany i liczony (decyzja autora po przegladzie S8: limit
        /// 1 doby). Po zamknieciu mapa czeka na cisze, zanim zacznie nowy epizod - dluga obecnosc wroga to jeden
        /// epizod, a nie seria.
        /// </summary>
        public int maxEpisodeTicks = 60000;

        /// <summary>Dziki czlowiek nieoswojony przez tyle dni liczy sie jako oferta odrzucona.</summary>
        public int wildManTimeoutDays = 15;

        /// <summary>Kategorie budynkow liczone jako obrona (defName DesignationCategoryDef).</summary>
        public List<string> defenseCategories = new List<string>();

        /// <summary>Kategorie budynkow liczone jako produkcja (defName DesignationCategoryDef).</summary>
        public List<string> productionCategories = new List<string>();

        /// <summary>Rekordy czasu liczone jako praca produktywna (defName RecordDef typu Time).</summary>
        public List<string> productiveRecords = new List<string>();

        /// <summary>Rekordy czasu liczone jako praca pomocnicza (mianownik pomiaru Praca).</summary>
        public List<string> supportRecords = new List<string>();

        /// <summary>Korzenie zadan, ktore sa ofertami dolaczenia (defName QuestScriptDef).</summary>
        public List<string> offerQuestRoots = new List<string>();

        public List<StyleSignalParams> signals = new List<StyleSignalParams>();

        public List<StylePrototype> prototypes = new List<StylePrototype>();

        public static PlayerStyleParams Default()
        {
            var p = new PlayerStyleParams();
            p.defenseCategories = DefaultDefenseCategories();
            p.productionCategories = DefaultProductionCategories();
            p.productiveRecords = DefaultProductiveRecords();
            p.supportRecords = DefaultSupportRecords();
            p.offerQuestRoots = DefaultOfferQuestRoots();
            p.signals = DefaultSignals();
            p.prototypes = DefaultPrototypes();
            return p;
        }

        public PlayerStyleParams Clone()
        {
            var c = new PlayerStyleParams
            {
                enabled = enabled,
                warmupDays = warmupDays,
                capacityDays = capacityDays,
                relativeScale = relativeScale,
                strongSideThreshold = strongSideThreshold,
                orientationWeight = orientationWeight,
                rhythmWeight = rhythmWeight,
                threatSampleTicks = threatSampleTicks,
                quietSamplesToClose = quietSamplesToClose,
                minEpisodeSamples = minEpisodeSamples,
                reactionCapTicks = reactionCapTicks,
                maxEpisodeTicks = maxEpisodeTicks,
                wildManTimeoutDays = wildManTimeoutDays,
                defenseCategories = Copy(defenseCategories),
                productionCategories = Copy(productionCategories),
                productiveRecords = Copy(productiveRecords),
                supportRecords = Copy(supportRecords),
                offerQuestRoots = Copy(offerQuestRoots),
                signals = new List<StyleSignalParams>(),
                prototypes = new List<StylePrototype>()
            };
            if (signals != null)
            {
                foreach (StyleSignalParams s in signals)
                {
                    if (s != null) c.signals.Add(s.Clone());
                }
            }
            if (prototypes != null)
            {
                foreach (StylePrototype pr in prototypes)
                {
                    if (pr != null) c.prototypes.Add(pr.Clone());
                }
            }
            return c;
        }

        /// <summary>
        /// Parametry pomiaru po nazwie; brak wpisu = wartosci domyslne kodu. Po Sanitize() kazdy pomiar
        /// ma dokladnie jeden wpis, wiec sciezka domyslna dziala tylko na parametrach niesanityzowanych.
        /// </summary>
        public StyleSignalParams Signal(StyleSignal s)
        {
            if (signals != null)
            {
                for (int i = 0; i < signals.Count; i++)
                {
                    StyleSignal parsed;
                    if (signals[i] != null && StyleSignals.TryParse(signals[i].signal, out parsed) && parsed == s)
                    {
                        return signals[i];
                    }
                }
            }
            return DefaultSignal(s);
        }

        /// <summary>
        /// Klamruje wartosci spoza dziedziny i zwraca opis poprawek (pusty, gdy nic nie poprawiono).
        /// Kazda poprawka musi trafic do logu - cicha korekta jest gorsza od zlej wartosci.
        /// </summary>
        public string Sanitize()
        {
            var popr = new List<string>();

            if (capacityDays < 1)
            {
                popr.Add("capacityDays " + I(capacityDays) + " -> " + I(DefaultCapacityDays));
                capacityDays = DefaultCapacityDays;
            }
            if (warmupDays < 1)
            {
                popr.Add("warmupDays " + I(warmupDays) + " -> " + I(DefaultWarmupDays));
                warmupDays = DefaultWarmupDays;
            }
            if (warmupDays > capacityDays)
            {
                // Rozgrzewka dluzsza niz kolejka: styl nigdy by sie nie wlaczyl.
                popr.Add("warmupDays " + I(warmupDays) + " > capacityDays -> " + I(capacityDays));
                warmupDays = capacityDays;
            }
            relativeScale = Dodatnia(relativeScale, 0.25f, "relativeScale", popr);
            if (!Skonczona(strongSideThreshold) || strongSideThreshold < 0f || strongSideThreshold > 1f)
            {
                popr.Add("strongSideThreshold " + F(strongSideThreshold) + " poza [0,1] -> 0.4");
                strongSideThreshold = 0.4f;
            }
            orientationWeight = Nieujemna(orientationWeight, 0.3f, "orientationWeight", popr);
            rhythmWeight = Nieujemna(rhythmWeight, 0.7f, "rhythmWeight", popr);
            threatSampleTicks = MinInt(threatSampleTicks, 1, 250, "threatSampleTicks", popr);
            quietSamplesToClose = MinInt(quietSamplesToClose, 1, 4, "quietSamplesToClose", popr);
            minEpisodeSamples = MinInt(minEpisodeSamples, 1, 2, "minEpisodeSamples", popr);
            reactionCapTicks = MinInt(reactionCapTicks, 1, 2500, "reactionCapTicks", popr);
            maxEpisodeTicks = MinInt(maxEpisodeTicks, 1, 60000, "maxEpisodeTicks", popr);
            wildManTimeoutDays = MinInt(wildManTimeoutDays, 1, 15, "wildManTimeoutDays", popr);

            defenseCategories = Lista(defenseCategories, DefaultDefenseCategories(), "defenseCategories", popr);
            productionCategories = Lista(productionCategories, DefaultProductionCategories(), "productionCategories", popr);
            productiveRecords = Lista(productiveRecords, DefaultProductiveRecords(), "productiveRecords", popr);
            supportRecords = Lista(supportRecords, DefaultSupportRecords(), "supportRecords", popr);
            offerQuestRoots = Lista(offerQuestRoots, DefaultOfferQuestRoots(), "offerQuestRoots", popr);

            SanitizeSignals(popr);
            SanitizePrototypes(popr);

            return popr.Count == 0 ? string.Empty : string.Join("; ", popr.ToArray());
        }

        private void SanitizeSignals(List<string> popr)
        {
            var wynik = new List<StyleSignalParams>();
            var byly = new bool[StyleSignals.Count];
            if (signals != null)
            {
                foreach (StyleSignalParams s in signals)
                {
                    StyleSignal sig;
                    if (s == null || !StyleSignals.TryParse(s.signal, out sig))
                    {
                        popr.Add("nieznany pomiar '" + (s == null ? "null" : s.signal) + "' pominiety");
                        continue;
                    }
                    if (byly[(int)sig])
                    {
                        popr.Add("pomiar " + s.signal + " zadeklarowany ponownie - pominiety");
                        continue;
                    }
                    byly[(int)sig] = true;
                    StyleSignalParams d = DefaultSignal(sig);
                    if (!Skonczona(s.weight) || s.weight < 0f)
                    {
                        popr.Add(s.signal + ".weight " + F(s.weight) + " -> 0");
                        s.weight = 0f;
                    }
                    if (!Skonczona(s.norm))
                    {
                        popr.Add(s.signal + ".norm nieliczbowe -> " + F(d.norm));
                        s.norm = d.norm;
                    }
                    if (!Skonczona(s.spread) || s.spread <= 0f)
                    {
                        // Rozpietosc 0 to dzielenie przez zero w mapowaniu na wspolna skale.
                        popr.Add(s.signal + ".spread " + F(s.spread) + " musi byc dodatnia -> " + F(d.spread));
                        s.spread = d.spread;
                    }
                    if (s.minEvents < 1)
                    {
                        popr.Add(s.signal + ".minEvents " + I(s.minEvents) + " -> 1");
                        s.minEvents = 1;
                    }
                    if (s.minNumerator < 0)
                    {
                        popr.Add(s.signal + ".minNumerator " + I(s.minNumerator) + " -> 0");
                        s.minNumerator = 0;
                    }
                    wynik.Add(s);
                }
            }
            for (int i = 0; i < StyleSignals.Count; i++)
            {
                if (!byly[i])
                {
                    popr.Add("brak pomiaru " + StyleSignals.Name((StyleSignal)i) + " -> wartosci domyslne");
                    wynik.Add(DefaultSignal((StyleSignal)i));
                }
            }
            // Kazda cecha musi miec dodatnia sume wag, inaczej nigdy nie bylaby "znana".
            for (int d = 0; d < StyleDimensions.Count; d++)
            {
                float suma = 0f;
                foreach (StyleSignalParams s in wynik)
                {
                    StyleSignal sig;
                    if (StyleSignals.TryParse(s.signal, out sig) && (int)StyleSignals.Dimension(sig) == d)
                    {
                        suma += s.weight;
                    }
                }
                if (suma <= 0f)
                {
                    popr.Add("cecha " + StyleDimensions.Name((StyleDimension)d) + " ma sume wag 0 -> wagi domyslne");
                    foreach (StyleSignalParams s in wynik)
                    {
                        StyleSignal sig;
                        if (StyleSignals.TryParse(s.signal, out sig) && (int)StyleSignals.Dimension(sig) == d)
                        {
                            s.weight = DefaultSignal(sig).weight;
                        }
                    }
                }
            }
            signals = wynik;
        }

        private void SanitizePrototypes(List<string> popr)
        {
            var wynik = new List<StylePrototype>();
            var etykiety = new HashSet<string>();
            if (prototypes != null)
            {
                foreach (StylePrototype pr in prototypes)
                {
                    if (pr == null || !SafeLabel(pr.label))
                    {
                        popr.Add("prototyp z niepoprawna etykieta '" + (pr == null ? "null" : pr.label) + "' pominiety");
                        continue;
                    }
                    if (!etykiety.Add(pr.label))
                    {
                        popr.Add("prototyp " + pr.label + " zadeklarowany ponownie - pominiety");
                        continue;
                    }
                    for (int d = 0; d < StyleDimensions.Count; d++)
                    {
                        float v = pr.Get((StyleDimension)d);
                        if (!Skonczona(v) || v < 0f || v > 1f)
                        {
                            float k = Skonczona(v) ? (v < 0f ? 0f : 1f) : 0.5f;
                            popr.Add("prototyp " + pr.label + "." + StyleDimensions.Name((StyleDimension)d)
                                     + " " + F(v) + " -> " + F(k));
                            pr.Set((StyleDimension)d, k);
                        }
                    }
                    wynik.Add(pr);
                }
            }
            if (wynik.Count == 0)
            {
                popr.Add("brak prototypow -> zestaw domyslny");
                wynik = DefaultPrototypes();
            }
            prototypes = wynik;
        }

        /// <summary>Etykieta: litery, cyfry i '_' (idzie do kolumn danych i do logu).</summary>
        public static bool SafeLabel(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            foreach (char ch in s)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '_';
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Jedna linia do [PN-CONFIG] i do logu startowego: wszystkie parametry efektywne.</summary>
        public string Describe()
        {
            var sb = new StringBuilder(640);
            sb.Append(enabled ? "wlaczony" : "WYLACZONY")
              .Append("; rozgrzewkaDni=").Append(I(warmupDays))
              .Append("; pojemnoscDni=").Append(I(capacityDays))
              .Append("; skalaWzgledna=").Append(F(relativeScale))
              .Append("; progMocnej=").Append(F(strongSideThreshold))
              .Append("; wagaOrientacji=").Append(F(orientationWeight))
              .Append("; wagaRytmu=").Append(F(rhythmWeight))
              .Append("; probkaZagrozenTicki=").Append(I(threatSampleTicks))
              .Append("; ciszaZamyka=").Append(I(quietSamplesToClose))
              .Append("; minProbekEpizodu=").Append(I(minEpisodeSamples))
              .Append("; limitReakcjiTicki=").Append(I(reactionCapTicks))
              .Append("; limitEpizoduTicki=").Append(I(maxEpisodeTicks))
              .Append("; dzikusLimitDni=").Append(I(wildManTimeoutDays))
              .Append("; obrona=").Append(Lacz(defenseCategories))
              .Append("; produkcja=").Append(Lacz(productionCategories))
              .Append("; pracaProduktywna=").Append(Lacz(productiveRecords))
              .Append("; pracaPomocnicza=").Append(Lacz(supportRecords))
              .Append("; oferty=").Append(Lacz(offerQuestRoots))
              .Append("; sygnaly=");
            for (int i = 0; i < StyleSignals.Count; i++)
            {
                StyleSignalParams s = Signal((StyleSignal)i);
                if (i > 0) sb.Append(',');
                sb.Append(StyleSignals.Name((StyleSignal)i))
                  .Append(":w").Append(F(s.weight))
                  .Append(":n").Append(F(s.norm))
                  .Append(":s").Append(F(s.spread))
                  .Append(":e").Append(I(s.minEvents))
                  .Append(":l").Append(I(s.minNumerator));
            }
            sb.Append("; prototypy=");
            if (prototypes != null)
            {
                for (int i = 0; i < prototypes.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(prototypes[i] == null ? "null" : prototypes[i].Describe());
                }
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            return "styl gracza: " + Describe();
        }

        // ---------------- wartosci domyslne (rowne XML) ----------------

        public static StyleSignalParams DefaultSignal(StyleSignal s)
        {
            // Normy i rozpietosci sa TYMCZASOWE (dlug kalibracji kroku 7) - szacunki dla typowej
            // kolonii, do przestrojenia na danych [PN-GRACZ] z sesji koncowej.
            switch (s)
            {
                case StyleSignal.Obrona: return Sig("Obrona", 1f, 0.04f, 0.04f, 1);
                // Przeglad S8 (decyzje autora): Inicjatywa znana dopiero z atakiem w oknie; Przyjecia od 3 ofert,
                // Werbunek od 2 schwytanych - jedno zdarzenie nie przelacza juz mocnej strony.
                case StyleSignal.Inicjatywa: return Sig("Inicjatywa", 0.5f, 0f, 1f / 30f, 1, 1);
                case StyleSignal.Praca: return Sig("Praca", 1f, 0.65f, 0.2f, 1);
                case StyleSignal.Produkcja: return Sig("Produkcja", 0.5f, 0.15f, 0.1f, 1);
                case StyleSignal.Przyjecia: return Sig("Przyjecia", 1f, 0.5f, 0.5f, 3);
                case StyleSignal.Werbunek: return Sig("Werbunek", 0.5f, 0.3f, 0.3f, 2);
                case StyleSignal.Poborowi: return Sig("Poborowi", 1f, 0.5f, 0.4f, 2);
                default: return Sig("Szybkosc", 0.5f, 0.5f, 0.4f, 2);
            }
        }

        public static List<StyleSignalParams> DefaultSignals()
        {
            var l = new List<StyleSignalParams>();
            for (int i = 0; i < StyleSignals.Count; i++)
            {
                l.Add(DefaultSignal((StyleSignal)i));
            }
            return l;
        }

        public static List<StylePrototype> DefaultPrototypes()
        {
            return new List<StylePrototype>
            {
                Proto("Wszechstronny", 0.5f, 0.5f, 0.5f, 0.5f),
                Proto("Wojownik", 0.8f, 0.5f, 0.5f, 0.5f),
                Proto("Gospodarz", 0.5f, 0.8f, 0.5f, 0.5f),
                Proto("Osadnik", 0.5f, 0.5f, 0.8f, 0.5f),
                Proto("Czujny", 0.5f, 0.5f, 0.5f, 0.8f),
                Proto("Kasztelan", 0.8f, 0.8f, 0.5f, 0.5f),
                Proto("Zdobywca", 0.8f, 0.5f, 0.8f, 0.5f),
                Proto("Dowodca", 0.8f, 0.5f, 0.5f, 0.8f),
                Proto("Zalozyciel", 0.5f, 0.8f, 0.8f, 0.5f),
                Proto("Zaradny", 0.5f, 0.8f, 0.5f, 0.8f),
                Proto("Opiekun", 0.5f, 0.5f, 0.8f, 0.8f),
                Proto("Zaangazowany", 0.75f, 0.75f, 0.75f, 0.75f),
                Proto("Bierny", 0.25f, 0.25f, 0.25f, 0.25f)
            };
        }

        public static List<string> DefaultDefenseCategories()
        {
            return new List<string> { "Security" };
        }

        public static List<string> DefaultProductionCategories()
        {
            return new List<string> { "Production", "Power" };
        }

        public static List<string> DefaultProductiveRecords()
        {
            return new List<string>
            {
                "TimeConstructing", "TimeSowingAndHarvesting", "TimeMining", "TimeResearching",
                "TimeHandlingAnimals", "TimeHunting"
            };
        }

        public static List<string> DefaultSupportRecords()
        {
            return new List<string> { "TimeHauling", "TimeCleaning" };
        }

        public static List<string> DefaultOfferQuestRoots()
        {
            return new List<string> { "WandererJoins", "RefugeePodCrash" };
        }

        private static StyleSignalParams Sig(string n, float w, float norm, float spread, int minEvents, int minNumerator = 0)
        {
            return new StyleSignalParams
            {
                signal = n, weight = w, norm = norm, spread = spread, minEvents = minEvents, minNumerator = minNumerator
            };
        }

        private static StylePrototype Proto(string label, float w, float g, float e, float r)
        {
            return new StylePrototype { label = label, walka = w, gospodarka = g, ekspansja = e, reaktywnosc = r };
        }

        // ---------------- pomocnicze ----------------

        private static List<string> Copy(List<string> l)
        {
            return l == null ? new List<string>() : new List<string>(l);
        }

        private static List<string> Lista(List<string> l, List<string> dflt, string nazwa, List<string> popr)
        {
            if (l == null || l.Count == 0)
            {
                popr.Add("brak listy " + nazwa + " -> " + Lacz(dflt));
                return dflt;
            }
            return l;
        }

        private static string Lacz(List<string> l)
        {
            return l == null || l.Count == 0 ? "-" : string.Join("/", l.ToArray());
        }

        private static float Dodatnia(float v, float d, string nazwa, List<string> popr)
        {
            if (!Skonczona(v) || v <= 0f)
            {
                popr.Add(nazwa + " " + F(v) + " musi byc dodatnia -> " + F(d));
                return d;
            }
            return v;
        }

        private static float Nieujemna(float v, float d, string nazwa, List<string> popr)
        {
            if (!Skonczona(v) || v < 0f)
            {
                popr.Add(nazwa + " " + F(v) + " musi byc nieujemna -> " + F(d));
                return d;
            }
            return v;
        }

        private static int MinInt(int v, int min, int d, string nazwa, List<string> popr)
        {
            if (v < min)
            {
                popr.Add(nazwa + " " + I(v) + " -> " + I(d));
                return d;
            }
            return v;
        }

        private static bool Skonczona(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        private static string F(float v)
        {
            return v.ToString("0.0#####", CultureInfo.InvariantCulture);
        }

        private static string I(int v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }
    }
}
