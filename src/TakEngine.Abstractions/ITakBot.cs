using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TakEngine.Abstractions;

public interface ITakBot
{
    BotDifficulty Difficulty { get; }
    TakMove SelectMove(TakBoardSnapshot snapshot, IReadOnlyList<TakMove> legalMoves);
    Task<TakMove> SelectMoveAsync(TakBoardSnapshot snapshot, IReadOnlyList<TakMove> legalMoves, CancellationToken cancellationToken = default);
}
