using System;
using System.Collections.Generic;
using FourWinsTikTok.Config;

namespace FourWinsTikTok.Voting
{
    public class VoteSystem
    {
        private readonly VoteRulesConfig _rulesConfig;
        private readonly int _boardColumns;
        private readonly Dictionary<string, int> _latestVotesByUser;
        private bool _isRoundActive;

        public VoteSystem(VoteRulesConfig rulesConfig, int boardColumns)
        {
            _rulesConfig = rulesConfig ?? throw new ArgumentNullException(nameof(rulesConfig));
            if (boardColumns <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(boardColumns));
            }

            _boardColumns = boardColumns;
            _latestVotesByUser = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        public void BeginRound()
        {
            _latestVotesByUser.Clear();
            _isRoundActive = true;
        }

        public void EndRound()
        {
            _isRoundActive = false;
        }

        public bool TryRegisterVote(string userId, string message)
        {
            if (!_isRoundActive || string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            if (!TryParseColumn(message, out int columnIndex))
            {
                return false;
            }

            _latestVotesByUser[userId] = columnIndex;
            return true;
        }

        public IReadOnlyList<VoteTally> GetRanking()
        {
            int[] counts = GetVoteCounts();
            List<VoteTally> ranking = new List<VoteTally>();
            for (int column = 0; column < counts.Length; column++)
            {
                int voteCount = counts[column];
                if (voteCount <= 0)
                {
                    continue;
                }

                ranking.Add(new VoteTally(column, voteCount));
            }

            ranking.Sort((a, b) =>
            {
                int voteComparison = b.VoteCount.CompareTo(a.VoteCount);
                if (voteComparison != 0)
                {
                    return voteComparison;
                }

                return a.ColumnIndex.CompareTo(b.ColumnIndex);
            });

            return ranking;
        }

        private int[] GetVoteCounts()
        {
            int[] counts = new int[_boardColumns];
            foreach (int vote in _latestVotesByUser.Values)
            {
                if (vote >= 0 && vote < _boardColumns)
                {
                    counts[vote]++;
                }
            }

            return counts;
        }

        private bool TryParseColumn(string message, out int columnIndex)
        {
            columnIndex = -1;
            if (message == null)
            {
                return false;
            }

            string normalized = message.Trim();

            switch (normalized)
            {
                case "1": columnIndex = 0; break;
                case "2": columnIndex = 1; break;
                case "3": columnIndex = 2; break;
                case "4": columnIndex = 3; break;
                case "5": columnIndex = 4; break;
                case "6": columnIndex = 5; break;
                case "7": columnIndex = 6; break;
                default: return false;
            }

            int displayColumn = columnIndex + 1;
            if (displayColumn < _rulesConfig.MinAcceptedVote || displayColumn > _rulesConfig.MaxAcceptedVote)
            {
                return false;
            }

            return columnIndex < _boardColumns;
        }
    }
}
