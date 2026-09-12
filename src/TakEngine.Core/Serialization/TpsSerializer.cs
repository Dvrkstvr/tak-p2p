using System;
using System.Collections.Generic;
using System.Text;
using TakEngine.Abstractions;
using TakEngine.Core.Board;

namespace TakEngine.Core.Serialization;

public static class TpsSerializer
{
    public static string Serialize(GameBoard board)
    {
        int size = (int)board.Size;
        var sb = new StringBuilder();

        // Ranks ordered from top rank down to rank 1 (y = size - 1 down to 0)
        for (int y = size - 1; y >= 0; y--)
        {
            int emptyCount = 0;
            var rowTokens = new List<string>();

            for (int x = 0; x < size; x++)
            {
                var stack = board.GetStack(new Coord(x, y));
                if (stack.IsEmpty)
                {
                    emptyCount++;
                }
                else
                {
                    if (emptyCount > 0)
                    {
                        rowTokens.Add(emptyCount == 1 ? "x" : $"x{emptyCount}");
                        emptyCount = 0;
                    }
                    rowTokens.Add(SerializeStack(stack));
                }
            }

            if (emptyCount > 0)
            {
                rowTokens.Add(emptyCount == 1 ? "x" : $"x{emptyCount}");
            }

            sb.Append(string.Join(",", rowTokens));
            if (y > 0)
            {
                sb.Append('/');
            }
        }

        int activeNum = board.ActivePlayer == PlayerColor.White ? 1 : 2;
        sb.Append($" {activeNum} {board.TurnNumber}");

        return sb.ToString();
    }

    private static string SerializeStack(PieceStack stack)
    {
        var sb = new StringBuilder();
        var pieces = stack.Pieces;
        for (int i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            sb.Append(p.Color == PlayerColor.White ? '1' : '2');
            if (i == pieces.Count - 1)
            {
                if (p.Type == PieceType.Standing)
                    sb.Append('S');
                else if (p.Type == PieceType.Capstone)
                    sb.Append('C');
            }
        }
        return sb.ToString();
    }

    public static GameBoard Deserialize(string tps)
    {
        if (string.IsNullOrWhiteSpace(tps))
            throw new ArgumentException("TPS string cannot be null or empty.", nameof(tps));

        string[] parts = tps.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            throw new FormatException($"Invalid TPS string format: '{tps}'. Expected at least 3 parts.");

        string boardPart = parts[0];
        string playerPart = parts[1];
        string turnPart = parts[2];

        string[] rows = boardPart.Split('/');
        int size = rows.Length;
        if (size is not (4 or 5 or 6))
            throw new FormatException($"Unsupported board size {size} in TPS string.");

        var boardSize = (BoardSize)size;
        var board = new GameBoard(boardSize);

        PlayerColor activePlayer = playerPart == "1" ? PlayerColor.White : PlayerColor.Black;
        int turnNumber = int.Parse(turnPart);

        int whiteStonesUsed = 0;
        int whiteCapstonesUsed = 0;
        int blackStonesUsed = 0;
        int blackCapstonesUsed = 0;

        // Clear default grid stacks so we can populate from TPS
        for (int r = 0; r < size; r++)
        {
            int y = size - 1 - r; // Ranks go from size-1 down to 0
            string rowStr = rows[r];
            string[] squareTokens = rowStr.Split(',');

            int x = 0;
            foreach (string token in squareTokens)
            {
                if (string.IsNullOrEmpty(token))
                    continue;

                if (token.StartsWith('x'))
                {
                    int count = token.Length > 1 ? int.Parse(token[1..]) : 1;
                    x += count;
                }
                else
                {
                    var stack = board.GetStack(new Coord(x, y));
                    ParseAndPopulateStack(token, stack, ref whiteStonesUsed, ref whiteCapstonesUsed, ref blackStonesUsed, ref blackCapstonesUsed);
                    x++;
                }
            }

            if (x != size)
                throw new FormatException($"Row {r} in TPS has {x} squares, expected {size}.");
        }

        // Set internal turn and reserves using reflection or internal state
        ApplyInternalState(board, boardSize, activePlayer, turnNumber, whiteStonesUsed, whiteCapstonesUsed, blackStonesUsed, blackCapstonesUsed);

        return board;
    }

    private static void ParseAndPopulateStack(
        string token,
        PieceStack stack,
        ref int whiteStonesUsed,
        ref int whiteCapstonesUsed,
        ref int blackStonesUsed,
        ref int blackCapstonesUsed)
    {
        int len = token.Length;
        PieceType topType = PieceType.Flat;
        if (token.EndsWith('S'))
        {
            topType = PieceType.Standing;
            len--;
        }
        else if (token.EndsWith('C'))
        {
            topType = PieceType.Capstone;
            len--;
        }

        for (int i = 0; i < len; i++)
        {
            char c = token[i];
            PlayerColor color = c == '1' ? PlayerColor.White : PlayerColor.Black;
            bool isTop = (i == len - 1);
            PieceType pieceType = isTop ? topType : PieceType.Flat;

            stack.Push(new Piece(color, pieceType));

            if (color == PlayerColor.White)
            {
                if (pieceType == PieceType.Capstone) whiteCapstonesUsed++;
                else whiteStonesUsed++;
            }
            else
            {
                if (pieceType == PieceType.Capstone) blackCapstonesUsed++;
                else blackStonesUsed++;
            }
        }
    }

    private static void ApplyInternalState(
        GameBoard board,
        BoardSize size,
        PlayerColor activePlayer,
        int turnNumber,
        int whiteStonesUsed,
        int whiteCapstonesUsed,
        int blackStonesUsed,
        int blackCapstonesUsed)
    {
        (int startingStones, int startingCapstones) = size switch
        {
            BoardSize.Four => (15, 0),
            BoardSize.Five => (21, 1),
            BoardSize.Six => (30, 1),
            _ => (0, 0)
        };

        var whiteReserves = new PlayerReserves(
            Math.Max(0, startingStones - whiteStonesUsed),
            Math.Max(0, startingCapstones - whiteCapstonesUsed));

        var blackReserves = new PlayerReserves(
            Math.Max(0, startingStones - blackStonesUsed),
            Math.Max(0, startingCapstones - blackCapstonesUsed));

        var phase = turnNumber == 1 ? GamePhase.FirstTurnPlacement : GamePhase.Playing;

        // Use reflection to set private setters on GameBoard
        typeof(GameBoard).GetProperty(nameof(GameBoard.ActivePlayer))!.SetValue(board, activePlayer);
        typeof(GameBoard).GetProperty(nameof(GameBoard.TurnNumber))!.SetValue(board, turnNumber);
        typeof(GameBoard).GetProperty(nameof(GameBoard.Phase))!.SetValue(board, phase);
        typeof(GameBoard).GetProperty(nameof(GameBoard.WhiteReserves))!.SetValue(board, whiteReserves);
        typeof(GameBoard).GetProperty(nameof(GameBoard.BlackReserves))!.SetValue(board, blackReserves);
    }
}
