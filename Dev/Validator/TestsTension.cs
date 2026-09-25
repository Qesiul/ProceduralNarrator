using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProceduralNarrator.Core.Composition;
using ProceduralNarrator.Core.Decision;
using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Tension;
using ProceduralNarrator.Core.Util;

/// <summary>
/// KROK 4 - krzywa dramaturgiczna, intencje i profile narratora.
///
/// Cala ta warstwa zyje w Core/, wiec walidator widzi ja W CALOSCI - inaczej niz warstwe
/// trwalosci z kroku 6, ktorej z definicji nie moze dotknac. To jest wazna roznica przy
/// opisie pokrycia testowego w pracy.
/// </summary>
static class TestsTension
{
    public static void Run(XmlConfig cfg)
    {
        TestNapiecie();
        TestZanikWCiszy();
        TestProgiIntencji();
        TestCzynnikIntencji();
        TestProfileZXml(cfg);
        TestProfileRoznicujaDecyzje(cfg);
        TestZanikPerWpis(cfg);
        TestKryzysSkrajny(cfg);
    }

    private static string F(double v)
    {
        return v.ToString("0.000", CultureInfo.InvariantCulture);
    }

    private static WorldSnapshot Swiat(int kolonistow, int powalonych, DangerLevel danger)
    {
        return new WorldSnapshot
        {
            DaysPassed = 30,
            ColonistCount = kolonistow,
            ColonistsOnMap = kolonistow,
            AcuteDownedCount = powalonych,
            Danger = danger,
            WealthRelative = 1f
        };
    }

    private static TensionModel Model(TensionParams p)
    {
        return new TensionModel(p, ContrastTuning.Default());
    }

    // ================================================================ napiecie
    private static void TestNapiecie()
    {
        T.Section("TEST 10a - NAPIECIE: monotonicznosc wzgledem ciezaru historii i sytuacji");

        var p = TensionParams.Default();
        TensionModel m = Model(p);
        WorldSnapshot spokoj = Swiat(6, 0, DangerLevel.None);

        // Historia pusta -> rytm zerowy -> czlon narracyjny zero.
        TensionReading puste = m.Compute(new EventHistory(), spokoj, 10f);
        T.Eq("pusta historia: napiecie == 0", puste.Tension, 0.0, 1e-6);

        // Seria zdarzen POZYTYWNYCH drobnych: Rc dodatni -> clamp01(-Rc) == 0.
        EventHistory dobre = TestsDecision.Hist(
            new object[] { "A", Theme.Economic, Valence.Positive, EventScale.Minor, 9.5f },
            new object[] { "B", Theme.Economic, Valence.Positive, EventScale.Minor, 9.8f });
        TensionReading rDobre = m.Compute(dobre, spokoj, 10f);
        T.Eq("same zdarzenia pozytywne: napiecie == 0 (a nie ujemne)", rDobre.Tension, 0.0, 1e-6);

        // Seria zdarzen NEGATYWNYCH wielkich, swiezych: napiecie wysokie.
        EventHistory zle = TestsDecision.Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 9.5f },
            new object[] { "B", Theme.Raid, Valence.Negative, EventScale.Major, 9.8f },
            new object[] { "C", Theme.Raid, Valence.Negative, EventScale.Major, 10f });
        TensionReading rZle = m.Compute(zle, spokoj, 10f);
        T.Ok("seria katastrof: napiecie wyraznie dodatnie", rZle.Tension > 0.4f, "napiecie=" + F(rZle.Tension));
        T.Ok("katastrofy daja WIECEJ napiecia niz dobre zdarzenia", rZle.Tension > rDobre.Tension,
             F(rZle.Tension) + " > " + F(rDobre.Tension));

        // MONOTONICZNOSC po czlonie sytuacyjnym - to jest wklad stanu swiata, ktorego
        // sama historia nie widzi.
        float bezPowalonych = TensionModel.Situational(Swiat(6, 0, DangerLevel.None), p);
        float jedenPowalony = TensionModel.Situational(Swiat(6, 1, DangerLevel.None), p);
        float trzechPowalonych = TensionModel.Situational(Swiat(6, 3, DangerLevel.None), p);
        T.Ok("sytuacja rosnie z liczba powalonych",
             bezPowalonych < jedenPowalony && jedenPowalony < trzechPowalonych,
             F(bezPowalonych) + " < " + F(jedenPowalony) + " < " + F(trzechPowalonych));
        T.Eq("polowa kolonii powalona == maksimum czlonu sytuacyjnego", trzechPowalonych, 1.0, 1e-6);

        T.Eq("zagrozenie None -> 0", TensionModel.Situational(Swiat(6, 0, DangerLevel.None), p), 0.0, 1e-6);
        T.Eq("zagrozenie Low -> 0.5", TensionModel.Situational(Swiat(6, 0, DangerLevel.Low), p), 0.5, 1e-6);
        T.Eq("zagrozenie High -> 1.0", TensionModel.Situational(Swiat(6, 0, DangerLevel.High), p), 1.0, 1e-6);

        // MAKSIMUM, nie suma - inaczej napad powalajacy kolonistow liczylby sie dwa razy.
        T.Eq("dwa sygnaly naraz: MAKSIMUM, nie suma",
             TensionModel.Situational(Swiat(6, 1, DangerLevel.Low), p),
             Math.Max(TensionModel.Situational(Swiat(6, 1, DangerLevel.None), p),
                      TensionModel.Situational(Swiat(6, 0, DangerLevel.Low), p)), 1e-6);

        // Kolonia jednoosobowa nie moze dzielic przez zero.
        T.Ok("kolonia 1-osobowa: brak dzielenia przez zero",
             !float.IsNaN(TensionModel.Situational(Swiat(1, 1, DangerLevel.None), p)),
             "wynik = " + F(TensionModel.Situational(Swiat(1, 1, DangerLevel.None), p)));

        // Zdegenerowane wagi: napiecie 0 i FLAGA, a nie ciche 0.5.
        TensionReading zdeg = Model(new TensionParams { narrativeWeight = 0f, situationalWeight = 0f })
            .Compute(zle, Swiat(6, 3, DangerLevel.High), 10f);
        T.Ok("obie wagi zerowe: napiecie 0 ORAZ flaga degeneracji",
             zdeg.Tension == 0f && zdeg.WeightsDegenerate, zdeg.Trace);
    }

    // ============================================== zanik w ciszy (petla zwrotna)
    private static void TestZanikWCiszy()
    {
        T.Section("TEST 10b - ZANIK NAPIECIA W CISZY: petla sprzezenia zwrotnego intencji Breathe");

        var p = TensionParams.Default();
        TensionModel m = Model(p);
        WorldSnapshot spokoj = Swiat(6, 0, DangerLevel.None);

        EventHistory zle = TestsDecision.Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 8f },
            new object[] { "B", Theme.Raid, Valence.Negative, EventScale.Major, 9f },
            new object[] { "C", Theme.Raid, Valence.Negative, EventScale.Major, 10f });

        // TO JEST NAJWAZNIEJSZA ASERCJA CALEGO KROKU 4.
        //
        // Rytm z Factor_DramaticContrast zanika po INDEKSIE wpisu, a nie po czasie: osiem
        // zdarzen wstecz wazy tyle samo, czy bylo to piec dni temu, czy piecdziesiat. Gdyby
        // napiecie bralo sam rytm, NIE OPADALOBY W CISZY - a wtedy intencja Breathe
        // zatrzasnelaby sie na stale: narrator milczy, cisza nie obniza napiecia, wiec milczy
        // dalej. Od polerowania etapu 4 petle domyka zanik KAZDEGO WPISU z osobna (wczesniej:
        // wspolny mnoznik z wieku najnowszego wpisu). Bez nowych wpisow cala suma przesuwa sie
        // o ten sam czas, wiec iloraz po jednym polokresie wynosi dokladnie 1/2 - tym razem
        // z WYPROWADZENIA, a nie z tego, ze wszystko mnozy jeden czynnik.
        float t0 = m.Compute(zle, spokoj, 10f).Tension;
        float t5 = m.Compute(zle, spokoj, 15f).Tension;
        float t20 = m.Compute(zle, spokoj, 30f).Tension;

        T.Ok("napiecie OPADA w ciszy (t=0 > t=+5dni > t=+20dni)",
             t0 > t5 && t5 > t20, "t0=" + F(t0) + " t5=" + F(t5) + " t20=" + F(t20));
        T.Eq("po jednym polokresie zanik do polowy", t5 / t0, 0.5, 1e-3);
        T.Ok("po 20 dniach ciszy napiecie praktycznie zniklo", t20 < 0.1f * t0,
             "t20=" + F(t20) + " wobec t0=" + F(t0));

        // Dluzszy polokres = wolniejszy zanik. To jest pokretlo odrozniajace profile.
        float dlugi = Model(new TensionParams { halfLifeDays = 10f }).Compute(zle, spokoj, 15f).Tension;
        float krotki = Model(new TensionParams { halfLifeDays = 2f }).Compute(zle, spokoj, 15f).Tension;
        T.Ok("dluzszy polowiczny zanik trzyma napiecie dluzej", dlugi > krotki,
             "halfLife 10 -> " + F(dlugi) + " vs halfLife 2 -> " + F(krotki));

        // ---- WLASNOSC WYKRYTA TESTEM, WARTA ZAPISANIA ----
        // Przy domyslnych wagach (narracyjna 1.0, sytuacyjna 1.0) sama historia NIE MOZE
        // wypchnac napiecia powyzej wN/(wN+wS) = 0.5, bo napiecie jest srednia wazona dwoch
        // czlonow, a czlon sytuacyjny w spokojnej kolonii wynosi 0. Skoro tenseAbove = 0.60,
        // to INTENCJA Breathe JEST NIEOSIAGALNA Z SAMEJ HISTORII.
        //
        // SPROSTOWANIE ASERCJI (polerowanie etapu 4). Poprzednio stala tu RownoSC t0 == 0.5 na
        // historii o wpisach z dni 8, 9 i 10 - czyli asercja mylila rownosc z SUPREMUM, a przechodzila
        // wylacznie dlatego, ze stara formula mnozyla cala sume przez zanik NAJNOWSZEGO wpisu.
        // Przy zaniku kazdego wpisu osobno t0 = 0.4496, bo wpisy z dni 8 i 9 juz troche wystygly.
        // Obowiazuja dwie asercje: supremum OSIAGANE na historii o wspolnym wieku zero (liczone
        // z parametrow, nie literal) oraz t0 ponizej supremum na historii rozstawionej.
        //
        // To nie jest usterka, tylko poprawny podzial odpowiedzialnosci - i wart jest asercji,
        // zeby nikt go potem nie "naprawil":
        //   "ostatnio bylo gesto"        -> obsluguje Factor_PassRestraint (gestosc -> cisza),
        //   "kolonia jest w tarapatach"  -> obsluguje Intent.Breathe (napiecie -> cisza).
        // Gdyby Breathe odpalal sie od samej historii, obie sciezki liczylyby to samo zdarzenie
        // dwa razy - dokladnie ten blad, przed ktorym chronilo zrownanie wzmocnien w kontrascie.
        EventHistory zleTeraz = TestsDecision.Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 10f },
            new object[] { "B", Theme.Raid, Valence.Negative, EventScale.Major, 10f },
            new object[] { "C", Theme.Raid, Valence.Negative, EventScale.Major, 10f });
        double supremum = p.narrativeWeight / (double)(p.narrativeWeight + p.situationalWeight);
        float tSup = m.Compute(zleTeraz, spokoj, 10f).Tension;
        T.Eq("SUPREMUM z samej historii == wN/(wN+wS), osiagane przy wspolnym wieku zero",
             tSup, supremum, 1e-5);
        T.Ok("historia rozstawiona w czasie lezy PONIZEJ supremum (kazdy wpis starzeje sie sam)",
             t0 <= supremum + 1e-6 && t0 < tSup,
             "t0=" + F(t0) + " sup=" + F(supremum));

        // JEDNORODNOSC W CZASIE przy przerywanej ciszy: decyzje PASS przesuwaja licznik decyzji,
        // ale nie dopisuja wpisow i NIE WPLYWAJA na napiecie - wiec iloraz po jednym polokresie ma
        // byc 1/2 takze na historii o roznych wiekach wpisow i z PASS-ami po drodze.
        // UWAGA (przeglad): ta wlasnosc NIE odroznia nowej formuly od starej - obie sa jednorodne
        // w czasie. Odroznia je TEST 10g (b)-(d); tutaj pilnujemy wylacznie tego, ze PASS-y
        // i licznik decyzji nie przeciekaja do napiecia.
        EventHistory zPasami = TestsDecision.Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 2f },
            new object[] { "Z", Theme.Economic, Valence.Positive, EventScale.Minor, 5f },
            new object[] { "B", Theme.Natural, Valence.Negative, EventScale.Moderate, 9f });
        zPasami.RecordPass(11f, 0, true);
        zPasami.RecordPass(13f, 0, true);
        float tp0 = m.Compute(zPasami, spokoj, 14f).Tension;
        float tp5 = m.Compute(zPasami, spokoj, 14f + p.halfLifeDays).Tension;
        T.Ok("STRAZNIK: historia z PASS-ami ma niezerowe napiecie", tp0 > 0.01f, "t=" + F(tp0));
        T.Eq("jednorodnosc w czasie: PASS-y nie ruszaja napiecia, po polokresie DOKLADNIE polowa",
             tp5 / tp0, 0.5, 1e-4);

        // Sprawdzane w SUPREMUM, a nie w t0: t0 lezy ponizej supremum (wpisy wystygly), wiec
        // asercja na t0 przepuszczalaby tenseAbove miedzy t0 a supremum - czyli dokladnie te
        // regresje, przed ktora ma chronic (znalezione wstrzyknieciem tenseAbove = 0.48).
        T.Ok("Breathe jest NIEOSIAGALNY z samej historii (tenseAbove > wN/(wN+wS)) - wymaga tarapatow",
             p.tenseAbove > supremum && IntentSelector.Select(tSup, p).Intent != Intent.Breathe,
             "tenseAbove=" + F(p.tenseAbove) + " sup=" + F(supremum) + " -> "
             + IntentSelector.Select(tSup, p).Intent);

        // Ten sam ciag zdarzen, ale kolonia FAKTYCZNIE w tarapatach: teraz Breathe wchodzi.
        float tTarapaty = m.Compute(zle, Swiat(6, 2, DangerLevel.High), 10f).Tension;
        Intent iTarapaty = IntentSelector.Select(tTarapaty, p).Intent;
        T.Ok("ta sama historia + powaleni i wrogowie na mapie -> Breathe",
             iTarapaty == Intent.Breathe,
             "napiecie=" + F(tTarapaty) + " -> " + iTarapaty);

        // PETLA ZAMKNIETA: tarapaty -> Breathe, a po dlugiej ciszy narrator wraca do eskalacji.
        Intent iPoCiszy = IntentSelector.Select(t20, p).Intent;
        T.Ok("PETLA ZAMKNIETA: Breathe w tarapatach, Escalate po 20 dniach ciszy",
             iTarapaty == Intent.Breathe && iPoCiszy == Intent.Escalate,
             "tarapaty=" + iTarapaty + " po ciszy=" + iPoCiszy);
    }

    // ================================================================ progi intencji
    private static void TestProgiIntencji()
    {
        T.Section("TEST 10c - INTENCJE: progi, ciaglosc docelowej mocy, zarezerwowany Pass");

        var p = TensionParams.Default();

        T.Ok("napiecie 0.00 -> Escalate", IntentSelector.Select(0.00f, p).Intent == Intent.Escalate, null);
        T.Ok("napiecie 0.45 -> Hold", IntentSelector.Select(0.45f, p).Intent == Intent.Hold, null);
        T.Ok("napiecie 1.00 -> Breathe", IntentSelector.Select(1.00f, p).Intent == Intent.Breathe, null);

        // Przejscia BEZ przeskoku: Escalate -> Hold -> Breathe, nigdy Escalate -> Breathe.
        Intent poprzednia = IntentSelector.Select(0f, p).Intent;
        var kolejnosc = new List<Intent> { poprzednia };
        bool przeskok = false;
        for (int i = 1; i <= 100; i++)
        {
            Intent biezaca = IntentSelector.Select(i / 100f, p).Intent;
            if (biezaca != poprzednia)
            {
                if (poprzednia == Intent.Escalate && biezaca == Intent.Breathe)
                {
                    przeskok = true;
                }
                kolejnosc.Add(biezaca);
                poprzednia = biezaca;
            }
        }
        T.Ok("rosnace napiecie daje Escalate -> Hold -> Breathe bez przeskoku", !przeskok,
             string.Join(" -> ", kolejnosc.Select(x => x.ToString()).ToArray()));
        T.EqI("dokladnie trzy stany po drodze", kolejnosc.Count, 3);

        // Intent.Pass jest ZAREZERWOWANY - patrz uzasadnienie w IntentSelector.
        // Bez tej asercji ktos moglby go "dolozyc dla kompletnosci" i odwrocic ujemne
        // sprzezenie w Factor_PassRestraint, przez co narrator zamilklby na stale.
        bool byloPass = false;
        for (int i = 0; i <= 1000; i++)
        {
            if (IntentSelector.Select(i / 1000f, p).Intent == Intent.Pass)
            {
                byloPass = true;
            }
        }
        T.Ok("Intent.Pass NIGDY nie jest zwracany (zarezerwowany, patrz IntentSelector)", !byloPass, null);

        // Docelowa moc: ciagla i malejaca z napieciem.
        T.Eq("napiecie 0 -> docelowa moc +span", IntentSelector.Select(0f, p).TargetIntensity, p.intensitySpan, 1e-6);
        T.Eq("napiecie 0.5 -> docelowa moc 0 (Normal)", IntentSelector.Select(0.5f, p).TargetIntensity, 0.0, 1e-6);
        T.Eq("napiecie 1 -> docelowa moc -span", IntentSelector.Select(1f, p).TargetIntensity, -p.intensitySpan, 1e-6);

        bool malejaca = true;
        float poprzedniaMoc = float.MaxValue;
        for (int i = 0; i <= 100; i++)
        {
            float moc = IntentSelector.Select(i / 100f, p).TargetIntensity;
            if (moc > poprzedniaMoc + 1e-6f)
            {
                malejaca = false;
            }
            poprzedniaMoc = moc;
        }
        T.Ok("docelowa moc jest CIAGLA i nierosnaca wzgledem napiecia", malejaca, null);

        // Odwrocone progi sa prostowane, a nie akceptowane.
        var odwrocone = new TensionParams { calmBelow = 0.8f, tenseAbove = 0.2f };
        string poprawki = odwrocone.Sanitize();
        T.Ok("odwrocone progi zostaja ZAMIENIONE i zgloszone",
             odwrocone.calmBelow <= odwrocone.tenseAbove && !string.IsNullOrEmpty(poprawki), poprawki);
    }

    // ============================================== czynnik zgodnosci z intencja
    private static void TestCzynnikIntencji()
    {
        T.Section("TEST 10d - CZYNNIK INTENCJI: ten sam kandydat, rozne intencje");

        var f = new Factor_IntentAlignment();
        string slad;

        ComposedEvent ciezkie = TestsDecision.Ev("PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major);
        ComposedEvent lagodne = TestsDecision.Ev("PN_Akcja_Zrzut", Theme.Economic, Valence.Positive, EventScale.Minor);

        var kCiezkie = new ScoredCandidate { Event = ciezkie, IsPass = false, SortKey = "C" };
        var kLagodne = new ScoredCandidate { Event = lagodne, IsPass = false, SortKey = "L" };

        var pusta = new EventHistory();
        DecisionContext esk = DecisionContext.Create(new WorldSnapshot(), pusta, 10f, Intent.Escalate, 1f, 0f);
        DecisionContext odd = DecisionContext.Create(new WorldSnapshot(), pusta, 10f, Intent.Breathe, -1f, 1f);

        float ciezkieEsk = f.Evaluate(kCiezkie, esk, out slad);
        float lagodneEsk = f.Evaluate(kLagodne, esk, out slad);
        float ciezkieOdd = f.Evaluate(kCiezkie, odd, out slad);
        float lagodneOdd = f.Evaluate(kLagodne, odd, out slad);

        // TO JEST ISTOTA CZYNNIKA: ten sam kandydat dostaje INNA ocene przy innej intencji,
        // a kierunek preferencji sie odwraca. Zaslepka z kroku 3 zwracala 0.5 zawsze.
        T.Ok("przy Escalate ciezkie zdarzenie bije lagodne", ciezkieEsk > lagodneEsk,
             F(ciezkieEsk) + " > " + F(lagodneEsk));
        T.Ok("przy Breathe lagodne zdarzenie bije ciezkie", lagodneOdd > ciezkieOdd,
             F(lagodneOdd) + " > " + F(ciezkieOdd));
        T.Ok("preferencja faktycznie sie ODWRACA miedzy intencjami",
             (ciezkieEsk - lagodneEsk) * (ciezkieOdd - lagodneOdd) < 0,
             "Escalate: " + F(ciezkieEsk - lagodneEsk) + ", Breathe: " + F(ciezkieOdd - lagodneOdd));

        // Zakres i brak NaN na wszystkich kombinacjach.
        bool wZakresie = true;
        foreach (Intent i in Enum.GetValues(typeof(Intent)))
        {
            foreach (ScoredCandidate k in new[] { kCiezkie, kLagodne })
            {
                float v = f.Evaluate(k, DecisionContext.Create(new WorldSnapshot(), pusta, 10f, i, 0f, 0f), out slad);
                if (float.IsNaN(v) || v < 0f || v > 1f)
                {
                    wZakresie = false;
                }
            }
        }
        T.Ok("wynik zawsze w [0,1], bez NaN, dla KAZDEJ wartosci enuma Intent", wZakresie, null);

        // PASS nie ma zdarzenia - wartosc neutralna, bez wyjatku.
        var kPass = new ScoredCandidate { Event = null, IsPass = true, SortKey = "PASS" };
        T.Eq("kandydat PASS -> wartosc neutralna 0.5", f.Evaluate(kPass, esk, out slad), 0.5, 1e-6);

        // Zadany ladunek miesci sie w REALNIE osiagalnym zbiorze katalogu.
        float zadanyBreathe = Factor_IntentAlignment.DesiredCharge(Intent.Breathe);
        float najlepszyOsiagalny = Factor_DramaticContrast.Charge(Valence.Positive, EventScale.Minor);
        T.Eq("zadany ladunek przy Breathe == najlepszy OSIAGALNY ladunek katalogu",
             zadanyBreathe, najlepszyOsiagalny, 1e-6);
        T.Eq("zadany ladunek przy Escalate == najgorszy osiagalny",
             Factor_IntentAlignment.DesiredCharge(Intent.Escalate),
             Factor_DramaticContrast.Charge(Valence.Negative, EventScale.Major), 1e-6);
    }

    // ================================================================ profile z XML
    private static void TestProfileZXml(XmlConfig cfg)
    {
        T.Section("TEST 10e - PROFILE NARRATORA: katalog XML czytany naprawde");

        // Straznik dryfu XML<->kod dla bloku <contrast>. Bez niego wystawienie wzmocnien
        // do XML byloby pozorne: mozna by je tam zmienic, a walidator nadal liczylby
        // na wartosciach domyslnych z kodu i niczego nie zauwazyl.
        // Straznik dryfu dla przelacznika zlozonego listu. Bez niego dalo by sie zmienic go
        // w XML, a walidator liczylby na wartosci domyslnej z kodu i niczego nie zauwazyl -
        // dokladnie ta pulapka, ktora przy gateTemperature przespala cala runde kroku 3.
        // Krok 8 (decyzja autora K8-4): opis dopisywany do listu gry dla 13/13 - wlaczony.
        T.Ok("XML useComposedLetter odczytany (decyzja o liscie jest jawna w konfiguracji)",
             cfg.useComposedLetter == true,
             "useComposedLetter = " + cfg.useComposedLetter);

        T.Eq("XML contrast.reliefGain", cfg.contrast.reliefGain, 1.0, 1e-6);
        T.Eq("XML contrast.strikeGain", cfg.contrast.strikeGain, 1.0, 1e-6);
        T.Eq("XML: wzmocnienia SYMETRYCZNE (asymetria przeniesiona do intencji w kroku 4)",
             cfg.contrast.strikeGain, cfg.contrast.reliefGain, 1e-6);

        T.Ok("katalog profili nie jest pusty", cfg.profiles.Count > 0,
             "profili: " + cfg.profiles.Count);
        T.EqI("trzy profile zgodnie z decyzja projektowa", cfg.profiles.Count, 3);

        foreach (NarratorProfile prof in cfg.profiles)
        {
            T.Ok("profil " + prof.Id + ": suma wag > 0", prof.Weights.Total() > 0f,
                 prof.Weights.Describe());
            T.Ok("profil " + prof.Id + ": waga intencji > 0 (krzywa faktycznie wplywa)",
                 prof.Weights.intentAlignment > 0f,
                 "intentAlignment = " + F(prof.Weights.intentAlignment));
            T.Ok("profil " + prof.Id + ": progi w porzadku calmBelow <= tenseAbove",
                 prof.Tension.calmBelow <= prof.Tension.tenseAbove,
                 F(prof.Tension.calmBelow) + " <= " + F(prof.Tension.tenseAbove));
            T.Ok("profil " + prof.Id + ": selectionWeight > 0 (bierze udzial w losowaniu)",
                 prof.SelectionWeight > 0f, F(prof.SelectionWeight));
        }

        // Profile MUSZA sie roznic - inaczej wzorzec Strategia jest atrapa.
        var odciski = new HashSet<string>();
        foreach (NarratorProfile prof in cfg.profiles)
        {
            odciski.Add(prof.Weights.Describe() + "|" + prof.Tension);
        }
        T.EqI("kazdy profil ma UNIKALNY zestaw parametrow", odciski.Count, cfg.profiles.Count);

        // Kierunek roznic zgodny z nazwami - inaczej etykiety w XML klamia.
        NarratorProfile pow = cfg.profiles.First(x => x.Id.Contains("Powsciagliwy"));
        NarratorProfile nap = cfg.profiles.First(x => x.Id.Contains("Napastliwy"));
        T.Ok("powsciagliwy siega Breathe WCZESNIEJ niz napastliwy",
             pow.Tension.tenseAbove < nap.Tension.tenseAbove,
             F(pow.Tension.tenseAbove) + " < " + F(nap.Tension.tenseAbove));
        T.Ok("napastliwy eskaluje LATWIEJ (wyzszy prog spokoju)",
             nap.Tension.calmBelow > pow.Tension.calmBelow,
             F(nap.Tension.calmBelow) + " > " + F(pow.Tension.calmBelow));
        T.Ok("napastliwy zapomina o ciezarze SZYBCIEJ (krotszy polokres)",
             nap.Tension.halfLifeDays < pow.Tension.halfLifeDays,
             F(nap.Tension.halfLifeDays) + " < " + F(pow.Tension.halfLifeDays));
    }

    // ======================================== zanik kazdego wpisu (polerowanie etapu 4)
    /// <summary>
    /// STARA FORMULA czlonu narracyjnego, odtworzona WYLACZNIE jako kontrola regresji:
    /// clamp01(-Rc) * zanik(wiek NAJNOWSZEGO wpisu). Nie jest to druga implementacja do uzytku -
    /// sluzy do pokazania, ze scenariusz testowy naprawde odtwarza defekt (inaczej asercja
    /// "nowa formula nie rosnie" moglaby przechodzic na scenariuszu, w ktorym i stara nie rosla).
    /// </summary>
    private static float StaraNarracja(Factor_DramaticContrast fc, EventHistory h, float dzien, float polokres)
    {
        float wiek = h.Newest == null ? 0f : Math.Max(0f, dzien - h.Newest.GameDay);
        return Curves.Clamp01(-fc.ComputeRhythm(h).Charge) * Curves.HalfLifeDecay(wiek, polokres);
    }

    private static readonly Valence[] Walencje = { Valence.Negative, Valence.Neutral, Valence.Positive };
    private static readonly EventScale[] Skale = { EventScale.Minor, EventScale.Moderate, EventScale.Major };

    private static void TestZanikPerWpis(XmlConfig cfg)
    {
        T.Section("TEST 10g - ZANIK KAZDEGO WPISU: prezent po ciszy nie ozywia starych katastrof");

        var p = TensionParams.Default();
        float h = p.halfLifeDays;
        var fc = new Factor_DramaticContrast(cfg.contrast);
        TensionModel m = new TensionModel(p, cfg.contrast);
        WorldSnapshot spokoj = Swiat(6, 0, DangerLevel.None);
        int okno = cfg.contrast.rhythmWindow > 0 ? cfg.contrast.rhythmWindow : 1;
        double lambda = cfg.contrast.lambda;
        double sumaWagOkna = 0.0;
        for (int i = 0; i < okno; i++) sumaWagOkna += Math.Pow(lambda, i);

        // ---- (a) TOZSAMOSC ZE STARA FORMULA PRZY WSPOLNYM WIEKU WPISOW ----
        // Poprawka ma zmieniac zachowanie WYLACZNIE tam, gdzie wieki wpisow sie roznia.
        var rng = new SeededRandom(20260921);
        int niezerowych = 0;
        double maxRozn = 0.0;
        for (int proba = 0; proba < 3000; proba++)
        {
            var hist = new EventHistory();
            int n = 1 + rng.Next(12);                 // do 12 wpisow - okno (8) bywa przepelnione
            float dzienWpisow = 5f + rng.Next(40);
            for (int i = 0; i < n; i++)
            {
                hist.RecordEvent(TestsDecision.Ev("A" + i, Theme.Raid, Walencje[rng.Next(3)], Skale[rng.Next(3)]),
                                 dzienWpisow, 0);
            }
            float ocena = dzienWpisow + rng.Next(31);
            float nowa = m.Compute(hist, spokoj, ocena).Narrative;
            float stara = StaraNarracja(fc, hist, ocena, h);
            maxRozn = Math.Max(maxRozn, Math.Abs(nowa - stara));
            if (stara > 0.01f) niezerowych++;
        }
        T.Ok("STRAZNIK: probka zawiera przypadki o niezerowym napieciu", niezerowych >= 300,
             "niezerowych: " + niezerowych + "/3000");
        T.Ok("wspolny wiek wpisow: nowa formula TOZSAMA ze stara (max roznica <= 1e-5)",
             maxRozn <= 1e-5, "max |nowa - stara| = " + maxRozn.ToString("E2", CultureInfo.InvariantCulture));

        // TOZSAMOSC NA STROJENIU ZDEGENEROWANYM. Sanityzacja okna i lambdy jest powielona
        // w ComputeAgedLoad i ComputeRhythm (ComputeRhythm zostaje nietkniety), a ContrastTuning
        // nie ma wlasnego Sanitize - wiec zla wartosc z XML trafia wprost do obu metod. Bez tych
        // przypadkow galezie sanityzacji nie byly wykonywane ani razu (przeglad: zmiana okna
        // zastepczego z 1 na 24 i lambdy zastepczej z 0 na 1 zostawiala walidator zielony).
        var zdegenerowane = new[]
        {
            new ContrastTuning { lambda = -0.5f }, new ContrastTuning { lambda = float.NaN },
            new ContrastTuning { lambda = 0f }, new ContrastTuning { rhythmWindow = 0 },
            new ContrastTuning { rhythmWindow = -2 }, new ContrastTuning { lambda = -1f, rhythmWindow = 0 }
        };
        double maxRoznZdeg = 0.0;
        int niezerowychZdeg = 0;
        foreach (ContrastTuning zt in zdegenerowane)
        {
            var fcZ = new Factor_DramaticContrast(zt);
            var mZ = new TensionModel(p, zt);
            for (int proba = 0; proba < 300; proba++)
            {
                var hist = new EventHistory();
                int n = 1 + rng.Next(12);
                float dz = 5f + rng.Next(40);
                for (int i = 0; i < n; i++)
                {
                    hist.RecordEvent(TestsDecision.Ev("Z" + i, Theme.Raid, Walencje[rng.Next(3)], Skale[rng.Next(3)]),
                                     dz, 0);
                }
                float oc = dz + rng.Next(31);
                float nowaZ = mZ.Compute(hist, spokoj, oc).Narrative;
                float staraZ = StaraNarracja(fcZ, hist, oc, h);
                maxRoznZdeg = Math.Max(maxRoznZdeg, Math.Abs(nowaZ - staraZ));
                if (staraZ > 0.01f) niezerowychZdeg++;
            }
        }
        T.Ok("STRAZNIK: strojenie zdegenerowane daje przypadki o niezerowym napieciu", niezerowychZdeg >= 300,
             "niezerowych: " + niezerowychZdeg + "/1800");
        T.Ok("strojenie zdegenerowane (lambda <0/NaN/0, okno <=0): nadal TOZSAMOSC ze stara formula",
             maxRoznZdeg <= 1e-5, "max |nowa - stara| = " + maxRoznZdeg.ToString("E2", CultureInfo.InvariantCulture));

        // WPIS Z PRZYSZLOSCI (po wczytaniu zapisu albo po symulatorze przywracajacym tick) ma PELNA
        // wage - ten sam kierunek klamry, ktory przyjmuja gestosc PASS i zaufanie kontrastu.
        EventHistory zPrzyszlosci = TestsDecision.Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 20f });
        EventHistory zTeraz = TestsDecision.Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 15f });
        AgedLoad lPrz = fc.ComputeAgedLoad(zPrzyszlosci, 15f, h);
        AgedLoad lTer = fc.ComputeAgedLoad(zTeraz, 15f, h);
        T.Ok("wpis z przyszlosci: pelna waga (sr.zanik 1) i ten sam wynik co wpis z biezacego dnia",
             Math.Abs(lPrz.MeanDecay - 1f) < 1e-6f && Math.Abs(lPrz.Negative - lTer.Negative) < 1e-6f,
             "zanik=" + F(lPrz.MeanDecay) + " obciazenie=" + F(lPrz.Negative) + " vs " + F(lTer.Negative));

        // ---- (b) ZROZNICOWANIE: przy ROZNYCH wiekach formuly MUSZA sie roznic ----
        // Bez tej asercji wstrzykniecie starej formuly przeszloby (a) na zielono - tozsamosc przy
        // wspolnym wieku jest spelniona trywialnie przez obie wersje.
        int roznych = 0;
        for (int proba = 0; proba < 1000; proba++)
        {
            var hist = new EventHistory();
            int n = 2 + rng.Next(10);
            float dzien = 1f;
            for (int i = 0; i < n; i++)
            {
                dzien += 0.5f + rng.Next(8);
                hist.RecordEvent(TestsDecision.Ev("B" + i, Theme.Raid, Walencje[rng.Next(3)], Skale[rng.Next(3)]),
                                 dzien, 0);
            }
            float ocena = dzien + rng.Next(10);
            if (Math.Abs(m.Compute(hist, spokoj, ocena).Narrative - StaraNarracja(fc, hist, ocena, h)) > 1e-3f)
            {
                roznych++;
            }
        }
        T.Ok("rozne wieki wpisow: formuly sie ROZNIA w istotnej czesci przypadkow", roznych >= 100,
             "roznych > 1e-3: " + roznych + "/1000");

        // ---- (c) SCENARIUSZ PRZEGLADU: prezent po dlugiej ciszy ----
        // Osiem napadow w dniach 10-17, potem cisza do dnia 70, w dniu 70 zrzut zaopatrzenia,
        // ocena w dniu 72 (nastepna decyzja). Stara formula "ozywiala" napady sprzed 50 dni.
        EventHistory napady = new EventHistory();
        EventHistory napadyIPrezent = new EventHistory();
        for (int i = 0; i < 8; i++)
        {
            ComposedEvent napad = TestsDecision.Ev("PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major);
            napady.RecordEvent(napad, 10f + i, 0);
            napadyIPrezent.RecordEvent(napad, 10f + i, 0);
        }
        napadyIPrezent.RecordEvent(TestsDecision.Ev("PN_Akcja_Zrzut", Theme.Economic, Valence.Positive, EventScale.Minor), 70f, 0);

        float nPrzed = m.Compute(napady, spokoj, 72f).Narrative;
        float nPo = m.Compute(napadyIPrezent, spokoj, 72f).Narrative;
        float sPrzed = StaraNarracja(fc, napady, 72f, h);
        float sPo = StaraNarracja(fc, napadyIPrezent, 72f, h);

        T.Ok("KONTROLA REGRESJI: stara formula OZYWIALA napiecie po prezencie (scenariusz odtwarza defekt)",
             sPo > sPrzed + 0.2f, "stara: " + F(sPrzed) + " -> " + F(sPo));
        T.Ok("NAPRAWA: prezent po ciszy NIE podnosi czlonu narracyjnego",
             nPo <= nPrzed + 1e-6f, "nowa: " + F(nPrzed) + " -> " + F(nPo));
        // SKUTEK DLA INTENCJI - na profilu, na ktorym stara formula go realnie dawala. Na profilu
        // domyslnym w dniu 72 stara formula tez dawala Escalate (przeglad: asercja byla pusta),
        // bo "ozywienie" trzymalo go w Hold tylko ok. 0.6 dnia. Powsciagliwy (polokres 8, progi
        // 0.15/0.45) trzymal Hold kilka dni - tu skutek jest widoczny i tu go sprawdzamy.
        NarratorProfile powsc = cfg.profiles.First(x => x.Id.Contains("Powsciagliwy"));
        var mPow = Model(powsc.Tension);
        float hPow = powsc.Tension.halfLifeDays;
        double wNp = powsc.Tension.narrativeWeight, wSp = powsc.Tension.situationalWeight;
        float tStaraPow = (float)(wNp * StaraNarracja(fc, napadyIPrezent, 72f, hPow) / (wNp + wSp));
        float tNowaPow = mPow.Compute(napadyIPrezent, spokoj, 72f).Tension;
        Intent iStaraPow = IntentSelector.Select(tStaraPow, powsc.Tension).Intent;
        Intent iNowaPow = IntentSelector.Select(tNowaPow, powsc.Tension).Intent;
        T.Ok("KONTROLA REGRESJI: u powsciagliwego stara formula przestawiala na Hold po prezencie",
             iStaraPow == Intent.Hold, "stara: napiecie=" + F(tStaraPow) + " -> " + iStaraPow);
        T.Ok("NAPRAWA: u powsciagliwego intencja po prezencie zostaje Escalate",
             iNowaPow == Intent.Escalate, "nowa: napiecie=" + F(tNowaPow) + " -> " + iNowaPow);

        // ---- (d) KRES WZROSTU po dopisaniu zdarzenia NIEUJEMNEGO ----
        // Wyprowadzenie: po dopisaniu wpisu o ladunku c_new >= 0 w chwili oceny, przy S = suma
        // w*c*zanik przed dopisaniem i pelnym oknie W:
        //     dN <= (-c_new + (1-lambda)*S + lambda^okno * c_wyp * zanik_wyp) / W
        // Gdy N > 0, S < 0, wiec jedynym skladnikiem, ktory moze podniesc N, jest ULGA wpisu
        // wypadajacego z okna. Kres: max(0, lambda^okno * c_wyp * zanik_wyp) / W. Przy niepelnym
        // oknie nic nie wypada i (-lambda*S)/(1+lambda*W) <= -S/W, czyli N nie rosnie wcale.
        // Kres liczony z WPISU WYPADAJACEGO, a nie z literalu katalogowego - inaczej asercja
        // padlaby na poprawnym zachowaniu po dolozeniu wiekszego zdarzenia pozytywnego.
        int pelnych = 0, niepelnych = 0, zDodatnimKresem = 0, naruszen = 0;
        double maxWzrostPelne = 0.0;
        string pierwszeNaruszenie = null;
        for (int proba = 0; proba < 4000; proba++)
        {
            var hist = new EventHistory();
            int n = 1 + rng.Next(14);
            float dzien = 1f;
            for (int i = 0; i < n; i++)
            {
                dzien += rng.Next(6) * 0.5f;
                hist.RecordEvent(TestsDecision.Ev("D" + i, Theme.Raid, Walencje[rng.Next(3)], Skale[rng.Next(3)]),
                                 dzien, 0);
            }
            float ocena = dzien + rng.Next(20) * 0.5f;

            double kres = 0.0;
            IReadOnlyList<EventHistoryEntry> wOknie = hist.Recent(okno);
            if (wOknie.Count >= okno)
            {
                EventHistoryEntry wyp = wOknie[okno - 1];
                double cWyp = Factor_DramaticContrast.Charge(wyp.Valence, wyp.Scale);
                double zWyp = Curves.HalfLifeDecay(ocena - wyp.GameDay, h);
                kres = Math.Max(0.0, Math.Pow(lambda, okno) * cWyp * zWyp) / sumaWagOkna;
                pelnych++;
                if (kres > 0.0) zDodatnimKresem++;
            }
            else
            {
                niepelnych++;
            }

            float przed = m.Compute(hist, spokoj, ocena).Narrative;
            Valence vNowa = rng.Next(2) == 0 ? Valence.Neutral : Valence.Positive;
            hist.RecordEvent(TestsDecision.Ev("NOWY", Theme.Economic, vNowa, Skale[rng.Next(3)]), ocena, 0);
            float po = m.Compute(hist, spokoj, ocena).Narrative;

            double wzrost = po - przed;
            if (wOknie.Count >= okno) maxWzrostPelne = Math.Max(maxWzrostPelne, wzrost);
            if (wzrost > kres + 1e-6)
            {
                naruszen++;
                if (pierwszeNaruszenie == null)
                {
                    pierwszeNaruszenie = "proba " + proba + ": " + F(przed) + " -> " + F(po) + " kres " + F(kres);
                }
            }
        }
        T.Ok("STRAZNIK: probka zawiera okna pelne z dodatnim kresem i okna niepelne",
             zDodatnimKresem > 50 && niepelnych > 50,
             "pelnych=" + pelnych + " z dodatnim kresem=" + zDodatnimKresem + " niepelnych=" + niepelnych);
        T.EqI("dopisanie zdarzenia nieujemnego NIGDY nie podnosi N ponad kres z wpisu wypadajacego",
              naruszen, 0);
        Console.WriteLine("     max wzrost przy pelnym oknie: " + F(maxWzrostPelne)
                          + " (kres ogolny lambda^okno/W = " + F(Math.Pow(lambda, okno) / sumaWagOkna) + ")"
                          + (pierwszeNaruszenie == null ? string.Empty : "; " + pierwszeNaruszenie));

        // ---- (e) KAZDY PROFIL: Breathe nieosiagalny z samej historii ----
        // Wyprowadzone: przy zerowym czlonie sytuacyjnym napiecie <= wN/(wN+wS). Dotad sprawdzane
        // tylko dla TensionParams.Default(); przestrojenie profilu ponizej tego progu przeszloby
        // po cichu i zlamalo podzial odpowiedzialnosci z TEST 10b.
        foreach (NarratorProfile prof in cfg.profiles)
        {
            double sup = prof.Tension.narrativeWeight
                         / (double)(prof.Tension.narrativeWeight + prof.Tension.situationalWeight);
            T.Ok("profil " + prof.Id + ": tenseAbove > wN/(wN+wS) (Breathe tylko z realnych tarapatow)",
                 prof.Tension.tenseAbove > sup,
                 F(prof.Tension.tenseAbove) + " > " + F(sup));
        }
    }

    // ======================================================== kryzys skrajny
    private static void TestKryzysSkrajny(XmlConfig cfg)
    {
        T.Section("TEST 10h - KRYZYS SKRAJNY: jawny sygnal, wspolny dla wszystkich profili");

        // Straznik dryfu XML<->kod: blok <crisis> ma byc w konfiguracji i niesc decyzje autora.
        T.Ok("XML: blok <crisis> obecny w StorytellerCompProperties_Generative", cfg.crisisBlockPresent, null);
        T.Eq("XML crisis.downedFraction == 0.5 (decyzja: polowa kolonii)", cfg.crisis.downedFraction, 0.5, 1e-6);
        T.Ok("XML crisis.enabled == true", cfg.crisis.enabled, null);
        T.EqI("XML crisis.minDowned == 1", cfg.crisis.minDowned, 1);

        CrisisParams kp = cfg.crisis.Clone();
        string poprawki = kp.Sanitize();
        T.Ok("konfiguracja kryzysu nie wymaga poprawek Sanitize", string.IsNullOrEmpty(poprawki), poprawki);

        // ---- predykat na tabeli przypadkow brzegowych (kolonistow, powalonych -> kryzys?) ----
        var tabela = new[]
        {
            new { k = 6, d = 2, oczek = false }, new { k = 6, d = 3, oczek = true },
            new { k = 3, d = 1, oczek = false }, new { k = 3, d = 2, oczek = true },
            new { k = 2, d = 1, oczek = true },  new { k = 1, d = 1, oczek = true },
            new { k = 4, d = 2, oczek = true },  new { k = 5, d = 0, oczek = false },
            new { k = 0, d = 0, oczek = false }, new { k = 12, d = 5, oczek = false },
            new { k = 12, d = 6, oczek = true },
        };
        foreach (var w in tabela)
        {
            CrisisReading r = CrisisDetector.Evaluate(Swiat(w.k, w.d, DangerLevel.None), kp);
            T.Ok("kryzys(" + w.d + "/" + w.k + ") == " + w.oczek, r.Extreme == w.oczek, r.Trace);
        }

        // Granica ulamka, na ktorej zawodzi porownanie floatow: 0.6f * 25 = 15.000001 w float.
        // (Pierwsza wersja tego testu uzywala 0.3f * 10 - wstrzykniecie naiwnego porownania
        // przeszlo wtedy na zielono, bo ten iloczyn zaokragla sie w float dokladnie do 3.0.
        // Przypadek wyszukany przegladem ulamkow 0.01-0.99 i kolonii do 40 osob.)
        var szescDziesiatych = new CrisisParams { downedFraction = 0.6f };
        T.Ok("prog calkowity: 15 z 25 przy ulamku 0.6 to kryzys (brak pulapki floatow)",
             CrisisDetector.Evaluate(Swiat(25, 15, DangerLevel.None), szescDziesiatych).Extreme,
             CrisisDetector.Evaluate(Swiat(25, 15, DangerLevel.None), szescDziesiatych).Trace);
        T.Ok("... a 14 z 25 jeszcze nie",
             !CrisisDetector.Evaluate(Swiat(25, 14, DangerLevel.None), szescDziesiatych).Extreme, null);

        // NIEZALEZNOSC OD ZAGROZENIA (decyzja autora): samo High przy zerze powalonych to nie kryzys,
        // a polowa kolonii powalona bez wrogow na mapie - tak.
        T.Ok("zagrozenie High, zero powalonych -> NIE kryzys (to zwykly napad)",
             !CrisisDetector.Evaluate(Swiat(6, 0, DangerLevel.High), kp).Extreme, null);
        T.Ok("polowa powalona, zagrozenie None -> kryzys (zaraza w stadium skrajnym, pozar)",
             CrisisDetector.Evaluate(Swiat(6, 3, DangerLevel.None), kp).Extreme, null);

        // minDowned DZIALA (przeglad: kazdy przypadek mial minDowned = 1, pokrywajace sie z podloga
        // zaszyta w Evaluate, wiec implementacja ignorujaca parametr przechodzila).
        var min2 = kp.Clone();
        min2.minDowned = 2;
        T.Ok("minDowned=2: 1 z 2 to NIE kryzys, choc sam ulamek by go dal",
             !CrisisDetector.Evaluate(Swiat(2, 1, DangerLevel.None), min2).Extreme,
             CrisisDetector.Evaluate(Swiat(2, 1, DangerLevel.None), min2).Trace);
        T.Ok("minDowned=2: 2 z 2 to kryzys",
             CrisisDetector.Evaluate(Swiat(2, 2, DangerLevel.None), min2).Extreme, null);
        T.Ok("minDowned=2 nie podnosi progu, gdy prog ulamkowy jest wyzszy: 3 z 10 nie, 5 z 10 tak",
             !CrisisDetector.Evaluate(Swiat(10, 3, DangerLevel.None), min2).Extreme
             && CrisisDetector.Evaluate(Swiat(10, 5, DangerLevel.None), min2).Extreme, null);

        // SANITIZE - sciezka naprawcza, na ktorej polega integracja. Bez niej downedFraction NaN
        // dawala prog 1 (kryzys przy jednym powalonym w dowolnie duzej kolonii), a 1.5 - prog
        // powyzej liczby kolonistow (regula martwa).
        var tabelaS = new[]
        {
            new { f = float.NaN, m = 1, fOcz = 0.5f, mOcz = 1 },
            new { f = float.PositiveInfinity, m = 1, fOcz = 0.5f, mOcz = 1 },
            new { f = 0f, m = 1, fOcz = 0.01f, mOcz = 1 },
            new { f = -1f, m = 1, fOcz = 0.01f, mOcz = 1 },
            new { f = 1.5f, m = 1, fOcz = 1f, mOcz = 1 },
            new { f = 0.5f, m = 0, fOcz = 0.5f, mOcz = 1 },
            new { f = 0.5f, m = -3, fOcz = 0.5f, mOcz = 1 },
        };
        foreach (var w in tabelaS)
        {
            var zle = new CrisisParams { downedFraction = w.f, minDowned = w.m };
            string opis = zle.Sanitize();
            T.Ok("Sanitize(downedFraction=" + F(w.f) + ", minDowned=" + w.m + ") -> ("
                 + F(w.fOcz) + ", " + w.mOcz + ") z opisem poprawki",
                 Math.Abs(zle.downedFraction - w.fOcz) < 1e-6f && zle.minDowned == w.mOcz
                 && !string.IsNullOrEmpty(opis),
                 "wynik=(" + F(zle.downedFraction) + ", " + zle.minDowned + ") opis=" + opis);
        }
        var nanPo = new CrisisParams { downedFraction = float.NaN };
        nanPo.Sanitize();
        var poltora = new CrisisParams { downedFraction = 1.5f };
        poltora.Sanitize();
        T.Ok("po Sanitize: NaN -> 1 z 12 to NIE kryzys; 1.5 -> 6 z 6 to kryzys",
             !CrisisDetector.Evaluate(Swiat(12, 1, DangerLevel.None), nanPo).Extreme
             && CrisisDetector.Evaluate(Swiat(6, 6, DangerLevel.None), poltora).Extreme, null);

        var wylaczony = kp.Clone();
        wylaczony.enabled = false;
        T.Ok("regula wylaczona -> nigdy kryzys", !CrisisDetector.Evaluate(Swiat(2, 2, DangerLevel.High), wylaczony).Extreme,
             CrisisDetector.Evaluate(Swiat(2, 2, DangerLevel.High), wylaczony).Trace);

        // ZGODNOSC Z CZLONEM SYTUACYJNYM przy tym samym ulamku i mianowniku: kryzys zachodzi
        // DOKLADNIE wtedy, gdy czlon powalonych osiaga nasycenie. Wspolny mianownik
        // (ColonistsOnMap) jest warunkiem tej wlasnosci - pilnuje go ta petla.
        int niezgodnych = 0, kryzysow = 0;
        TensionParams tp = TensionParams.Default();
        for (int k = 1; k <= 12; k++)
        {
            for (int d = 0; d <= k; d++)
            {
                bool kryzys = CrisisDetector.Evaluate(Swiat(k, d, DangerLevel.None), kp).Extreme;
                bool nasycenie = TensionModel.Situational(Swiat(k, d, DangerLevel.None), tp) >= 1f - 1e-6f;
                if (kryzys) kryzysow++;
                if (kryzys != nasycenie) niezgodnych++;
            }
        }
        T.Ok("STRAZNIK: siatka zawiera stany kryzysowe", kryzysow > 20, "kryzysow: " + kryzysow);

        // MIANOWNIK Z LISTY OBECNYCH, nie z ColonistCount. Osmioro kolonistow, dwoje w kriokomorach:
        // na mapie jest szescioro, troje lezy - to polowa obecnych. Stary mianownik (ColonistCount)
        // dawal Ramp(3, 0, 4) = 0.75 i brak nasycenia, choc polowa tych, ktorzy moga cokolwiek
        // zrobic, lezy. Swiat() ustawia oba pola na te sama wartosc, wiec bez tego przypadku
        // powrot do starego mianownika przeszedlby niezauwazony.
        WorldSnapshot zKriokomorami = Swiat(6, 3, DangerLevel.None);
        zKriokomorami.ColonistCount = 8;
        T.Eq("czlon sytuacyjny liczy ulamek wsrod OBECNYCH (3/6), nie wsrod wszystkich (3/8)",
             TensionModel.Situational(zKriokomorami, tp), 1.0, 1e-6);
        T.Ok("... i predykat kryzysu uzywa tego samego mianownika",
             CrisisDetector.Evaluate(zKriokomorami, kp).Extreme,
             CrisisDetector.Evaluate(zKriokomorami, kp).Trace);
        T.EqI("przy ulamku 0.5: kryzys <=> nasycenie czlonu powalonych (wspolny mianownik)", niezgodnych, 0);

        // ---- ApplyCrisis: Breathe u KAZDEGO profilu, moc = min(moc profilu, 0) ----
        WorldSnapshot kryzysowy = Swiat(6, 3, DangerLevel.None);
        CrisisReading rk = CrisisDetector.Evaluate(kryzysowy, kp);
        int zmienionych = 0;
        foreach (NarratorProfile prof in cfg.profiles)
        {
            // Pusta historia - dokladnie przypadek 3.1 z przegladu: czlon narracyjny zerowy
            // rozciencza sygnal sytuacyjny.
            TensionReading r = Model(prof.Tension).Compute(new EventHistory(), kryzysowy, 30f);
            IntentDecision przed = IntentSelector.Select(r.Tension, prof.Tension);
            Intent iPrzed = przed.Intent;
            float mPrzed = przed.TargetIntensity;
            IntentDecision po = IntentSelector.ApplyCrisis(przed, rk);

            if (iPrzed != Intent.Breathe) zmienionych++;
            T.Ok("profil " + prof.Id + ": w kryzysie intencja Breathe", po.Intent == Intent.Breathe,
                 "napiecie=" + F(r.Tension) + " " + iPrzed + " -> " + po.Intent);
            T.Eq("profil " + prof.Id + ": moc w kryzysie == min(moc profilu, 0)",
                 po.TargetIntensity, Math.Min(mPrzed, 0f), 1e-6);
            T.Ok("profil " + prof.Id + ": napiecie NIE jest nadpisywane (loguje sie surowe)",
                 Math.Abs(po.Tension - r.Tension) < 1e-7f && po.CrisisOverride, null);
        }
        T.Ok("KONTROLA REGRESJI 3.1: bez reguly co najmniej jeden profil NIE dawal Breathe w kryzysie",
             zmienionych >= 1, "profili zmienionych przez regule: " + zmienionych);

        // ---- LANCUCH END-TO-END przez TurnPlanner (ten sam kod, ktory woluje integracja) ----
        // Przedtem kryzys do kontekstu decyzji wkladala warstwa integracji, a testy podawaly flage
        // literalem - pomylenie kolejnosci nie mialo wykrywacza. Tu snapshot 3/6 idzie przez caly
        // lancuch: napiecie -> intencja -> regula kryzysu -> DecisionContext.
        foreach (NarratorProfile prof in cfg.profiles)
        {
            TurnPlan plan = TurnPlanner.Plan(Model(prof.Tension), kp, new EventHistory(), kryzysowy, 30f);
            T.Ok("TurnPlanner, profil " + prof.Id + ": kryzys dociera do kontekstu (ExtremeCrisis, Breathe, moc <= 0)",
                 plan.Crisis.Extreme && plan.Context.ExtremeCrisis
                 && plan.Context.Intent == Intent.Breathe && plan.Context.TargetIntensity <= 0f
                 && plan.Context.TargetIntensity == plan.Intent.TargetIntensity
                 && Math.Abs(plan.Context.Tension - plan.Tension.Tension) < 1e-7f,
                 plan.Intent.Trace);
        }
        TurnPlan spokojnyPlan = TurnPlanner.Plan(Model(tp), kp, new EventHistory(), Swiat(6, 2, DangerLevel.High), 30f);
        T.Ok("TurnPlanner poza kryzysem: kontekst bez flagi, intencja wprost z krzywej",
             !spokojnyPlan.Context.ExtremeCrisis
             && spokojnyPlan.Context.Intent == IntentSelector.Select(spokojnyPlan.Tension.Tension, tp).Intent,
             spokojnyPlan.Intent.Trace);

        // Poza kryzysem ApplyCrisis nie rusza niczego.
        IntentDecision spokojna = IntentSelector.Select(0.1f, tp);
        Intent iSp = spokojna.Intent;
        float mSp = spokojna.TargetIntensity;
        IntentDecision spokojnaPo = IntentSelector.ApplyCrisis(spokojna,
            CrisisDetector.Evaluate(Swiat(6, 1, DangerLevel.High), kp));
        T.Ok("poza kryzysem ApplyCrisis jest tozsamoscia (intencja, moc, flaga)",
             spokojnaPo.Intent == iSp && spokojnaPo.TargetIntensity == mSp && !spokojnaPo.CrisisOverride,
             iSp + " / " + F(mSp));

        // Moc ujemna profilu NIE jest podnoszona do zera - min, nie nadpisanie.
        IntentDecision ciezka = IntentSelector.Select(0.95f, tp);
        float mCiezka = ciezka.TargetIntensity;
        IntentSelector.ApplyCrisis(ciezka, rk);
        T.Ok("moc juz ujemna zostaje bez zmian (regula nie PODNOSI mocy)",
             mCiezka < 0f && Math.Abs(ciezka.TargetIntensity - mCiezka) < 1e-7f,
             F(mCiezka) + " -> " + F(ciezka.TargetIntensity));

        // Galezie obronne ApplyCrisis (dzis martwe - Select i Evaluate nigdy nie zwracaja null).
        T.Ok("ApplyCrisis(null, kryzys) == null i ApplyCrisis(d, null) zwraca d bez zmian",
             IntentSelector.ApplyCrisis(null, rk) == null
             && ReferenceEquals(IntentSelector.ApplyCrisis(spokojnaPo, null), spokojnaPo)
             && !spokojnaPo.CrisisOverride, null);
    }

    // ============================================ profile roznicuja decyzje
    private static void TestProfileRoznicujaDecyzje(XmlConfig cfg)
    {
        T.Section("TEST 10f - STRATEGIA: ten sam stan i to samo ziarno, rozne profile");

        // Historia i swiat IDENTYCZNE dla wszystkich profili. Jedyna zmienna jest profil -
        // to jest dokladnie ta izolacja, ktorej wymaga demonstracja wzorca Strategia
        // w rozdziale o ewaluacji.
        EventHistory historia = TestsDecision.Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 8.0f },
            new object[] { "B", Theme.Natural, Valence.Negative, EventScale.Moderate, 9.0f },
            new object[] { "C", Theme.Raid, Valence.Negative, EventScale.Major, 9.6f });
        WorldSnapshot swiat = Swiat(6, 1, DangerLevel.Low);
        const float dzien = 10f;

        var napiecia = new List<float>();
        var intencje = new List<Intent>();

        foreach (NarratorProfile prof in cfg.profiles)
        {
            TensionReading r = Model(prof.Tension).Compute(historia, swiat, dzien);
            IntentDecision d = IntentSelector.Select(r.Tension, prof.Tension);
            napiecia.Add(r.Tension);
            intencje.Add(d.Intent);
            Console.WriteLine("     " + prof.Id.PadRight(26) + " napiecie=" + F(r.Tension)
                              + "  intencja=" + d.Intent
                              + "  docelowaMoc=" + F(d.TargetIntensity));
        }

        // ROZRZUT, a nie Distinct na floatach: po zmianie formuly roznica miedzy profilami
        // powsciagliwym i napastliwym spadla do 0.0009, a Distinct zaliczylby nawet 1e-7.
        // Prog 0.005 to rzad wielkosci ponizej rozstepu wag profili - wystarczy, zeby odroznic
        // realne roznicowanie od szumu zaokraglen.
        T.Ok("profile daja ROZNE napiecia na tym samym stanie (test dymny, rozrzut > 0.005)",
             napiecia.Max() - napiecia.Min() > 0.005f,
             string.Join(", ", napiecia.Select(x => F(x)).ToArray()));

        // WYPROWADZENIE zamiast progu z reki (przeglad): przy historii najciezszej w chwili oceny
        // (N = 1) i spokojnej kolonii (czlon sytuacyjny 0) napiecie profilu wynosi DOKLADNIE
        // wN/(wN+wS). Wagi profili roznia sie mocno, wiec roznice sa tu rzedu 0.1 - scenariusz
        // wyzej je znosi, bo oba czlony leza blisko siebie.
        EventHistory najciezsza = TestsDecision.Hist(
            new object[] { "A", Theme.Raid, Valence.Negative, EventScale.Major, 10f },
            new object[] { "B", Theme.Raid, Valence.Negative, EventScale.Major, 10f });
        var wyprowadzone = new List<double>();
        foreach (NarratorProfile prof in cfg.profiles)
        {
            double oczek = prof.Tension.narrativeWeight
                           / (double)(prof.Tension.narrativeWeight + prof.Tension.situationalWeight);
            float got = Model(prof.Tension).Compute(najciezsza, Swiat(6, 0, DangerLevel.None), 10f).Tension;
            T.Eq("profil " + prof.Id + ": napiecie przy N=1 i spokoju == wN/(wN+wS)", got, oczek, 1e-5);
            wyprowadzone.Add(oczek);
        }
        T.Ok("wyprowadzone napiecia profili roznia sie istotnie (rozrzut >= 0.1)",
             wyprowadzone.Max() - wyprowadzone.Min() >= 0.1 - 1e-9,
             string.Join(", ", wyprowadzone.Select(x => F(x)).ToArray()));

        T.Ok("profile daja co najmniej dwie ROZNE intencje na tym samym stanie",
             intencje.Distinct().Count() >= 2,
             string.Join(", ", intencje.Select(x => x.ToString()).ToArray()));

        // Ten sam kandydat, rozne profile -> rozna uzytecznosc. To domyka lancuch:
        // profil -> napiecie -> intencja -> czynnik -> uzytecznosc.
        ComposedEvent kandydat = TestsDecision.Ev("PN_Akcja_Napad", Theme.Raid, Valence.Negative, EventScale.Major);
        var uzytecznosci = new List<float>();

        foreach (NarratorProfile prof in cfg.profiles)
        {
            TensionReading r = Model(prof.Tension).Compute(historia, swiat, dzien);
            IntentDecision d = IntentSelector.Select(r.Tension, prof.Tension);
            DecisionContext ctx = DecisionContext.Create(swiat, historia, dzien, d.Intent,
                                                         d.TargetIntensity, r.Tension);
            var scorer = new UtilityScorer(TestsDecision.CzynnikiZdarzen(), prof.Weights,
                                           UtilityScorer.BuildPassFactors(cfg.pass), cfg.pass,
                                           cfg.vetoContextFitBelow);
            uzytecznosci.Add(scorer.Score(kandydat, ctx).Utility);
        }

        T.Ok("ten sam kandydat dostaje ROZNE uzytecznosci pod roznymi profilami",
             uzytecznosci.Distinct().Count() > 1,
             string.Join(", ", uzytecznosci.Select(x => F(x)).ToArray()));

        // STRAZNIK PUSTEGO TESTU: bez profili powyzsze petle nie wykonalyby ani jednej
        // asercji, a sekcja przechodzilaby na zielono nie sprawdzajac niczego.
        T.Ok("STRAZNIK: przebadano co najmniej trzy profile", cfg.profiles.Count >= 3,
             "profili: " + cfg.profiles.Count);
    }
}
