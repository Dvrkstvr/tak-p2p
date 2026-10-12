using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

    // Placement: optional piece prefix + square. Slide: optional lift count + square + direction + optional drop counts.
    private static readonly Regex PlaceRegex = new(@"^(?<type>[FSC])?(?<square>[a-h][1-8])$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SlideRegex = new(
        @"^(?<lift>[1-8])?(?<square>[a-h][1-8])(?<dir>[+\-<>])(?<drops>[1-8]*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses one PTN move. Tak marks (<c>'</c>, <c>''</c>, <c>"</c>) and annotations (<c>!</c>, <c>?</c>, <c>*</c>, <c>#</c>)
    /// are stripped. Anything else that is not a well-formed move (unknown piece prefix, a lift count that differs from the
    /// sum of the drops, a zero count) throws <see cref="FormatException"/>.
    /// </summary>
    public static TakMove ParseMove(string moveStr)
    {
        if (string.IsNullOrWhiteSpace(moveStr))
            throw new ArgumentException("Move string cannot be empty.", nameof(moveStr));

        string clean = moveStr.Trim().TrimEnd('*', '!', '?', '#', '\'', '"');

        Match place = PlaceRegex.Match(clean);
        if (place.Success)
        {
            PieceType type = place.Groups["type"].Success
                ? char.ToUpperInvariant(place.Groups["type"].Value[0]) switch
                {
                    'S' => PieceType.Standing,
                    'C' => PieceType.Capstone,
                    _ => PieceType.Flat
                }
                : PieceType.Flat;
            return new PlaceMove(Coord.FromAlgebraic(place.Groups["square"].Value), type);
        }

        Match slide = SlideRegex.Match(clean);
        if (!slide.Success)
            throw new FormatException($"Not a valid PTN move: '{moveStr}'.");

        int lift = slide.Groups["lift"].Success ? slide.Groups["lift"].Value[0] - '0' : 1;
        var direction = slide.Groups["dir"].Value[0] switch
        {
            '+' => Direction.North,
            '-' => Direction.South,
            '>' => Direction.East,
            _ => Direction.West
        };

        string dropDigits = slide.Groups["drops"].Value;
        int[] drops = dropDigits.Length == 0 ? [lift] : dropDigits.Select(c => c - '0').ToArray();
        if (drops.Sum() != lift)
            throw new FormatException($"Drops in '{moveStr}' add up to {drops.Sum()}, but the lift count is {lift}.");

        return new SlideMove(Coord.FromAlgebraic(slide.Groups["square"].Value), direction, lift, drops);
    }

    public static string FormatMove(TakMove move) => move.ToPtn();

    /// <summary>Parses a PTN <c>Komi</c> header ("2", "2.5") into half flats; anything but a whole or half number throws.</summary>
    public static int ParseKomiHalves(string value)
    {
        Match m = Regex.Match(value?.Trim() ?? "", @"^(?<whole>\d{1,2})(?:\.(?<frac>[05]))?$");
        if (!m.Success)
            throw new FormatException($"Komi '{value}' is not a whole or half number of flats.");

        int halves = 2 * int.Parse(m.Groups["whole"].Value, CultureInfo.InvariantCulture) + (m.Groups["frac"].Value == "5" ? 1 : 0);
        if (halves > GameBoard.MaxKomiHalves)
            throw new FormatException($"Komi '{value}' is larger than {GameBoard.MaxKomiHalves / 2} flats.");
        return halves;
    }

    /// <summary>Formats half flats as a PTN <c>Komi</c> value: 4 -> "2", 5 -> "2.5".</summary>
    public static string FormatKomi(int komiHalves) =>
        komiHalves % 2 == 0
            ? (komiHalves / 2).ToString(CultureInfo.InvariantCulture)
            : (komiHalves / 2).ToString(CultureInfo.InvariantCulture) + ".5";

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
            if (token is "R-0" or "0-R" or "F-0" or "0-F" or "1-0" or "0-1" or "1/2-1/2" or "0-0")
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
        if (board.KomiHalves > 0)
            headers["Komi"] = FormatKomi(board.KomiHalves);

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
