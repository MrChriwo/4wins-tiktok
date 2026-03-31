using System;
using System.Collections.Generic;
using TikTokLiveSharp.Events.Objects;
using UnityEngine;

namespace FourWinsTikTok.Gameplay
{
    public class ParticipantRegistryService : MonoBehaviour
    {
        public static ParticipantRegistryService Instance { get; private set; }

        public static ParticipantRegistryService EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            GameObject serviceObject = new GameObject("ParticipantRegistryService");
            return serviceObject.AddComponent<ParticipantRegistryService>();
        }

        public event Action<ParticipantInfo> OnParticipantRegistered;
        public event Action OnCleared;

        private readonly Dictionary<string, ParticipantInfo> _participantsByUserId =
            new Dictionary<string, ParticipantInfo>(StringComparer.OrdinalIgnoreCase);

        public int Count => _participantsByUserId.Count;

        public IReadOnlyCollection<ParticipantInfo> Participants => _participantsByUserId.Values;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void ResetParticipants()
        {
            _participantsByUserId.Clear();
            OnCleared?.Invoke();
        }

        public bool IsRegistered(string userId)
        {
            return !string.IsNullOrWhiteSpace(userId) && _participantsByUserId.ContainsKey(userId);
        }

        public bool TryRegisterParticipant(string userId, string displayName, Picture avatarPicture, out ParticipantInfo participant)
        {
            participant = null;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            if (_participantsByUserId.TryGetValue(userId, out participant))
            {
                return false;
            }

            participant = new ParticipantInfo(userId, string.IsNullOrWhiteSpace(displayName) ? userId : displayName, avatarPicture);
            _participantsByUserId[userId] = participant;
            OnParticipantRegistered?.Invoke(participant);
            return true;
        }
    }

    public sealed class ParticipantInfo
    {
        public ParticipantInfo(string userId, string displayName, Picture avatarPicture)
        {
            UserId = userId;
            DisplayName = displayName;
            AvatarPicture = avatarPicture;
        }

        public string UserId { get; }
        public string DisplayName { get; }
        public Picture AvatarPicture { get; }
    }
}
