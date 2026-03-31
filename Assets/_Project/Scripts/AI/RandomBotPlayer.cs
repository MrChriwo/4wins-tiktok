using System;
using System.Collections.Generic;

namespace FourWinsTikTok.AI
{
    public class RandomBotPlayer : IBotPlayer
    {
        private readonly Random _random;

        public RandomBotPlayer(Random random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public int SelectColumn(IReadOnlyList<int> validColumns)
        {
            if (validColumns == null || validColumns.Count == 0)
            {
                return -1;
            }

            int index = _random.Next(0, validColumns.Count);
            return validColumns[index];
        }
    }
}
