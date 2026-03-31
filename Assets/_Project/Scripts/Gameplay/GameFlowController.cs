using System;
using System.Collections;
using System.Collections.Generic;
using FourWinsTikTok.AI;
using FourWinsTikTok.Bootstrap;
using FourWinsTikTok.Config;
using FourWinsTikTok.Core;
using FourWinsTikTok.TikTok;
using FourWinsTikTok.Voting;
using UnityEngine;

namespace FourWinsTikTok.Gameplay
{
    public class GameFlowController : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private ConnectFourGameConfig gameConfig;
        [SerializeField] private VoteRulesConfig voteRulesConfig;

        [Header("References")]
        [SerializeField] private TurnTimer turnTimer;
        [SerializeField] private TikTokLiveChatAdapter chatAdapter;

        [Header("Modes")]
        [SerializeField] private bool useLocalStreamerInsteadOfBot = true;

        [Header("Debug")]
        [SerializeField] private bool logVoteFlow = true;

        public event Action<int, int> OnBoardInitialized;
        public event Action<BoardState> OnBoardChanged;
        public event Action<int, int, PlayerSide> OnDiscPlaced;
        public event Action<GameFlowState> OnStateChanged;
        public event Action<float> OnTimerChanged;
        public event Action<IReadOnlyList<VoteTally>> OnVoteUpdated;
        public event Action<string> OnStatusMessage;
        public event Action<PlayerSide> OnGameOver;
        public event Action<int, int, int, int> OnRoundScoreChanged;
        public event Action<PlayerSide, bool> OnRoundCompleted;

        private readonly System.Random _random = new System.Random();
        private IBotPlayer _botPlayer;
        private VoteSystem _voteSystem;
        private ParticipantRegistryService _participantRegistry;
        private Coroutine _botTurnRoutine;
        private int _currentRound = 1;
        private int _communityWins;
        private int _opponentWins;
        private bool _matchOver;
        private int _matchWinsRequired = 5;

        public BoardState CurrentBoard { get; private set; }
        public GameFlowState CurrentState { get; private set; } = GameFlowState.Idle;
        public PlayerSide ActivePlayer { get; private set; } = PlayerSide.None;
        public int CurrentRound => _currentRound;
        public int CommunityWins => _communityWins;
        public int OpponentWins => _opponentWins;
        public bool IsMatchOver => _matchOver;
        public int MatchWinsRequired => _matchWinsRequired;

        private void Awake()
        {
            if (turnTimer == null)
            {
                turnTimer = GetComponent<TurnTimer>();
            }
        }

        private void OnEnable()
        {
            if (turnTimer != null)
            {
                turnTimer.OnTick += HandleTimerTick;
                turnTimer.OnCompleted += HandleCommunityTimerCompleted;
            }

            if (chatAdapter != null)
            {
                chatAdapter.OnChatMessageReceived += HandleChatMessage;
            }
        }

        private void Start()
        {
            if (gameConfig != null && gameConfig.AutoStartOnPlay)
            {
                StartNewGame();
            }
        }

        private void OnDisable()
        {
            if (turnTimer != null)
            {
                turnTimer.OnTick -= HandleTimerTick;
                turnTimer.OnCompleted -= HandleCommunityTimerCompleted;
            }

            if (chatAdapter != null)
            {
                chatAdapter.OnChatMessageReceived -= HandleChatMessage;
            }
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

            turnTimer.CancelCountdown();

            _voteSystem = new VoteSystem(voteRulesConfig, gameConfig.Columns);
            _participantRegistry = ParticipantRegistryService.EnsureInstance();
            if (!useLocalStreamerInsteadOfBot)
            {
                _botPlayer = new RandomBotPlayer(_random);
            }

            _matchWinsRequired = Mathf.Clamp(
                PlayerPrefs.GetInt(BootstrapKeys.MatchWinsToWinPlayerPrefsKey, gameConfig.MatchWinsRequired),
                1,
                25);

            _currentRound = 1;
            _communityWins = 0;
            _opponentWins = 0;
            _matchOver = false;

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

            turnTimer.CancelCountdown();
            _currentRound++;

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

            if (voteRulesConfig == null)
            {
                Debug.LogError("GameFlowController: Missing vote rules config.");
                return false;
            }

            if (turnTimer == null)
            {
                Debug.LogError("GameFlowController: Missing TurnTimer reference.");
                return false;
            }

            return true;
        }

        private void InitializeBoardForRound()
        {
            CurrentBoard = new BoardState(gameConfig.Columns, gameConfig.Rows, gameConfig.ConnectLength);
            OnBoardInitialized?.Invoke(CurrentBoard.Columns, CurrentBoard.Rows);
            OnBoardChanged?.Invoke(CurrentBoard);
            OnVoteUpdated?.Invoke(Array.Empty<VoteTally>());
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
                return;
            }

            List<int> validColumns = CurrentBoard.GetValidColumns();
            if (validColumns.Count == 0)
            {
                EndAsDraw();
                return;
            }

            ActivePlayer = PlayerSide.Community;
            _voteSystem.BeginRound();
            OnVoteUpdated?.Invoke(_voteSystem.GetRanking());

            SetState(GameFlowState.WaitingForCommunityVote);
            OnStatusMessage?.Invoke("Community turn: vote with 1-7 in chat.");

            turnTimer.StartCountdown(gameConfig.CommunityVoteSeconds);
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
            string sanitizedMessage = chatMessage.Message?.Trim();

            if (logVoteFlow)
            {
                Debug.Log($"GameFlowController: Chat received user='{chatMessage.UserId}', message='{sanitizedMessage}', state='{CurrentState}'.");
            }

            if (CurrentState != GameFlowState.WaitingForCommunityVote || _voteSystem == null)
            {
                if (logVoteFlow)
                {
                    Debug.Log("GameFlowController: Vote ignored because community vote phase is not active.");
                }

                return;
            }

            if (_participantRegistry != null && !_participantRegistry.IsRegistered(chatMessage.UserId))
            {
                if (logVoteFlow)
                {
                    Debug.Log($"GameFlowController: Vote ignored because user '{chatMessage.UserId}' is not registered.");
                }

                return;
            }

            bool looksNumericVote = sanitizedMessage is "1" or "2" or "3" or "4" or "5" or "6" or "7";
            if (logVoteFlow && looksNumericVote)
            {
                Debug.Log($"GameFlowController: Numeric vote detected -> '{sanitizedMessage}' from '{chatMessage.UserId}'.");
            }

            if (!_voteSystem.TryRegisterVote(chatMessage.UserId, sanitizedMessage))
            {
                if (logVoteFlow)
                {
                    Debug.Log($"GameFlowController: Vote rejected for user='{chatMessage.UserId}', message='{sanitizedMessage}'.");
                }

                return;
            }

            if (logVoteFlow)
            {
                Debug.Log($"GameFlowController: Vote accepted for user='{chatMessage.UserId}', message='{sanitizedMessage}'.");
            }

            OnVoteUpdated?.Invoke(_voteSystem.GetRanking());
        }

        private void HandleCommunityTimerCompleted()
        {
            if (CurrentState != GameFlowState.WaitingForCommunityVote || CurrentBoard == null || _voteSystem == null)
            {
                return;
            }

            ResolveCommunityMove();
        }

        private void ResolveCommunityMove()
        {
            SetState(GameFlowState.ResolvingCommunityMove);
            _voteSystem.EndRound();

            int selectedColumn = SelectCommunityColumn();
            if (!TryExecuteMove(selectedColumn, PlayerSide.Community))
            {
                EndAsDraw();
                return;
            }

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

        private int SelectCommunityColumn()
        {
            List<int> validColumns = CurrentBoard.GetValidColumns();
            if (validColumns.Count == 0)
            {
                return -1;
            }

            HashSet<int> validSet = new HashSet<int>(validColumns);
            IReadOnlyList<VoteTally> ranking = _voteSystem.GetRanking();

            for (int index = 0; index < ranking.Count; index++)
            {
                int rankedColumn = ranking[index].ColumnIndex;
                if (validSet.Contains(rankedColumn))
                {
                    return rankedColumn;
                }
            }

            int randomValidIndex = _random.Next(0, validColumns.Count);
            return validColumns[randomValidIndex];
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
            turnTimer.CancelCountdown();
            OnStatusMessage?.Invoke("Draw!");
            OnGameOver?.Invoke(PlayerSide.None);
            PublishScoreState();
            OnRoundCompleted?.Invoke(PlayerSide.None, false);
        }

        private void SetState(GameFlowState state)
        {
            CurrentState = state;
            OnStateChanged?.Invoke(state);
        }
    }
}
