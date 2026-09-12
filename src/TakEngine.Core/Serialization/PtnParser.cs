using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TakEngine.Abstractions;
using TakEngine.Core.Board;

namespace TakEngine.Core.Serialization;

public sealed record PtnGame(
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<TakMove> Moves,
    string? Result = null);

public static class PtnParser
{
    private static readonly Regex HeaderRegex = new(
        @"^\[(?<key>\w+)\s+""(?<value>[^""]*)""\]",
        RegexOptions.Compiled);

    public static TakMove ParseMove(string moveStr)
    {
        if (string.IsNullOrWhiteSpace(moveStr))
            throw new ArgumentException("Move string cannot be empty.", nameof(moveStr));

        // Trim annotations and trailing whitespace (*, ?, !, etc.)
        string clean = moveStr.Trim().TrimEnd('*', '!', '?', '#');

        // Check for slide move: contains direction character '+', '-', '>', or '<'
        int dirIndex = clean.IndexOfAny(['+', '-', '>', '<']);
        if (dirIndex >= 0)
        {
            return ParseSlideMove(clean, dirIndex);
        }

        // Placement move
        return ParsePlaceMove(clean);
    }

    private static PlaceMove ParsePlaceMove(string s)
    {
        PieceType type = PieceType.Flat;
        string coordStr = s;

        if (s.Length >= 3 && char.IsLetter(s[1]))
        {
            char prefix = char.ToUpperInvariant(s[0]);
            type = prefix switch
            {
                'S' => PieceType.Standing,
                'C' => PieceType.Capstone,
                'F' => PieceType.Flat,
                _ => PieceType.Flat
            };
            coordStr = s[1..];
        }

        var coord = Coord.FromAlgebraic(coordStr);
        return new PlaceMove(coord, type);
    }

    private static SlideMove ParseSlideMove(string s, int dirIndex)
    {
        char dirChar = s[dirIndex];
        var direction = dirChar switch
        {
            '+' => Direction.North,
            '-' => Direction.South,
            '>' => Direction.East,
            '<' => Direction.West,
            _ => throw new FormatException($"Invalid direction character: {dirChar}")
        };

        string beforeDir = s[..dirIndex];
        string afterDir = s[(dirIndex + 1)..];

        int lift = 1;
        string coordStr;

        if (char.IsDigit(beforeDir[0]))
        {
            lift = beforeDir[0] - '0';
            coordStr = beforeDir[1..];
        }
        else
        {
            coordStr = beforeDir;
        }

        var origin = Coord.FromAlgebraic(coordStr);

        var drops = new List<int>();
        if (string.IsNullOrEmpty(afterDir))
        {
            drops.Add(lift);
        }
        else
        {
            foreach (char c in afterDir)
            {
                if (char.IsDigit(c))
                {
                    drops.Add(c - '0');
                }
            }
        }

        return new SlideMove(origin, direction, lift, drops.ToArray());
    }

    public static string FormatMove(TakMove move) => move.ToPtn();

    public static PtnGame ParseGame(string ptnText)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var moves = new List<TakMove>();
        string? result = null;

        if (string.IsNullOrWhiteSpace(ptnText))
            return new PtnGame(headers, moves, result);

        string[] lines = ptnText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var bodySb = new StringBuilder();

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();

            // Ignore line comments
            if (line.StartsWith(';') || line.StartsWith('#'))
                continue;

            // Match header tags: [Key "Value"]
            var match = HeaderRegex.Match(line);
            if (match.Success)
            {
                headers[match.Groups["key"].Value] = match.Groups["value"].Value;
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                bodySb.Append(' ').Append(line);
            }
        }

        // Remove curly-bracket comments {...}
        string body = Regex.Replace(bodySb.ToString(), @"\{[^}]*\}", " ");

        // Tokenize body
        string[] tokens = body.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

        foreach (string rawToken in tokens)
        {
            string token = rawToken.Trim();

            // Skip move numbers (e.g. "1.", "2.", "15.", "--")
            if (Regex.IsMatch(token, @"^\d+\.+$") || token == "--")
                continue;

            // Check for game results
            if (token is "R-0" or "0-R" or "F-0" or "0-F" or "1-0" or "0-1" or "1/2-1/2")
            {
                result = token;
                continue;
            }

            // Strip leading move number if merged like "1.a1"
            string movePart = Regex.Replace(token, @"^\d+\.+", "");
            if (!string.IsNullOrWhiteSpace(movePart))
            {
                moves.Add(ParseMove(movePart));
            }
        }

        return new PtnGame(headers, moves, result);
    }

    public static string FormatGame(
        GameBoard board,
        IReadOnlyList<TakMove> moves,
        IDictionary<string, string>? customHeaders = null)
    {
        var sb = new StringBuilder();

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Site"] = "Tak P2P",
            ["Size"] = ((int)board.Size).ToString(),
            ["Date"] = DateTime.UtcNow.ToString("yyyy.MM.dd"),
            ["Clock"] = "0",
            ["Result"] = board.Result != null ? FormatResult(board.Result) : "*"
        };

        if (customHeaders != null)
        {
            foreach (var (k, v) in customHeaders)
                headers[k] = v;
        }

        foreach (var (k, v) in headers)
        {
            sb.AppendLine($"[{k} \"{v}\"]");
        }

        sb.AppendLine();

        int turn = 1;
        for (int i = 0; i < moves.Count; i += 2)
        {
            sb.Append($"{turn}. {moves[i].ToPtn()}");
            if (i + 1 < moves.Count)
            {
                sb.Append($" {moves[i + 1].ToPtn()}");
            }
            sb.AppendLine();
            turn++;
        }

        if (board.Result != null)
        {
            sb.AppendLine(FormatResult(board.Result));
        }

        return sb.ToString();
    }

    private static string FormatResult(GameResult result)
    {
        if (result.IsDraw)
            return "1/2-1/2";

        if (result.Reason == GameEndReason.Road)
            return result.Winner == PlayerColor.White ? "R-0" : "0-R";

        if (result.Reason == GameEndReason.FlatCount)
            return result.Winner == PlayerColor.White ? "F-0" : "0-F";

        return result.Winner == PlayerColor.White ? "1-0" : "0-1";
    }
}
