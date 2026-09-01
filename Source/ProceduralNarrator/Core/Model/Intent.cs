namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Intencja narratora na najblizsze tury - wyjscie krzywej dramaturgicznej (krok 4),
    /// wejscie warstwy decyzyjnej (krok 3). Cztery wartosci to cztery odpowiedzi na pytanie
    /// "co narracja ma teraz zrobic": podniesc napiecie, dac oddech, utrzymac biezacy poziom
    /// albo swiadomie zamilknac.
    ///
    /// W kroku 3 warstwa planowania jeszcze nie istnieje, wiec BuildRecipe() ustawia na stale
    /// Hold. Oba czynniki zgodnosci z intencja (Factor_IntentAlignment po stronie zdarzen
    /// i Factor_PassIntent po stronie PASS-u) maja wtedy wage 0, ale sa MIMO TO liczone
    /// i trafiaja do sladu decyzji. Powod: format logu badawczego ma byc identyczny w kroku 3
    /// i 4, zeby dolozenie krzywej dramaturgicznej nie wymuszalo przepisania skryptow
    /// agregujacych w Pythonie (krok 8).
    ///
    /// UWAGA - dwa rozne pojecia "pass" w tej warstwie. Intent.Pass to INTENCJA ("narrator
    /// chce teraz ciszy"), czyli WEJSCIE scoringu. PassReason to POWOD faktycznej decyzji
    /// o milczeniu, czyli jego WYJSCIE. Utozsamienie ich zamknelo by petle sprzezeniem
    /// zwrotnym, w ktorym narrator uzasadnia cisze wlasna checia ciszy.
    /// </summary>
    public enum Intent
    {
        /// <summary>Podnies napiecie - preferuj zdarzenia mocniejsze i bardziej negatywne.</summary>
        Escalate,

        /// <summary>Daj oddech - preferuj zdarzenia lagodne albo pozytywne.</summary>
        Breathe,

        /// <summary>Utrzymaj biezacy poziom. Wartosc DOMYSLNA i jedyna uzywana w kroku 3.</summary>
        Hold,

        /// <summary>Cisza jest celem - narrator chce, zeby ta tura nie przyniosla zdarzenia.</summary>
        Pass
    }
}
