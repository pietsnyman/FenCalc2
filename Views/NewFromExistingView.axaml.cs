using Avalonia.Controls;
using Avalonia.Interactivity;
using FenCalc2.ViewModels;

namespace FenCalc2.Views;

public partial class NewFromExistingView : Window
{
    public NewFromExistingView()
    {
        InitializeComponent();
    }

    // Destination AutoCompleteBoxes are one-way bound (NewProjectView pattern): push
    // the typed text back into the VM so brand-new client/project names reach Ok().
    private void OnDestClientTextChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewFromExistingViewModel vm && sender is AutoCompleteBox box)
            vm.DestClient = box.Text;
    }

    private void OnDestProjectTextChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewFromExistingViewModel vm && sender is AutoCompleteBox box)
            vm.DestProject = box.Text;
    }

    // VM Cancel sets DialogResult=false (already false → no PropertyChanged) — close directly.
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
