using System;
using System.Collections.Generic;
using FourWinsTikTok.Core;

namespace FourWinsTikTok.Gameplay
{
    public class BoardState
    {
        private readonly PlayerSide[,] _grid;

        public BoardState(int columns, int rows, int connectLength)
        {
            if (columns < 4)
            {
                throw new ArgumentOutOfRangeException(nameof(columns), "Columns must be >= 4.");
            }

            if (rows < 4)
            {
                throw new ArgumentOutOfRangeException(nameof(rows), "Rows must be >= 4.");
            }

            if (connectLength < 4)
            {
                throw new ArgumentOutOfRangeException(nameof(connectLength), "ConnectLength must be >= 4.");
            }

            Columns = columns;
            Rows = rows;
            ConnectLength = connectLength;
            _grid = new PlayerSide[Columns, Rows];
        }

        public int Columns { get; }
        public int Rows { get; }
        public int ConnectLength { get; }

        public PlayerSide GetCell(int column, int row)
        {
            if (!IsInBounds(column, row))
            {
                return PlayerSide.None;
            }

            return _grid[column, row];
        }

        public bool TryPlaceDisc(int column, PlayerSide side, out int placedRow)
        {
            placedRow = -1;
            if (side == PlayerSide.None || column < 0 || column >= Columns)
            {
                return false;
            }

            for (int row = 0; row < Rows; row++)
            {
                if (_grid[column, row] != PlayerSide.None)
                {
                    continue;
                }

                _grid[column, row] = side;
                placedRow = row;
                return true;
            }

            return false;
        }

        public bool IsColumnFull(int column)
        {
            if (column < 0 || column >= Columns)
            {
                return true;
            }

            return _grid[column, Rows - 1] != PlayerSide.None;
        }

        public List<int> GetValidColumns()
        {
            List<int> validColumns = new List<int>();
            for (int column = 0; column < Columns; column++)
            {
                if (!IsColumnFull(column))
                {
                    validColumns.Add(column);
                }
            }

            return validColumns;
        }

        public bool IsDraw()
        {
            for (int column = 0; column < Columns; column++)
            {
                if (!IsColumnFull(column))
                {
                    return false;
                }
            }

            return true;
        }

        public bool CheckWinFrom(int column, int row, PlayerSide side)
        {
            if (side == PlayerSide.None || !IsInBounds(column, row))
            {
                return false;
            }

            return HasConnection(column, row, side, 1, 0)
                || HasConnection(column, row, side, 0, 1)
                || HasConnection(column, row, side, 1, 1)
                || HasConnection(column, row, side, 1, -1);
        }

        public void Reset()
        {
            for (int column = 0; column < Columns; column++)
            {
                for (int row = 0; row < Rows; row++)
                {
                    _grid[column, row] = PlayerSide.None;
                }
            }
        }

        private bool HasConnection(int originColumn, int originRow, PlayerSide side, int directionX, int directionY)
        {
            int count = 1;
            count += CountDirection(originColumn, originRow, side, directionX, directionY);
            count += CountDirection(originColumn, originRow, side, -directionX, -directionY);
            return count >= ConnectLength;
        }

        private int CountDirection(int originColumn, int originRow, PlayerSide side, int directionX, int directionY)
        {
            int count = 0;
            int column = originColumn + directionX;
            int row = originRow + directionY;

            while (IsInBounds(column, row) && _grid[column, row] == side)
            {
                count++;
                column += directionX;
                row += directionY;
            }

            return count;
        }

        private bool IsInBounds(int column, int row)
        {
            return column >= 0 && column < Columns && row >= 0 && row < Rows;
        }
    }
}
