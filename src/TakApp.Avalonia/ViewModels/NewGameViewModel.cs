using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TakEngine.Abstractions;

namespace TakApp.Avalonia.ViewModels;

public partial class NewGameViewModel : ViewModelBase
{
    private readonly Action<BoardSize, bool> _onStartGame;

    public ObservableCollection<BoardSize> AvailableSizes { get; } = new()
    {
        BoardSize.Four,
        BoardSize.Five,
        BoardSize.Six
    };

    [ObservableProperty]
    public partial BoardSize SelectedSize { get; set; } = BoardSize.Five;

    [ObservableProperty]
    public partial bool IsRemoteMatch { get; set; } = false;

    public NewGameViewModel(Action<BoardSize, bool> onStartGame)
    {
        _onStartGame = onStartGame;
    }

    [RelayCommand]
    private void StartLocalGame()
    {
        _onStartGame(SelectedSize, false);
    }

    [RelayCommand]
    private void StartRemoteGame()
    {
        _onStartGame(SelectedSize, true);
    }
}
