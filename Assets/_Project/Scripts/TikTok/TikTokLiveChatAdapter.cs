using System;
using System.Collections;
using FourWinsTikTok.Config;
using TikTokLiveSharp.Client;
using TikTokLiveSharp.Events;
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

        private TikTokLiveManager Manager => TikTokLiveManager.Instance;
        private bool _subscribed;
        private TikTokLiveManager _boundManager;

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
            _subscribed = false;
            _boundManager = null;
        }

        public void Connect()
        {
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
            if (!logConnectionEvents)
            {
                return;
            }

            string target = !string.IsNullOrWhiteSpace(Manager.HostName)
                ? Manager.HostName
                : !string.IsNullOrWhiteSpace(Manager.RoomId) ? Manager.RoomId : "unknown";
            Debug.Log($"TikTokLiveChatAdapter: Connected to '{target}'.");
        }

        private void HandleDisconnected(TikTokLiveClient sender, bool connected)
        {
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

            OnChatMessageReceived?.Invoke(new ChatMessage(userId, message));
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
