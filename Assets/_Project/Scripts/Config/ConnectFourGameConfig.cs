using UnityEngine;

namespace FourWinsTikTok.Config
{
    [CreateAssetMenu(fileName = "ConnectFourGameConfig", menuName = "4WinsTikTok/Config/Game Config")]
    public class ConnectFourGameConfig : ScriptableObject
    {
        [field: SerializeField, Min(4)] public int Columns { get; private set; } = 7;
        [field: SerializeField, Min(4)] public int Rows { get; private set; } = 6;
        [field: SerializeField, Min(4)] public int ConnectLength { get; private set; } = 4;
        [field: SerializeField, Min(1f)] public float CommunityVoteSeconds { get; private set; } = 10f;
        [field: SerializeField, Min(0f)] public float BotThinkSeconds { get; private set; } = 0.5f;
        [field: SerializeField, Min(1)] public int MatchWinsRequired { get; private set; } = 5;
        [field: SerializeField] public bool AutoStartOnPlay { get; private set; } = true;
    }
}
