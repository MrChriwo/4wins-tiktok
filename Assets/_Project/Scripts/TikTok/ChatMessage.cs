namespace FourWinsTikTok.TikTok
{
    public readonly struct ChatMessage
    {
        public ChatMessage(string userId, string message)
        {
            UserId = userId;
            Message = message;
        }

        public string UserId { get; }
        public string Message { get; }
    }
}
