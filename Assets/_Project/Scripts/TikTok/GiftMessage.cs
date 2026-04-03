using TikTokLiveSharp.Events.Objects;

namespace FourWinsTikTok.TikTok
{
    public readonly struct GiftMessage
    {
        public GiftMessage(string userId, string displayName, string giftName, Picture avatarPicture, string avatarUrl = null)
            : this(userId, displayName, giftName, 1, avatarPicture, avatarUrl)
        {
        }

        public GiftMessage(string userId, string displayName, string giftName, int giftCoins, Picture avatarPicture, string avatarUrl = null)
        {
            UserId = userId;
            DisplayName = displayName;
            GiftName = giftName;
            GiftCoins = giftCoins;
            AvatarPicture = avatarPicture;
            AvatarUrl = avatarUrl;
        }

        public string UserId { get; }
        public string DisplayName { get; }
        public string GiftName { get; }
        public int GiftCoins { get; }
        public Picture AvatarPicture { get; }
        public string AvatarUrl { get; }
    }
}
