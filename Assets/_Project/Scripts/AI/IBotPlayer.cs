using System.Collections.Generic;

namespace FourWinsTikTok.AI
{
    public interface IBotPlayer
    {
        int SelectColumn(IReadOnlyList<int> validColumns);
    }
}
