using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FenCalc2.Data;

namespace FenCalc2.ViewModels;

public partial class DeleteRangeViewModel : ViewModelBase
{
    private readonly CatalogueRepository _catalogueRepo;

    public DeleteRangeViewModel(CatalogueRepository catalogueRepo)
    {
        _catalogueRepo = catalogueRepo;
        LoadRanges();
    }

    [ObservableProperty] private string? _selectedRange;
    [ObservableProperty] private bool _dialogResult;

    public ObservableCollection<string> Ranges { get; } = new();

    private void LoadRanges()
    {
        foreach (var r in _catalogueRepo.GetRanges())
            Ranges.Add(r.WinRange);
    }

    [RelayCommand]
    private void Ok()
    {
        if (SelectedRange is null) return;
        _catalogueRepo.DeleteRange(SelectedRange);
        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel() => DialogResult = false;
}
