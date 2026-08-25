using System.Collections.Generic;

namespace ProceduralNarrator.Core.Util
{
    /// <summary>
    /// Zrodlo losowosci wstrzykiwane do rdzenia. Rdzen NIE siega po Verse.Rand ani
    /// po globalny Random - dzieki temu decyzje sa odtwarzalne przy tym samym ziarnie
    /// (wymaganie niefunkcjonalne: determinizm) i testowalne bez gry.
    /// </summary>
    public interface IRandomSource
    {
        int Next(int maxExclusive);
        T Pick<T>(IReadOnlyList<T> items);
    }
}
