using Avalonia.Controls;
using Avalonia.Interactivity;
using FenCalc2.ViewModels;

namespace FenCalc2.Views;

public partial class NewProjectView : Window
{
    public NewProjectView()
    {
        InitializeComponent();
    }

    // AutoCompleteBox TextChanged: push the raw text into the VM explicitly (binding is
    // one-way here) and refresh the project-suggestion list for the typed client.
    private void OnClientBoxTextChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewProjectViewModel vm && sender is AutoCompleteBox box)
        {
            vm.ClientName = box.Text ?? string.Empty;
            vm.OnClientTextChanged();
        }
    }

    // VM Cancel sets DialogResult=false, but it is already false, so no PropertyChanged
    // fires and ShowDialogAsync wouldn't close the window — close directly from the view.
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
