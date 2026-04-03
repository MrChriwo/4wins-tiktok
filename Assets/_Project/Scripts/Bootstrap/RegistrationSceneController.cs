using System.Collections;
using System.Text;
using FourWinsTikTok.Gameplay;
using FourWinsTikTok.TikTok;
using TikTokLiveUnity;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace FourWinsTikTok.Bootstrap
{
    public class RegistrationSceneController : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private TikTokLiveChatAdapter tikTokAdapter;
        [SerializeField] private string gameplaySceneName = "Game";

        [Header("Challenge UI")]
        [SerializeField] private float challengeUiTopPx = 320f;
        [SerializeField] private float challengeUiHeightPx = 260f;
        [SerializeField] private float challengeCircleSizePx = 190f;
        [SerializeField] private float challengeCounterSpacingPx = 18f;

        private Label _giftLabel;
        private Label _timerLabel;
        private Label _statusLabel;
        private Button _startGameButton;
        private ScrollView _participantsScroll;

        private string _requiredGiftName;
        private float _remainingSeconds;
        private string _beginnerChallengeGiftName;
        private int _beginnerChallengeCoinTarget;
        private float _beginnerChallengeDurationSeconds;
        private bool _beginnerChallengeActive;
        private int _beginnerChallengeAccumulatedCoins;
        private ParticipantRegistryService _registry;
        private AsyncOperation _preloadGameOperation;
        private bool _countdownFinished;
        private bool _isStartingGame;
        private bool _registrationClosed;
        private bool _challengeStarted;
        private bool _challengeFinished;
        private readonly List<TikTokLiveChatAdapter> _subscribedAdapters = new List<TikTokLiveChatAdapter>();
        private float _nextAdapterResolveAt;
        private VisualElement _participantsGrid;
        private VisualElement _activeRegistrationColumn;
        private int _activeRegistrationColumnCount;
        private VisualElement _challengePodRoot;
        private VisualElement _challengePodCircleShell;
        private VisualElement _challengePodCircleFill;
        private Label _challengePodCountLabel;
        private Label _challengePodPercentLabel;

        private const int ParticipantsPerColumn = 4;
        private const string StartingSideCommunity = "community";
        private const string StartingSideStreamer = "streamer";

        private void OnEnable()
        {
            if (uiDocument == null)
            {
                Debug.LogError("RegistrationSceneController: Missing UIDocument reference.");
                enabled = false;
                return;
            }

            TikTokLiveChatAdapter persistentAdapter = TikTokLiveChatAdapter.Instance;
            if (persistentAdapter != null)
            {
                tikTokAdapter = persistentAdapter;
            }
            else if (tikTokAdapter == null)
            {
                tikTokAdapter = FindFirstObjectByType<TikTokLiveChatAdapter>();
            }

            _registry = ParticipantRegistryService.EnsureInstance();
            _registry.ResetParticipants();
            ParticipantSnapshotStore.Clear();

            _requiredGiftName = PlayerPrefs.GetString(BootstrapKeys.RegistrationGiftNamePlayerPrefsKey, "Rose").Trim();
            if (string.IsNullOrWhiteSpace(_requiredGiftName))
            {
                _requiredGiftName = "Rose";
                Debug.LogWarning("RegistrationSceneController: Registration gift was empty. Falling back to 'Rose'.");
            }

            _remainingSeconds = Mathf.Clamp(PlayerPrefs.GetInt(BootstrapKeys.RegistrationDurationSecondsPlayerPrefsKey, 20), 5, 600);
            _beginnerChallengeGiftName = PlayerPrefs.GetString(BootstrapKeys.BeginnerChallengeGiftNamePlayerPrefsKey, "Rose").Trim();
            if (string.IsNullOrWhiteSpace(_beginnerChallengeGiftName))
            {
                _beginnerChallengeGiftName = "Rose";
            }

            _beginnerChallengeCoinTarget = Mathf.Clamp(PlayerPrefs.GetInt(BootstrapKeys.BeginnerChallengeCoinTargetPlayerPrefsKey, 500), 1, 500000);
            _beginnerChallengeDurationSeconds = Mathf.Clamp(PlayerPrefs.GetInt(BootstrapKeys.BeginnerChallengeDurationSecondsPlayerPrefsKey, 10), 1, 120);

            VisualElement root = uiDocument.rootVisualElement;
            _giftLabel = root.Q<Label>("gift-label");
            _timerLabel = root.Q<Label>("timer-label");
            _statusLabel = root.Q<Label>("status-label");
            _startGameButton = root.Q<Button>("start-game-button");
            _participantsScroll = root.Q<ScrollView>("participants-scroll");

            if (_giftLabel == null || _timerLabel == null || _statusLabel == null || _startGameButton == null || _participantsScroll == null)
            {
                Debug.LogError("RegistrationSceneController: Missing required UI elements in RegistrationScreen UXML.");
                enabled = false;
                return;
            }

            SpawnChallengePod(root);
            ApplyChallengeUiLayoutSettings();

            _giftLabel.text = $"Send gift to register: {_requiredGiftName}";
            _statusLabel.text = "Waiting for participants...";
            _startGameButton.style.display = DisplayStyle.None;
            _startGameButton.clicked += HandleStartGameClicked;
            _countdownFinished = false;
            _isStartingGame = false;
            _registrationClosed = false;
            _challengeStarted = false;
            _challengeFinished = false;
            _beginnerChallengeActive = false;
            _beginnerChallengeAccumulatedCoins = 0;
            SetChallengePodVisible(false);
            UpdateChallengePodVisuals();
            SetChallengeFocusMode(false);
            _nextAdapterResolveAt = Time.unscaledTime;
            BuildParticipantsGrid();

            Debug.Log($"RegistrationSceneController: Opened registration. RequiredGift='{_requiredGiftName}', Duration={_remainingSeconds:0}s.");

            if (tikTokAdapter != null)
            {
                Debug.Log($"RegistrationSceneController: Adapter resolved. Connected={tikTokAdapter.IsConnected}, Connecting={tikTokAdapter.IsConnecting}.");
            }
            else
            {
                Debug.LogWarning("RegistrationSceneController: TikTok adapter not found in registration scene.");
            }

            EnsureAdapterSubscription();

            _registry.OnParticipantRegistered += HandleParticipantRegistered;
            _registry.OnCleared += HandleParticipantsCleared;

            StartCoroutine(RegistrationCountdownRoutine());
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextAdapterResolveAt)
            {
                return;
            }

            _nextAdapterResolveAt = Time.unscaledTime + 1f;
            EnsureAdapterSubscription();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            challengeUiTopPx = Mathf.Clamp(challengeUiTopPx, 0f, 4000f);
            challengeUiHeightPx = Mathf.Clamp(challengeUiHeightPx, 80f, 2000f);
            challengeCircleSizePx = Mathf.Clamp(challengeCircleSizePx, 60f, 1200f);
            challengeCounterSpacingPx = Mathf.Clamp(challengeCounterSpacingPx, 0f, 400f);

            ApplyChallengeUiLayoutSettings();

            if (!Application.isPlaying)
            {
                return;
            }

            UpdateChallengePodVisuals();
        }
#endif

        private void OnDisable()
        {
            DetachAdapterSubscription();

            if (_registry != null)
            {
                _registry.OnParticipantRegistered -= HandleParticipantRegistered;
                _registry.OnCleared -= HandleParticipantsCleared;
            }

            if (_startGameButton != null)
            {
                _startGameButton.clicked -= HandleStartGameClicked;
            }
        }

        private void EnsureAdapterSubscription()
        {
            TikTokLiveChatAdapter[] adapters = FindObjectsByType<TikTokLiveChatAdapter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            tikTokAdapter = ResolvePreferredAdapter();

            if (adapters.Length == 0)
            {
                Debug.LogWarning("RegistrationSceneController: Adapter still missing, waiting for resolve...");
                DetachAdapterSubscription();
                return;
            }

            for (int index = _subscribedAdapters.Count - 1; index >= 0; index--)
            {
                TikTokLiveChatAdapter existing = _subscribedAdapters[index];
                if (existing == null || System.Array.IndexOf(adapters, existing) < 0)
                {
                    UnsubscribeAdapter(existing);
                    _subscribedAdapters.RemoveAt(index);
                }
            }

            for (int index = 0; index < adapters.Length; index++)
            {
                TikTokLiveChatAdapter adapter = adapters[index];
                if (adapter == null || _subscribedAdapters.Contains(adapter))
                {
                    continue;
                }

                adapter.OnGiftReceived += HandleGiftReceived;
                adapter.OnAdminCommandReceived += HandleAdminCommandReceived;
#if UNITY_EDITOR
                adapter.OnChatMessageReceived += HandleChatMessage;
#endif
                _subscribedAdapters.Add(adapter);
            }

            int connectedCount = 0;
            for (int index = 0; index < _subscribedAdapters.Count; index++)
            {
                if (_subscribedAdapters[index] != null && _subscribedAdapters[index].IsConnected)
                {
                    connectedCount++;
                }
            }

            Debug.Log($"RegistrationSceneController: Adapter subscriptions active={_subscribedAdapters.Count}, connected={connectedCount}, preferredConnected={tikTokAdapter != null && tikTokAdapter.IsConnected}.");
        }

        private TikTokLiveChatAdapter ResolvePreferredAdapter()
        {
            TikTokLiveChatAdapter[] adapters = FindObjectsByType<TikTokLiveChatAdapter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            TikTokLiveChatAdapter fallback = TikTokLiveChatAdapter.Instance;

            for (int index = 0; index < adapters.Length; index++)
            {
                TikTokLiveChatAdapter candidate = adapters[index];
                if (candidate != null && candidate.IsConnected)
                {
                    return candidate;
                }
            }

            for (int index = 0; index < adapters.Length; index++)
            {
                TikTokLiveChatAdapter candidate = adapters[index];
                if (candidate != null && candidate.IsConnecting)
                {
                    return candidate;
                }
            }

            if (fallback != null)
            {
                return fallback;
            }

            return adapters.Length > 0 ? adapters[0] : null;
        }

        private void DetachAdapterSubscription()
        {
            if (_subscribedAdapters.Count == 0)
            {
                return;
            }

            for (int index = _subscribedAdapters.Count - 1; index >= 0; index--)
            {
                UnsubscribeAdapter(_subscribedAdapters[index]);
            }

            _subscribedAdapters.Clear();
        }

        private void UnsubscribeAdapter(TikTokLiveChatAdapter adapter)
        {
            if (adapter == null)
            {
                return;
            }

            adapter.OnGiftReceived -= HandleGiftReceived;
            adapter.OnAdminCommandReceived -= HandleAdminCommandReceived;
#if UNITY_EDITOR
            adapter.OnChatMessageReceived -= HandleChatMessage;
#endif
        }

        private IEnumerator RegistrationCountdownRoutine()
        {
            _preloadGameOperation = null;

            while (_remainingSeconds > 0f)
            {
                _timerLabel.text = Mathf.CeilToInt(_remainingSeconds).ToString();
                _remainingSeconds -= Time.deltaTime;
                yield return null;
            }

            _timerLabel.text = "0";
            _registrationClosed = true;
            Debug.Log($"RegistrationSceneController: Registration closed. Participants={_registry.Count}.");

            _statusLabel.text = "Registration abgeschlossen. Starte jetzt die Beginner Challenge.";
            _startGameButton.text = "Start Beginner Challenge";
            _startGameButton.style.display = DisplayStyle.Flex;
            _startGameButton.SetEnabled(true);
            _countdownFinished = true;
            yield break;
        }

        private IEnumerator BeginnerChallengeRoutine()
        {
            if (_challengeStarted)
            {
                yield break;
            }

            _challengeStarted = true;
            _challengeFinished = false;
            _beginnerChallengeActive = true;
            _beginnerChallengeAccumulatedCoins = 0;
            SetChallengePodVisible(true);
            SetChallengeFocusMode(true);
            UpdateChallengePodVisuals();
            _startGameButton.style.display = DisplayStyle.None;
            _startGameButton.SetEnabled(false);

            _giftLabel.text = $"Beginner Challenge: {_beginnerChallengeGiftName} ({_beginnerChallengeCoinTarget} coins in {Mathf.RoundToInt(_beginnerChallengeDurationSeconds)}s)";
            _statusLabel.text = $"Community challenge started: 0 / {_beginnerChallengeCoinTarget} coins.";

            float remaining = _beginnerChallengeDurationSeconds;
            while (remaining > 0f)
            {
                _timerLabel.text = Mathf.CeilToInt(remaining).ToString();
                remaining -= Time.deltaTime;
                yield return null;
            }

            _timerLabel.text = "0";
            _beginnerChallengeActive = false;
            _challengeFinished = true;
            UpdateChallengePodVisuals();

            bool communityStarts = _beginnerChallengeAccumulatedCoins >= _beginnerChallengeCoinTarget;
            string startingSide = communityStarts ? StartingSideCommunity : StartingSideStreamer;
            PlayerPrefs.SetString(BootstrapKeys.MatchStartingSidePlayerPrefsKey, startingSide);
            PlayerPrefs.Save();

            _startGameButton.text = "Start Game";
            _startGameButton.style.display = DisplayStyle.Flex;
            _startGameButton.SetEnabled(true);

            _statusLabel.text = communityStarts
                ? $"Challenge won ({_beginnerChallengeAccumulatedCoins}/{_beginnerChallengeCoinTarget}). Community starts. Press Start Game."
                : $"Challenge failed ({_beginnerChallengeAccumulatedCoins}/{_beginnerChallengeCoinTarget}). Streamer starts. Press Start Game.";
        }

        private void HandleStartGameClicked()
        {
            if (_isStartingGame || !_countdownFinished)
            {
                return;
            }

            if (_registrationClosed && !_challengeStarted)
            {
                StartCoroutine(BeginnerChallengeRoutine());
                return;
            }

            if (!_challengeFinished)
            {
                return;
            }

            StartCoroutine(StartGameRoutine());
        }

        private IEnumerator StartGameRoutine()
        {
            _isStartingGame = true;
            _startGameButton.SetEnabled(false);
            _statusLabel.text = "Starting game...";

            if (_preloadGameOperation == null)
            {
                yield return SceneManager.LoadSceneAsync(gameplaySceneName, LoadSceneMode.Single);
                yield break;
            }

            while (_preloadGameOperation.progress < 0.9f)
            {
                yield return null;
            }

            _preloadGameOperation.allowSceneActivation = true;
            while (!_preloadGameOperation.isDone)
            {
                yield return null;
            }
        }

        private void HandleGiftReceived(GiftMessage giftMessage)
        {
            Debug.Log($"RegistrationSceneController: Gift event user='{giftMessage.UserId}', gift='{giftMessage.GiftName}', closed={_registrationClosed}, remaining={_remainingSeconds:0.0}s.");

            string receivedGiftName = giftMessage.GiftName?.Trim();
            if (string.IsNullOrWhiteSpace(receivedGiftName))
            {
                Debug.Log("RegistrationSceneController: Ignored gift event because gift name is empty.");
                return;
            }

            if (_registrationClosed)
            {
                if (!_beginnerChallengeActive)
                {
                    Debug.Log($"RegistrationSceneController: Ignored gift from '{giftMessage.UserId}' because registration and challenge are closed.");
                    return;
                }

                if (!IsGiftMatchForChallenge(receivedGiftName))
                {
                    return;
                }

                int coinsToAdd = Mathf.Max(1, giftMessage.GiftCoins);
                _beginnerChallengeAccumulatedCoins += coinsToAdd;
                _statusLabel.text = $"Community challenge: {_beginnerChallengeAccumulatedCoins} / {_beginnerChallengeCoinTarget} coins.";
                UpdateChallengePodVisuals();
                Debug.Log($"RegistrationSceneController: Challenge progress +{coinsToAdd} by '{giftMessage.UserId}'. Total={_beginnerChallengeAccumulatedCoins}/{_beginnerChallengeCoinTarget}");
                return;
            }

            if (!IsRegistrationGiftMatch(receivedGiftName))
            {
                Debug.Log($"RegistrationSceneController: Ignored gift '{receivedGiftName}'. Required='{_requiredGiftName}'.");
                return;
            }

            if (_registry.TryRegisterParticipant(giftMessage.UserId, giftMessage.DisplayName, giftMessage.AvatarPicture, giftMessage.AvatarUrl, out _))
            {
                _statusLabel.text = $"Registered participants: {_registry.Count}";
                Debug.Log($"RegistrationSceneController: Registered '{giftMessage.UserId}' via gift '{receivedGiftName}'. Total={_registry.Count}.");
            }
            else
            {
                Debug.Log($"RegistrationSceneController: Gift matched but participant '{giftMessage.UserId}' was already registered.");
            }
        }

        private bool IsGiftMatchForChallenge(string receivedGiftName)
        {
            string requiredNormalized = NormalizeGiftName(_beginnerChallengeGiftName);
            string receivedNormalized = NormalizeGiftName(receivedGiftName);

            if (string.IsNullOrWhiteSpace(requiredNormalized) || string.IsNullOrWhiteSpace(receivedNormalized))
            {
                return false;
            }

            if (string.Equals(requiredNormalized, receivedNormalized, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return receivedNormalized.Contains(requiredNormalized, System.StringComparison.OrdinalIgnoreCase)
                   || requiredNormalized.Contains(receivedNormalized, System.StringComparison.OrdinalIgnoreCase);
        }

        private void HandleAdminCommandReceived(AdminCommandMessage adminCommand)
        {
            if (adminCommand.Command == null)
            {
                return;
            }

            switch (adminCommand.Command)
            {
                case "register":
                    HandleAdminRegisterDuringRegistration(adminCommand);
                    break;
                case "kick":
                    HandleAdminKickDuringRegistration(adminCommand);
                    break;
            }
        }

        private void HandleAdminRegisterDuringRegistration(AdminCommandMessage adminCommand)
        {
            if (_registrationClosed)
            {
                return;
            }

            string userId = string.IsNullOrWhiteSpace(adminCommand.TargetUserId)
                ? adminCommand.IssuedByUserId
                : adminCommand.TargetUserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            string displayName = string.IsNullOrWhiteSpace(adminCommand.TargetDisplayName)
                ? userId
                : adminCommand.TargetDisplayName;

            if (_registry.TryRegisterParticipant(userId, displayName, null, null, out _))
            {
                _statusLabel.text = $"Registered participants: {_registry.Count}";
            }
        }

        private void HandleAdminKickDuringRegistration(AdminCommandMessage adminCommand)
        {
            if (_registrationClosed || string.IsNullOrWhiteSpace(adminCommand.TargetUserId))
            {
                return;
            }

            if (_registry.RemoveParticipant(adminCommand.TargetUserId))
            {
                _statusLabel.text = $"Registered participants: {_registry.Count}";
            }
        }

        private bool IsRegistrationGiftMatch(string receivedGiftName)
        {
            string requiredNormalized = NormalizeGiftName(_requiredGiftName);
            string receivedNormalized = NormalizeGiftName(receivedGiftName);

            if (string.IsNullOrWhiteSpace(requiredNormalized) || string.IsNullOrWhiteSpace(receivedNormalized))
            {
                return false;
            }

            if (string.Equals(requiredNormalized, receivedNormalized, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            bool fuzzyMatch = receivedNormalized.Contains(requiredNormalized, System.StringComparison.OrdinalIgnoreCase)
                              || requiredNormalized.Contains(receivedNormalized, System.StringComparison.OrdinalIgnoreCase);

            if (fuzzyMatch)
            {
                Debug.Log($"RegistrationSceneController: Fuzzy gift match Required='{_requiredGiftName}' ({requiredNormalized}), Received='{receivedGiftName}' ({receivedNormalized}).");
            }

            return fuzzyMatch;
        }

        private static string NormalizeGiftName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char character = char.ToLowerInvariant(value[index]);
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

#if UNITY_EDITOR
        private void HandleChatMessage(ChatMessage chatMessage)
        {
            if (_registrationClosed)
            {
                Debug.Log($"RegistrationSceneController: Ignored chat register command from '{chatMessage.UserId}' because registration is closed.");
                return;
            }

            string sanitizedMessage = chatMessage.Message?.Trim();
            if (!string.Equals(chatMessage.UserId, "ichriwo", System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool isRegisterCommand = string.Equals(sanitizedMessage, "/register", System.StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(sanitizedMessage, "/registration", System.StringComparison.OrdinalIgnoreCase);
            if (!isRegisterCommand)
            {
                return;
            }

            string displayName = string.IsNullOrWhiteSpace(chatMessage.DisplayName) ? chatMessage.UserId : chatMessage.DisplayName;
            if (_registry.TryRegisterParticipant(chatMessage.UserId, displayName, chatMessage.AvatarPicture, chatMessage.AvatarUrl, out _))
            {
                _statusLabel.text = $"Registered participants: {_registry.Count}";
            }
        }
#endif

        private void HandleParticipantsCleared()
        {
            ParticipantSnapshotStore.Clear();
            BuildParticipantsGrid();
        }

        private void HandleParticipantRegistered(ParticipantInfo participant)
        {
            EnsureParticipantsGrid();

            if (_activeRegistrationColumn == null || _activeRegistrationColumnCount >= ParticipantsPerColumn)
            {
                _activeRegistrationColumn = new VisualElement();
                _activeRegistrationColumn.AddToClassList("participants-column");
                _participantsGrid.Insert(0, _activeRegistrationColumn);
                _activeRegistrationColumnCount = 0;
            }

            VisualElement row = new VisualElement();
            row.AddToClassList("participant-row");

            VisualElement avatar = new VisualElement();
            avatar.AddToClassList("participant-avatar");
            row.Add(avatar);

            Label nameLabel = new Label(participant.DisplayName);
            nameLabel.AddToClassList("participant-name");
            row.Add(nameLabel);

            _activeRegistrationColumn.Add(row);
            _activeRegistrationColumnCount++;
            row.schedule.Execute(() => row.AddToClassList("visible")).ExecuteLater(16);

            ParticipantSnapshotStore.SaveFromRegistry(_registry);
            Debug.Log($"RegistrationSceneController: Snapshot saved. Participants={_registry.Count}.");

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

        private void EnsureParticipantsGrid()
        {
            if (_participantsGrid != null)
            {
                return;
            }

            BuildParticipantsGrid();
        }

        private void BuildParticipantsGrid()
        {
            _participantsScroll.contentContainer.Clear();

            _participantsGrid = new VisualElement();
            _participantsGrid.AddToClassList("participants-grid");
            _participantsScroll.contentContainer.Add(_participantsGrid);

            _activeRegistrationColumn = null;
            _activeRegistrationColumnCount = 0;
        }

        private void SpawnChallengePod(VisualElement root)
        {
            if (root == null || _challengePodRoot != null)
            {
                return;
            }

            _challengePodRoot = new VisualElement();
            _challengePodRoot.name = "challenge-pod-root";
            _challengePodRoot.AddToClassList("challenge-pod-root");
            _challengePodRoot.pickingMode = PickingMode.Ignore;

            _challengePodCountLabel = new Label("0 / 0");
            _challengePodCountLabel.AddToClassList("challenge-pod-count");
            _challengePodCountLabel.pickingMode = PickingMode.Ignore;
            _challengePodRoot.Add(_challengePodCountLabel);

            _challengePodCircleShell = new VisualElement();
            _challengePodCircleShell.AddToClassList("challenge-pod-circle-shell");
            _challengePodCircleShell.pickingMode = PickingMode.Ignore;

            _challengePodCircleFill = new VisualElement();
            _challengePodCircleFill.AddToClassList("challenge-pod-circle-fill");
            _challengePodCircleFill.pickingMode = PickingMode.Ignore;
            _challengePodCircleShell.Add(_challengePodCircleFill);

            _challengePodPercentLabel = new Label("0%");
            _challengePodPercentLabel.AddToClassList("challenge-pod-percent");
            _challengePodPercentLabel.pickingMode = PickingMode.Ignore;
            _challengePodCircleShell.Add(_challengePodPercentLabel);
            _challengePodRoot.Add(_challengePodCircleShell);
            root.Add(_challengePodRoot);

            ApplyChallengeUiLayoutSettings();
        }

        private void ApplyChallengeUiLayoutSettings()
        {
            if (_challengePodRoot != null)
            {
                _challengePodRoot.style.top = challengeUiTopPx;
                _challengePodRoot.style.height = challengeUiHeightPx;
            }

            if (_challengePodCircleShell != null)
            {
                float radius = challengeCircleSizePx * 0.5f;
                _challengePodCircleShell.style.width = challengeCircleSizePx;
                _challengePodCircleShell.style.height = challengeCircleSizePx;
                _challengePodCircleShell.style.borderTopLeftRadius = radius;
                _challengePodCircleShell.style.borderTopRightRadius = radius;
                _challengePodCircleShell.style.borderBottomLeftRadius = radius;
                _challengePodCircleShell.style.borderBottomRightRadius = radius;
            }

            if (_challengePodCountLabel != null)
            {
                _challengePodCountLabel.style.marginBottom = challengeCounterSpacingPx;
            }
        }

        private void SetChallengePodVisible(bool isVisible)
        {
            if (_challengePodRoot == null)
            {
                return;
            }

            _challengePodRoot.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetChallengeFocusMode(bool isActive)
        {
            if (_participantsScroll != null)
            {
                _participantsScroll.style.display = isActive ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        private void UpdateChallengePodVisuals()
        {
            if (_challengePodCountLabel == null || _challengePodPercentLabel == null || _challengePodCircleFill == null || _challengePodCircleShell == null)
            {
                return;
            }

            int currentCoins = Mathf.Max(0, _beginnerChallengeAccumulatedCoins);
            int targetCoins = Mathf.Max(1, _beginnerChallengeCoinTarget);
            float progress = Mathf.Clamp01((float)currentCoins / targetCoins);

            _challengePodCountLabel.text = progress >= 1f
                ? "Congratulations! Community starts"
                : $"{currentCoins} / {targetCoins}";
            _challengePodPercentLabel.text = $"{Mathf.RoundToInt(progress * 100f)}%";

            _challengePodCircleFill.style.height = Length.Percent(progress * 100f);
            Color fillColor = Color.Lerp(new Color(0.22f, 0.08f, 0.08f, 0.94f), new Color(1f, 0.48f, 0.12f, 0.98f), progress);
            _challengePodCircleFill.style.backgroundColor = new StyleColor(fillColor);

            Color ringColor = Color.Lerp(new Color(0.78f, 0.28f, 0.2f, 1f), new Color(1f, 0.62f, 0.26f, 1f), progress);
            _challengePodCircleShell.style.borderTopColor = new StyleColor(ringColor);
            _challengePodCircleShell.style.borderBottomColor = new StyleColor(ringColor);
            _challengePodCircleShell.style.borderLeftColor = new StyleColor(ringColor);
            _challengePodCircleShell.style.borderRightColor = new StyleColor(ringColor);
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
                        Debug.LogWarning($"RegistrationSceneController: Avatar download failed ({request.error}) for '{candidate}'.");
                        continue;
                    }

                    byte[] imageBytes = request.downloadHandler.data;
                    if (imageBytes == null || imageBytes.Length == 0)
                    {
                        Debug.LogWarning($"RegistrationSceneController: Avatar download returned empty body for '{candidate}'.");
                        continue;
                    }

                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    bool loaded = texture.LoadImage(imageBytes, false);
                    if (!loaded)
                    {
                        Destroy(texture);
                        Debug.LogWarning($"RegistrationSceneController: Avatar texture decode failed for '{candidate}'.");
                        continue;
                    }

                    avatarElement.style.backgroundImage = new StyleBackground(texture);
                    yield break;
                }
            }

            Debug.LogWarning($"RegistrationSceneController: Avatar loading failed for all candidates derived from '{avatarUrl}'.");
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
    }
}
