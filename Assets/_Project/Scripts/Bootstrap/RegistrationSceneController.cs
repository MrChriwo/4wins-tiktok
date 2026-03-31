using System.Collections;
using FourWinsTikTok.Gameplay;
using FourWinsTikTok.TikTok;
using TikTokLiveUnity;
using UnityEngine;
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
        private ScrollView _participantsScroll;

        private string _requiredGiftName;
        private float _remainingSeconds;
        private ParticipantRegistryService _registry;

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
            _participantsScroll = root.Q<ScrollView>("participants-scroll");

            if (_giftLabel == null || _timerLabel == null || _statusLabel == null || _participantsScroll == null)
            {
                Debug.LogError("RegistrationSceneController: Missing required UI elements in RegistrationScreen UXML.");
                enabled = false;
                return;
            }

            _giftLabel.text = $"Send gift to register: {_requiredGiftName}";
            _statusLabel.text = "Waiting for participants...";

            if (tikTokAdapter != null)
            {
                tikTokAdapter.OnGiftReceived += HandleGiftReceived;
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
            }

            if (_registry != null)
            {
                _registry.OnParticipantRegistered -= HandleParticipantRegistered;
                _registry.OnCleared -= HandleParticipantsCleared;
            }
        }

        private IEnumerator RegistrationCountdownRoutine()
        {
            AsyncOperation preloadGameOperation = SceneManager.LoadSceneAsync(gameplaySceneName, LoadSceneMode.Single);
            if (preloadGameOperation != null)
            {
                preloadGameOperation.allowSceneActivation = false;
            }

            while (_remainingSeconds > 0f)
            {
                _timerLabel.text = Mathf.CeilToInt(_remainingSeconds).ToString();
                _remainingSeconds -= Time.deltaTime;
                yield return null;
            }

            _timerLabel.text = "0";
            _statusLabel.text = "Starting game...";

            if (preloadGameOperation == null)
            {
                yield return SceneManager.LoadSceneAsync(gameplaySceneName, LoadSceneMode.Single);
                yield break;
            }

            while (preloadGameOperation.progress < 0.9f)
            {
                yield return null;
            }

            preloadGameOperation.allowSceneActivation = true;
            while (!preloadGameOperation.isDone)
            {
                yield return null;
            }
        }

        private void HandleGiftReceived(GiftMessage giftMessage)
        {
            if (string.IsNullOrWhiteSpace(giftMessage.GiftName) ||
                !string.Equals(giftMessage.GiftName.Trim(), _requiredGiftName, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_registry.TryRegisterParticipant(giftMessage.UserId, giftMessage.DisplayName, giftMessage.AvatarPicture, out _))
            {
                _statusLabel.text = $"Registered participants: {_registry.Count}";
            }
        }

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

            if (participant.AvatarPicture == null)
            {
                return;
            }

            TikTokLiveManager.Instance.RequestSprite(participant.AvatarPicture, sprite =>
            {
                if (sprite != null)
                {
                    avatar.style.backgroundImage = new StyleBackground(sprite);
                }
            });
        }
    }
}
