using System;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Cryptography;
using TakEngine.Core.Serialization;
using TakEngine.Core.Session;
using TakEngine.Crypto;
using Xunit;

namespace TakEngine.Core.Tests;

/// <summary>F-057: engine edge cases that resume (restore from a snapshot) and tamper rejection depend on.</summary>
public class RulesEdgeCaseTests
{
    [Fact]
    public void FromSnapshot_AfterTheOpeningSwap_IsInPlayingPhase()
    {
        var board = new GameBoard(BoardSize.Five);
        Assert.True(board.Place(new Coord(0, 0), PieceType.Flat).IsSuccess);
        Assert.True(board.Place(new Coord(4, 4), PieceType.Flat).IsSuccess);
        Assert.Equal(2, board.TurnNumber);

        var restored = GameBoard.FromSnapshot(board.ToSnapshot());

        Assert.Equal(GamePhase.Playing, restored.Phase);
        Assert.True(restored.Place(new Coord(2, 2), PieceType.Flat).IsSuccess);
        Assert.Equal(PlayerColor.White, restored.GetStack(new Coord(2, 2)).TopPiece!.Value.Color); // no swap any more
    }

    [Fact]
    public void FromSnapshot_DuringTheOpening_KeepsTheSwap()
    {
        var board = new GameBoard(BoardSize.Five);
        Assert.True(board.Place(new Coord(0, 0), PieceType.Flat).IsSuccess);

        var restored = GameBoard.FromSnapshot(board.ToSnapshot());

        Assert.Equal(GamePhase.FirstTurnPlacement, restored.Phase);
        Assert.True(restored.Place(new Coord(4, 4), PieceType.Flat).IsSuccess);
        Assert.Equal(PlayerColor.White, restored.GetStack(new Coord(4, 4)).TopPiece!.Value.Color); // Black placed White's flat
    }

    [Fact]
    public void FromSnapshot_OfAFinishedGame_KeepsTheResult()
    {
        var board = new GameBoard(BoardSize.Five);
        Assert.True(board.Place(new Coord(0, 0), PieceType.Flat).IsSuccess);
        Assert.True(board.Resign(PlayerColor.Black).IsSuccess);

        var restored = GameBoard.FromSnapshot(board.ToSnapshot());

        Assert.Equal(GamePhase.Completed, restored.Phase);
        Assert.Equal(board.Result, restored.Result);
        Assert.False(restored.Place(new Coord(2, 2), PieceType.Flat).IsSuccess);
    }

    [Fact]
    public void Slide_WhoseLiftCountDisagreesWithTheDrops_IsRejectedAndChangesNothing()
    {
        // a1 holds a 3-stack (White, Black, White on top), White to move. Built from TPS so the stack is exact.
        var board = TpsSerializer.Deserialize("x5/x5/x5/x5/121,x4 1 5");
        string before = TpsSerializer.Serialize(board);

        var result = board.Execute(new SlideMove(new Coord(0, 0), Direction.East, 2, [1, 1, 1]));

        Assert.False(result.IsSuccess);
        Assert.Equal(before, TpsSerializer.Serialize(board));
    }

    [Theory]
    [InlineData("2a1>111")] // lift 2, drops sum to 3
    [InlineData("3a1>11")]  // lift 3, drops sum to 2
    [InlineData("Xa1")]     // unknown piece prefix
    [InlineData("a1>1x")]   // garbage in the drop list
    [InlineData("0a1>")]    // lift of zero
    [InlineData("a1>0")]    // drop of zero
    public void ParseMove_RejectsMalformedMoves(string ptn)
    {
        Assert.Throws<FormatException>(() => PtnParser.ParseMove(ptn));
    }

    [Theory]
    [InlineData("a1'", "a1")]
    [InlineData("Sc3''", "Sc3")]
    [InlineData("3c3+12'", "3c3+12")]
    [InlineData("c3+'!?", "c3+")]
    [InlineData("Ce5\"", "Ce5")]
    public void ParseMove_StripsTakMarksAndAnnotations(string ptn, string canonical)
    {
        Assert.Equal(canonical, PtnParser.ParseMove(ptn).ToPtn());
    }

    [Fact]
    public void ParseGame_RecognisesTheNoResultToken()
    {
        var game = PtnParser.ParseGame("[Size \"5\"]\n\n1. a1 e5\n0-0");

        Assert.Equal("0-0", game.Result);
        Assert.Equal(2, game.Moves.Count);
    }

    [Fact]
    public void RemoteMove_InNonCanonicalPtn_IsAViolationAndChangesNothing()
    {
        SecretKey alice = TestKeys.Create(1);
        SecretKey bob = TestKeys.Create(2);
        var bobSession = TakGameSession.CreateRemote(GameId.New(), BoardSize.Five, PlayerColor.Black, bob, alice.PublicKey);
        ProtocolViolationException? violation = null;
        bobSession.OnProtocolViolationDetected += ex => violation = ex;
        string hashBefore = bobSession.CurrentStateHash;

        // "Fa1" is a legal spelling of "a1", but the chain hashes the PTN string, so two spellings of one move would
        // give the two peers different hashes. Only the canonical form is accepted from a peer.
        string sig = PayloadSignature.Sign(alice, $"{hashBefore}:Fa1");
        var result = bobSession.ProcessRemoteMove(alice.PublicKey.ToHex(), hashBefore, "Fa1", sig);

        Assert.False(result.IsSuccess);
        Assert.NotNull(violation);
        Assert.Equal(hashBefore, bobSession.CurrentStateHash);
        Assert.Empty(bobSession.CurrentBoard.Stacks);
    }
}
