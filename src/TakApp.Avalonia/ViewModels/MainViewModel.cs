using CommunityToolkit.Mvvm.ComponentModel;
using TakEngine.Abstractions;
using TakEngine.Core.Cryptography;
using TakEngine.Core.Session;

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
            var (pubA, privA) = CryptoSigner.GenerateKeyPair();
            var (pubB, privB) = CryptoSigner.GenerateKeyPair();
            session = TakGameSession.CreateRemote(GameId.New(), size, PlayerColor.White, privA, pubB);
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
