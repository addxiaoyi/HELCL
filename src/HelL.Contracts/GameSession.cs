using System;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Contracts;

public enum GameState { Preparing, Ready, Running, Stopping, Exited, Failed }

public sealed class GameSession
{
    public string Id { get; }
    public GameState State { get; }
    public int? ExitCode { get; }
    public string Message { get; }

    public GameSession(string id, GameState state, int? exitCode = null, string message = "")
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A session ID is required.", nameof(id));
        if (!Enum.IsDefined(typeof(GameState), state)) throw new ArgumentOutOfRangeException(nameof(state));
        if (exitCode.HasValue && state != GameState.Exited && state != GameState.Failed)
            throw new ArgumentException("Only a finished session can have an exit code.", nameof(exitCode));
        Id = id;
        State = state;
        ExitCode = exitCode;
        Message = message;
    }
}

public interface IGameSession
{
    GameSession Snapshot { get; }
    event Action<GameSession> Changed;
    event Action<string> Log;
    Task StopAsync(CancellationToken cancellation);
}
