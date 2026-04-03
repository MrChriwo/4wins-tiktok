using System.Collections;
using FourWinsTikTok.TikTok;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FourWinsTikTok.Bootstrap
{
    public class StartScreenController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private TikTokLiveChatAdapter tikTokAdapter;

        [Header("Flow")]
        [SerializeField] private string registrationSceneName = "Registration";
        [SerializeField] private bool loadAdditively = false;
        [SerializeField] private float connectTimeoutSeconds = 45f;
        [SerializeField] private bool requireTikTokConnection = true;

        private Button _startButton;
        private Button _settingsButton;
        private Label _statusLabel;
        private ProgressBar _progressBar;

        private VisualElement _settingsModal;
        private TextField _settingsUsernameField;
        private TextField _settingsCommunityDisplayField;
        private TextField _settingsStreamerDisplayField;
        private IntegerField _settingsRoundsField;
        private TextField _settingsGiftNameField;
        private IntegerField _settingsRegistrationSecondsField;
        private IntegerField _settingsParticipantTurnSecondsField;
        private TextField _settingsChallengeGiftField;
        private IntegerField _settingsChallengeCoinTargetField;
        private IntegerField _settingsChallengeSecondsField;
        private Label _settingsHintLabel;
        private Button _settingsSaveButton;
        private Button _settingsCancelButton;

        private bool _isConnected;
        private string _connectedTarget = string.Empty;
        private bool _isLaunching;
        private string _savedUsername = string.Empty;
        private string _savedCommunityDisplayName = "Chat";
        private string _savedStreamerDisplayName = "Streamer";
        private int _savedMatchWinsToWin = 5;
        private string _savedRegistrationGiftName = "Rose";
        private int _savedRegistrationSeconds = 20;
        private int _savedParticipantTurnSeconds = 60;
        private string _savedChallengeGiftName = "Rose";
        private int _savedChallengeCoinTarget = 500;
        private int _savedChallengeSeconds = 10;

        private void OnEnable()
        {
            TikTokLiveChatAdapter persistentAdapter = TikTokLiveChatAdapter.Instance;
            if (persistentAdapter != null)
            {
                tikTokAdapter = persistentAdapter;
            }

            if (uiDocument == null)
            {
                Debug.LogError("StartScreenController: UIDocument reference missing.");
                enabled = false;
                return;
            }

            VisualElement root = uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("StartScreenController: rootVisualElement is null.");
                enabled = false;
                return;
            }

            _startButton = root.Q<Button>("start-button");
            _settingsButton = root.Q<Button>("settings-button");
            _statusLabel = root.Q<Label>("status-label");
            _progressBar = root.Q<ProgressBar>("loading-progress");

            _settingsModal = root.Q<VisualElement>("settings-modal");
            _settingsUsernameField = root.Q<TextField>("settings-username-field");
            _settingsCommunityDisplayField = root.Q<TextField>("settings-community-display-field");
            _settingsStreamerDisplayField = root.Q<TextField>("settings-streamer-display-field");
            _settingsRoundsField = root.Q<IntegerField>("settings-rounds-field");
            _settingsGiftNameField = root.Q<TextField>("settings-gift-field");
            _settingsRegistrationSecondsField = root.Q<IntegerField>("settings-registration-seconds-field");
            _settingsParticipantTurnSecondsField = root.Q<IntegerField>("settings-participant-turn-seconds-field");
            _settingsChallengeGiftField = root.Q<TextField>("settings-challenge-gift-field");
            _settingsChallengeCoinTargetField = root.Q<IntegerField>("settings-challenge-coin-target-field");
            _settingsChallengeSecondsField = root.Q<IntegerField>("settings-challenge-seconds-field");
            _settingsHintLabel = root.Q<Label>("settings-hint-label");
            _settingsSaveButton = root.Q<Button>("settings-save-button");
            _settingsCancelButton = root.Q<Button>("settings-cancel-button");

            if (!ValidateUiReferences())
            {
                enabled = false;
                return;
            }

            _savedUsername = PlayerPrefs.GetString(BootstrapKeys.StreamerUsernamePlayerPrefsKey, string.Empty).Trim();
            _savedCommunityDisplayName = PlayerPrefs.GetString(BootstrapKeys.CommunityDisplayNamePlayerPrefsKey, "Chat").Trim();
            _savedStreamerDisplayName = PlayerPrefs.GetString(BootstrapKeys.StreamerDisplayNamePlayerPrefsKey, "Streamer").Trim();
            _savedMatchWinsToWin = Mathf.Clamp(PlayerPrefs.GetInt(BootstrapKeys.MatchWinsToWinPlayerPrefsKey, 5), 1, 25);
            _savedRegistrationGiftName = PlayerPrefs.GetString(BootstrapKeys.RegistrationGiftNamePlayerPrefsKey, "Rose").Trim();
            _savedRegistrationSeconds = Mathf.Clamp(PlayerPrefs.GetInt(BootstrapKeys.RegistrationDurationSecondsPlayerPrefsKey, 20), 5, 600);
            _savedParticipantTurnSeconds = Mathf.Clamp(PlayerPrefs.GetInt(BootstrapKeys.ParticipantTurnDurationSecondsPlayerPrefsKey, 60), 5, 300);
            _savedChallengeGiftName = PlayerPrefs.GetString(BootstrapKeys.BeginnerChallengeGiftNamePlayerPrefsKey, "Rose").Trim();
            _savedChallengeCoinTarget = Mathf.Clamp(PlayerPrefs.GetInt(BootstrapKeys.BeginnerChallengeCoinTargetPlayerPrefsKey, 500), 1, 500000);
            _savedChallengeSeconds = Mathf.Clamp(PlayerPrefs.GetInt(BootstrapKeys.BeginnerChallengeDurationSecondsPlayerPrefsKey, 10), 1, 120);

            _startButton.clicked += HandleStartClicked;
            _settingsButton.clicked += HandleSettingsClicked;
            _settingsSaveButton.clicked += HandleSettingsSaveClicked;
            _settingsCancelButton.clicked += HandleSettingsCancelClicked;

            _progressBar.value = 0f;
            _statusLabel.text = "Ready.";
            SetSettingsModalVisible(false, string.Empty);

            if (tikTokAdapter != null)
            {
                tikTokAdapter.OnConnectionStateChanged += HandleConnectionStateChanged;
            }
        }

        private bool ValidateUiReferences()
        {
            if (_startButton == null || _settingsButton == null || _statusLabel == null || _progressBar == null ||
                _settingsModal == null || _settingsUsernameField == null ||
                _settingsCommunityDisplayField == null || _settingsStreamerDisplayField == null ||
                _settingsRoundsField == null || _settingsGiftNameField == null || _settingsRegistrationSecondsField == null ||
                _settingsParticipantTurnSecondsField == null || _settingsChallengeGiftField == null ||
                _settingsChallengeCoinTargetField == null || _settingsChallengeSecondsField == null ||
                _settingsHintLabel == null || _settingsSaveButton == null || _settingsCancelButton == null)
            {
                Debug.LogError("StartScreenController: Missing required UI elements in StartScreen UXML.");
                return false;
            }

            return true;
        }

        private void OnDisable()
        {
            if (_startButton != null)
            {
                _startButton.clicked -= HandleStartClicked;
            }

            if (_settingsButton != null)
            {
                _settingsButton.clicked -= HandleSettingsClicked;
            }

            if (_settingsSaveButton != null)
            {
                _settingsSaveButton.clicked -= HandleSettingsSaveClicked;
            }

            if (_settingsCancelButton != null)
            {
                _settingsCancelButton.clicked -= HandleSettingsCancelClicked;
            }

            if (tikTokAdapter != null)
            {
                tikTokAdapter.OnConnectionStateChanged -= HandleConnectionStateChanged;
            }
        }

        private void HandleStartClicked()
        {
            if (_isLaunching)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_savedUsername))
            {
                SetSettingsModalVisible(true, "Please set your TikTok username first.");
                return;
            }

            StartCoroutine(LaunchGameRoutine(_savedUsername));
        }

        private void HandleSettingsClicked()
        {
            _settingsUsernameField.value = _savedUsername;
            _settingsCommunityDisplayField.value = _savedCommunityDisplayName;
            _settingsStreamerDisplayField.value = _savedStreamerDisplayName;
            _settingsRoundsField.value = _savedMatchWinsToWin;
            _settingsGiftNameField.value = _savedRegistrationGiftName;
            _settingsRegistrationSecondsField.value = _savedRegistrationSeconds;
            _settingsParticipantTurnSecondsField.value = _savedParticipantTurnSeconds;
            _settingsChallengeGiftField.value = _savedChallengeGiftName;
            _settingsChallengeCoinTargetField.value = _savedChallengeCoinTarget;
            _settingsChallengeSecondsField.value = _savedChallengeSeconds;
            SetSettingsModalVisible(true, string.Empty);
        }

        private void HandleSettingsSaveClicked()
        {
            string username = _settingsUsernameField.value?.Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                SetSettingsModalVisible(true, "Username cannot be empty.");
                return;
            }

            int roundsToWin = Mathf.Clamp(_settingsRoundsField.value, 1, 25);
            string communityDisplayName = _settingsCommunityDisplayField.value?.Trim();
            string streamerDisplayName = _settingsStreamerDisplayField.value?.Trim();
            string registrationGiftName = _settingsGiftNameField.value?.Trim();
            int registrationSeconds = Mathf.Clamp(_settingsRegistrationSecondsField.value, 5, 600);
            int participantTurnSeconds = Mathf.Clamp(_settingsParticipantTurnSecondsField.value, 5, 300);
            string challengeGiftName = _settingsChallengeGiftField.value?.Trim();
            int challengeCoinTarget = Mathf.Clamp(_settingsChallengeCoinTargetField.value, 1, 500000);
            int challengeSeconds = Mathf.Clamp(_settingsChallengeSecondsField.value, 1, 120);

            if (string.IsNullOrWhiteSpace(registrationGiftName))
            {
                SetSettingsModalVisible(true, "Registration gift cannot be empty.");
                return;
            }

            if (string.IsNullOrWhiteSpace(challengeGiftName))
            {
                SetSettingsModalVisible(true, "Beginner challenge gift cannot be empty.");
                return;
            }

            _savedUsername = username;
            _savedCommunityDisplayName = communityDisplayName;
            _savedStreamerDisplayName = streamerDisplayName;
            _savedMatchWinsToWin = roundsToWin;
            _savedRegistrationGiftName = registrationGiftName;
            _savedRegistrationSeconds = registrationSeconds;
            _savedParticipantTurnSeconds = participantTurnSeconds;
            _savedChallengeGiftName = challengeGiftName;
            _savedChallengeCoinTarget = challengeCoinTarget;
            _savedChallengeSeconds = challengeSeconds;
            PlayerPrefs.SetString(BootstrapKeys.StreamerUsernamePlayerPrefsKey, _savedUsername);
            PlayerPrefs.SetString(BootstrapKeys.CommunityDisplayNamePlayerPrefsKey, _savedCommunityDisplayName);
            PlayerPrefs.SetString(BootstrapKeys.StreamerDisplayNamePlayerPrefsKey, _savedStreamerDisplayName);
            PlayerPrefs.SetInt(BootstrapKeys.MatchWinsToWinPlayerPrefsKey, _savedMatchWinsToWin);
            PlayerPrefs.SetString(BootstrapKeys.RegistrationGiftNamePlayerPrefsKey, _savedRegistrationGiftName);
            PlayerPrefs.SetInt(BootstrapKeys.RegistrationDurationSecondsPlayerPrefsKey, _savedRegistrationSeconds);
            PlayerPrefs.SetInt(BootstrapKeys.ParticipantTurnDurationSecondsPlayerPrefsKey, _savedParticipantTurnSeconds);
            PlayerPrefs.SetString(BootstrapKeys.BeginnerChallengeGiftNamePlayerPrefsKey, _savedChallengeGiftName);
            PlayerPrefs.SetInt(BootstrapKeys.BeginnerChallengeCoinTargetPlayerPrefsKey, _savedChallengeCoinTarget);
            PlayerPrefs.SetInt(BootstrapKeys.BeginnerChallengeDurationSecondsPlayerPrefsKey, _savedChallengeSeconds);
            PlayerPrefs.Save();

            SetSettingsModalVisible(false, string.Empty);
            _statusLabel.text = $"Settings saved: {_savedUsername}, first to {_savedMatchWinsToWin}.";
        }

        private void HandleSettingsCancelClicked()
        {
            SetSettingsModalVisible(false, string.Empty);
        }

        private IEnumerator LaunchGameRoutine(string username)
        {
            _isLaunching = true;
            SetInteractable(false);
            SetSettingsModalVisible(false, string.Empty);
            _statusLabel.text = "Preloading game scene...";
            _progressBar.value = 0f;

            bool requireConnectionThisRun = requireTikTokConnection;

            AsyncOperation loadOperation = loadAdditively
                ? SceneManager.LoadSceneAsync(registrationSceneName, LoadSceneMode.Additive)
                : SceneManager.LoadSceneAsync(registrationSceneName, LoadSceneMode.Single);

            if (loadOperation == null)
            {
                _statusLabel.text = $"Failed to load scene '{registrationSceneName}'.";
                SetInteractable(true);
                _isLaunching = false;
                yield break;
            }

            loadOperation.allowSceneActivation = false;

            if (requireConnectionThisRun && tikTokAdapter != null && !tikTokAdapter.IsConnected)
            {
                _statusLabel.text = "Connecting to TikTok...";
                tikTokAdapter.ConnectWithHostId(username);
            }
            else if (requireConnectionThisRun && tikTokAdapter == null)
            {
                _statusLabel.text = "TikTok adapter missing. Assign TikTokLiveChatAdapter in inspector.";
                SetInteractable(true);
                _isLaunching = false;
                yield break;
            }
            else if (!requireConnectionThisRun)
            {
                _isConnected = true;
            }
            else
            {
                _isConnected = true;
                _connectedTarget = username;
            }

            float connectStartTime = Time.unscaledTime;
            while (true)
            {
                if (requireConnectionThisRun && tikTokAdapter != null && tikTokAdapter.IsConnected)
                {
                    _isConnected = true;
                    if (string.IsNullOrWhiteSpace(_connectedTarget))
                    {
                        _connectedTarget = username;
                    }
                }

                float preloadProgress = Mathf.Clamp01(loadOperation.progress / 0.9f);
                _progressBar.value = preloadProgress * 100f;

                bool sceneReady = loadOperation.progress >= 0.9f;
                bool connectionReady = !requireConnectionThisRun || _isConnected;
                bool timeout = requireConnectionThisRun && (Time.unscaledTime - connectStartTime) >= connectTimeoutSeconds;

                if (timeout)
                {
                    _statusLabel.text = "TikTok connection timed out. Check username/live status and retry.";
                    loadOperation.allowSceneActivation = false;
                    SetInteractable(true);
                    _isLaunching = false;
                    yield break;
                }

                if (sceneReady && connectionReady)
                {
                    break;
                }

                if (requireConnectionThisRun)
                {
                    if (_isConnected)
                    {
                        _statusLabel.text = $"Connected to {_connectedTarget}. Finalizing...";
                    }
                    else
                    {
                        _statusLabel.text = $"Connecting to TikTok... ({Mathf.CeilToInt(connectTimeoutSeconds - (Time.unscaledTime - connectStartTime))}s)";
                    }
                }

                yield return null;
            }

            _statusLabel.text = "Starting game...";
            loadOperation.allowSceneActivation = true;

            while (!loadOperation.isDone)
            {
                yield return null;
            }

            _isLaunching = false;
        }

        private void HandleConnectionStateChanged(bool connected, string target)
        {
            _isConnected = connected;
            _connectedTarget = target;
        }

        private void SetInteractable(bool interactable)
        {
            _startButton.SetEnabled(interactable);
            _settingsButton.SetEnabled(interactable);
            _settingsSaveButton.SetEnabled(interactable);
            _settingsCancelButton.SetEnabled(interactable);
            _settingsUsernameField.SetEnabled(interactable);
            _settingsCommunityDisplayField.SetEnabled(interactable);
            _settingsStreamerDisplayField.SetEnabled(interactable);
            _settingsRoundsField.SetEnabled(interactable);
            _settingsGiftNameField.SetEnabled(interactable);
            _settingsRegistrationSecondsField.SetEnabled(interactable);
            _settingsParticipantTurnSecondsField.SetEnabled(interactable);
            _settingsChallengeGiftField.SetEnabled(interactable);
            _settingsChallengeCoinTargetField.SetEnabled(interactable);
            _settingsChallengeSecondsField.SetEnabled(interactable);
        }

        private void SetSettingsModalVisible(bool visible, string hint)
        {
            _settingsModal.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _settingsHintLabel.text = hint;
            if (visible)
            {
                _settingsUsernameField.Focus();
            }
        }
    }
}
