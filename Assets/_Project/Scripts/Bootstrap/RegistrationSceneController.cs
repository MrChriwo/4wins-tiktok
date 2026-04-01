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

        private Label _giftLabel;
        private Label _timerLabel;
        private Label _statusLabel;
        private Button _startGameButton;
        private ScrollView _participantsScroll;

        private string _requiredGiftName;
        private float _remainingSeconds;
        private ParticipantRegistryService _registry;
        private AsyncOperation _preloadGameOperation;
        private bool _countdownFinished;
        private bool _isStartingGame;
        private bool _registrationClosed;
        private readonly List<TikTokLiveChatAdapter> _subscribedAdapters = new List<TikTokLiveChatAdapter>();
        private float _nextAdapterResolveAt;
        private VisualElement _participantsGrid;
        private VisualElement _activeRegistrationColumn;
        private int _activeRegistrationColumnCount;

        private const int ParticipantsPerColumn = 4;

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

            _giftLabel.text = $"Send gift to register: {_requiredGiftName}";
            _statusLabel.text = "Waiting for participants...";
            _startGameButton.style.display = DisplayStyle.None;
            _startGameButton.clicked += HandleStartGameClicked;
            _countdownFinished = false;
            _isStartingGame = false;
            _registrationClosed = false;
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
#if UNITY_EDITOR
            adapter.OnChatMessageReceived -= HandleChatMessage;
#endif
        }

        private IEnumerator RegistrationCountdownRoutine()
        {
            _preloadGameOperation = SceneManager.LoadSceneAsync(gameplaySceneName, LoadSceneMode.Single);
            if (_preloadGameOperation != null)
            {
                _preloadGameOperation.allowSceneActivation = false;
            }

            while (_remainingSeconds > 0f)
            {
                _timerLabel.text = Mathf.CeilToInt(_remainingSeconds).ToString();
                _remainingSeconds -= Time.deltaTime;
                yield return null;
            }

            _timerLabel.text = "0";
            _countdownFinished = true;
            _registrationClosed = true;
            _statusLabel.text = "Registration finished. Press Start Game.";
            _startGameButton.style.display = DisplayStyle.Flex;
            _startGameButton.SetEnabled(true);
            Debug.Log($"RegistrationSceneController: Registration closed. Participants={_registry.Count}.");
        }

        private void HandleStartGameClicked()
        {
            if (_isStartingGame || !_countdownFinished)
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

            if (_registrationClosed)
            {
                Debug.Log($"RegistrationSceneController: Ignored gift from '{giftMessage.UserId}' because registration is closed.");
                return;
            }

            string receivedGiftName = giftMessage.GiftName?.Trim();
            if (string.IsNullOrWhiteSpace(receivedGiftName))
            {
                Debug.Log("RegistrationSceneController: Ignored gift event because gift name is empty.");
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
