using System.Collections.Generic;
using System.Text;
using FourWinsTikTok.Config;
using FourWinsTikTok.Core;
using FourWinsTikTok.Gameplay;
using FourWinsTikTok.Voting;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FourWinsTikTok.UI
{
    public class ConnectFourUIController : MonoBehaviour
    {
        [SerializeField] private GameFlowController gameFlowController;
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private ConnectFourUIConfig uiConfig;
        [SerializeField] private bool renderBoardInUi;

        [Header("Runtime Layout Debug")]
        [SerializeField] private bool enableRuntimeLayoutDebug = true;
        [SerializeField] private bool autoApplyLayoutChanges = true;
        [SerializeField, Min(0.25f)] private float debugScaleMultiplier = 1f;
        [SerializeField] private Vector2 debugCellSizeOffset = Vector2.zero;
        [SerializeField] private Vector2 debugSpacingOffset = Vector2.zero;
        [SerializeField] private Vector4 debugInsetsOffset = Vector4.zero;
        [SerializeField, Range(-0.8f, 0.8f)] private float debugDiscSizeOffset;

        private ConnectFourBoardView _boardView;

        private Label _stateLabel;
        private Label _timerLabel;
        private Label _votingLabel;
        private Label _statusLabel;
        private Label _winnerLabel;
        private Button _restartButton;

        private float _lastDebugScaleMultiplier;
        private Vector2 _lastDebugCellSizeOffset;
        private Vector2 _lastDebugSpacingOffset;
        private Vector4 _lastDebugInsetsOffset;
        private float _lastDebugDiscSizeOffset;

        private void Update()
        {
            if (!enableRuntimeLayoutDebug || !autoApplyLayoutChanges || _boardView == null)
            {
                return;
            }

            if (!HasRuntimeLayoutChanged())
            {
                return;
            }

            ApplyRuntimeLayoutAndRefreshBoard();
        }

        private void OnEnable()
        {
            if (gameFlowController == null)
            {
                Debug.LogError("ConnectFourUIController: GameFlowController reference missing.");
                return;
            }

            if (!TryBindUi())
            {
                return;
            }

            gameFlowController.OnBoardInitialized += HandleBoardInitialized;
            gameFlowController.OnBoardChanged += HandleBoardChanged;
            gameFlowController.OnStateChanged += HandleStateChanged;
            gameFlowController.OnTimerChanged += HandleTimerChanged;
            gameFlowController.OnVoteUpdated += HandleVoteUpdated;
            gameFlowController.OnStatusMessage += HandleStatusMessage;
            gameFlowController.OnGameOver += HandleGameOver;

            if (_restartButton != null)
            {
                _restartButton.clicked += HandleRestartClicked;
            }
        }

        private void OnDisable()
        {
            if (gameFlowController != null)
            {
                gameFlowController.OnBoardInitialized -= HandleBoardInitialized;
                gameFlowController.OnBoardChanged -= HandleBoardChanged;
                gameFlowController.OnStateChanged -= HandleStateChanged;
                gameFlowController.OnTimerChanged -= HandleTimerChanged;
                gameFlowController.OnVoteUpdated -= HandleVoteUpdated;
                gameFlowController.OnStatusMessage -= HandleStatusMessage;
                gameFlowController.OnGameOver -= HandleGameOver;
            }

            if (_restartButton != null)
            {
                _restartButton.clicked -= HandleRestartClicked;
            }
        }

        private bool TryBindUi()
        {
            if (uiDocument == null)
            {
                Debug.LogError("ConnectFourUIController: UIDocument reference missing.");
                return false;
            }

            VisualElement root = uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("ConnectFourUIController: UIDocument has no rootVisualElement.");
                return false;
            }

            _stateLabel = root.Q<Label>("state-label");
            _timerLabel = root.Q<Label>("timer-label");
            _votingLabel = root.Q<Label>("voting-label");
            _statusLabel = root.Q<Label>("status-label");
            _winnerLabel = root.Q<Label>("winner-label");
            _restartButton = root.Q<Button>("restart-button");
            VisualElement boardFrame = root.Q<VisualElement>("board-frame");
            VisualElement boardGrid = root.Q<VisualElement>("board-grid");

            if (renderBoardInUi)
            {
                if (boardGrid == null)
                {
                    Debug.LogError("ConnectFourUIController: Missing board-grid element in UXML while renderBoardInUi is enabled.");
                    return false;
                }

                _boardView = new ConnectFourBoardView(boardFrame, boardGrid, uiConfig);
            }

            return true;
        }

        private void HandleBoardInitialized(int columns, int rows)
        {
            if (_boardView != null)
            {
                ApplyRuntimeAdjustmentsToBoardView();
                _boardView.Initialize(columns, rows);
            }

            if (_winnerLabel != null)
            {
                _winnerLabel.text = string.Empty;
            }
        }

        private void HandleBoardChanged(BoardState board)
        {
            if (_boardView != null)
            {
                _boardView.Render(board);
            }
        }

        private void HandleStateChanged(GameFlowState state)
        {
            if (_stateLabel != null)
            {
                _stateLabel.text = $"State: {state}";
            }

            if (state != GameFlowState.WaitingForCommunityVote && _timerLabel != null)
            {
                _timerLabel.text = "Time: -";
            }
        }

        private void HandleTimerChanged(float remainingSeconds)
        {
            if (_timerLabel != null)
            {
                _timerLabel.text = $"Time: {remainingSeconds:0.0}s";
            }
        }

        private void HandleVoteUpdated(IReadOnlyList<VoteTally> ranking)
        {
            if (_votingLabel == null)
            {
                return;
            }

            if (ranking == null || ranking.Count == 0)
            {
                _votingLabel.text = "Votes: no valid votes yet";
                return;
            }

            StringBuilder builder = new StringBuilder("Votes: ");
            for (int index = 0; index < ranking.Count; index++)
            {
                VoteTally tally = ranking[index];
                builder.Append(tally.DisplayColumn);
                builder.Append('(');
                builder.Append(tally.VoteCount);
                builder.Append(')');

                if (index < ranking.Count - 1)
                {
                    builder.Append(" | ");
                }
            }

            _votingLabel.text = builder.ToString();
        }

        private void HandleStatusMessage(string message)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = message;
            }
        }

        private void HandleGameOver(PlayerSide winner)
        {
            if (_winnerLabel == null)
            {
                return;
            }

            _winnerLabel.text = winner == PlayerSide.None
                ? "Result: Draw"
                : winner == PlayerSide.Community
                    ? "Result: Community wins"
                    : winner == PlayerSide.Streamer
                        ? "Result: Streamer wins"
                        : "Result: Bot wins";
        }

        private void HandleRestartClicked()
        {
            if (gameFlowController != null)
            {
                gameFlowController.StartNewGame();
            }
        }

        private bool HasRuntimeLayoutChanged()
        {
            return !Mathf.Approximately(_lastDebugScaleMultiplier, debugScaleMultiplier)
                   || _lastDebugCellSizeOffset != debugCellSizeOffset
                   || _lastDebugSpacingOffset != debugSpacingOffset
                   || _lastDebugInsetsOffset != debugInsetsOffset
                     || !Mathf.Approximately(_lastDebugDiscSizeOffset, debugDiscSizeOffset);
        }

        private void ApplyRuntimeLayoutAndRefreshBoard()
        {
            if (_boardView == null || gameFlowController == null)
            {
                return;
            }

            ApplyRuntimeAdjustmentsToBoardView();

            BoardState board = gameFlowController.CurrentBoard;
            if (board == null)
            {
                return;
            }

            _boardView.Initialize(board.Columns, board.Rows);
            _boardView.Render(board);
        }

        private void ApplyRuntimeAdjustmentsToBoardView()
        {
            _boardView.ApplyRuntimeAdjustments(
                debugScaleMultiplier,
                debugCellSizeOffset,
                debugSpacingOffset,
                debugInsetsOffset,
                debugDiscSizeOffset);

            _lastDebugScaleMultiplier = debugScaleMultiplier;
            _lastDebugCellSizeOffset = debugCellSizeOffset;
            _lastDebugSpacingOffset = debugSpacingOffset;
            _lastDebugInsetsOffset = debugInsetsOffset;
            _lastDebugDiscSizeOffset = debugDiscSizeOffset;
        }

        [ContextMenu("Bake Debug Layout Into UI Config")]
        private void BakeDebugLayoutIntoUiConfig()
        {
            if (uiConfig == null)
            {
                Debug.LogWarning("ConnectFourUIController: Cannot bake debug values because UI config is missing.");
                return;
            }

            float newBoardScale = Mathf.Max(0.25f, uiConfig.BoardScale * Mathf.Max(0.25f, debugScaleMultiplier));
            Vector2 newCellSize = new Vector2(
                Mathf.Max(1f, uiConfig.CellSize.x + debugCellSizeOffset.x),
                Mathf.Max(1f, uiConfig.CellSize.y + debugCellSizeOffset.y));
            Vector2 newCellSpacing = uiConfig.CellSpacing + debugSpacingOffset;
            Vector4 newGridInsets = uiConfig.GridInsets + debugInsetsOffset;
            float newDiscSizeFactor = Mathf.Clamp(uiConfig.DiscSizeFactor + debugDiscSizeOffset, 0.1f, 1.2f);

            uiConfig.SetLayout(newBoardScale, newCellSize, newCellSpacing, newGridInsets, newDiscSizeFactor);

            debugScaleMultiplier = 1f;
            debugCellSizeOffset = Vector2.zero;
            debugSpacingOffset = Vector2.zero;
            debugInsetsOffset = Vector4.zero;
            debugDiscSizeOffset = 0f;

#if UNITY_EDITOR
            EditorUtility.SetDirty(uiConfig);
            AssetDatabase.SaveAssets();
#endif

            ApplyRuntimeLayoutAndRefreshBoard();
            Debug.Log("ConnectFourUIController: Baked debug layout values into UI config.");
        }
    }
}
