using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FourWinsTikTok.AI;
using FourWinsTikTok.Bootstrap;
using FourWinsTikTok.Config;
using FourWinsTikTok.Core;
using FourWinsTikTok.TikTok;
using FourWinsTikTok.World;
using UnityEngine;

namespace FourWinsTikTok.Gameplay
{
    public class GameFlowController : MonoBehaviour
    {
        public static GameFlowController Instance { get; private set; }

        private static readonly Regex ColumnCommandRegex = new Regex(@"(?:^|\s|\(|\[|\{|#)([1-7])(?:$|\s|\)|\]|\}|[\.,!?:;])", RegexOptions.Compiled);

        [Header("Config")]
        [SerializeField] private ConnectFourGameConfig gameConfig;
        [SerializeField, Min(5f)] private float communityParticipantTurnSeconds = 60f;

        [Header("References")]
        [SerializeField] private TurnTimer turnTimer;
        [SerializeField] private TikTokLiveChatAdapter chatAdapter;

        [Header("Modes")]
        [SerializeField] private bool useLocalStreamerInsteadOfBot = true;

        [Header("Debug")]
        [SerializeField] private bool logVoteFlow = true;
        [SerializeField] private bool ensureWorldBoardView = true;

        public event Action<int, int> OnBoardInitialized;
        public event Action<BoardState> OnBoardChanged;
        public event Action<int, int, PlayerSide> OnDiscPlaced;
        public event Action<GameFlowState> OnStateChanged;
        public event Action<float> OnTimerChanged;
        public event Action<IReadOnlyList<Voting.VoteTally>> OnVoteUpdated;
        public event Action<string> OnStatusMessage;
        public event Action<PlayerSide> OnGameOver;
        public event Action<int, int, int, int> OnRoundScoreChanged;
        public event Action<PlayerSide, bool> OnRoundCompleted;
        public event Action<string> OnCommunityParticipantTurnChanged;

        private readonly System.Random _random = new System.Random();
        private IBotPlayer _botPlayer;
        private ParticipantRegistryService _participantRegistry;
        private Coroutine _botTurnRoutine;
        private TurnTimer _subscribedTurnTimer;
        private TikTokLiveChatAdapter _subscribedChatAdapter;
        private readonly List<TikTokLiveChatAdapter> _subscribedChatAdapters = new List<TikTokLiveChatAdapter>();
        private readonly List<ParticipantInfo> _communityTurnParticipants = new List<ParticipantInfo>();
        private int _activeCommunityParticipantIndex = -1;
        private string _activeCommunityParticipantUserId = string.Empty;
        private Coroutine _awaitParticipantsRoutine;
        private int _currentRound = 1;
        private int _communityWins;
        private int _opponentWins;
        private bool _matchOver;
        private int _matchWinsRequired = 5;
        private float _participantTurnSecondsRuntime = 60f;
        private float _nextRuntimeReconcileAt;
        private float _communityTurnDeadlineRealtime;
        private float _lastHandledCommunityTimeoutRealtime = -10f;

        public BoardState CurrentBoard { get; private set; }
        public GameFlowState CurrentState { get; private set; } = GameFlowState.Idle;
        public PlayerSide ActivePlayer { get; private set; } = PlayerSide.None;
        public int CurrentRound => _currentRound;
        public int CommunityWins => _communityWins;
        public int OpponentWins => _opponentWins;
        public bool IsMatchOver => _matchOver;
        public int MatchWinsRequired => _matchWinsRequired;
        public string ActiveCommunityParticipantUserId => _activeCommunityParticipantUserId;
        public float CommunityTurnRemainingSeconds
        {
            get
            {
                if (CurrentState != GameFlowState.WaitingForCommunityVote)
                {
                    return 0f;
                }

                if (_communityTurnDeadlineRealtime > 0f)
                {
                    return Mathf.Max(0f, _communityTurnDeadlineRealtime - Time.realtimeSinceStartup);
                }

                return EnsureTurnTimer() != null ? turnTimer.RemainingTime : 0f;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("GameFlowController: Duplicate instance detected. Destroying newer instance.");
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (turnTimer == null)
            {
                turnTimer = GetComponent<TurnTimer>();
                if (turnTimer == null)
                {
                    turnTimer = gameObject.AddComponent<TurnTimer>();
                    Debug.LogWarning("GameFlowController: TurnTimer reference missing. Added TurnTimer component at runtime.");
                }
            }

            EnsureWorldBoardViewExists();
        }

        private void EnsureWorldBoardViewExists()
        {
            if (!ensureWorldBoardView)
            {
                return;
            }

            ConnectFourWorldBoardView existingWorldView = FindFirstObjectByType<ConnectFourWorldBoardView>();
            if (existingWorldView != null)
            {
                existingWorldView.ConfigureRuntime(this);
                return;
            }

            ConnectFourWorldBoardView runtimeWorldView = gameObject.AddComponent<ConnectFourWorldBoardView>();
            runtimeWorldView.ConfigureRuntime(this);
            if (logVoteFlow)
            {
                Debug.Log("[TRACE][GameFlow] Auto-created ConnectFourWorldBoardView on GameRoot.");
            }
        }

        private void OnEnable()
        {
            ResolveChatAdapterReference();
            _participantRegistry = ParticipantRegistryService.EnsureInstance();
            EnsureTurnTimer();
            EnsureChatAdapterSubscription();

            if (_participantRegistry != null)
            {
                _participantRegistry.OnParticipantRegistered += HandleParticipantRegistered;
                _participantRegistry.OnCleared += HandleParticipantsCleared;
            }
        }

        private void Start()
        {
            if (CurrentBoard == null)
            {
                StartNewGame();
            }
        }

        private void Update()
        {
            if (!isActiveAndEnabled || Time.unscaledTime < _nextRuntimeReconcileAt)
            {
                return;
            }

            _nextRuntimeReconcileAt = Time.unscaledTime + 0.5f;
            ReconcileRuntimeBindings();
            TickCommunityTurnDeadline();
        }

        private void OnDisable()
        {
            DetachTurnTimerEvents();
            DetachChatAdapterEvents();

            if (_participantRegistry != null)
            {
                _participantRegistry.OnParticipantRegistered -= HandleParticipantRegistered;
                _participantRegistry.OnCleared -= HandleParticipantsCleared;
            }

            StopAwaitParticipantsRoutine();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void ResolveChatAdapterReference()
        {
            chatAdapter = ResolvePreferredChatAdapter();

            if (chatAdapter != null && _subscribedChatAdapter != chatAdapter)
            {
                Trace("ResolveChatAdapterReference: adapter resolved/replaced.");
            }
        }

        private static TikTokLiveChatAdapter ResolvePreferredChatAdapter()
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

        private void ReconcileRuntimeBindings()
        {
            EnsureTurnTimer();
            EnsureChatAdapterSubscription();

            if (CurrentState != GameFlowState.WaitingForCommunityVote)
            {
                return;
            }

            if (_communityTurnParticipants.Count == 0)
            {
                RefreshCommunityParticipants();
            }

            if (string.IsNullOrWhiteSpace(_activeCommunityParticipantUserId))
            {
                if (_communityTurnParticipants.Count > 0)
                {
                    AdvanceToNextCommunityParticipant();
                }

                return;
            }

            TurnTimer timer = EnsureTurnTimer();
            if (timer == null)
            {
                return;
            }

            float remaining = CommunityTurnRemainingSeconds;
            if (remaining <= 0.01f)
            {
                HandleCommunityTimerCompleted();
                return;
            }

            if (timer.IsRunning)
            {
                return;
            }

            timer.StartCountdown(remaining);
            OnTimerChanged?.Invoke(remaining);
            Debug.LogWarning($"GameFlowController: Recovered stopped timer for active participant '{_activeCommunityParticipantUserId}'.");
        }

        private void TickCommunityTurnDeadline()
        {
            if (CurrentState != GameFlowState.WaitingForCommunityVote || string.IsNullOrWhiteSpace(_activeCommunityParticipantUserId))
            {
                return;
            }

            if (_communityTurnDeadlineRealtime <= 0f)
            {
                return;
            }

            if (Time.realtimeSinceStartup < _communityTurnDeadlineRealtime)
            {
                return;
            }

            HandleCommunityTimerCompleted();
        }

        public void StartNewGame()
        {
            if (!ValidateDependencies())
            {
                return;
            }

            if (_botTurnRoutine != null)
            {
                StopCoroutine(_botTurnRoutine);
                _botTurnRoutine = null;
            }

            EnsureTurnTimer()?.CancelCountdown();

            _participantRegistry = ParticipantRegistryService.EnsureInstance();
            HydrateParticipantsFromSnapshotIfNeeded();
            Trace($"StartNewGame: registryCount={_participantRegistry?.Count ?? 0}");
            if (!useLocalStreamerInsteadOfBot)
            {
                _botPlayer = new RandomBotPlayer(_random);
            }

            _matchWinsRequired = Mathf.Clamp(
                PlayerPrefs.GetInt(BootstrapKeys.MatchWinsToWinPlayerPrefsKey, gameConfig.MatchWinsRequired),
                1,
                25);
            _participantTurnSecondsRuntime = Mathf.Clamp(
                PlayerPrefs.GetInt(BootstrapKeys.ParticipantTurnDurationSecondsPlayerPrefsKey, Mathf.RoundToInt(communityParticipantTurnSeconds)),
                5,
                300);

            _currentRound = 1;
            _communityWins = 0;
            _opponentWins = 0;
            _matchOver = false;
            _activeCommunityParticipantIndex = -1;
            _activeCommunityParticipantUserId = string.Empty;
            _communityTurnDeadlineRealtime = 0f;
            StopAwaitParticipantsRoutine();

            SetState(GameFlowState.Idle);
            ActivePlayer = PlayerSide.None;

            InitializeBoardForRound();
            PublishScoreState();

            BeginCommunityTurn();
        }

        public void StartNextRound()
        {
            if (gameConfig == null)
            {
                return;
            }

            if (_matchOver)
            {
                StartNewGame();
                return;
            }

            if (_botTurnRoutine != null)
            {
                StopCoroutine(_botTurnRoutine);
                _botTurnRoutine = null;
            }

            EnsureTurnTimer()?.CancelCountdown();
            _currentRound++;
            _communityTurnDeadlineRealtime = 0f;

            SetState(GameFlowState.Idle);
            ActivePlayer = PlayerSide.None;

            InitializeBoardForRound();
            PublishScoreState();

            BeginCommunityTurn();
        }

        private bool ValidateDependencies()
        {
            if (gameConfig == null)
            {
                Debug.LogError("GameFlowController: Missing game config.");
                return false;
            }

            if (EnsureTurnTimer() == null)
            {
                Debug.LogError("GameFlowController: TurnTimer could not be resolved.");
                return false;
            }

            ResolveChatAdapterReference();
            if (EnsureChatAdapterSubscription() == null)
            {
                Debug.LogWarning("GameFlowController: TikTokLiveChatAdapter not resolved. Chat moves will be unavailable.");
            }

            return true;
        }

        private void HydrateParticipantsFromSnapshotIfNeeded()
        {
            if (_participantRegistry == null || _participantRegistry.Count > 0)
            {
                return;
            }

            IReadOnlyList<ParticipantSnapshotEntry> snapshot = ParticipantSnapshotStore.Load();
            if (snapshot == null || snapshot.Count == 0)
            {
                return;
            }

            for (int index = 0; index < snapshot.Count; index++)
            {
                ParticipantSnapshotEntry entry = snapshot[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.userId))
                {
                    continue;
                }

                string displayName = string.IsNullOrWhiteSpace(entry.displayName)
                    ? entry.userId
                    : entry.displayName;

                _participantRegistry.TryRegisterParticipant(entry.userId, displayName, null, entry.avatarUrl, out _);
            }
        }

        private TurnTimer EnsureTurnTimer()
        {
            if (!this)
            {
                return null;
            }

            if (turnTimer == null)
            {
                turnTimer = GetComponent<TurnTimer>();
                if (turnTimer == null)
                {
                    turnTimer = gameObject.AddComponent<TurnTimer>();
                    Debug.LogWarning("GameFlowController: Missing TurnTimer reference. Added TurnTimer component at runtime.");
                }
            }

            if (_subscribedTurnTimer != turnTimer)
            {
                DetachTurnTimerEvents();
                if (turnTimer != null)
                {
                    turnTimer.OnTick += HandleTimerTick;
                    turnTimer.OnCompleted += HandleCommunityTimerCompleted;
                    _subscribedTurnTimer = turnTimer;
                }
            }

            return turnTimer;
        }

        private TikTokLiveChatAdapter EnsureChatAdapterSubscription()
        {
            if (!this)
            {
                return null;
            }

            ResolveChatAdapterReference();

            TikTokLiveChatAdapter[] adapters = FindObjectsByType<TikTokLiveChatAdapter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            for (int index = _subscribedChatAdapters.Count - 1; index >= 0; index--)
            {
                TikTokLiveChatAdapter existing = _subscribedChatAdapters[index];
                if (existing == null || System.Array.IndexOf(adapters, existing) < 0)
                {
                    existing.OnChatMessageReceived -= HandleChatMessage;
                    _subscribedChatAdapters.RemoveAt(index);
                }
            }

            for (int index = 0; index < adapters.Length; index++)
            {
                TikTokLiveChatAdapter adapter = adapters[index];
                if (adapter == null || _subscribedChatAdapters.Contains(adapter))
                {
                    continue;
                }

                adapter.OnChatMessageReceived += HandleChatMessage;
                _subscribedChatAdapters.Add(adapter);
            }

            if (_subscribedChatAdapter != chatAdapter)
            {
                _subscribedChatAdapter = chatAdapter;
            }

            if (logVoteFlow)
            {
                int connectedCount = 0;
                for (int index = 0; index < _subscribedChatAdapters.Count; index++)
                {
                    if (_subscribedChatAdapters[index] != null && _subscribedChatAdapters[index].IsConnected)
                    {
                        connectedCount++;
                    }
                }

                Trace($"ChatAdapterSubscribed: total={_subscribedChatAdapters.Count}, connected={connectedCount}");
            }

            return chatAdapter;
        }

        private void DetachTurnTimerEvents()
        {
            if (_subscribedTurnTimer == null)
            {
                return;
            }

            _subscribedTurnTimer.OnTick -= HandleTimerTick;
            _subscribedTurnTimer.OnCompleted -= HandleCommunityTimerCompleted;
            _subscribedTurnTimer = null;
        }

        private void DetachChatAdapterEvents()
        {
            for (int index = _subscribedChatAdapters.Count - 1; index >= 0; index--)
            {
                TikTokLiveChatAdapter adapter = _subscribedChatAdapters[index];
                if (adapter != null)
                {
                    adapter.OnChatMessageReceived -= HandleChatMessage;
                }
            }

            _subscribedChatAdapters.Clear();
            Trace("ChatAdapterUnsubscribed");
            _subscribedChatAdapter = null;
        }

        private void InitializeBoardForRound()
        {
            CurrentBoard = new BoardState(gameConfig.Columns, gameConfig.Rows, gameConfig.ConnectLength);
            OnBoardInitialized?.Invoke(CurrentBoard.Columns, CurrentBoard.Rows);
            OnBoardChanged?.Invoke(CurrentBoard);
            OnVoteUpdated?.Invoke(Array.Empty<Voting.VoteTally>());
            OnTimerChanged?.Invoke(0f);
        }

        private void PublishScoreState()
        {
            OnRoundScoreChanged?.Invoke(_currentRound, _communityWins, _opponentWins, _matchWinsRequired);
        }

        private void BeginCommunityTurn()
        {
            if (CurrentBoard == null)
            {
                Debug.LogWarning("GameFlowController: BeginCommunityTurn called with null board.");
                return;
            }

            List<int> validColumns = CurrentBoard.GetValidColumns();
            if (validColumns.Count == 0)
            {
                EndAsDraw();
                return;
            }

            RefreshCommunityParticipants();
            if (_communityTurnParticipants.Count == 0)
            {
                HydrateParticipantsFromSnapshotIfNeeded();
                RefreshCommunityParticipants();
            }

            Trace($"BeginCommunityTurn: participants={_communityTurnParticipants.Count}");
            if (_communityTurnParticipants.Count > 0)
            {
                Trace($"BeginCommunityTurnParticipants: {BuildParticipantSummary(_communityTurnParticipants)}");
            }

            if (_communityTurnParticipants.Count == 0)
            {
                SetState(GameFlowState.WaitingForCommunityVote);
                ActivePlayer = PlayerSide.Community;
                _activeCommunityParticipantUserId = string.Empty;
                _communityTurnDeadlineRealtime = 0f;
                OnCommunityParticipantTurnChanged?.Invoke(string.Empty);
                OnTimerChanged?.Invoke(0f);
                OnStatusMessage?.Invoke("No participants registered yet. Waiting for participants...");
                StartAwaitParticipantsRoutine();
                Trace("BeginCommunityTurn: waiting for participants, timer stays at 0");
                return;
            }

            StopAwaitParticipantsRoutine();
            ActivePlayer = PlayerSide.Community;
            OnVoteUpdated?.Invoke(Array.Empty<Voting.VoteTally>());

            SetState(GameFlowState.WaitingForCommunityVote);
            Debug.Log($"GameFlowController: BeginCommunityTurn with {_communityTurnParticipants.Count} participant(s).");
            AdvanceToNextCommunityParticipant();
        }

        private void RefreshCommunityParticipants()
        {
            _communityTurnParticipants.Clear();
            if (_participantRegistry == null)
            {
                return;
            }

            foreach (ParticipantInfo participant in _participantRegistry.Participants)
            {
                _communityTurnParticipants.Add(participant);
            }
        }

        private void HandleParticipantRegistered(ParticipantInfo _)
        {
            if (CurrentState != GameFlowState.WaitingForCommunityVote)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_activeCommunityParticipantUserId))
            {
                return;
            }

            RefreshCommunityParticipants();
            if (_communityTurnParticipants.Count == 0)
            {
                return;
            }

            _activeCommunityParticipantIndex = -1;
            StopAwaitParticipantsRoutine();
            AdvanceToNextCommunityParticipant();
        }

        private void HandleParticipantsCleared()
        {
            if (CurrentState != GameFlowState.WaitingForCommunityVote)
            {
                return;
            }

            _communityTurnParticipants.Clear();
            _activeCommunityParticipantIndex = -1;
            _activeCommunityParticipantUserId = string.Empty;
            _communityTurnDeadlineRealtime = 0f;
            OnCommunityParticipantTurnChanged?.Invoke(string.Empty);
            OnTimerChanged?.Invoke(0f);
            StartAwaitParticipantsRoutine();
        }

        private void StartAwaitParticipantsRoutine()
        {
            if (_awaitParticipantsRoutine != null)
            {
                return;
            }

            _awaitParticipantsRoutine = StartCoroutine(AwaitParticipantsRoutine());
        }

        private void StopAwaitParticipantsRoutine()
        {
            if (_awaitParticipantsRoutine == null)
            {
                return;
            }

            StopCoroutine(_awaitParticipantsRoutine);
            _awaitParticipantsRoutine = null;
        }

        private IEnumerator AwaitParticipantsRoutine()
        {
            WaitForSecondsRealtime wait = new WaitForSecondsRealtime(0.5f);

            while (CurrentState == GameFlowState.WaitingForCommunityVote && string.IsNullOrWhiteSpace(_activeCommunityParticipantUserId))
            {
                RefreshCommunityParticipants();
                if (_communityTurnParticipants.Count > 0)
                {
                    _activeCommunityParticipantIndex = -1;
                    AdvanceToNextCommunityParticipant();
                    _awaitParticipantsRoutine = null;
                    yield break;
                }

                yield return wait;
            }

            _awaitParticipantsRoutine = null;
        }

        private void HandleTimerTick(float remainingSeconds)
        {
            if (CurrentState == GameFlowState.WaitingForCommunityVote)
            {
                OnTimerChanged?.Invoke(remainingSeconds);
            }
        }

        private void HandleChatMessage(ChatMessage chatMessage)
        {
            if (!this || !isActiveAndEnabled)
            {
                return;
            }

            if (CurrentBoard == null)
            {
                return;
            }

            ResolveChatAdapterReference();

            string sanitizedMessage = chatMessage.Message?.Trim();

#if UNITY_EDITOR
            if (TryHandleEditorAdminCommand(chatMessage, sanitizedMessage))
            {
                return;
            }
#endif

            if (logVoteFlow)
            {
                Debug.Log($"GameFlowController: Chat received user='{chatMessage.UserId}', message='{sanitizedMessage}', state='{CurrentState}', active='{_activeCommunityParticipantUserId}', participants={_communityTurnParticipants.Count}, remaining={CommunityTurnRemainingSeconds:0.0}s.");
            }

            if (CurrentState != GameFlowState.WaitingForCommunityVote)
            {
                if (logVoteFlow)
                {
                    Debug.Log("GameFlowController: Message ignored because community participant turn phase is not active.");
                }

                return;
            }

            if (_communityTurnParticipants.Count == 0)
            {
                RefreshCommunityParticipants();
                if (_communityTurnParticipants.Count == 0 && TryParseColumnFromChat(sanitizedMessage, out _))
                {
                    Trace($"AutoRegisterFromChat: user='{chatMessage.UserId}'");
                    EnsureParticipantExistsForChatMessage(chatMessage);
                }
            }

            if (string.IsNullOrWhiteSpace(_activeCommunityParticipantUserId) && _communityTurnParticipants.Count > 0)
            {
                int participantIndex = _communityTurnParticipants.FindIndex(participant =>
                    IsMatchingParticipant(participant, chatMessage));

                if (participantIndex < 0 && TryParseColumnFromChat(sanitizedMessage, out _))
                {
                    EnsureParticipantExistsForChatMessage(chatMessage);
                    RefreshCommunityParticipants();

                    participantIndex = _communityTurnParticipants.FindIndex(participant =>
                        IsMatchingParticipant(participant, chatMessage));
                }

                if (participantIndex >= 0)
                {
                    ActivateCommunityParticipant(participantIndex, _participantTurnSecondsRuntime);
                }
            }

            if (!IsSameUserId(chatMessage.UserId, _activeCommunityParticipantUserId))
            {
                if (logVoteFlow)
                {
                    Debug.Log($"GameFlowController: Message ignored because user '{chatMessage.UserId}' is not the active participant.");
                }

                return;
            }

            if (!TryParseColumnFromChat(sanitizedMessage, out int selectedColumn))
            {
                if (logVoteFlow)
                {
                    Debug.Log($"GameFlowController: Message '{sanitizedMessage}' is not a valid column number.");
                }

                return;
            }

            if (CurrentBoard.IsColumnFull(selectedColumn))
            {
                if (logVoteFlow)
                {
                    Debug.Log($"GameFlowController: Column '{selectedColumn + 1}' is full.");
                }

                OnStatusMessage?.Invoke($"Column {selectedColumn + 1} is full. {_activeCommunityParticipantUserId}, pick another (1-7).");

                return;
            }

            EnsureTurnTimer()?.CancelCountdown();
            _communityTurnDeadlineRealtime = 0f;
            if (!TryExecuteMove(selectedColumn, PlayerSide.Community))
            {
                EndAsDraw();
                return;
            }

            Trace($"CommunityMoveAccepted: user='{chatMessage.UserId}', column={selectedColumn + 1}");

            _activeCommunityParticipantUserId = string.Empty;
            OnCommunityParticipantTurnChanged?.Invoke(string.Empty);

            if (CurrentState != GameFlowState.GameOver)
            {
                if (useLocalStreamerInsteadOfBot)
                {
                    BeginStreamerTurn();
                }
                else
                {
                    BeginBotTurn();
                }
            }
        }

        private void EnsureParticipantExistsForChatMessage(ChatMessage chatMessage)
        {
            _participantRegistry ??= ParticipantRegistryService.EnsureInstance();
            if (_participantRegistry == null)
            {
                return;
            }

            string userId = chatMessage.UserId?.Trim();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            string displayName = string.IsNullOrWhiteSpace(chatMessage.DisplayName)
                ? userId
                : chatMessage.DisplayName;

            _participantRegistry.TryRegisterParticipant(userId, displayName, chatMessage.AvatarPicture, chatMessage.AvatarUrl, out _);
            RefreshCommunityParticipants();
        }

        private void ActivateCommunityParticipant(int participantIndex, float turnDurationSeconds)
        {
            if (participantIndex < 0 || participantIndex >= _communityTurnParticipants.Count)
            {
                return;
            }

            _activeCommunityParticipantIndex = participantIndex;
            _activeCommunityParticipantUserId = _communityTurnParticipants[participantIndex].UserId;
            OnCommunityParticipantTurnChanged?.Invoke(_activeCommunityParticipantUserId);
            StartCommunityParticipantCountdown(turnDurationSeconds);
        }

        private static bool IsMatchingParticipant(ParticipantInfo participant, ChatMessage chatMessage)
        {
            if (participant == null)
            {
                return false;
            }

            if (IsSameUserId(participant.UserId, chatMessage.UserId))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(chatMessage.DisplayName)
                && !string.IsNullOrWhiteSpace(participant.DisplayName)
                && string.Equals(participant.DisplayName.Trim(), chatMessage.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

#if UNITY_EDITOR
        private bool TryHandleEditorAdminCommand(ChatMessage chatMessage, string sanitizedMessage)
        {
            if (!IsSameUserId(chatMessage.UserId, "ichriwo"))
            {
                return false;
            }

            bool isRegisterCommand = string.Equals(sanitizedMessage, "/register", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(sanitizedMessage, "/registration", StringComparison.OrdinalIgnoreCase);
            if (isRegisterCommand)
            {
                _participantRegistry ??= ParticipantRegistryService.EnsureInstance();
                string displayName = string.IsNullOrWhiteSpace(chatMessage.DisplayName) ? chatMessage.UserId : chatMessage.DisplayName;
                if (_participantRegistry.TryRegisterParticipant(chatMessage.UserId, displayName, chatMessage.AvatarPicture, chatMessage.AvatarUrl, out ParticipantInfo participant))
                {
                    if (CurrentState == GameFlowState.WaitingForCommunityVote)
                    {
                        _communityTurnParticipants.Add(participant);
                    }

                    OnStatusMessage?.Invoke($"Admin register: {_participantRegistry.Count} participants.");
                }

                return true;
            }

            if (string.Equals(sanitizedMessage, "/takeover", StringComparison.OrdinalIgnoreCase))
            {
                if (CurrentState == GameFlowState.WaitingForCommunityVote)
                {
                    _participantRegistry ??= ParticipantRegistryService.EnsureInstance();

                    ParticipantInfo takeoverParticipant = _communityTurnParticipants.Find(participant =>
                        IsSameUserId(participant.UserId, chatMessage.UserId));

                    if (takeoverParticipant == null)
                    {
                        string displayName = string.IsNullOrWhiteSpace(chatMessage.DisplayName) ? chatMessage.UserId : chatMessage.DisplayName;
                        if (_participantRegistry.TryRegisterParticipant(chatMessage.UserId, displayName, chatMessage.AvatarPicture, chatMessage.AvatarUrl, out ParticipantInfo registeredParticipant))
                        {
                            takeoverParticipant = registeredParticipant;
                        }
                        else
                        {
                            foreach (ParticipantInfo participant in _participantRegistry.Participants)
                            {
                                if (string.Equals(participant.UserId, chatMessage.UserId, StringComparison.OrdinalIgnoreCase))
                                {
                                    takeoverParticipant = participant;
                                    break;
                                }
                            }
                        }

                        if (takeoverParticipant != null)
                        {
                            _communityTurnParticipants.Add(takeoverParticipant);
                        }
                    }

                    int takeoverIndex = _communityTurnParticipants.FindIndex(participant =>
                        IsSameUserId(participant.UserId, chatMessage.UserId));

                    if (takeoverIndex >= 0)
                    {
                        EnsureTurnTimer()?.CancelCountdown();
                        ActivateCommunityParticipant(takeoverIndex, _participantTurnSecondsRuntime);
                        OnStatusMessage?.Invoke("Admin takeover: ichriwo is up now.");
                    }
                }

                return true;
            }

            return false;
        }
#endif

        private void HandleCommunityTimerCompleted()
        {
            if (CurrentState != GameFlowState.WaitingForCommunityVote || CurrentBoard == null)
            {
                return;
            }

            if (Time.realtimeSinceStartup - _lastHandledCommunityTimeoutRealtime < 0.05f)
            {
                return;
            }

            _lastHandledCommunityTimeoutRealtime = Time.realtimeSinceStartup;
            _communityTurnDeadlineRealtime = 0f;

            if (_communityTurnParticipants.Count == 0)
            {
                RefreshCommunityParticipants();
                if (_communityTurnParticipants.Count == 0)
                {
                    OnTimerChanged?.Invoke(0f);
                    return;
                }
            }

            AdvanceToNextCommunityParticipant();
        }

        public bool TrySubmitStreamerMoveFromDisplayColumn(int displayColumn)
        {
            return TrySubmitStreamerMoveFromColumnIndex(displayColumn - 1);
        }

        public bool TrySubmitStreamerMoveFromColumnIndex(int columnIndex)
        {
            if (CurrentState != GameFlowState.WaitingForStreamerMove)
            {
                if (logVoteFlow)
                {
                    Debug.Log($"GameFlowController: Streamer move ignored because state is '{CurrentState}'.");
                }

                return false;
            }

            if (columnIndex < 0 || CurrentBoard == null || columnIndex >= CurrentBoard.Columns)
            {
                if (logVoteFlow)
                {
                    Debug.Log($"GameFlowController: Streamer move rejected because column '{columnIndex}' is out of range.");
                }

                return false;
            }

            if (CurrentBoard.IsColumnFull(columnIndex))
            {
                if (logVoteFlow)
                {
                    Debug.Log($"GameFlowController: Streamer move rejected because column '{columnIndex + 1}' is full.");
                }

                return false;
            }

            if (!TryExecuteMove(columnIndex, PlayerSide.Streamer))
            {
                return false;
            }

            if (CurrentState != GameFlowState.GameOver)
            {
                BeginCommunityTurn();
            }

            return true;
        }

        private static bool TryParseColumnFromChat(string message, out int columnIndex)
        {
            columnIndex = -1;
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string sanitized = NormalizeChatMessageForColumnParsing(message);
            if (sanitized.Length == 1 && sanitized[0] >= '1' && sanitized[0] <= '7')
            {
                columnIndex = sanitized[0] - '1';
                return true;
            }

            Match match = ColumnCommandRegex.Match(sanitized);
            if (!match.Success)
            {
                return false;
            }

            if (!int.TryParse(match.Groups[1].Value, out int displayColumn))
            {
                return false;
            }

            columnIndex = displayColumn - 1;
            return columnIndex >= 0 && columnIndex <= 6;
        }

        private static string NormalizeChatMessageForColumnParsing(string message)
        {
            string trimmed = message.Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            char[] normalizedChars = trimmed.ToCharArray();
            for (int index = 0; index < normalizedChars.Length; index++)
            {
                char character = normalizedChars[index];
                if (character >= '１' && character <= '７')
                {
                    normalizedChars[index] = (char)('1' + (character - '１'));
                }
                else if (character >= '0' && character <= '9')
                {
                    normalizedChars[index] = character;
                }
            }

            return new string(normalizedChars)
                .Replace("1️⃣", "1")
                .Replace("2️⃣", "2")
                .Replace("3️⃣", "3")
                .Replace("4️⃣", "4")
                .Replace("5️⃣", "5")
                .Replace("6️⃣", "6")
                .Replace("7️⃣", "7");
        }

        private void StartCommunityParticipantCountdown(float durationSeconds)
        {
            float clampedDuration = Mathf.Max(0f, durationSeconds);
            _communityTurnDeadlineRealtime = clampedDuration > 0f
                ? Time.realtimeSinceStartup + clampedDuration
                : 0f;

            EnsureTurnTimer()?.StartCountdown(clampedDuration);
            OnTimerChanged?.Invoke(clampedDuration);
            Trace($"ParticipantTurnStart: user='{_activeCommunityParticipantUserId}', duration={clampedDuration:0.0}s");
        }

        private static string BuildParticipantSummary(IReadOnlyList<ParticipantInfo> participants)
        {
            if (participants == null || participants.Count == 0)
            {
                return "none";
            }

            int limit = Mathf.Min(8, participants.Count);
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int index = 0; index < limit; index++)
            {
                ParticipantInfo participant = participants[index];
                if (participant == null)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(string.IsNullOrWhiteSpace(participant.DisplayName) ? participant.UserId : participant.DisplayName);
                builder.Append("(");
                builder.Append(participant.UserId);
                builder.Append(")");
            }

            if (participants.Count > limit)
            {
                builder.Append($", ... +{participants.Count - limit}");
            }

            return builder.ToString();
        }

        private void Trace(string message)
        {
            if (!logVoteFlow)
            {
                return;
            }

            Debug.Log($"[TRACE][GameFlow] {message}");
        }

        private static string NormalizeUserIdForComparison(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return string.Empty;
            }

            string normalized = userId.Trim();
            if (normalized.StartsWith("@", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1);
            }

            return normalized;
        }

        private static bool IsSameUserId(string left, string right)
        {
            return string.Equals(
                NormalizeUserIdForComparison(left),
                NormalizeUserIdForComparison(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private void AdvanceToNextCommunityParticipant()
        {
            if (_communityTurnParticipants.Count == 0)
            {
                EndAsDraw();
                return;
            }

            _activeCommunityParticipantIndex++;
            if (_activeCommunityParticipantIndex >= _communityTurnParticipants.Count)
            {
                _activeCommunityParticipantIndex = 0;
            }

            ParticipantInfo activeParticipant = _communityTurnParticipants[_activeCommunityParticipantIndex];
            _activeCommunityParticipantUserId = activeParticipant.UserId;

            string displayName = string.IsNullOrWhiteSpace(activeParticipant.DisplayName)
                ? activeParticipant.UserId
                : activeParticipant.DisplayName;

            OnCommunityParticipantTurnChanged?.Invoke(_activeCommunityParticipantUserId);
            OnStatusMessage?.Invoke($"{displayName} is up! Send 1-7 in {Mathf.RoundToInt(_participantTurnSecondsRuntime)}s.");
            StartCommunityParticipantCountdown(_participantTurnSecondsRuntime);
            Debug.Log($"GameFlowController: Active participant='{_activeCommunityParticipantUserId}', timer={_participantTurnSecondsRuntime}s.");
        }

        private void BeginBotTurn()
        {
            SetState(GameFlowState.BotTurn);
            ActivePlayer = PlayerSide.Bot;
            OnStatusMessage?.Invoke("Bot turn...");

            if (_botTurnRoutine != null)
            {
                StopCoroutine(_botTurnRoutine);
            }

            _botTurnRoutine = StartCoroutine(BotTurnRoutine());
        }

        private IEnumerator BotTurnRoutine()
        {
            if (gameConfig.BotThinkSeconds > 0f)
            {
                yield return new WaitForSeconds(gameConfig.BotThinkSeconds);
            }

            List<int> validColumns = CurrentBoard.GetValidColumns();
            int selectedColumn = _botPlayer.SelectColumn(validColumns);

            if (!TryExecuteMove(selectedColumn, PlayerSide.Bot))
            {
                EndAsDraw();
                yield break;
            }

            if (CurrentState != GameFlowState.GameOver)
            {
                BeginCommunityTurn();
            }

            _botTurnRoutine = null;
        }

        private void BeginStreamerTurn()
        {
            List<int> validColumns = CurrentBoard.GetValidColumns();
            if (validColumns.Count == 0)
            {
                EndAsDraw();
                return;
            }

            SetState(GameFlowState.WaitingForStreamerMove);
            ActivePlayer = PlayerSide.Streamer;
            _activeCommunityParticipantUserId = string.Empty;
            _communityTurnDeadlineRealtime = 0f;
            OnCommunityParticipantTurnChanged?.Invoke(string.Empty);
            OnStatusMessage?.Invoke("Streamer turn: click a column or press 1-7.");
            OnTimerChanged?.Invoke(0f);
        }

        private bool TryExecuteMove(int column, PlayerSide side)
        {
            if (CurrentBoard == null || side == PlayerSide.None || column < 0)
            {
                return false;
            }

            if (!CurrentBoard.TryPlaceDisc(column, side, out int row))
            {
                return false;
            }

            OnDiscPlaced?.Invoke(column, row, side);
            OnBoardChanged?.Invoke(CurrentBoard);

            if (CurrentBoard.CheckWinFrom(column, row, side))
            {
                if (side == PlayerSide.Community)
                {
                    _communityWins++;
                }
                else if (side == PlayerSide.Streamer || side == PlayerSide.Bot)
                {
                    _opponentWins++;
                }

                _matchOver = _communityWins >= _matchWinsRequired || _opponentWins >= _matchWinsRequired;

                SetState(GameFlowState.GameOver);
                ActivePlayer = PlayerSide.None;
                OnStatusMessage?.Invoke(side == PlayerSide.Community
                    ? "Community wins!"
                    : side == PlayerSide.Streamer ? "Streamer wins!" : "Bot wins!");
                OnGameOver?.Invoke(side);
                PublishScoreState();
                OnRoundCompleted?.Invoke(side, _matchOver);
                return true;
            }

            if (CurrentBoard.IsDraw())
            {
                EndAsDraw();
                return true;
            }

            return true;
        }

        private void EndAsDraw()
        {
            SetState(GameFlowState.GameOver);
            ActivePlayer = PlayerSide.None;
            EnsureTurnTimer()?.CancelCountdown();
            _communityTurnDeadlineRealtime = 0f;
            OnStatusMessage?.Invoke("Draw!");
            OnGameOver?.Invoke(PlayerSide.None);
            PublishScoreState();
            OnRoundCompleted?.Invoke(PlayerSide.None, false);
        }

        private void SetState(GameFlowState state)
        {
            if (CurrentState != state)
            {
                Trace($"StateChange: {CurrentState} -> {state}");
            }

            CurrentState = state;
            OnStateChanged?.Invoke(state);
        }
    }
}
