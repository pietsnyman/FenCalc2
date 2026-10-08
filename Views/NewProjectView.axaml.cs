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

    // Same push for Project Name — a one-way binding alone leaves ProjectName empty
    // and OK reports "all fields are required" even though the box shows text.
    private void OnProjectBoxTextChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewProjectViewModel vm && sender is AutoCompleteBox box)
            vm.ProjectName = box.Text ?? string.Empty;
    }

    // Site Town (2026): push so the VM's OnTownNameChanged can look the town up
    // (energy zone, latitude, SCCP) as the user types or picks a suggestion.
    private void OnTownBoxTextChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewProjectViewModel vm && sender is AutoCompleteBox box)
            vm.TownName = box.Text ?? string.Empty;
    }

    // VM Cancel sets DialogResult=false, but it is already false, so no PropertyChanged
    // fires and ShowDialogAsync wouldn't close the window — close directly from the view.
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
