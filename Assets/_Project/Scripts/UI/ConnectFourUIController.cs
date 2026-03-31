using System.Collections;
using System.Collections.Generic;
using System.Text;
using FourWinsTikTok.Bootstrap;
using FourWinsTikTok.Config;
using FourWinsTikTok.Core;
using FourWinsTikTok.Gameplay;
using FourWinsTikTok.TikTok;
using FourWinsTikTok.Voting;
using TikTokLiveUnity;
using UnityEngine;
using UnityEngine.Networking;
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
        private Label _roundLabel;
        private Label _chatNameLabel;
        private Label _streamerNameLabel;
        private Label _chatScoreLabel;
        private Label _streamerScoreLabel;
        private Label _timerLabel;
        private Label _votingLabel;
        private Label _statusLabel;
        private Label _winnerLabel;
        private ScrollView _participantsScroll;
        private VisualElement _winnerPopup;
        private Label _popupWinnerLabel;
        private Button _nextRoundButton;
        private ParticipantRegistryService _participantRegistry;
        private readonly Dictionary<string, VisualElement> _participantRowsByUserId = new Dictionary<string, VisualElement>(System.StringComparer.OrdinalIgnoreCase);
        private string _activeParticipantUserId = string.Empty;
        private IVisualElementScheduledItem _activePulseItem;
        private VisualElement _activePulseRow;
        private bool _activePulseState;

        private float _lastDebugScaleMultiplier;
        private Vector2 _lastDebugCellSizeOffset;
        private Vector2 _lastDebugSpacingOffset;
        private Vector4 _lastDebugInsetsOffset;
        private float _lastDebugDiscSizeOffset;
        private int _lastCommunityWins = -1;
        private int _lastOpponentWins = -1;

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
            gameFlowController.OnRoundScoreChanged += HandleRoundScoreChanged;
            gameFlowController.OnRoundCompleted += HandleRoundCompleted;
            gameFlowController.OnCommunityParticipantTurnChanged += HandleCommunityParticipantTurnChanged;

            if (_nextRoundButton != null)
            {
                _nextRoundButton.clicked += HandleNextRoundClicked;
            }

            _participantRegistry = ParticipantRegistryService.EnsureInstance();
            _participantRegistry.OnParticipantRegistered += HandleParticipantRegistered;
            _participantRegistry.OnCleared += HandleParticipantsCleared;
            RebuildParticipantList();

            UpdateStreamerNameLabel();
            SetWinnerPopupVisible(false);
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
                gameFlowController.OnRoundScoreChanged -= HandleRoundScoreChanged;
                gameFlowController.OnRoundCompleted -= HandleRoundCompleted;
                gameFlowController.OnCommunityParticipantTurnChanged -= HandleCommunityParticipantTurnChanged;
            }

            if (_nextRoundButton != null)
            {
                _nextRoundButton.clicked -= HandleNextRoundClicked;
            }

            if (_participantRegistry != null)
            {
                _participantRegistry.OnParticipantRegistered -= HandleParticipantRegistered;
                _participantRegistry.OnCleared -= HandleParticipantsCleared;
            }

            StopActiveParticipantPulse();
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
            _roundLabel = root.Q<Label>("round-label");
            _chatNameLabel = root.Q<Label>("chat-name-label");
            _streamerNameLabel = root.Q<Label>("streamer-name-label");
            _chatScoreLabel = root.Q<Label>("chat-score-label");
            _streamerScoreLabel = root.Q<Label>("streamer-score-label");
            _timerLabel = root.Q<Label>("timer-label");
            _votingLabel = root.Q<Label>("voting-label");
            _statusLabel = root.Q<Label>("status-label");
            _winnerLabel = root.Q<Label>("winner-label");
            _participantsScroll = root.Q<ScrollView>("participants-scroll");
            _winnerPopup = root.Q<VisualElement>("winner-popup");
            _popupWinnerLabel = root.Q<Label>("popup-winner-label");
            _nextRoundButton = root.Q<Button>("next-round-button");
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

            SetWinnerPopupVisible(false);
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
                _timerLabel.text = string.Empty;
            }
        }

        private void HandleTimerChanged(float remainingSeconds)
        {
            if (_timerLabel != null)
            {
                _timerLabel.text = $"{remainingSeconds:0.0}";
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
                _votingLabel.text = "Turn mode: participants";
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

        private void HandleRoundScoreChanged(int round, int communityWins, int opponentWins, int targetWins)
        {
            bool communityIncreased = _lastCommunityWins >= 0 && communityWins > _lastCommunityWins;
            bool opponentIncreased = _lastOpponentWins >= 0 && opponentWins > _lastOpponentWins;

            if (_roundLabel != null)
            {
                _roundLabel.text = $"ROUND {round}";
            }

            if (_chatNameLabel != null)
            {
                _chatNameLabel.text = GetCommunityDisplayName();
            }

            if (_chatScoreLabel != null)
            {
                _chatScoreLabel.text = communityWins.ToString();
            }

            if (_streamerScoreLabel != null)
            {
                _streamerScoreLabel.text = opponentWins.ToString();
            }

            if (_statusLabel != null)
            {
                _statusLabel.text = $"First to {targetWins}";
            }

            if (communityIncreased)
            {
                StartCoroutine(PulseScoreLabel(_chatScoreLabel));
            }

            if (opponentIncreased)
            {
                StartCoroutine(PulseScoreLabel(_streamerScoreLabel));
            }

            _lastCommunityWins = communityWins;
            _lastOpponentWins = opponentWins;
        }

        private IEnumerator PulseScoreLabel(Label label)
        {
            if (label == null)
            {
                yield break;
            }

            label.RemoveFromClassList("score-win-pulse");
            yield return null;
            label.AddToClassList("score-win-pulse");
            yield return new WaitForSeconds(0.38f);
            label.RemoveFromClassList("score-win-pulse");
        }

        private void HandleRoundCompleted(PlayerSide winner, bool isMatchOver)
        {
            string communityName = GetCommunityDisplayName();
            string opponentName = GetStreamerDisplayName();

            if (_popupWinnerLabel != null)
            {
                _popupWinnerLabel.text = winner == PlayerSide.None
                    ? "Draw"
                    : winner == PlayerSide.Community
                        ? isMatchOver ? $"{communityName} Wins The Match" : $"{communityName} Wins"
                        : winner == PlayerSide.Streamer
                            ? isMatchOver ? $"{opponentName} Wins The Match" : $"{opponentName} Wins"
                            : isMatchOver ? "Bot Wins The Match" : "Bot Wins";
            }

            if (_nextRoundButton != null)
            {
                _nextRoundButton.text = isMatchOver ? "New Match" : "Next Round";
            }

            SetWinnerPopupVisible(true);
        }

        private void HandleNextRoundClicked()
        {
            if (gameFlowController != null)
            {
                gameFlowController.StartNextRound();
            }

            SetWinnerPopupVisible(false);
        }

        private void RebuildParticipantList()
        {
            if (_participantsScroll == null)
            {
                return;
            }

            _participantsScroll.contentContainer.Clear();
            _participantRowsByUserId.Clear();
            if (_participantRegistry == null)
            {
                return;
            }

            foreach (ParticipantInfo participant in _participantRegistry.Participants)
            {
                AddParticipantRow(participant);
            }
        }

        private void HandleParticipantsCleared()
        {
            if (_participantsScroll == null)
            {
                return;
            }

            _participantsScroll.contentContainer.Clear();
            _participantRowsByUserId.Clear();
            _activeParticipantUserId = string.Empty;
            StopActiveParticipantPulse();
        }

        private void HandleParticipantRegistered(ParticipantInfo participant)
        {
            AddParticipantRow(participant);
        }

        private void AddParticipantRow(ParticipantInfo participant)
        {
            if (_participantsScroll == null || participant == null)
            {
                return;
            }

            VisualElement row = new VisualElement();
            row.AddToClassList("participant-row");

            VisualElement avatar = new VisualElement();
            avatar.AddToClassList("participant-avatar");
            row.Add(avatar);

            Label nameLabel = new Label(participant.DisplayName);
            nameLabel.AddToClassList("participant-name");
            row.Add(nameLabel);

            _participantsScroll.Add(row);
            _participantRowsByUserId[participant.UserId] = row;
            if (string.Equals(_activeParticipantUserId, participant.UserId, System.StringComparison.OrdinalIgnoreCase))
            {
                SetActiveParticipantRow(row);
            }

            if (participant.AvatarPicture != null && TikTokLiveManager.Instance != null)
            {
                TikTokLiveManager.Instance.RequestSprite(participant.AvatarPicture, sprite =>
                {
                    if (sprite != null)
                    {
                        avatar.style.backgroundImage = new StyleBackground(sprite);
                    }
                });
                return;
            }

            if (!string.IsNullOrWhiteSpace(participant.AvatarUrl))
            {
                StartCoroutine(LoadAvatarFromUrlRoutine(avatar, participant.AvatarUrl));
            }
        }

        private IEnumerator LoadAvatarFromUrlRoutine(VisualElement avatarElement, string avatarUrl)
        {
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(avatarUrl))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    yield break;
                }

                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                if (texture == null)
                {
                    yield break;
                }

                Rect textureRect = new Rect(0f, 0f, texture.width, texture.height);
                Sprite sprite = Sprite.Create(texture, textureRect, new Vector2(0.5f, 0.5f));
                avatarElement.style.backgroundImage = new StyleBackground(sprite);
            }
        }

        private void SetWinnerPopupVisible(bool visible)
        {
            if (_winnerPopup != null)
            {
                _winnerPopup.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void HandleCommunityParticipantTurnChanged(string userId)
        {
            _activeParticipantUserId = userId;

            if (string.IsNullOrWhiteSpace(userId))
            {
                StopActiveParticipantPulse();
                return;
            }

            if (_participantRowsByUserId.TryGetValue(userId, out VisualElement row))
            {
                SetActiveParticipantRow(row);
            }
        }

        private void SetActiveParticipantRow(VisualElement row)
        {
            StopActiveParticipantPulse();
            _activePulseRow = row;
            _activePulseRow.AddToClassList("active-turn");
            _activePulseState = false;

            _activePulseItem = _activePulseRow.schedule.Execute(() =>
            {
                if (_activePulseRow == null)
                {
                    return;
                }

                _activePulseState = !_activePulseState;
                if (_activePulseState)
                {
                    _activePulseRow.AddToClassList("active-turn-pulse");
                }
                else
                {
                    _activePulseRow.RemoveFromClassList("active-turn-pulse");
                }
            }).Every(420);
        }

        private void StopActiveParticipantPulse()
        {
            if (_activePulseItem != null)
            {
                _activePulseItem.Pause();
                _activePulseItem = null;
            }

            if (_activePulseRow != null)
            {
                _activePulseRow.RemoveFromClassList("active-turn");
                _activePulseRow.RemoveFromClassList("active-turn-pulse");
                _activePulseRow = null;
            }

            _activePulseState = false;
        }

        private void UpdateStreamerNameLabel()
        {
            if (_streamerNameLabel == null)
            {
                return;
            }

            _streamerNameLabel.text = GetStreamerDisplayName();
        }

        private string GetCommunityDisplayName()
        {
            string value = PlayerPrefs.GetString(BootstrapKeys.CommunityDisplayNamePlayerPrefsKey, string.Empty).Trim();
            return string.IsNullOrWhiteSpace(value) ? "Chat" : value;
        }

        private string GetStreamerDisplayName()
        {
            string value = PlayerPrefs.GetString(BootstrapKeys.StreamerDisplayNamePlayerPrefsKey, string.Empty).Trim();
            return string.IsNullOrWhiteSpace(value) ? "Streamer" : value;
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
