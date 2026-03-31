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
        [SerializeField] private string gameSceneName = "Game";
        [SerializeField] private bool loadAdditively = false;
        [SerializeField] private float connectTimeoutSeconds = 25f;
        [SerializeField] private bool requireTikTokConnection = true;

        private Button _startButton;
        private Button _settingsButton;
        private Label _statusLabel;
        private ProgressBar _progressBar;
        private Label _usernameInfoLabel;

        private VisualElement _settingsModal;
        private TextField _settingsUsernameField;
        private Label _settingsHintLabel;
        private Button _settingsSaveButton;
        private Button _settingsCancelButton;

        private bool _isConnected;
        private string _connectedTarget = string.Empty;
        private bool _isLaunching;
        private string _savedUsername = string.Empty;

        private void OnEnable()
        {
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
            _usernameInfoLabel = root.Q<Label>("username-info-label");

            _settingsModal = root.Q<VisualElement>("settings-modal");
            _settingsUsernameField = root.Q<TextField>("settings-username-field");
            _settingsHintLabel = root.Q<Label>("settings-hint-label");
            _settingsSaveButton = root.Q<Button>("settings-save-button");
            _settingsCancelButton = root.Q<Button>("settings-cancel-button");

            if (!ValidateUiReferences())
            {
                enabled = false;
                return;
            }

            _savedUsername = PlayerPrefs.GetString(BootstrapKeys.StreamerUsernamePlayerPrefsKey, string.Empty).Trim();

            _startButton.clicked += HandleStartClicked;
            _settingsButton.clicked += HandleSettingsClicked;
            _settingsSaveButton.clicked += HandleSettingsSaveClicked;
            _settingsCancelButton.clicked += HandleSettingsCancelClicked;

            _progressBar.value = 0f;
            _statusLabel.text = "Ready.";
            ApplySavedUsernameToUi();
            SetSettingsModalVisible(false, string.Empty);

            if (tikTokAdapter != null)
            {
                tikTokAdapter.OnConnectionStateChanged += HandleConnectionStateChanged;
            }
        }

        private bool ValidateUiReferences()
        {
            if (_startButton == null || _settingsButton == null || _statusLabel == null || _progressBar == null ||
                _usernameInfoLabel == null || _settingsModal == null || _settingsUsernameField == null ||
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

            _savedUsername = username;
            PlayerPrefs.SetString(BootstrapKeys.StreamerUsernamePlayerPrefsKey, _savedUsername);
            PlayerPrefs.Save();

            ApplySavedUsernameToUi();
            SetSettingsModalVisible(false, string.Empty);
            _statusLabel.text = $"Username saved: {_savedUsername}";
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

            AsyncOperation loadOperation = loadAdditively
                ? SceneManager.LoadSceneAsync(gameSceneName, LoadSceneMode.Additive)
                : SceneManager.LoadSceneAsync(gameSceneName, LoadSceneMode.Single);

            if (loadOperation == null)
            {
                _statusLabel.text = $"Failed to load scene '{gameSceneName}'.";
                SetInteractable(true);
                _isLaunching = false;
                yield break;
            }

            loadOperation.allowSceneActivation = false;

            if (requireTikTokConnection && tikTokAdapter != null && !tikTokAdapter.IsConnected)
            {
                _statusLabel.text = "Connecting to TikTok...";
                tikTokAdapter.ConnectWithHostId(username);
            }
            else if (requireTikTokConnection && tikTokAdapter == null)
            {
                _statusLabel.text = "TikTok adapter missing. Assign TikTokLiveChatAdapter in inspector.";
                SetInteractable(true);
                _isLaunching = false;
                yield break;
            }
            else if (!requireTikTokConnection)
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
                float preloadProgress = Mathf.Clamp01(loadOperation.progress / 0.9f);
                _progressBar.value = preloadProgress * 100f;

                bool sceneReady = loadOperation.progress >= 0.9f;
                bool connectionReady = !requireTikTokConnection || _isConnected;
                bool timeout = requireTikTokConnection && (Time.unscaledTime - connectStartTime) >= connectTimeoutSeconds;

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

                if (requireTikTokConnection)
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
        }

        private void ApplySavedUsernameToUi()
        {
            _usernameInfoLabel.text = string.IsNullOrWhiteSpace(_savedUsername)
                ? "No username set. Open settings."
                : $"Configured streamer: {_savedUsername}";
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
