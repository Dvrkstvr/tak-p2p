using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using TakEngine.Abstractions;
using TakEngine.Core.Session;
using TakEngine.Crypto;

namespace TakApp.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial ViewModelBase CurrentView { get; set; }

    public MainViewModel()
    {
        CurrentView = new NewGameViewModel(OnStartGame);
    }

    private void OnStartGame(BoardSize size, bool isRemote)
    {
        ITakGameSession session;
        if (isRemote)
        {
            // Simulated remote mode (two throwaway keys in one process) until M6 replaces it.
            var localKey = SecretKey.Generate(() => RandomNumberGenerator.GetBytes(SecretKey.Length));
            var opponentKey = SecretKey.Generate(() => RandomNumberGenerator.GetBytes(SecretKey.Length));
            session = TakGameSession.CreateRemote(GameId.New(), size, PlayerColor.White, localKey, opponentKey.PublicKey);
        }
        else
        {
            session = TakGameSession.CreateLocal(size);
        }

        CurrentView = new GameViewModel(session, () =>
        {
            CurrentView = new NewGameViewModel(OnStartGame);
        });
    }
}
