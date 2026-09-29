using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FenCalc2.Data;

namespace FenCalc2.ViewModels;

public partial class DeleteWindowTypeViewModel : ViewModelBase
{
    private readonly CatalogueRepository _catalogueRepo;

    public DeleteWindowTypeViewModel(CatalogueRepository catalogueRepo)
    {
        _catalogueRepo = catalogueRepo;
        LoadRanges();
    }

    [ObservableProperty] private string? _selectedRange;
    [ObservableProperty] private string? _selectedType;
    [ObservableProperty] private bool _dialogResult;

    public ObservableCollection<string> Ranges { get; } = new();
    public ObservableCollection<string> Types { get; } = new();

    private void LoadRanges()
    {
        foreach (var r in _catalogueRepo.GetRanges())
            Ranges.Add(r.WinRange);
    }

    partial void OnSelectedRangeChanged(string? value)
    {
        Types.Clear();
        SelectedType = null;
        if (value is null) return;
        foreach (var t in _catalogueRepo.GetTypesByRange(value))
            Types.Add(t.Name);
    }

    [RelayCommand]
    private void Ok()
    {
        if (SelectedRange is null || SelectedType is null) return;
        _catalogueRepo.DeleteType(SelectedRange, SelectedType);
        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel() => DialogResult = false;
}
