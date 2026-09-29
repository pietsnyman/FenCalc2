using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FenCalc2.ViewModels;

// Help > Getting Started: the user guide. The text lives in Views/GettingStartedView.axaml
// (formatting is easier there than in strings) — this VM only closes the window.
public partial class GettingStartedViewModel : ViewModelBase
{
    [ObservableProperty] private bool _dialogResult;

    // false -> true fires PropertyChanged, so ShowDialogAsync closes the window.
    [RelayCommand]
    private void Close() => DialogResult = true;
}
