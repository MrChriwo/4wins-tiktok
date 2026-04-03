using System;
using System.Collections;
using System.Text;
using FourWinsTikTok.Bootstrap;
using FourWinsTikTok.Config;
using TikTokLiveSharp.Client;
using TikTokLiveSharp.Events;
using TikTokLiveSharp.Events.Objects;
using TikTokLiveUnity;
using UnityEngine;
using UnityEngine.Networking;

namespace FourWinsTikTok.TikTok
{
    public class TikTokLiveChatAdapter : MonoBehaviour, IChatMessageSource
    {
        public static TikTokLiveChatAdapter Instance { get; private set; }

        [Header("Direct TikTok")]
        [SerializeField] private TikTokConnectionConfig connectionConfig;

        [Header("Bridge")]
        [SerializeField] private bool useBridgeInWebGL = true;
        [SerializeField] private bool useBridgeInEditorForTesting;
        [SerializeField] private string bridgeBaseUrl = "http://localhost:3010";
        [SerializeField, Min(0.1f)] private float bridgePollIntervalSeconds = 0.75f;
        [SerializeField, Min(1f)] private float bridgeRequestTimeoutSeconds = 10f;
        [SerializeField] private bool bridgeAutoReconnect = true;
        [SerializeField, Min(0.5f)] private float bridgeReconnectDelaySeconds = 2f;

        [Header("Logging")]
        [SerializeField] private bool logConnectionEvents = true;
        [SerializeField] private bool logChatMessages;
        [SerializeField] private bool logGiftMessages = true;
        [SerializeField] private bool logBridgePollDetails;

        public event Action<ChatMessage> OnChatMessageReceived;
        public event Action<GiftMessage> OnGiftReceived;
        public event Action<AdminCommandMessage> OnAdminCommandReceived;
        public event Action<bool, string> OnConnectionStateChanged;

        private TikTokLiveManager Manager => TikTokLiveManager.Instance;

        private bool _subscribed;
        private TikTokLiveManager _boundManager;

        private bool _bridgeConnected;
        private bool _bridgeConnecting;
        private string _bridgeHostId = string.Empty;
        private long _bridgeCursor;
        private Coroutine _bridgePollRoutine;
        private bool _isQuitting;
        private bool _manualBridgeDisconnect;
        private bool _resetCursorOnNextBridgeConnect;
        private float _nextBridgeReconnectAt;

        public bool IsConnected => ShouldUseBridgeRuntime()
            ? _bridgeConnected
            : _boundManager != null && _boundManager.Connected;

        public bool IsConnecting => ShouldUseBridgeRuntime()
            ? _bridgeConnecting
            : _boundManager != null && _boundManager.Connecting;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            StartCoroutine(BindManagerRoutine());
        }

        private void Update()
        {
            if (!ShouldUseBridgeRuntime() || !bridgeAutoReconnect)
            {
                return;
            }

            if (_isQuitting || _manualBridgeDisconnect || _bridgeConnected || _bridgeConnecting || _bridgePollRoutine != null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_bridgeHostId))
            {
                return;
            }

            if (Time.unscaledTime < _nextBridgeReconnectAt)
            {
                return;
            }

            _nextBridgeReconnectAt = Time.unscaledTime + Mathf.Max(0.5f, bridgeReconnectDelaySeconds);
            LogWarning($"Bridge reconnect attempt for '{_bridgeHostId}'.");
            StartBridgePolling(_bridgeHostId);
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Disconnect();
            }
            else
            {
                StopBridgePolling(false);
            }

            UnsubscribeFromManager();
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
            Disconnect();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private IEnumerator BindManagerRoutine()
        {
            yield return null;

            try
            {
                _boundManager = Manager;
            }
            catch (Exception exception)
            {
                LogWarning($"Failed to initialize TikTokLiveManager instance. {exception.Message}");
                yield break;
            }

            SubscribeToManager();

            if (connectionConfig != null && connectionConfig.AutoConnectOnEnable)
            {
                StartCoroutine(DelayedAutoConnect());
            }
        }

        public void Connect()
        {
            LogInfo($"Connect requested. Mode={GetRuntimeModeTag()}");

            if (ShouldUseBridgeRuntime())
            {
                string bridgeTargetHostId = connectionConfig != null ? NormalizeHostId(connectionConfig.HostId) : string.Empty;
                if (string.IsNullOrWhiteSpace(bridgeTargetHostId))
                {
                    LogWarning("Bridge mode requires a HostId but none is configured.");
                    OnConnectionStateChanged?.Invoke(false, "bridge-host-missing");
                    return;
                }

                ConnectWithHostId(bridgeTargetHostId);
                return;
            }

            ConnectDirectWithConfig();
        }

        public void ConnectWithHostId(string hostId)
        {
            string normalizedHostId = NormalizeHostId(hostId);
            if (string.IsNullOrWhiteSpace(normalizedHostId))
            {
                LogWarning("Cannot connect. Host id is empty.");
                return;
            }

            LogInfo($"ConnectWithHostId requested for '{normalizedHostId}'. Mode={GetRuntimeModeTag()}");

            if (ShouldUseBridgeRuntime())
            {
                _manualBridgeDisconnect = false;
                StartBridgePolling(normalizedHostId);
                return;
            }

            ConnectDirectWithHostId(normalizedHostId);
        }

        public void Disconnect()
        {
            if (ShouldUseBridgeRuntime())
            {
                _manualBridgeDisconnect = true;
                _resetCursorOnNextBridgeConnect = true;
                string hostId = _bridgeHostId;
                StopBridgePolling(true);

                if (!string.IsNullOrWhiteSpace(hostId) && this != null && isActiveAndEnabled)
                {
                    StartCoroutine(NotifyBridgeDisconnectRoutine(hostId));
                }

                return;
            }

            TikTokLiveManager manager = _boundManager;
            if (manager == null)
            {
                return;
            }

            if (manager.Connected || manager.Connecting)
            {
                LogInfo("Disconnect requested for direct TikTok mode.");
                manager.DisconnectFromLivestreamAsync();
            }
        }

        private void ConnectDirectWithConfig()
        {
            if (connectionConfig == null)
            {
                LogWarning("Cannot connect because TikTok connection config is missing.");
                return;
            }

            TikTokLiveManager manager = _boundManager ?? Manager;
            _boundManager = manager;

            if (!_subscribed)
            {
                SubscribeToManager();
            }

            if (manager.Connected || manager.Connecting)
            {
                LogInfo($"Direct connect skipped (Connected={manager.Connected}, Connecting={manager.Connecting}).");
                return;
            }

            if (!string.IsNullOrWhiteSpace(connectionConfig.RoomId))
            {
                LogInfo($"Direct connect to room id '{connectionConfig.RoomId.Trim()}'.");
                manager.ConnectToRoomAsync(connectionConfig.RoomId.Trim(), HandleDirectConnectException);
                return;
            }

            string normalizedHostId = NormalizeHostId(connectionConfig.HostId);
            if (string.IsNullOrWhiteSpace(normalizedHostId))
            {
                LogWarning("HostId or RoomId missing in connection config.");
                return;
            }

            ConnectDirectWithHostId(normalizedHostId);
        }

        private void ConnectDirectWithHostId(string normalizedHostId)
        {
            TikTokLiveManager manager = _boundManager ?? Manager;
            _boundManager = manager;

            if (!_subscribed)
            {
                SubscribeToManager();
            }

            if (manager.Connected || manager.Connecting)
            {
                LogInfo($"Direct connect with host id skipped (Connected={manager.Connected}, Connecting={manager.Connecting}).");
                return;
            }

            LogInfo($"Direct connect to host id '{normalizedHostId}'.");
            manager.ConnectToStreamAsync(normalizedHostId, HandleDirectConnectException);
        }

        private void HandleDirectConnectException(Exception exception)
        {
            LogError($"Direct connect raised exception: {exception.Message}");
            Debug.LogException(exception);
            OnConnectionStateChanged?.Invoke(false, "direct-connect-exception");
        }

        private IEnumerator DelayedAutoConnect()
        {
            yield return null;
            Connect();
        }

        private void SubscribeToManager()
        {
            if (_subscribed)
            {
                return;
            }

            if (_boundManager == null)
            {
                _boundManager = Manager;
            }

            _boundManager.OnChatMessage -= HandleChatMessage;
            _boundManager.OnChatMessage += HandleChatMessage;

            _boundManager.OnConnected -= HandleConnected;
            _boundManager.OnConnected += HandleConnected;

            _boundManager.OnDisconnected -= HandleDisconnected;
            _boundManager.OnDisconnected += HandleDisconnected;

            _boundManager.OnGift -= HandleGift;
            _boundManager.OnGift += HandleGift;

            _subscribed = true;
            LogInfo("Subscribed to TikTokLiveManager events.");
        }

        private void UnsubscribeFromManager()
        {
            if (!_subscribed || _boundManager == null)
            {
                return;
            }

            _boundManager.OnChatMessage -= HandleChatMessage;
            _boundManager.OnConnected -= HandleConnected;
            _boundManager.OnDisconnected -= HandleDisconnected;
            _boundManager.OnGift -= HandleGift;
            _subscribed = false;
            _boundManager = null;
        }

        private void HandleConnected(TikTokLiveClient sender, bool connected)
        {
            string target = !string.IsNullOrWhiteSpace(Manager.HostName)
                ? Manager.HostName
                : !string.IsNullOrWhiteSpace(Manager.RoomId) ? Manager.RoomId : "unknown";

            OnConnectionStateChanged?.Invoke(true, target);
            LogInfo($"Direct TikTok connected to '{target}'.");
        }

        private void HandleDisconnected(TikTokLiveClient sender, bool connected)
        {
            OnConnectionStateChanged?.Invoke(false, string.Empty);
            LogWarning("Direct TikTok disconnected.");
        }

        private void HandleChatMessage(TikTokLiveClient sender, Chat chatMessage)
        {
            if (chatMessage == null || chatMessage.Sender == null)
            {
                return;
            }

            string userId = chatMessage.Sender.UniqueId;
            string message = chatMessage.Message?.Trim();
            if (string.IsNullOrEmpty(userId) || message == null)
            {
                return;
            }

            if (logChatMessages)
            {
                LogInfo($"[DIRECT][CHAT] user='{userId}', message='{message}'");
            }

            OnChatMessageReceived?.Invoke(new ChatMessage(
                userId,
                chatMessage.Sender.UniqueId,
                message,
                chatMessage.Sender.AvatarThumbnail,
                null));
        }

        private void HandleGift(TikTokLiveClient sender, TikTokGift gift)
        {
            if (gift == null || gift.Sender == null || gift.Gift == null)
            {
                return;
            }

            string userId = gift.Sender.UniqueId;
            string giftName = gift.Gift.Name;
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(giftName))
            {
                return;
            }

            string normalizedGiftName = giftName.Trim();
            if (logGiftMessages)
            {
                LogInfo($"[DIRECT][GIFT] user='{userId}', gift='{normalizedGiftName}'");
            }

            OnGiftReceived?.Invoke(new GiftMessage(
                userId,
                gift.Sender.UniqueId,
                normalizedGiftName,
                gift.Sender.AvatarThumbnail,
                null));
        }

        private bool ShouldUseBridgeRuntime()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return useBridgeInWebGL;
#else
            return useBridgeInEditorForTesting;
#endif
        }

        private void StartBridgePolling(string normalizedHostId)
        {
            if (_bridgeConnected && string.Equals(_bridgeHostId, normalizedHostId, StringComparison.OrdinalIgnoreCase))
            {
                LogInfo($"Bridge already connected for '{normalizedHostId}', reconnect skipped.");
                return;
            }

            bool hostChanged = !string.Equals(_bridgeHostId, normalizedHostId, StringComparison.OrdinalIgnoreCase);
            if (hostChanged || _resetCursorOnNextBridgeConnect)
            {
                _bridgeCursor = 0;
                _resetCursorOnNextBridgeConnect = false;
                LogInfo($"Bridge cursor reset for new session. host='{normalizedHostId}'.");
            }

            StopBridgePolling(false);
            _bridgeHostId = normalizedHostId;
            _manualBridgeDisconnect = false;
            LogInfo($"Starting bridge polling. host='{_bridgeHostId}', baseUrl='{bridgeBaseUrl}', pollInterval={bridgePollIntervalSeconds}s");
            _bridgePollRoutine = StartCoroutine(BridgePollingRoutine());
        }

        private void StopBridgePolling(bool notifyDisconnect)
        {
            if (_bridgePollRoutine != null)
            {
                StopCoroutine(_bridgePollRoutine);
                _bridgePollRoutine = null;
            }

            bool wasConnected = _bridgeConnected;
            _bridgeConnected = false;
            _bridgeConnecting = false;

            if (wasConnected)
            {
                LogInfo("Bridge polling stopped.");
            }

            if (notifyDisconnect && wasConnected)
            {
                OnConnectionStateChanged?.Invoke(false, string.Empty);
            }
        }

        private IEnumerator BridgePollingRoutine()
        {
            if (string.IsNullOrWhiteSpace(bridgeBaseUrl))
            {
                LogWarning("Bridge mode enabled but Bridge Base Url is empty.");
                OnConnectionStateChanged?.Invoke(false, "bridge-url-missing");
                yield break;
            }

            _bridgeConnecting = true;

            string connectUrl = BuildBridgeUrl("connect", _bridgeHostId, _bridgeCursor);
            LogInfo($"Bridge connect request -> {connectUrl} (host='{_bridgeHostId}')");

            string registrationGiftName = GetRegistrationGiftName();

            using (UnityWebRequest connectRequest = BuildJsonPostRequest(connectUrl, new BridgeConnectRequest
            {
                hostId = _bridgeHostId,
                registrationGiftName = registrationGiftName
            }))
            {
                connectRequest.timeout = Mathf.CeilToInt(bridgeRequestTimeoutSeconds);
                yield return connectRequest.SendWebRequest();

                if (connectRequest.result != UnityWebRequest.Result.Success)
                {
                    _bridgeConnecting = false;
                    _bridgeConnected = false;
                    _nextBridgeReconnectAt = Time.unscaledTime + Mathf.Max(0.5f, bridgeReconnectDelaySeconds);
                    LogWarning($"Bridge connect failed: {connectRequest.error}");
                    OnConnectionStateChanged?.Invoke(false, "bridge-connect-failed");
                    yield break;
                }
            }

            _bridgeConnecting = false;
            _bridgeConnected = true;
            OnConnectionStateChanged?.Invoke(true, _bridgeHostId);
            LogInfo($"Bridge connected for '{_bridgeHostId}'.");

            WaitForSecondsRealtime wait = new WaitForSecondsRealtime(bridgePollIntervalSeconds);
            while (_bridgeConnected)
            {
                string eventsUrl = BuildBridgeUrl("events", _bridgeHostId, _bridgeCursor);
                using (UnityWebRequest eventsRequest = UnityWebRequest.Get(eventsUrl))
                {
                    eventsRequest.timeout = Mathf.CeilToInt(bridgeRequestTimeoutSeconds);
                    yield return eventsRequest.SendWebRequest();

                    if (eventsRequest.result != UnityWebRequest.Result.Success)
                    {
                        LogWarning($"Bridge polling failed: {eventsRequest.error}");
                        OnConnectionStateChanged?.Invoke(false, "bridge-poll-failed");
                        _bridgeConnected = false;
                        _nextBridgeReconnectAt = Time.unscaledTime + Mathf.Max(0.5f, bridgeReconnectDelaySeconds);
                        break;
                    }

                    if (logBridgePollDetails)
                    {
                        LogInfo($"Bridge poll ok. after={_bridgeCursor}, payloadLength={eventsRequest.downloadHandler.text?.Length ?? 0}");
                    }

                    ProcessBridgeEvents(eventsRequest.downloadHandler.text);
                }

                if (_bridgeConnected)
                {
                    yield return wait;
                }
            }

            _bridgePollRoutine = null;

            if (!_manualBridgeDisconnect && bridgeAutoReconnect && !_isQuitting)
            {
                _nextBridgeReconnectAt = Time.unscaledTime + Mathf.Max(0.5f, bridgeReconnectDelaySeconds);
            }
        }

        private IEnumerator NotifyBridgeDisconnectRoutine(string hostId)
        {
            if (string.IsNullOrWhiteSpace(bridgeBaseUrl) || string.IsNullOrWhiteSpace(hostId))
            {
                yield break;
            }

            string disconnectUrl = BuildBridgeUrl("disconnect", hostId, _bridgeCursor);
            using (UnityWebRequest request = BuildJsonPostRequest(disconnectUrl, new BridgeConnectRequest { hostId = hostId }))
            {
                request.timeout = Mathf.CeilToInt(Mathf.Clamp(bridgeRequestTimeoutSeconds, 1f, 5f));
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    LogInfo($"Bridge disconnect request sent for '{hostId}'.");
                }
                else if (!_isQuitting)
                {
                    LogWarning($"Bridge disconnect request failed for '{hostId}': {request.error}");
                }
            }
        }

        private void ProcessBridgeEvents(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            BridgeEventBatch batch;
            try
            {
                batch = JsonUtility.FromJson<BridgeEventBatch>(json);
            }
            catch (Exception exception)
            {
                LogWarning($"Failed to parse bridge events JSON. {exception.Message}");
                return;
            }

            if (batch?.events == null || batch.events.Length == 0)
            {
                return;
            }

            if (logBridgePollDetails)
            {
                LogInfo($"Bridge received {batch.events.Length} event(s). cursorBefore={_bridgeCursor}");
            }

            for (int index = 0; index < batch.events.Length; index++)
            {
                BridgeEvent bridgeEvent = batch.events[index];
                if (bridgeEvent == null)
                {
                    continue;
                }

                if (bridgeEvent.id > _bridgeCursor)
                {
                    _bridgeCursor = bridgeEvent.id;
                }

                switch (bridgeEvent.type)
                {
                    case "chat":
                    {
                        if (string.IsNullOrWhiteSpace(bridgeEvent.userId) || string.IsNullOrWhiteSpace(bridgeEvent.message))
                        {
                            break;
                        }

                        string displayName = string.IsNullOrWhiteSpace(bridgeEvent.displayName)
                            ? bridgeEvent.userId
                            : bridgeEvent.displayName;
                        string avatarUrl = ResolveBridgeAvatarUrl(bridgeEvent.avatarUrl);

                        string normalizedMessage = bridgeEvent.message.Trim();

                        if (logConnectionEvents)
                        {
                            int subscriberCount = OnChatMessageReceived?.GetInvocationList()?.Length ?? 0;
                            LogInfo($"[BRIDGE][CHAT] dispatch user='{bridgeEvent.userId}', msg='{normalizedMessage}', subscribers={subscriberCount}, cursor={_bridgeCursor}");
                        }

                        OnChatMessageReceived?.Invoke(new ChatMessage(
                            bridgeEvent.userId,
                            displayName,
                            normalizedMessage,
                            null,
                            avatarUrl));
                        break;
                    }

                    case "gift":
                    {
                        if (string.IsNullOrWhiteSpace(bridgeEvent.userId) || string.IsNullOrWhiteSpace(bridgeEvent.giftName))
                        {
                            break;
                        }

                        string displayName = string.IsNullOrWhiteSpace(bridgeEvent.displayName)
                            ? bridgeEvent.userId
                            : bridgeEvent.displayName;
                        string avatarUrl = ResolveBridgeAvatarUrl(bridgeEvent.avatarUrl);

                        string normalizedGiftName = bridgeEvent.giftName.Trim();

                        if (logConnectionEvents)
                        {
                            int subscriberCount = OnGiftReceived?.GetInvocationList()?.Length ?? 0;
                            LogInfo($"[BRIDGE][GIFT] dispatch user='{bridgeEvent.userId}', gift='{normalizedGiftName}', subscribers={subscriberCount}, cursor={_bridgeCursor}");
                        }

                        OnGiftReceived?.Invoke(new GiftMessage(
                            bridgeEvent.userId,
                            displayName,
                            normalizedGiftName,
                            null,
                            avatarUrl));
                        break;
                    }

                    case "disconnected":
                    {
                        _bridgeConnected = false;
                        LogWarning("Bridge server reported disconnect event.");
                        OnConnectionStateChanged?.Invoke(false, string.Empty);
                        break;
                    }

                    case "admin_command":
                    {
                        if (string.IsNullOrWhiteSpace(bridgeEvent.command) || string.IsNullOrWhiteSpace(bridgeEvent.userId))
                        {
                            break;
                        }

                        string issuedByDisplayName = string.IsNullOrWhiteSpace(bridgeEvent.displayName)
                            ? bridgeEvent.userId
                            : bridgeEvent.displayName;

                        OnAdminCommandReceived?.Invoke(new AdminCommandMessage(
                            bridgeEvent.command.Trim().ToLowerInvariant(),
                            bridgeEvent.userId,
                            issuedByDisplayName,
                            bridgeEvent.targetUserId,
                            bridgeEvent.targetDisplayName));
                        break;
                    }
                }
            }
        }

        private string BuildBridgeUrl(string endpoint, string hostId, long afterCursor)
        {
            string baseUrl = bridgeBaseUrl.TrimEnd('/');
            if (string.Equals(endpoint, "events", StringComparison.OrdinalIgnoreCase))
            {
                return $"{baseUrl}/bridge/{endpoint}?hostId={UnityWebRequest.EscapeURL(hostId)}&after={afterCursor}";
            }

            return $"{baseUrl}/bridge/{endpoint}";
        }

        private static string GetRegistrationGiftName()
        {
            return PlayerPrefs.GetString(BootstrapKeys.RegistrationGiftNamePlayerPrefsKey, "Rose").Trim();
        }

        private string ResolveBridgeAvatarUrl(string rawAvatarUrl)
        {
            if (string.IsNullOrWhiteSpace(rawAvatarUrl))
            {
                return rawAvatarUrl;
            }

            string trimmed = rawAvatarUrl.Trim();
            if (!ShouldUseBridgeRuntime() || string.IsNullOrWhiteSpace(bridgeBaseUrl))
            {
                return trimmed;
            }

            if (trimmed.Contains("/bridge/avatar?", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            string baseUrl = bridgeBaseUrl.TrimEnd('/');
            string encodedAvatarUrl = UnityWebRequest.EscapeURL(trimmed);
            return $"{baseUrl}/bridge/avatar?url={encodedAvatarUrl}";
        }

        private static UnityWebRequest BuildJsonPostRequest(string url, object payload)
        {
            string json = JsonUtility.ToJson(payload);
            byte[] body = Encoding.UTF8.GetBytes(json);

            UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        private static string NormalizeHostId(string hostId)
        {
            if (string.IsNullOrWhiteSpace(hostId))
            {
                return string.Empty;
            }

            string trimmed = hostId.Trim();
            return trimmed.StartsWith("@", StringComparison.Ordinal) ? trimmed.Substring(1) : trimmed;
        }

        private string GetRuntimeModeTag()
        {
            return ShouldUseBridgeRuntime() ? "BRIDGE" : "DIRECT";
        }

        private void LogInfo(string message)
        {
            if (logConnectionEvents)
            {
                Debug.Log($"TikTokLiveChatAdapter[{GetRuntimeModeTag()}]: {message}");
            }
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"TikTokLiveChatAdapter[{GetRuntimeModeTag()}]: {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"TikTokLiveChatAdapter[{GetRuntimeModeTag()}]: {message}");
        }

        [Serializable]
        private class BridgeConnectRequest
        {
            public string hostId;
            public string registrationGiftName;
        }

        [Serializable]
        private class BridgeEventBatch
        {
            public bool ok;
            public BridgeEvent[] events;
        }

        [Serializable]
        private class BridgeEvent
        {
            public long id;
            public string type;
            public string command;
            public string userId;
            public string displayName;
            public string message;
            public string giftName;
            public string avatarUrl;
            public string targetUserId;
            public string targetDisplayName;
        }
    }
}
