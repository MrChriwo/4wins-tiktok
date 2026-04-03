namespace FourWinsTikTok.TikTok
{
    public readonly struct AdminCommandMessage
    {
        public AdminCommandMessage(string command, string issuedByUserId, string issuedByDisplayName, string targetUserId, string targetDisplayName)
        {
            Command = command;
            IssuedByUserId = issuedByUserId;
            IssuedByDisplayName = issuedByDisplayName;
            TargetUserId = targetUserId;
            TargetDisplayName = targetDisplayName;
        }

        public string Command { get; }
        public string IssuedByUserId { get; }
        public string IssuedByDisplayName { get; }
        public string TargetUserId { get; }
        public string TargetDisplayName { get; }
    }
}
