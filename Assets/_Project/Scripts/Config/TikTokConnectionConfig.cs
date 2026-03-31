using UnityEngine;

namespace FourWinsTikTok.Config
{
    [CreateAssetMenu(fileName = "TikTokConnectionConfig", menuName = "4WinsTikTok/Config/TikTok Connection")]
    public class TikTokConnectionConfig : ScriptableObject
    {
        [field: SerializeField] public bool AutoConnectOnEnable { get; private set; } = false;
        [field: SerializeField] public string HostId { get; private set; } = string.Empty;
        [field: SerializeField] public string RoomId { get; private set; } = string.Empty;
    }
}
