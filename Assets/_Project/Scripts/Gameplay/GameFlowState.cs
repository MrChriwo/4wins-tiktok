namespace FourWinsTikTok.Gameplay
{
    public enum GameFlowState
    {
        Idle = 0,
        WaitingForCommunityVote = 1,
        ResolvingCommunityMove = 2,
        BotTurn = 3,
        WaitingForStreamerMove = 4,
        GameOver = 5
    }
}
