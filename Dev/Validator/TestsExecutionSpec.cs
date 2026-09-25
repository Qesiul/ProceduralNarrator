using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ProceduralNarrator.Core.Model;

/// <summary>
/// TEST 15 - specyfikacja wykonania (krok 8, dlug 9).
///
/// Przed krokiem 8 klucz wykonania i tlumaczenie kompozycji na IncidentParms laczylo tylko sasiedztwo
/// w pliku. Teraz oba powstaja z ExecutionSpec, a IncidentParmsBuilder.Apply (warstwa integracji,
/// niewidoczna dla walidatora) przyjmuje WYLACZNIE ten obiekt. Ten test pilnuje drugiej polowy
/// konstrukcji: ze kazde pole specyfikacji zmienia klucz. Nowe pole bez odzwierciedlenia w kluczu
/// (albo bez przypadku w tabeli ponizej) zapala test.
/// </summary>
static class TestsExecutionSpec
{
    static ComposedEvent Zdarzenie(string payload, IntensityLevel moc)
    {
        return new ComposedEvent { ActionPayload = payload, Intensity = moc };
    }

    public static void Run()
    {
        T.Section("TEST 15 - ExecutionSpec: jedno zrodlo klucza wykonania i parametrow (dlug 9)");

        // 15a. Format klucza BEZ ZMIAN wobec krokow 5-7 (payload|moc[|Ffrakcja], moc jako liczba
        // calkowita enuma). Literaly sa tu celowo: to kontrakt z danymi archiwalnymi, a nie kopia
        // formuly - IntensityLevel ma wartosci -2..2 (VeryLow..VeryHigh).
        T.EqS("15a klucz bez frakcji, moc Normal", ExecutionSpec.From(Zdarzenie("RaidEnemy", IntensityLevel.Normal), null).Key, "RaidEnemy|0");
        T.EqS("15a klucz bez frakcji, moc VeryLow", ExecutionSpec.From(Zdarzenie("Flashstorm", IntensityLevel.VeryLow), null).Key, "Flashstorm|-2");
        T.EqS("15a klucz z frakcja", ExecutionSpec.From(Zdarzenie("RaidEnemy", IntensityLevel.High), "Faction_7").Key, "RaidEnemy|1|FFaction_7");
        T.EqS("15a pusty identyfikator frakcji = brak frakcji", ExecutionSpec.From(Zdarzenie("RaidEnemy", IntensityLevel.High), "").Key, "RaidEnemy|1");
        T.EqS("15a brak payloadu -> znak zapytania", ExecutionSpec.From(Zdarzenie(null, IntensityLevel.Normal), null).Key, "?|0");
        T.Ok("15a brak zdarzenia -> brak specyfikacji", ExecutionSpec.From(null, "Faction_7") == null, null);

        // 15b. Pola specyfikacji - ZBIOR ZAMKNIETY. Nowe pole musi dostac przypadek w tabeli 15c;
        // bez tego test sie zapala, zanim ktokolwiek zapomni dopisac je do klucza.
        var pola = typeof(ExecutionSpec)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var znane = new List<string> { "FactionId", "Intensity", "Payload" };
        T.EqS("15b pola ExecutionSpec = {FactionId, Intensity, Payload}", string.Join(",", pola), string.Join(",", znane));
        T.Ok("15b wszystkie pola tylko do odczytu",
             typeof(ExecutionSpec).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).All(f => f.IsInitOnly),
             null);

        // 15c. Kazde pole ZMIENIA klucz - para specyfikacji rozniacych sie tylko tym polem.
        var baza = ExecutionSpec.From(Zdarzenie("ManhunterPack", IntensityLevel.Normal), "Faction_3");
        var zmiany = new Dictionary<string, ExecutionSpec>
        {
            { "Payload", ExecutionSpec.From(Zdarzenie("Infestation", IntensityLevel.Normal), "Faction_3") },
            { "Intensity", ExecutionSpec.From(Zdarzenie("ManhunterPack", IntensityLevel.Low), "Faction_3") },
            { "FactionId", ExecutionSpec.From(Zdarzenie("ManhunterPack", IntensityLevel.Normal), "Faction_4") },
        };
        int sprawdzonych = 0;
        foreach (string pole in znane)
        {
            ExecutionSpec inny;
            if (!zmiany.TryGetValue(pole, out inny))
            {
                T.Ok("15c przypadek dla pola " + pole, false, "brak przypadku w tabeli zmian");
                continue;
            }
            // Warunek wstepny: specyfikacje roznia sie DOKLADNIE tym polem (inaczej test niczego nie dowodzi).
            int roznych = typeof(ExecutionSpec).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Count(f => !Equals(f.GetValue(baza), f.GetValue(inny)));
            T.EqI("15c para rozni sie jednym polem: " + pole, roznych, 1);
            T.Ok("15c pole " + pole + " zmienia klucz", baza.Key != inny.Key, baza.Key + " vs " + inny.Key);
            sprawdzonych++;
        }
        // Straznik pustego testu: petla przeszla po wszystkich polach.
        T.EqI("15c sprawdzono wszystkie pola", sprawdzonych, znane.Count);

        // 15d. Klucz nie zalezy od niczego SPOZA specyfikacji: dwa zdarzenia rozne w opisie, sygnaturze,
        // temacie i klockach, a zgodne w payloadzie i mocy, maja ten sam klucz.
        var a = new ComposedEvent { ActionPayload = "RaidEnemy", Intensity = IntensityLevel.High, Description = "A", Signature = "s1", ActionBlockId = "PN_Akcja_Napad" };
        var b = new ComposedEvent { ActionPayload = "RaidEnemy", Intensity = IntensityLevel.High, Description = "B inny", Signature = "s2", ActionBlockId = "PN_Akcja_Inna" };
        T.EqS("15d opis, sygnatura i klocek poza kluczem", ExecutionSpec.From(a, null).Key, ExecutionSpec.From(b, null).Key);
    }
}
