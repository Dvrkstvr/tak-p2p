using System;
using System.Collections.Generic;
using TakEngine.Abstractions;
using TakEngine.Core.Session;
using TakEngine.Crypto;
using Xunit;

namespace TakEngine.Core.Tests;

public class TakGameSessionTests
{
    [Fact]
    public void CreateLocal_InitializesCorrectDefaultState()
    {
        var session = TakGameSession.CreateLocal(BoardSize.Five);

        Assert.NotEqual(Guid.Empty, session.Id.Value);
        Assert.Equal(BoardSize.Five, session.Size);
        Assert.Equal(PlayerColor.White, session.LocalColor);
        Assert.Equal(GamePhase.FirstTurnPlacement, session.CurrentPhase);
        Assert.NotNull(session.CurrentBoard);
        Assert.NotEmpty(session.CurrentStateHash);
    }

    [Fact]
    public void LocalGame_FirstTurnSwapRule_PlacesOpponentStone()
    {
        var session = TakGameSession.CreateLocal(BoardSize.Five);
        bool moveExecuted = false;
        session.OnMoveExecuted += (snapshot, move) => moveExecuted = true;

        // Turn 1, Move 1: White places a Flat at a1 (places Black piece per swap rule)
        var res1 = session.SubmitPlacement(new Coord(0, 0), PieceType.Flat);
        Assert.True(res1.IsSuccess);
        Assert.True(moveExecuted);

        var snap1 = session.CurrentBoard;
        var stackA1 = snap1.Stacks[new Coord(0, 0)];
        Assert.Equal(PlayerColor.Black, stackA1.TopPiece!.Value.Color);
        Assert.Equal(PlayerColor.Black, snap1.ActivePlayer);

        // Turn 1, Move 2: Black places a Flat at a2 (places White piece per swap rule)
        var res2 = session.SubmitPlacement(new Coord(0, 1), PieceType.Flat);
        Assert.True(res2.IsSuccess);

        var snap2 = session.CurrentBoard;
        var stackA2 = snap2.Stacks[new Coord(0, 1)];
        Assert.Equal(PlayerColor.White, stackA2.TopPiece!.Value.Color);
        Assert.Equal(GamePhase.Playing, session.CurrentPhase);
        Assert.Equal(PlayerColor.White, snap2.ActivePlayer);
    }

    [Fact]
    public void GetLegalMovesForSquare_ReturnsCorrectMoves()
    {
        var session = TakGameSession.CreateLocal(BoardSize.Five);

        // First turn: only Flat allowed on empty square
        var emptyMoves = session.GetLegalMovesForSquare(new Coord(2, 2));
        Assert.Single(emptyMoves);
        Assert.IsType<PlaceMove>(emptyMoves[0]);
        Assert.Equal(PieceType.Flat, ((PlaceMove)emptyMoves[0]).PieceType);

        // Advance past first turns
        session.SubmitPlacement(new Coord(0, 0), PieceType.Flat);
        session.SubmitPlacement(new Coord(0, 1), PieceType.Flat);

        // Now in Playing phase: empty square should allow Flat, Standing, and Capstone
        var playingEmptyMoves = session.GetLegalMovesForSquare(new Coord(2, 2));
        Assert.Equal(3, playingEmptyMoves.Count);

        // Occupied square owned by opponent (a1 has Black piece, current active is White)
        var opponentMoves = session.GetLegalMovesForSquare(new Coord(0, 0));
        Assert.Empty(opponentMoves);

        // Occupied square owned by active player (a2 has White piece)
        var ownedMoves = session.GetLegalMovesForSquare(new Coord(0, 1));
        Assert.NotEmpty(ownedMoves);
    }

    [Fact]
    public void Resign_EndsGameAndFiresEvent()
    {
        var session = TakGameSession.CreateLocal(BoardSize.Five);
        GameResult? endResult = null;
        session.OnGameEnded += (snapshot, result) => endResult = result;

        var res = session.Resign();
        Assert.True(res.IsSuccess);
        Assert.Equal(GamePhase.Completed, session.CurrentPhase);
        Assert.NotNull(endResult);
        Assert.False(endResult.IsDraw);
        Assert.Equal(PlayerColor.Black, endResult.Winner);
        Assert.Equal(GameEndReason.Resignation, endResult.Reason);
    }

    [Fact]
    public void LocalGame_SlideMove_LiftsAndDropsCorrectly()
    {
        var session = TakGameSession.CreateLocal(BoardSize.Five);

        // Turn 1 swap
        session.SubmitPlacement(new Coord(0, 0), PieceType.Flat); // Black at a1
        session.SubmitPlacement(new Coord(0, 1), PieceType.Flat); // White at a2

        // Turn 2: White moves a2 North to a3
        var slideRes = session.SubmitMove(new Coord(0, 1), Direction.North, new[] { 1 });
        Assert.True(slideRes.IsSuccess);

        var snap = session.CurrentBoard;
        Assert.False(snap.Stacks.ContainsKey(new Coord(0, 1)));
        Assert.True(snap.Stacks.ContainsKey(new Coord(0, 2)));
        Assert.Equal(PlayerColor.White, snap.Stacks[new Coord(0, 2)].TopPiece!.Value.Color);
    }

    [Fact]
    public void LocalGame_RoadVictory_FiresGameEndedWithWinner()
    {
        var session = TakGameSession.CreateLocal(BoardSize.Four);
        GameResult? gameEndResult = null;
        session.OnGameEnded += (snap, result) => gameEndResult = result;

        // Turn 1 swap
        session.SubmitPlacement(new Coord(3, 3), PieceType.Flat); // Black at d4
        session.SubmitPlacement(new Coord(0, 0), PieceType.Flat); // White at a1

        // Build White vertical road on column b: b1, b2, b3, b4
        // Turn 2:
        session.SubmitPlacement(new Coord(1, 0), PieceType.Flat); // White at b1
        session.SubmitPlacement(new Coord(2, 0), PieceType.Flat); // Black at c1
        // Turn 3:
        session.SubmitPlacement(new Coord(1, 1), PieceType.Flat); // White at b2
        session.SubmitPlacement(new Coord(2, 1), PieceType.Flat); // Black at c2
        // Turn 4:
        session.SubmitPlacement(new Coord(1, 2), PieceType.Flat); // White at b3
        session.SubmitPlacement(new Coord(2, 2), PieceType.Flat); // Black at c3
        // Turn 5:
        session.SubmitPlacement(new Coord(1, 3), PieceType.Flat); // White at b4 -> ROAD COMPLETED!

        Assert.NotNull(gameEndResult);
        Assert.False(gameEndResult.IsDraw);
        Assert.Equal(PlayerColor.White, gameEndResult.Winner);
        Assert.Equal(GameEndReason.Road, gameEndResult.Reason);
        Assert.Equal(GamePhase.Completed, session.CurrentPhase);
    }

    [Fact]
    public void LocalGame_StaleWarning_FiresEvent()
    {
        var session = TakGameSession.CreateLocal(BoardSize.Five);
        TimeSpan? warningSpan = null;
        session.OnStaleWarning += span => warningSpan = span;

        session.NotifyStaleWarning(TimeSpan.FromDays(4));

        Assert.NotNull(warningSpan);
        Assert.Equal(4, warningSpan.Value.Days);
    }
}
