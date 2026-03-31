using TikTokLiveSharp.Events.Objects;

namespace FourWinsTikTok.TikTok
{
    public readonly struct GiftMessage
    {
        public GiftMessage(string userId, string displayName, string giftName, Picture avatarPicture)
        {
            UserId = userId;
            DisplayName = displayName;
            GiftName = giftName;
            AvatarPicture = avatarPicture;
        }

        public string UserId { get; }
        public string DisplayName { get; }
        public string GiftName { get; }
        public Picture AvatarPicture { get; }
    }
}
