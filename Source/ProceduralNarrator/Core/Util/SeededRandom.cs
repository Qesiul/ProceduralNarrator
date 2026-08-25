using System;
using System.Collections.Generic;

namespace ProceduralNarrator.Core.Util
{
    /// <summary>Deterministyczne zrodlo losowosci oparte o ziarno.</summary>
    public class SeededRandom : IRandomSource
    {
        private readonly Random random;

        public SeededRandom(int seed)
        {
            random = new Random(seed);
        }

        public int Next(int maxExclusive)
        {
            return maxExclusive <= 0 ? 0 : random.Next(maxExclusive);
        }

        public T Pick<T>(IReadOnlyList<T> items)
        {
            if (items == null || items.Count == 0)
            {
                return default(T);
            }
            return items[random.Next(items.Count)];
        }
    }
}
