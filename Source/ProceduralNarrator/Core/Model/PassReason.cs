namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Powod, dla ktorego tura narratora zakonczyla sie bez zdarzenia. Wyjscie polityki wyboru,
    /// zapisywane do linii maszynowej [PN-DATA] jako SYMBOL enuma.
    ///
    /// Dlaczego enum, a nie tekst po polsku: metryka "ile razy narrator swiadomie milczal"
    /// jest zliczana skryptem w kroku 8. Zliczanie regexem po polskim zdaniu jest kruche -
    /// literowka albo kosmetyczna zmiana sformulowania miedzy wersjami po cichu wyzerowalaby
    /// caly slupek w wynikach. Czytelny opis idzie do linii [PN], symbol enuma do [PN-DATA].
    ///
    /// KRYTYCZNE DLA EWALUACJI - te wartosci NIE sa rownowazne:
    ///   Competitive        = jedyny PASS bedacy DECYZJA. Pseudo-kandydat PASS wygral scoring
    ///                        z realnymi kandydatami. Tylko to liczy sie jako swiadome milczenie.
    ///   NoCandidates       = warstwa kompozycji nie dala z czego wybierac (zaden klocek akcji
    ///                        nie przeszedl twardych warunkow w tym kontekscie).
    ///   AllVetoed          = wszyscy kandydaci wypadli na wecie contextFit.
    ///   BelowCutoff        = wszyscy kandydaci wypadli na progu jakosci.
    ///   AllRefusedByGame   = gra odmowila odpalenia kazdego wybranego incydentu (CanFireNow).
    ///   RoundBudgetExhausted = skonczyly sie rundy wyboru, choc pula wciaz byla NIEPUSTA.
    ///
    /// Dwie ostatnie to PORAZKI SILNIKA, nie decyzje narracyjne, i musza byc ODEJMOWANE
    /// od udzialu PASS w tescie kalibracyjnym - inaczej narrator wygladalby na powsciagliwy
    /// wtedy, gdy w rzeczywistosci nie potrafil nic wystrzelic. Rozdzielenie ich (SPEC 5)
    /// od AllRefusedByGame jest istotne, bo diagnozuja dwa rozne bledy: pierwszy zbyt ciasny
    /// budzet rund, drugi zbyt luzne warunki twarde wobec wymagan IncidentWorkera.
    /// </summary>
    public enum PassReason
    {
        /// <summary>Nie bylo PASS-u - tura zakonczyla sie zdarzeniem.</summary>
        None,

        /// <summary>
        /// PASS wygral scoring w turze, w ktorej gra NICZEGO nie odmowila.
        /// JEDYNY powod liczony jako SWIADOME milczenie w metryce ewaluacyjnej.
        /// </summary>
        Competitive,

        /// <summary>
        /// PASS wygral scoring, ale dopiero PO tym, jak gra odmowila odpalenia co najmniej
        /// jednego wczesniejszego zwyciezcy (CanFireNow).
        ///
        /// Dlaczego to osobna wartosc, a nie Competitive: PASS wygral uczciwie, ale z pula
        /// OKROJONA przez silnik - narrator chcial cos zrobic i nie mogl, a dopiero potem
        /// cisza okazala sie najlepsza z tego, co zostalo. Wliczanie tego do "swiadomego
        /// milczenia" zawyzaloby metryke NIESYMETRYCZNIE: najbardziej tam, gdzie gra duzo
        /// odmawia (kolonia bez stropu gorskiego odrzucajaca infestacje, wczesna gra
        /// odrzucajaca napady), czyli obciazenie bylo by skorelowane z kontekstem, a nie losowe.
        /// </summary>
        CompetitiveAfterRefusal,

        /// <summary>Warstwa kompozycji nie zwrocila zadnego kandydata.</summary>
        NoCandidates,

        /// <summary>Kazdy kandydat zostal zawetowany za sprzecznosc z kontekstem.</summary>
        AllVetoed,

        /// <summary>Kazdy kandydat wypadl ponizej progu jakosci.</summary>
        BelowCutoff,

        /// <summary>Gra odmowila odpalenia kazdego wybranego incydentu (CanFireNow).</summary>
        AllRefusedByGame,

        /// <summary>Wyczerpal sie budzet rund wyboru przy wciaz niepustej puli kandydatow.</summary>
        RoundBudgetExhausted
    }
}
