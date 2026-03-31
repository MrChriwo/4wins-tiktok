using TikTokLiveSharp.Events.Objects;

namespace FourWinsTikTok.TikTok
{
    public readonly struct ChatMessage
    {
        public ChatMessage(string userId, string message)
            : this(userId, userId, message, null, null)
        {
        }

        public ChatMessage(string userId, string displayName, string message, Picture avatarPicture, string avatarUrl = null)
        {
            UserId = userId;
            DisplayName = displayName;
            Message = message;
            AvatarPicture = avatarPicture;
            AvatarUrl = avatarUrl;
        }

        public string UserId { get; }
        public string DisplayName { get; }
        public string Message { get; }
        public Picture AvatarPicture { get; }
        public string AvatarUrl { get; }
    }
}
