using System;
using System.Collections.Generic;
using FourWinsTikTok.Bootstrap;
using UnityEngine;

namespace FourWinsTikTok.Gameplay
{
    public static class ParticipantSnapshotStore
    {
        public static void SaveFromRegistry(ParticipantRegistryService registry)
        {
            if (registry == null)
            {
                return;
            }

            List<ParticipantSnapshotEntry> entries = new List<ParticipantSnapshotEntry>();
            foreach (ParticipantInfo participant in registry.Participants)
            {
                if (participant == null || string.IsNullOrWhiteSpace(participant.UserId))
                {
                    continue;
                }

                entries.Add(new ParticipantSnapshotEntry
                {
                    userId = participant.UserId,
                    displayName = participant.DisplayName,
                    avatarUrl = participant.AvatarUrl
                });
            }

            ParticipantSnapshotCollection collection = new ParticipantSnapshotCollection
            {
                participants = entries.ToArray()
            };

            string json = JsonUtility.ToJson(collection);
            PlayerPrefs.SetString(BootstrapKeys.RegisteredParticipantsSnapshotPlayerPrefsKey, json);
            PlayerPrefs.Save();
        }

        public static IReadOnlyList<ParticipantSnapshotEntry> Load()
        {
            string json = PlayerPrefs.GetString(BootstrapKeys.RegisteredParticipantsSnapshotPlayerPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<ParticipantSnapshotEntry>();
            }

            ParticipantSnapshotCollection collection;
            try
            {
                collection = JsonUtility.FromJson<ParticipantSnapshotCollection>(json);
            }
            catch
            {
                return Array.Empty<ParticipantSnapshotEntry>();
            }

            return collection?.participants ?? Array.Empty<ParticipantSnapshotEntry>();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(BootstrapKeys.RegisteredParticipantsSnapshotPlayerPrefsKey);
            PlayerPrefs.Save();
        }

        [Serializable]
        private sealed class ParticipantSnapshotCollection
        {
            public ParticipantSnapshotEntry[] participants;
        }
    }

    [Serializable]
    public sealed class ParticipantSnapshotEntry
    {
        public string userId;
        public string displayName;
        public string avatarUrl;
    }
}
