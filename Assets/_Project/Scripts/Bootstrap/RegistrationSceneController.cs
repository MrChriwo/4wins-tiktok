using System.Collections;
using FourWinsTikTok.Gameplay;
using FourWinsTikTok.TikTok;
using TikTokLiveUnity;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

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

        private void OnEnable()
        {
            if (uiDocument == null)
            {
                Debug.LogError("RegistrationSceneController: Missing UIDocument reference.");
                enabled = false;
                return;
            }

            if (tikTokAdapter == null)
            {
                tikTokAdapter = FindFirstObjectByType<TikTokLiveChatAdapter>();
            }

            _registry = ParticipantRegistryService.EnsureInstance();
            _registry.ResetParticipants();

            _requiredGiftName = PlayerPrefs.GetString(BootstrapKeys.RegistrationGiftNamePlayerPrefsKey, "Rose").Trim();
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

            if (tikTokAdapter != null)
            {
                tikTokAdapter.OnGiftReceived += HandleGiftReceived;
#if UNITY_EDITOR
                tikTokAdapter.OnChatMessageReceived += HandleChatMessage;
#endif
            }

            _registry.OnParticipantRegistered += HandleParticipantRegistered;
            _registry.OnCleared += HandleParticipantsCleared;

            StartCoroutine(RegistrationCountdownRoutine());
        }

        private void OnDisable()
        {
            if (tikTokAdapter != null)
            {
                tikTokAdapter.OnGiftReceived -= HandleGiftReceived;
#if UNITY_EDITOR
                tikTokAdapter.OnChatMessageReceived -= HandleChatMessage;
#endif
            }

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
            _statusLabel.text = "Registration finished. Press Start Game.";
            _startGameButton.style.display = DisplayStyle.Flex;
            _startGameButton.SetEnabled(true);
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
            string receivedGiftName = giftMessage.GiftName?.Trim();
            if (string.IsNullOrWhiteSpace(receivedGiftName))
            {
                Debug.Log("RegistrationSceneController: Ignored gift event because gift name is empty.");
                return;
            }

            if (!string.Equals(receivedGiftName, _requiredGiftName, System.StringComparison.OrdinalIgnoreCase))
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

#if UNITY_EDITOR
        private void HandleChatMessage(ChatMessage chatMessage)
        {
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
            _participantsScroll.contentContainer.Clear();
        }

        private void HandleParticipantRegistered(ParticipantInfo participant)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("participant-row");

            VisualElement avatar = new VisualElement();
            avatar.AddToClassList("participant-avatar");
            row.Add(avatar);

            Label nameLabel = new Label(participant.DisplayName);
            nameLabel.AddToClassList("participant-name");
            row.Add(nameLabel);

            _participantsScroll.Add(row);
            row.schedule.Execute(() => row.AddToClassList("visible")).ExecuteLater(16);

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
    }
}
