namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Typ klocka wg sekcji 7 koncepcji technicznej (model danych).
    /// Klocek to elementarna, wielokrotnego uzytku cegielka wydarzenia.
    /// </summary>
    public enum BlockType
    {
        Trigger,      // wyzwalacz    - co uruchamia wydarzenie
        Actor,        // aktor        - kto je wywoluje
        Action,       // akcja        - co sie dzieje (nosi ladunek dla warstwy integracji)
        Target,       // cel          - w co uderza
        Modifier,     // modyfikator  - jak mocno / w jakim wariancie
        Consequence   // konsekwencja - co zostawia po sobie (krok 6: blackboard)
    }
}
