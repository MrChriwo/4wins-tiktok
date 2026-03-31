using System;
using System.Collections;
using FourWinsTikTok.Config;
using TikTokLiveSharp.Client;
using TikTokLiveSharp.Events;
using TikTokLiveSharp.Events.Objects;
using TikTokLiveUnity;
using UnityEngine;

namespace FourWinsTikTok.TikTok
{
    public class TikTokLiveChatAdapter : MonoBehaviour, IChatMessageSource
    {
        [SerializeField] private TikTokConnectionConfig connectionConfig;
        [SerializeField] private bool logChatMessages;
        [SerializeField] private bool logConnectionEvents = true;

        public event Action<ChatMessage> OnChatMessageReceived;
        public event Action<GiftMessage> OnGiftReceived;
        public event Action<bool, string> OnConnectionStateChanged;

        private TikTokLiveManager Manager => TikTokLiveManager.Instance;
        private bool _subscribed;
        private TikTokLiveManager _boundManager;

        public bool IsConnected => _boundManager != null && _boundManager.Connected;
        public bool IsConnecting => _boundManager != null && _boundManager.Connecting;

        private void OnEnable()
        {
            StartCoroutine(BindManagerRoutine());
        }

        private void OnDisable()
        {
            UnsubscribeFromManager();
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
                Debug.LogWarning($"TikTokLiveChatAdapter: Failed to initialize TikTokLiveManager instance. {exception.Message}");
                yield break;
            }

            SubscribeToManager();

            if (connectionConfig != null && connectionConfig.AutoConnectOnEnable)
            {
                StartCoroutine(DelayedAutoConnect());
            }
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

            if (logConnectionEvents)
            {
                Debug.Log("TikTokLiveChatAdapter: Subscribed to TikTokLiveManager events.");
            }
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

        public void Connect()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (logConnectionEvents)
            {
                Debug.LogWarning("TikTokLiveChatAdapter: Connect is disabled on WebGL runtime.");
            }

            OnConnectionStateChanged?.Invoke(false, "webgl-unsupported");
            return;
#endif

            if (connectionConfig == null)
            {
                if (logConnectionEvents)
                {
                    Debug.LogWarning("TikTokLiveChatAdapter: Cannot connect because connection config is missing.");
                }

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
                if (logConnectionEvents)
                {
                    Debug.Log($"TikTokLiveChatAdapter: Connect skipped (Connected={manager.Connected}, Connecting={manager.Connecting}).");
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(connectionConfig.RoomId))
            {
                if (logConnectionEvents)
                {
                    Debug.Log($"TikTokLiveChatAdapter: Connecting to room id '{connectionConfig.RoomId}'.");
                }

                manager.ConnectToRoomAsync(connectionConfig.RoomId.Trim(), Debug.LogException);
            }
            else if (!string.IsNullOrWhiteSpace(connectionConfig.HostId))
            {
                string normalizedHostId = NormalizeHostId(connectionConfig.HostId);
                if (logConnectionEvents)
                {
                    Debug.Log($"TikTokLiveChatAdapter: Connecting to host id '{normalizedHostId}'.");
                }

                manager.ConnectToStreamAsync(normalizedHostId, Debug.LogException);
            }
            else
            {
                Debug.LogWarning("TikTokLiveChatAdapter: HostId or RoomId missing.");
            }
        }

        public void ConnectWithHostId(string hostId)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (logConnectionEvents)
            {
                Debug.LogWarning("TikTokLiveChatAdapter: ConnectWithHostId is disabled on WebGL runtime.");
            }

            OnConnectionStateChanged?.Invoke(false, "webgl-unsupported");
            return;
#endif

            string normalizedHostId = NormalizeHostId(hostId);
            if (string.IsNullOrWhiteSpace(normalizedHostId))
            {
                Debug.LogWarning("TikTokLiveChatAdapter: Cannot connect. Host id is empty.");
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
                if (logConnectionEvents)
                {
                    Debug.Log($"TikTokLiveChatAdapter: ConnectWithHostId skipped (Connected={manager.Connected}, Connecting={manager.Connecting}).");
                }

                return;
            }

            if (logConnectionEvents)
            {
                Debug.Log($"TikTokLiveChatAdapter: Connecting to host id '{normalizedHostId}' (runtime).");
            }

            manager.ConnectToStreamAsync(normalizedHostId, Debug.LogException);
        }

        public void Disconnect()
        {
            TikTokLiveManager manager = _boundManager;
            if (manager == null)
            {
                return;
            }

            if (manager.Connected || manager.Connecting)
            {
                manager.DisconnectFromLivestreamAsync();
            }
        }

        private IEnumerator DelayedAutoConnect()
        {
            yield return null;
            Connect();
        }

        private void HandleConnected(TikTokLiveClient sender, bool connected)
        {
            string target = !string.IsNullOrWhiteSpace(Manager.HostName)
                ? Manager.HostName
                : !string.IsNullOrWhiteSpace(Manager.RoomId) ? Manager.RoomId : "unknown";

            OnConnectionStateChanged?.Invoke(true, target);

            if (!logConnectionEvents)
            {
                return;
            }

            Debug.Log($"TikTokLiveChatAdapter: Connected to '{target}'.");
        }

        private void HandleDisconnected(TikTokLiveClient sender, bool connected)
        {
            OnConnectionStateChanged?.Invoke(false, "");

            if (logConnectionEvents)
            {
                Debug.Log("TikTokLiveChatAdapter: Disconnected from livestream.");
            }
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
                Debug.Log($"TikTok chat '{userId}': {message}");
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

            OnGiftReceived?.Invoke(new GiftMessage(
                userId,
                gift.Sender.UniqueId,
                giftName.Trim(),
                gift.Sender.AvatarThumbnail,
                null));
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
    }
}
