using System;

namespace FourWinsTikTok.TikTok
{
    public interface IChatMessageSource
    {
        event Action<ChatMessage> OnChatMessageReceived;
    }
}
