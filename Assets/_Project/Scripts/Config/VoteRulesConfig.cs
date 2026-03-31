using UnityEngine;

namespace FourWinsTikTok.Config
{
    [CreateAssetMenu(fileName = "VoteRulesConfig", menuName = "4WinsTikTok/Config/Vote Rules")]
    public class VoteRulesConfig : ScriptableObject
    {
        [field: SerializeField, Range(1, 9)] public int MinAcceptedVote { get; private set; } = 1;
        [field: SerializeField, Range(1, 9)] public int MaxAcceptedVote { get; private set; } = 7;
    }
}
