namespace FourWinsTikTok.Voting
{
    public readonly struct VoteTally
    {
        public VoteTally(int columnIndex, int voteCount)
        {
            ColumnIndex = columnIndex;
            VoteCount = voteCount;
        }

        public int ColumnIndex { get; }
        public int VoteCount { get; }
        public int DisplayColumn => ColumnIndex + 1;
    }
}
