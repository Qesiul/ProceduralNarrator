namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Intencja narratora na najblizsze tury - wyjscie krzywej dramaturgicznej (krok 4),
    /// wejscie warstwy decyzyjnej (krok 3). Cztery wartosci to cztery odpowiedzi na pytanie
    /// "co narracja ma teraz zrobic": podniesc napiecie, dac oddech, utrzymac biezacy poziom
    /// albo swiadomie zamilknac.
    ///
    /// STAN OD KROKU 4: intencje wyznacza IntentSelector z napiecia (Escalate / Hold / Breathe;
    /// Pass jest zarezerwowany i NIGDY nie jest zwracany), regula kryzysu skrajnego wymusza
    /// Breathe, a caly lancuch spina TurnPlanner. Oba czynniki zgodnosci z intencja maja
    /// niezerowe wagi: Factor_IntentAlignment z profilu narratora, Factor_PassIntent 0.25
    /// w &lt;pass&gt;. Intencja jedzie w DecisionContext, nie w przepisie kompozycji.
    ///
    /// HISTORIA (krok 3): warstwa planowania nie istniala, intencja byla stale Hold, a oba
    /// czynniki mialy wage 0 i mimo to byly liczone - zeby format logu badawczego byl
    /// identyczny w kroku 3 i 4.
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

        /// <summary>Utrzymaj biezacy poziom. Wartosc DOMYSLNA (w kroku 3 jedyna uzywana).</summary>
        Hold,

        /// <summary>
        /// Cisza jest celem. ZAREZERWOWANA - IntentSelector jej nie zwraca: Breathe juz daje
        /// FitFor ciszy 1.0, a intencja Pass wyprowadzona z napiecia odwrocilaby ujemne
        /// sprzezenie Factor_PassRestraint na dodatnie i narrator zamilklby na stale.
        /// </summary>
        Pass
    }
}
