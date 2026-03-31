using System.Collections.Generic;
using FourWinsTikTok.Config;
using FourWinsTikTok.Core;
using FourWinsTikTok.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace FourWinsTikTok.UI
{
    public class ConnectFourBoardView
    {
        private readonly VisualElement _boardFrame;
        private readonly VisualElement _boardGrid;
        private readonly ConnectFourUIConfig _uiConfig;

        private readonly List<VisualElement> _cells = new List<VisualElement>();
        private int _columns;
        private int _rows;

        private float _runtimeScaleMultiplier = 1f;
        private Vector2 _runtimeCellSizeOffset = Vector2.zero;
        private Vector2 _runtimeSpacingOffset = Vector2.zero;
        private Vector4 _runtimeInsetsOffset = Vector4.zero;
        private float _runtimeDiscSizeOffset;

        public ConnectFourBoardView(VisualElement boardFrame, VisualElement boardGrid, ConnectFourUIConfig uiConfig)
        {
            _boardFrame = boardFrame;
            _boardGrid = boardGrid;
            _uiConfig = uiConfig;
        }

        public void ApplyRuntimeAdjustments(float scaleMultiplier, Vector2 cellSizeOffset, Vector2 spacingOffset, Vector4 insetsOffset, float discSizeOffset)
        {
            _runtimeScaleMultiplier = Mathf.Max(0.25f, scaleMultiplier);
            _runtimeCellSizeOffset = cellSizeOffset;
            _runtimeSpacingOffset = spacingOffset;
            _runtimeInsetsOffset = insetsOffset;
            _runtimeDiscSizeOffset = discSizeOffset;
        }

        public void Initialize(int columns, int rows)
        {
            _columns = columns;
            _rows = rows;

            if (_boardGrid == null)
            {
                Debug.LogError("ConnectFourBoardView: Board grid VisualElement missing.");
                return;
            }

            float scale = _uiConfig != null ? Mathf.Max(0.25f, _uiConfig.BoardScale) : 1f;
            scale *= _runtimeScaleMultiplier;
            Vector2 baseCellSizeRaw = _uiConfig != null ? _uiConfig.CellSize : new Vector2(60f, 60f);
            Vector2 baseSpacingRaw = _uiConfig != null ? _uiConfig.CellSpacing : new Vector2(6f, 6f);
            Vector4 baseInsetsRaw = _uiConfig != null ? _uiConfig.GridInsets : new Vector4(20f, 20f, 20f, 20f);

            Vector2 cellSize = new Vector2(
                Mathf.Max(1f, baseCellSizeRaw.x + _runtimeCellSizeOffset.x),
                Mathf.Max(1f, baseCellSizeRaw.y + _runtimeCellSizeOffset.y)) * scale;

            Vector2 spacing = new Vector2(
                baseSpacingRaw.x + _runtimeSpacingOffset.x,
                baseSpacingRaw.y + _runtimeSpacingOffset.y) * scale;

            Vector4 insets = new Vector4(
                baseInsetsRaw.x + _runtimeInsetsOffset.x,
                baseInsetsRaw.y + _runtimeInsetsOffset.y,
                baseInsetsRaw.z + _runtimeInsetsOffset.z,
                baseInsetsRaw.w + _runtimeInsetsOffset.w) * scale;

            float gridWidth = _columns * cellSize.x + Mathf.Max(0, _columns - 1) * spacing.x;
            float gridHeight = _rows * cellSize.y + Mathf.Max(0, _rows - 1) * spacing.y;

            float frameWidth;
            float frameHeight;
            if (_uiConfig != null && _uiConfig.BoardFrameTexture != null)
            {
                frameWidth = Mathf.Max(1f, _uiConfig.BoardFrameTexture.width * scale);
                frameHeight = Mathf.Max(1f, _uiConfig.BoardFrameTexture.height * scale);
            }
            else
            {
                Vector2 fallbackFrameSize = _uiConfig != null ? _uiConfig.FallbackBoardFrameSize : new Vector2(900f, 780f);
                frameWidth = Mathf.Max(1f, fallbackFrameSize.x * scale);
                frameHeight = Mathf.Max(1f, fallbackFrameSize.y * scale);
            }

            if (_boardFrame != null)
            {
                _boardFrame.style.position = Position.Relative;
                _boardFrame.style.width = frameWidth;
                _boardFrame.style.height = frameHeight;

                if (_uiConfig != null && _uiConfig.BoardFrameTexture != null)
                {
                    _boardFrame.style.backgroundImage = new StyleBackground(_uiConfig.BoardFrameTexture);
                }
            }

            _boardGrid.style.position = Position.Absolute;
            _boardGrid.style.left = insets.x;
            _boardGrid.style.top = insets.y;
            _boardGrid.style.width = gridWidth;
            _boardGrid.style.height = gridHeight;

            RebuildCells();
        }

        public void Render(BoardState board)
        {
            if (board == null || _cells.Count == 0)
            {
                return;
            }

            for (int column = 0; column < _columns; column++)
            {
                for (int row = 0; row < _rows; row++)
                {
                    VisualElement cell = GetCellImage(column, row);
                    if (cell == null)
                    {
                        continue;
                    }

                    PlayerSide side = board.GetCell(column, row);
                    cell.style.backgroundColor = ResolveCellColor(side);
                    cell.style.opacity = side == PlayerSide.None && _uiConfig != null && !_uiConfig.ShowEmptyCells ? 0f : 1f;
                }
            }
        }

        private void RebuildCells()
        {
            if (_boardGrid == null)
            {
                return;
            }

            _boardGrid.Clear();

            _cells.Clear();
            float scale = _uiConfig != null ? Mathf.Max(0.25f, _uiConfig.BoardScale) : 1f;
            scale *= _runtimeScaleMultiplier;
            Vector2 baseCellSize = _uiConfig != null ? _uiConfig.CellSize : new Vector2(60f, 60f);
            Vector2 baseSpacing = _uiConfig != null ? _uiConfig.CellSpacing : new Vector2(6f, 6f);

            Vector2 cellSize = new Vector2(
                Mathf.Max(1f, baseCellSize.x + _runtimeCellSizeOffset.x),
                Mathf.Max(1f, baseCellSize.y + _runtimeCellSizeOffset.y)) * scale;

            Vector2 spacing = new Vector2(
                baseSpacing.x + _runtimeSpacingOffset.x,
                baseSpacing.y + _runtimeSpacingOffset.y) * scale;

            float baseDiscSizeFactor = _uiConfig != null ? _uiConfig.DiscSizeFactor : 0.9f;
            float discSizeFactor = Mathf.Clamp(baseDiscSizeFactor + _runtimeDiscSizeOffset, 0.1f, 1.2f);
            Vector2 discSize = cellSize * discSizeFactor;
            Vector2 discOffset = (cellSize - discSize) * 0.5f;

            for (int visualRow = 0; visualRow < _rows; visualRow++)
            {
                for (int column = 0; column < _columns; column++)
                {
                    float slotX = column * (cellSize.x + spacing.x);
                    float slotY = visualRow * (cellSize.y + spacing.y);

                    VisualElement cell = new VisualElement();
                    cell.AddToClassList("board-cell");
                    cell.pickingMode = PickingMode.Ignore;
                    cell.style.position = Position.Absolute;
                    cell.style.left = slotX + discOffset.x;
                    cell.style.top = slotY + discOffset.y;
                    cell.style.width = discSize.x;
                    cell.style.height = discSize.y;
                    cell.style.backgroundColor = ResolveCellColor(PlayerSide.None);
                    cell.style.opacity = _uiConfig != null && !_uiConfig.ShowEmptyCells ? 0f : 1f;

                    _boardGrid.Add(cell);
                    _cells.Add(cell);
                }
            }
        }

        private VisualElement GetCellImage(int column, int row)
        {
            int visualRow = _rows - 1 - row;
            int index = visualRow * _columns + column;
            if (index < 0 || index >= _cells.Count)
            {
                return null;
            }

            return _cells[index];
        }

        private Color ResolveCellColor(PlayerSide playerSide)
        {
            if (_uiConfig == null)
            {
                return Color.white;
            }

            switch (playerSide)
            {
                case PlayerSide.Community: return _uiConfig.CommunityCellColor;
                case PlayerSide.Streamer: return _uiConfig.StreamerCellColor;
                case PlayerSide.Bot: return _uiConfig.BotCellColor;
                default: return _uiConfig.EmptyCellColor;
            }
        }
    }
}
