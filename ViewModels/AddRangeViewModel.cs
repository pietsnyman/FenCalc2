using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FenCalc2.Data;

namespace FenCalc2.ViewModels;

public partial class AddRangeViewModel : ViewModelBase
{
    private readonly CatalogueRepository _catalogueRepo;

    public AddRangeViewModel(CatalogueRepository catalogueRepo)
    {
        _catalogueRepo = catalogueRepo;
    }

    [ObservableProperty] private string _rangeName = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _dialogResult;

    [RelayCommand]
    private void Ok()
    {
        if (string.IsNullOrWhiteSpace(RangeName))
        {
            ErrorMessage = "Range name is required.";
            return;
        }
        _catalogueRepo.InsertRange(RangeName.Trim());
        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel() => DialogResult = false;
}
