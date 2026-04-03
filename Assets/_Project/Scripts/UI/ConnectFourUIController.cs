using System.Collections;
using System.Collections.Generic;
using FourWinsTikTok.Bootstrap;
using FourWinsTikTok.Config;
using FourWinsTikTok.Core;
using FourWinsTikTok.Gameplay;
using FourWinsTikTok.TikTok;
using FourWinsTikTok.Voting;
using TikTokLiveUnity;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
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
        [SerializeField] private string startSceneName = "Start";
        [SerializeField] private string registrationSceneName = "Registration";

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
        private Label _activePlayerNameLabel;
        private Label _statusLabel;
        private Label _winnerLabel;
        private ScrollView _participantsScroll;
        private VisualElement _activePlayerAvatar;
        private VisualElement _winnerPopup;
        private Label _popupWinnerLabel;
        private Button _nextRoundButton;
        private Button _pauseButton;
        private VisualElement _pausePopup;
        private Button _pauseResumeButton;
        private Button _pauseExitButton;
        private VisualElement _sabotageOverlay;
        private VisualElement _sabotageAvatar;
        private Label _sabotageText;
        private Coroutine _sabotageOverlayRoutine;
        private ParticipantRegistryService _participantRegistry;
        private readonly Dictionary<string, ParticipantInfo> _participantsByUserId = new Dictionary<string, ParticipantInfo>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VisualElement> _participantRowsByUserId = new Dictionary<string, VisualElement>(System.StringComparer.OrdinalIgnoreCase);
        private string _activeParticipantUserId = string.Empty;
        private VisualElement _activePulseRow;
        private IVisualElementScheduledItem _gameplayUiSyncItem;
        private GameFlowController _subscribedGameFlowController;
        private bool _hasUiTimerCountdown;
        private float _uiTimerCountdownEndRealtime;

        private float _lastDebugScaleMultiplier;
        private Vector2 _lastDebugCellSizeOffset;
        private Vector2 _lastDebugSpacingOffset;
        private Vector4 _lastDebugInsetsOffset;
        private float _lastDebugDiscSizeOffset;
        private int _lastCommunityWins = -1;
        private int _lastOpponentWins = -1;
        private bool _nextRoundRequiresRegistration;

        private void Update()
        {
            TickUiTimerCountdown();
            ForceSyncTimerFromGameFlow();

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

        private void ForceSyncTimerFromGameFlow()
        {
            if (gameFlowController == null)
            {
                return;
            }

            if (gameFlowController.CurrentState != GameFlowState.WaitingForCommunityVote
                && gameFlowController.CurrentState != GameFlowState.WaitingForStreamerMove)
            {
                return;
            }

            float authoritativeRemaining = gameFlowController.CommunityTurnRemainingSeconds;
            SetTimerLabel(authoritativeRemaining);
        }

        private void OnEnable()
        {
            ResolveGameFlowControllerReference();
            if (gameFlowController == null)
            {
                Debug.LogError("ConnectFourUIController: GameFlowController reference missing.");
                return;
            }

            if (!TryBindUi())
            {
                return;
            }

            EnsureGameFlowSubscription();

            if (gameFlowController.CurrentBoard == null)
            {
                gameFlowController.StartNewGame();
            }

            if (_nextRoundButton != null)
            {
                _nextRoundButton.clicked += HandleNextRoundClicked;
            }

            if (_pauseButton != null)
            {
                _pauseButton.clicked += HandlePauseClicked;
            }

            if (_pauseResumeButton != null)
            {
                _pauseResumeButton.clicked += HandlePauseResumeClicked;
            }

            if (_pauseExitButton != null)
            {
                _pauseExitButton.clicked += HandlePauseExitClicked;
            }

            _participantRegistry = ParticipantRegistryService.EnsureInstance();
            _participantRegistry.OnParticipantRegistered += HandleParticipantRegistered;
            _participantRegistry.OnParticipantRemoved += HandleParticipantRemoved;
            _participantRegistry.OnCleared += HandleParticipantsCleared;
            RebuildParticipantList();

            _gameplayUiSyncItem = uiDocument.rootVisualElement.schedule.Execute(SyncGameplayIndicators).Every(120);
            SyncGameplayIndicators();

            UpdateStreamerNameLabel();
            if (_sabotageOverlay != null)
            {
                _sabotageOverlay.style.display = DisplayStyle.None;
            }
            SetWinnerPopupVisible(false);
            SetPausePopupVisible(false);
        }

        private void OnDisable()
        {
            DetachGameFlowSubscription();

            if (_nextRoundButton != null)
            {
                _nextRoundButton.clicked -= HandleNextRoundClicked;
            }

            if (_pauseButton != null)
            {
                _pauseButton.clicked -= HandlePauseClicked;
            }

            if (_pauseResumeButton != null)
            {
                _pauseResumeButton.clicked -= HandlePauseResumeClicked;
            }

            if (_pauseExitButton != null)
            {
                _pauseExitButton.clicked -= HandlePauseExitClicked;
            }

            if (_participantRegistry != null)
            {
                _participantRegistry.OnParticipantRegistered -= HandleParticipantRegistered;
                _participantRegistry.OnParticipantRemoved -= HandleParticipantRemoved;
                _participantRegistry.OnCleared -= HandleParticipantsCleared;
            }

            if (_gameplayUiSyncItem != null)
            {
                _gameplayUiSyncItem.Pause();
                _gameplayUiSyncItem = null;
            }

            if (_sabotageOverlayRoutine != null)
            {
                StopCoroutine(_sabotageOverlayRoutine);
                _sabotageOverlayRoutine = null;
            }

            if (_sabotageOverlay != null)
            {
                _sabotageOverlay.style.display = DisplayStyle.None;
            }

            StopActiveParticipantPulse();
        }

        private void SyncGameplayIndicators()
        {
            EnsureGameFlowSubscription();
            if (gameFlowController == null)
            {
                return;
            }

            if (gameFlowController.CurrentState == GameFlowState.WaitingForCommunityVote || gameFlowController.CurrentState == GameFlowState.WaitingForStreamerMove)
            {
                float controllerRemaining = gameFlowController.CommunityTurnRemainingSeconds;
                SetTimerLabel(controllerRemaining);

                if (gameFlowController.CurrentState == GameFlowState.WaitingForStreamerMove)
                {
                    StopActiveParticipantPulse();

                    if (gameFlowController.IsSabotageTurnActive)
                    {
                        string hijackerUserId = gameFlowController.ActiveCommunityParticipantUserId;
                        if (!string.IsNullOrWhiteSpace(hijackerUserId))
                        {
                            if (_participantsByUserId.TryGetValue(hijackerUserId, out ParticipantInfo hijackerParticipant))
                            {
                                SetActivePlayerDisplay(hijackerParticipant);
                            }
                            else
                            {
                                SetActivePlayerDisplay(hijackerUserId, null);
                            }

                            return;
                        }
                    }

                    SetActivePlayerDisplay(GetStreamerDisplayName(), null);
                    return;
                }

                string activeUserId = gameFlowController.ActiveCommunityParticipantUserId;
                if (string.IsNullOrWhiteSpace(activeUserId))
                {
                    StopActiveParticipantPulse();
                    SetActivePlayerDisplay(string.Empty, null);
                    return;
                }

                _activeParticipantUserId = activeUserId;
                if (_participantRowsByUserId.TryGetValue(activeUserId, out VisualElement row))
                {
                    SetActiveParticipantRow(row);
                }

                if (_participantsByUserId.TryGetValue(activeUserId, out ParticipantInfo participant))
                {
                    SetActivePlayerDisplay(participant);
                }
                else
                {
                    SetActivePlayerDisplay(activeUserId, null);
                }

                return;
            }

            if (_timerLabel != null)
            {
                _timerLabel.text = string.Empty;
            }

            _hasUiTimerCountdown = false;

            StopActiveParticipantPulse();
            SetActivePlayerDisplay(string.Empty, null);
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
            _activePlayerNameLabel = root.Q<Label>("active-player-name");
            _statusLabel = root.Q<Label>("status-label");
            _winnerLabel = root.Q<Label>("winner-label");
            _participantsScroll = root.Q<ScrollView>("participants-scroll");
            _activePlayerAvatar = root.Q<VisualElement>("active-player-avatar");
            _winnerPopup = root.Q<VisualElement>("winner-popup");
            _popupWinnerLabel = root.Q<Label>("popup-winner-label");
            _nextRoundButton = root.Q<Button>("next-round-button");
            _pauseButton = root.Q<Button>("pause-button");
            _pausePopup = root.Q<VisualElement>("pause-popup");
            _pauseResumeButton = root.Q<Button>("pause-resume-button");
            _pauseExitButton = root.Q<Button>("pause-exit-button");
            _sabotageOverlay = root.Q<VisualElement>("sabotage-overlay");
            _sabotageAvatar = root.Q<VisualElement>("sabotage-avatar");
            _sabotageText = root.Q<Label>("sabotage-text");
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
            float clamped = Mathf.Max(0f, remainingSeconds);
            SetTimerLabel(clamped);
        }

        private void TickUiTimerCountdown()
        {
            EnsureGameFlowSubscription();
            if (gameFlowController == null)
            {
                SetTimerLabel(0f);
                return;
            }

            if (gameFlowController.CurrentState != GameFlowState.WaitingForCommunityVote
                && gameFlowController.CurrentState != GameFlowState.WaitingForStreamerMove)
            {
                return;
            }

            float remaining = Mathf.Max(0f, gameFlowController.CommunityTurnRemainingSeconds);
            SetTimerLabel(remaining);
        }

        private void SetTimerLabel(float remainingSeconds)
        {
            if (_timerLabel == null)
            {
                return;
            }

            _timerLabel.text = $"{Mathf.Max(0f, remainingSeconds):0.0}";
        }

        private void ResolveGameFlowControllerReference()
        {
            if (gameFlowController != null)
            {
                return;
            }

            gameFlowController = FindFirstObjectByType<GameFlowController>();
        }

        private void EnsureGameFlowSubscription()
        {
            ResolveGameFlowControllerReference();

            if (_subscribedGameFlowController == gameFlowController)
            {
                return;
            }

            DetachGameFlowSubscription();
            if (gameFlowController == null)
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
            gameFlowController.OnSabotageTriggered += HandleSabotageTriggered;
            _subscribedGameFlowController = gameFlowController;
        }

        private void DetachGameFlowSubscription()
        {
            if (_subscribedGameFlowController == null)
            {
                return;
            }

            _subscribedGameFlowController.OnBoardInitialized -= HandleBoardInitialized;
            _subscribedGameFlowController.OnBoardChanged -= HandleBoardChanged;
            _subscribedGameFlowController.OnStateChanged -= HandleStateChanged;
            _subscribedGameFlowController.OnTimerChanged -= HandleTimerChanged;
            _subscribedGameFlowController.OnVoteUpdated -= HandleVoteUpdated;
            _subscribedGameFlowController.OnStatusMessage -= HandleStatusMessage;
            _subscribedGameFlowController.OnGameOver -= HandleGameOver;
            _subscribedGameFlowController.OnRoundScoreChanged -= HandleRoundScoreChanged;
            _subscribedGameFlowController.OnRoundCompleted -= HandleRoundCompleted;
            _subscribedGameFlowController.OnCommunityParticipantTurnChanged -= HandleCommunityParticipantTurnChanged;
            _subscribedGameFlowController.OnSabotageTriggered -= HandleSabotageTriggered;
            _subscribedGameFlowController = null;
        }

        private void HandleSabotageTriggered(GiftMessage giftMessage)
        {
            if (_sabotageOverlay == null)
            {
                return;
            }

            string displayName = string.IsNullOrWhiteSpace(giftMessage.DisplayName)
                ? giftMessage.UserId
                : giftMessage.DisplayName;

            if (_sabotageText != null)
            {
                _sabotageText.text = $"{displayName} hijacked the turn!";
            }

            if (_sabotageAvatar != null)
            {
                _sabotageAvatar.style.backgroundImage = StyleKeyword.Null;

                if (giftMessage.AvatarPicture != null && TikTokLiveManager.Instance != null)
                {
                    TikTokLiveManager.Instance.RequestSprite(giftMessage.AvatarPicture, sprite =>
                    {
                        if (sprite != null && _sabotageAvatar != null)
                        {
                            _sabotageAvatar.style.backgroundImage = new StyleBackground(sprite);
                        }
                    });
                }
                else if (!string.IsNullOrWhiteSpace(giftMessage.AvatarUrl))
                {
                    StartCoroutine(LoadAvatarFromUrlRoutine(_sabotageAvatar, giftMessage.AvatarUrl));
                }
            }

            if (_sabotageOverlayRoutine != null)
            {
                StopCoroutine(_sabotageOverlayRoutine);
            }

            _sabotageOverlayRoutine = StartCoroutine(ShowSabotageOverlayRoutine());
        }

        private IEnumerator ShowSabotageOverlayRoutine()
        {
            _sabotageOverlay.style.display = DisplayStyle.Flex;
            yield return new WaitForSeconds(2f);
            _sabotageOverlay.style.display = DisplayStyle.None;
            _sabotageOverlayRoutine = null;
        }

        private void HandleVoteUpdated(IReadOnlyList<VoteTally> ranking)
        {
            // Voting text removed from HUD by design.
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
            _nextRoundRequiresRegistration = !isMatchOver && winner != PlayerSide.None;
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
                _nextRoundButton.text = isMatchOver
                    ? "New Match"
                    : _nextRoundRequiresRegistration
                        ? "Start New Registration"
                        : "Next Round";
            }

            SetWinnerPopupVisible(true);
        }

        private void HandleNextRoundClicked()
        {
            Debug.Log($"ConnectFourUIController: NextRound clicked. requiresRegistration={_nextRoundRequiresRegistration}, registrationSceneName='{registrationSceneName}'.");

            if (_nextRoundRequiresRegistration)
            {
                PersistMatchProgressForRegistrationLoop();
                if (!TryLoadRegistrationScene())
                {
                    if (_statusLabel != null)
                    {
                        _statusLabel.text = "Registration scene could not be loaded. Check scene name/build settings.";
                    }

                    return;
                }

                SetWinnerPopupVisible(false);
                return;
            }

            if (gameFlowController != null)
            {
                gameFlowController.StartNextRound();
            }

            _nextRoundRequiresRegistration = false;
            SetWinnerPopupVisible(false);
        }

        private void PersistMatchProgressForRegistrationLoop()
        {
            if (gameFlowController == null)
            {
                return;
            }

            int nextRound = Mathf.Max(1, gameFlowController.CurrentRound + 1);
            PlayerPrefs.SetInt(BootstrapKeys.MatchResumePendingPlayerPrefsKey, 1);
            PlayerPrefs.SetInt(BootstrapKeys.MatchResumeRoundPlayerPrefsKey, nextRound);
            PlayerPrefs.SetInt(BootstrapKeys.MatchResumeCommunityWinsPlayerPrefsKey, gameFlowController.CommunityWins);
            PlayerPrefs.SetInt(BootstrapKeys.MatchResumeOpponentWinsPlayerPrefsKey, gameFlowController.OpponentWins);
            PlayerPrefs.Save();
        }

        private bool TryLoadRegistrationScene()
        {
            string configuredScene = string.IsNullOrWhiteSpace(registrationSceneName)
                ? "Registration"
                : registrationSceneName.Trim();

            int sceneIndex = ResolveSceneBuildIndex(configuredScene);
            if (sceneIndex >= 0)
            {
                Debug.Log($"ConnectFourUIController: Loading registration scene by index {sceneIndex} (configured='{configuredScene}').");
                SceneManager.LoadScene(sceneIndex, LoadSceneMode.Single);
                return true;
            }

            const string fallbackSceneName = "Registration";
            if (!string.Equals(configuredScene, fallbackSceneName, System.StringComparison.OrdinalIgnoreCase))
            {
                int fallbackIndex = ResolveSceneBuildIndex(fallbackSceneName);
                if (fallbackIndex >= 0)
                {
                    Debug.LogWarning($"ConnectFourUIController: Scene '{configuredScene}' not loadable. Falling back to '{fallbackSceneName}' (index {fallbackIndex}).");
                    SceneManager.LoadScene(fallbackIndex, LoadSceneMode.Single);
                    return true;
                }
            }

            const string fallbackScenePath = "Assets/_Project/Scenes/Registration.unity";
            int fallbackPathIndex = ResolveSceneBuildIndex(fallbackScenePath);
            if (fallbackPathIndex >= 0)
            {
                Debug.LogWarning($"ConnectFourUIController: Scene '{configuredScene}' not loadable. Falling back to '{fallbackScenePath}' (index {fallbackPathIndex}).");
                SceneManager.LoadScene(fallbackPathIndex, LoadSceneMode.Single);
                return true;
            }

            Debug.LogError($"ConnectFourUIController: Registration scene load failed. Configured='{configuredScene}'. Available build scenes: {GetBuildScenesDebugList()}");
            return false;
        }

        private static int ResolveSceneBuildIndex(string configuredScene)
        {
            if (string.IsNullOrWhiteSpace(configuredScene))
            {
                return -1;
            }

            string normalized = configuredScene.Trim();
            string configuredFileName = Path.GetFileNameWithoutExtension(normalized);

            for (int index = 0; index < SceneManager.sceneCountInBuildSettings; index++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(index);
                string sceneFileName = Path.GetFileNameWithoutExtension(scenePath);

                if (string.Equals(scenePath, normalized, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sceneFileName, normalized, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sceneFileName, configuredFileName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private static string GetBuildScenesDebugList()
        {
            if (SceneManager.sceneCountInBuildSettings <= 0)
            {
                return "<none>";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int index = 0; index < SceneManager.sceneCountInBuildSettings; index++)
            {
                if (index > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(index);
                builder.Append(':');
                builder.Append(SceneUtility.GetScenePathByBuildIndex(index));
            }

            return builder.ToString();
        }

        private void HandlePauseClicked()
        {
            if (gameFlowController == null || gameFlowController.CurrentState == GameFlowState.GameOver)
            {
                return;
            }

            gameFlowController.PauseGameplay();
            SetPausePopupVisible(true);
        }

        private void HandlePauseResumeClicked()
        {
            if (gameFlowController != null)
            {
                gameFlowController.ResumeGameplay();
            }

            SetPausePopupVisible(false);
        }

        private void HandlePauseExitClicked()
        {
            if (gameFlowController != null)
            {
                gameFlowController.ResumeGameplay();
            }

            TikTokLiveChatAdapter adapter = TikTokLiveChatAdapter.Instance;
            if (adapter == null)
            {
                adapter = FindFirstObjectByType<TikTokLiveChatAdapter>();
            }

            if (adapter != null)
            {
                adapter.Disconnect();
            }

            ParticipantRegistryService registry = ParticipantRegistryService.Instance;
            if (registry != null)
            {
                registry.ResetParticipants();
            }

            ParticipantSnapshotStore.Clear();
            SetPausePopupVisible(false);
            SceneManager.LoadScene(startSceneName, LoadSceneMode.Single);
        }

        private void RebuildParticipantList()
        {
            if (_participantsScroll == null)
            {
                return;
            }

            _participantsScroll.contentContainer.Clear();
            _participantsByUserId.Clear();
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
            _participantsByUserId.Clear();
            _participantRowsByUserId.Clear();
            _activeParticipantUserId = string.Empty;
            StopActiveParticipantPulse();
            SetActivePlayerDisplay(string.Empty, null);
        }

        private void HandleParticipantRegistered(ParticipantInfo participant)
        {
            AddParticipantRow(participant);
        }

        private void HandleParticipantRemoved(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            _participantsByUserId.Remove(userId);

            if (_participantRowsByUserId.TryGetValue(userId, out VisualElement row))
            {
                row?.RemoveFromHierarchy();
                _participantRowsByUserId.Remove(userId);
            }

            if (string.Equals(_activeParticipantUserId, userId, System.StringComparison.OrdinalIgnoreCase))
            {
                _activeParticipantUserId = string.Empty;
                StopActiveParticipantPulse();
                SetActivePlayerDisplay(string.Empty, null);
            }
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
            _participantsByUserId[participant.UserId] = participant;
            _participantRowsByUserId[participant.UserId] = row;
            if (string.Equals(_activeParticipantUserId, participant.UserId, System.StringComparison.OrdinalIgnoreCase))
            {
                SetActiveParticipantRow(row);
                SetActivePlayerDisplay(participant.DisplayName, participant.AvatarUrl);
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
            string[] candidates = BuildAvatarUrlCandidates(avatarUrl);
            for (int index = 0; index < candidates.Length; index++)
            {
                string candidate = candidates[index];
                using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(candidate))
                {
                    request.timeout = 10;
                    yield return request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning($"ConnectFourUIController: Avatar download failed ({request.error}) for '{candidate}'.");
                        continue;
                    }

                    byte[] imageBytes = request.downloadHandler.data;
                    if (imageBytes == null || imageBytes.Length == 0)
                    {
                        Debug.LogWarning($"ConnectFourUIController: Avatar download returned empty body for '{candidate}'.");
                        continue;
                    }

                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    bool loaded = texture.LoadImage(imageBytes, false);
                    if (!loaded)
                    {
                        Destroy(texture);
                        Debug.LogWarning($"ConnectFourUIController: Avatar texture decode failed for '{candidate}'.");
                        continue;
                    }

                    avatarElement.style.backgroundImage = new StyleBackground(texture);
                    yield break;
                }
            }

            Debug.LogWarning($"ConnectFourUIController: Avatar loading failed for all candidates derived from '{avatarUrl}'.");
        }

        private static string[] BuildAvatarUrlCandidates(string originalUrl)
        {
            if (string.IsNullOrWhiteSpace(originalUrl))
            {
                return new string[0];
            }

            string trimmed = originalUrl.Trim();
            if (trimmed.Contains("/bridge/avatar?", System.StringComparison.OrdinalIgnoreCase))
            {
                return new[] { trimmed };
            }

            string lower = trimmed.ToLowerInvariant();
            if (!lower.Contains(".webp"))
            {
                return new[] { trimmed };
            }

            string jpegCandidate = trimmed.Replace(".webp", ".jpeg");
            string jpgCandidate = trimmed.Replace(".webp", ".jpg");
            if (jpegCandidate == trimmed && jpgCandidate == trimmed)
            {
                return new[] { trimmed };
            }

            return new[] { trimmed, jpegCandidate, jpgCandidate };
        }

        private void SetWinnerPopupVisible(bool visible)
        {
            if (_winnerPopup != null)
            {
                _winnerPopup.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void SetPausePopupVisible(bool visible)
        {
            if (_pausePopup != null)
            {
                _pausePopup.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void HandleCommunityParticipantTurnChanged(string userId)
        {
            _activeParticipantUserId = userId;

            if (string.IsNullOrWhiteSpace(userId))
            {
                StopActiveParticipantPulse();
                if (gameFlowController != null && gameFlowController.CurrentState == GameFlowState.WaitingForStreamerMove)
                {
                    SetActivePlayerDisplay(GetStreamerDisplayName(), null);
                }
                else
                {
                    SetActivePlayerDisplay(string.Empty, null);
                }
                return;
            }

            if (_participantRowsByUserId.TryGetValue(userId, out VisualElement row))
            {
                SetActiveParticipantRow(row);
            }

            if (_participantsByUserId.TryGetValue(userId, out ParticipantInfo participant))
            {
                SetActivePlayerDisplay(participant);
            }
            else
            {
                SetActivePlayerDisplay(userId, null);
            }
        }

        private void SetActiveParticipantRow(VisualElement row)
        {
            if (_activePulseRow == row)
            {
                return;
            }

            StopActiveParticipantPulse();
            _activePulseRow = row;
            _activePulseRow.AddToClassList("active-turn");
        }

        private void StopActiveParticipantPulse()
        {
            if (_activePulseRow != null)
            {
                _activePulseRow.RemoveFromClassList("active-turn");
                _activePulseRow = null;
            }
        }

        private void SetActivePlayerDisplay(string displayName, string avatarUrl)
        {
            if (_activePlayerNameLabel != null)
            {
                _activePlayerNameLabel.text = string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName;
            }

            if (_activePlayerAvatar == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(avatarUrl))
            {
                _activePlayerAvatar.style.backgroundImage = StyleKeyword.Null;
                return;
            }

            StartCoroutine(LoadAvatarFromUrlRoutine(_activePlayerAvatar, avatarUrl));
        }

        private void SetActivePlayerDisplay(ParticipantInfo participant)
        {
            if (participant == null)
            {
                SetActivePlayerDisplay(string.Empty, null);
                return;
            }

            string displayName = string.IsNullOrWhiteSpace(participant.DisplayName)
                ? participant.UserId
                : participant.DisplayName;

            if (participant.AvatarPicture != null && TikTokLiveManager.Instance != null && _activePlayerAvatar != null)
            {
                if (_activePlayerNameLabel != null)
                {
                    _activePlayerNameLabel.text = displayName;
                }

                TikTokLiveManager.Instance.RequestSprite(participant.AvatarPicture, sprite =>
                {
                    if (sprite != null && _activePlayerAvatar != null)
                    {
                        _activePlayerAvatar.style.backgroundImage = new StyleBackground(sprite);
                    }
                });
                return;
            }

            SetActivePlayerDisplay(displayName, participant.AvatarUrl);
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
