using UnityEngine;

namespace FourWinsTikTok.Config
{
    [CreateAssetMenu(fileName = "ConnectFourWorldConfig", menuName = "4WinsTikTok/Config/World Config")]
    public class ConnectFourWorldConfig : ScriptableObject
    {
        [field: SerializeField, Min(0.1f)] public float ColumnSpacing { get; private set; } = 1f;
        [field: SerializeField, Min(0.1f)] public float RowSpacing { get; private set; } = 1f;
        [field: SerializeField, Min(0f)] public float DropStartHeight { get; private set; } = 4f;
        [field: SerializeField, Min(0f)] public float DropDuration { get; private set; } = 0.2f;
        [field: SerializeField] public Vector3 CellOffset { get; private set; } = Vector3.zero;
        [field: SerializeField] public bool GenerateColumnClickTargets { get; private set; } = true;
        [field: SerializeField, Min(0.1f)] public float ClickTargetWidthFactor { get; private set; } = 0.9f;
        [field: SerializeField, Min(0.1f)] public float ClickTargetDepth { get; private set; } = 0.8f;
        [field: SerializeField] public Vector3 FallbackChipScale { get; private set; } = new Vector3(0.8f, 0.2f, 0.8f);
        [field: SerializeField] public Color CommunityFallbackColor { get; private set; } = new Color(0.95f, 0.25f, 0.25f, 1f);
        [field: SerializeField] public Color StreamerFallbackColor { get; private set; } = new Color(0.2f, 0.7f, 1f, 1f);
        [field: SerializeField] public Color BotFallbackColor { get; private set; } = new Color(0.95f, 0.85f, 0.2f, 1f);
        [field: SerializeField] public GameObject CommunityChipPrefab { get; private set; }
        [field: SerializeField] public GameObject StreamerChipPrefab { get; private set; }
        [field: SerializeField] public GameObject BotChipPrefab { get; private set; }
    }
}
