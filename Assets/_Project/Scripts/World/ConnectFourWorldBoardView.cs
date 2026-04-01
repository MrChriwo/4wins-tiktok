using System.Collections;
using System.Collections.Generic;
using FourWinsTikTok.Config;
using FourWinsTikTok.Core;
using FourWinsTikTok.Gameplay;
using UnityEngine;

namespace FourWinsTikTok.World
{
    public class ConnectFourWorldBoardView : MonoBehaviour
    {
        [SerializeField] private GameFlowController gameFlowController;
        [SerializeField] private ConnectFourWorldConfig worldConfig;
        [SerializeField] private Transform boardRoot;
        [SerializeField] private Transform chipRoot;

        private readonly Dictionary<Vector2Int, Transform> _cellAnchors = new Dictionary<Vector2Int, Transform>();
        private readonly Dictionary<Vector2Int, GameObject> _spawnedChips = new Dictionary<Vector2Int, GameObject>();

        private int _columns;
        private int _rows;

        public void ConfigureRuntime(GameFlowController controller, ConnectFourWorldConfig config = null)
        {
            gameFlowController = controller;
            if (config != null)
            {
                worldConfig = config;
            }
        }

        private void Awake()
        {
            if (boardRoot == null)
            {
                boardRoot = transform;
            }

            if (chipRoot == null)
            {
                GameObject chipRootObject = new GameObject("Chips");
                chipRootObject.transform.SetParent(boardRoot, false);
                chipRoot = chipRootObject.transform;
            }
        }

        private void OnEnable()
        {
            if (gameFlowController == null)
            {
                gameFlowController = FindFirstObjectByType<GameFlowController>();
            }

            if (gameFlowController == null)
            {
                Debug.LogError("ConnectFourWorldBoardView: Missing GameFlowController reference.");
                return;
            }

            gameFlowController.OnBoardInitialized += HandleBoardInitialized;
            gameFlowController.OnBoardChanged += HandleBoardChanged;
            gameFlowController.OnDiscPlaced += HandleDiscPlaced;
        }

        private void OnDisable()
        {
            if (gameFlowController == null)
            {
                return;
            }

            gameFlowController.OnBoardInitialized -= HandleBoardInitialized;
            gameFlowController.OnBoardChanged -= HandleBoardChanged;
            gameFlowController.OnDiscPlaced -= HandleDiscPlaced;
        }

        private void HandleBoardInitialized(int columns, int rows)
        {
            _columns = columns;
            _rows = rows;
            BuildAnchors(columns, rows);
            ClearAllChips();
        }

        private void HandleBoardChanged(BoardState board)
        {
            if (board == null)
            {
                return;
            }

            for (int column = 0; column < _columns; column++)
            {
                for (int row = 0; row < _rows; row++)
                {
                    Vector2Int key = new Vector2Int(column, row);
                    PlayerSide side = board.GetCell(column, row);

                    if (side == PlayerSide.None)
                    {
                        RemoveChip(key);
                    }
                    else if (!_spawnedChips.ContainsKey(key))
                    {
                        SpawnChip(side, key, animateDrop: false);
                    }
                }
            }
        }

        private void HandleDiscPlaced(int column, int row, PlayerSide side)
        {
            Vector2Int key = new Vector2Int(column, row);
            if (_spawnedChips.ContainsKey(key))
            {
                return;
            }

            SpawnChip(side, key, animateDrop: true);
        }

        private void BuildAnchors(int columns, int rows)
        {
            ClearChildrenExceptChipRoot();
            _cellAnchors.Clear();

            float columnSpacing = worldConfig != null ? worldConfig.ColumnSpacing : 1f;
            float rowSpacing = worldConfig != null ? worldConfig.RowSpacing : 1f;
            Vector3 cellOffset = worldConfig != null ? worldConfig.CellOffset : Vector3.zero;

            float xStart = -(columns - 1) * 0.5f * columnSpacing;

            for (int column = 0; column < columns; column++)
            {
                for (int row = 0; row < rows; row++)
                {
                    Vector3 localPosition = new Vector3(
                        xStart + column * columnSpacing,
                        row * rowSpacing,
                        0f) + cellOffset;

                    GameObject anchorObject = new GameObject($"Cell_{column}_{row}");
                    anchorObject.transform.SetParent(boardRoot, false);
                    anchorObject.transform.localPosition = localPosition;

                    _cellAnchors[new Vector2Int(column, row)] = anchorObject.transform;
                }
            }

            BuildColumnClickTargets(columns, rows, xStart, columnSpacing, rowSpacing, cellOffset);
        }

        private void BuildColumnClickTargets(int columns, int rows, float xStart, float columnSpacing, float rowSpacing, Vector3 cellOffset)
        {
            bool generateTargets = worldConfig == null || worldConfig.GenerateColumnClickTargets;
            if (!generateTargets)
            {
                return;
            }

            float widthFactor = worldConfig != null ? Mathf.Max(0.1f, worldConfig.ClickTargetWidthFactor) : 0.9f;
            float targetDepth = worldConfig != null ? Mathf.Max(0.1f, worldConfig.ClickTargetDepth) : 0.8f;

            for (int column = 0; column < columns; column++)
            {
                GameObject targetObject = new GameObject($"ColumnTarget_{column}");
                targetObject.transform.SetParent(boardRoot, false);

                float x = xStart + column * columnSpacing + cellOffset.x;
                float y = ((rows - 1) * rowSpacing * 0.5f) + cellOffset.y;
                float z = cellOffset.z;
                targetObject.transform.localPosition = new Vector3(x, y, z);

                BoxCollider collider = targetObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(columnSpacing * widthFactor, rows * rowSpacing + rowSpacing * 0.5f, targetDepth);

                ConnectFourColumnClickTarget clickTarget = targetObject.AddComponent<ConnectFourColumnClickTarget>();
                clickTarget.SetColumnIndex(column);
            }
        }

        private void SpawnChip(PlayerSide side, Vector2Int key, bool animateDrop)
        {
            if (!_cellAnchors.TryGetValue(key, out Transform anchor))
            {
                return;
            }

            GameObject chipObject = CreateChipObject(side);
            chipObject.name = $"Chip_{side}_{key.x}_{key.y}";
            chipObject.transform.SetParent(chipRoot, false);
            chipObject.transform.localRotation = Quaternion.identity;

            float startHeight = worldConfig != null ? worldConfig.DropStartHeight : 4f;
            float dropDuration = worldConfig != null ? worldConfig.DropDuration : 0.2f;

            Vector3 targetPosition = anchor.localPosition;
            if (animateDrop)
            {
                Vector3 startPosition = targetPosition + Vector3.up * startHeight;
                chipObject.transform.localPosition = startPosition;
                StartCoroutine(AnimateDrop(chipObject.transform, startPosition, targetPosition, dropDuration));
            }
            else
            {
                chipObject.transform.localPosition = targetPosition;
            }

            _spawnedChips[key] = chipObject;
        }

        private IEnumerator AnimateDrop(Transform chipTransform, Vector3 startPosition, Vector3 targetPosition, float duration)
        {
            if (duration <= 0f)
            {
                chipTransform.localPosition = targetPosition;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - (1f - t) * (1f - t);
                chipTransform.localPosition = Vector3.LerpUnclamped(startPosition, targetPosition, eased);
                yield return null;
            }

            chipTransform.localPosition = targetPosition;
        }

        private GameObject CreateChipObject(PlayerSide side)
        {
            GameObject prefab = null;
            if (worldConfig != null)
            {
                switch (side)
                {
                    case PlayerSide.Community:
                        prefab = worldConfig.CommunityChipPrefab;
                        break;
                    case PlayerSide.Streamer:
                        prefab = worldConfig.StreamerChipPrefab;
                        break;
                    default:
                        prefab = worldConfig.BotChipPrefab;
                        break;
                }
            }

            if (prefab != null)
            {
                return Instantiate(prefab);
            }

            GameObject fallbackChip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            fallbackChip.transform.localScale = worldConfig != null ? worldConfig.FallbackChipScale : new Vector3(0.8f, 0.2f, 0.8f);

            Renderer renderer = fallbackChip.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                renderer.material.color = ResolveFallbackColor(side);
            }

            Collider collider = fallbackChip.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            return fallbackChip;
        }

        private Color ResolveFallbackColor(PlayerSide side)
        {
            if (worldConfig == null)
            {
                return side == PlayerSide.Community ? Color.red : side == PlayerSide.Streamer ? Color.cyan : Color.yellow;
            }

            return side == PlayerSide.Community
                ? worldConfig.CommunityFallbackColor
                : side == PlayerSide.Streamer
                    ? worldConfig.StreamerFallbackColor
                    : worldConfig.BotFallbackColor;
        }

        private void RemoveChip(Vector2Int key)
        {
            if (!_spawnedChips.TryGetValue(key, out GameObject chip))
            {
                return;
            }

            _spawnedChips.Remove(key);
            if (chip != null)
            {
                Destroy(chip);
            }
        }

        private void ClearAllChips()
        {
            foreach (KeyValuePair<Vector2Int, GameObject> entry in _spawnedChips)
            {
                if (entry.Value != null)
                {
                    Destroy(entry.Value);
                }
            }

            _spawnedChips.Clear();
        }

        private void ClearChildrenExceptChipRoot()
        {
            List<Transform> toDelete = new List<Transform>();
            for (int i = 0; i < boardRoot.childCount; i++)
            {
                Transform child = boardRoot.GetChild(i);
                if (chipRoot != null && child == chipRoot)
                {
                    continue;
                }

                toDelete.Add(child);
            }

            for (int i = 0; i < toDelete.Count; i++)
            {
                Destroy(toDelete[i].gameObject);
            }
        }
    }
}
