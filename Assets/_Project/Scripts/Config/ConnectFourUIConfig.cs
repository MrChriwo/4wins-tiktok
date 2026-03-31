using UnityEngine;

namespace FourWinsTikTok.Config
{
    [CreateAssetMenu(fileName = "ConnectFourUIConfig", menuName = "4WinsTikTok/Config/UI Config")]
    public class ConnectFourUIConfig : ScriptableObject
    {
        [field: SerializeField] public Color EmptyCellColor { get; private set; } = new Color(0f, 0f, 0f, 0f);
        [field: SerializeField] public Color CommunityCellColor { get; private set; } = new Color(1f, 0.3f, 0.3f, 1f);
        [field: SerializeField] public Color StreamerCellColor { get; private set; } = new Color(0.2f, 0.7f, 1f, 1f);
        [field: SerializeField] public Color BotCellColor { get; private set; } = new Color(1f, 0.85f, 0.2f, 1f);
        [field: SerializeField, Min(0.25f)] public float BoardScale { get; private set; } = 1.5f;
        [field: SerializeField, Range(0.1f, 1.2f)] public float DiscSizeFactor { get; private set; } = 0.9f;
        [field: SerializeField] public bool ShowEmptyCells { get; private set; } = false;
        [field: SerializeField] public Vector2 CellSize { get; private set; } = new Vector2(60f, 60f);
        [field: SerializeField] public Vector2 CellSpacing { get; private set; } = new Vector2(6f, 6f);
        [field: SerializeField] public Texture2D BoardFrameTexture { get; private set; }
        [field: SerializeField] public Vector2 FallbackBoardFrameSize { get; private set; } = new Vector2(900f, 780f);
        [field: SerializeField] public Vector4 GridInsets { get; private set; } = new Vector4(20f, 20f, 20f, 20f); // Left, Top, Right, Bottom

        public void SetLayout(float boardScale, Vector2 cellSize, Vector2 cellSpacing, Vector4 gridInsets, float discSizeFactor)
        {
            BoardScale = Mathf.Max(0.25f, boardScale);
            CellSize = new Vector2(Mathf.Max(1f, cellSize.x), Mathf.Max(1f, cellSize.y));
            CellSpacing = cellSpacing;
            GridInsets = gridInsets;
            DiscSizeFactor = Mathf.Clamp(discSizeFactor, 0.1f, 1.2f);
        }
    }
}
