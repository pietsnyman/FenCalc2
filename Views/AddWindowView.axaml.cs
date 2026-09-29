using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FenCalc2.Views;

public partial class AddWindowView : Window
{
    public AddWindowView()
    {
        InitializeComponent();
    }

    // VM Cancel sets DialogResult=false, but it is already false, so no PropertyChanged
    // fires and ShowDialogAsync wouldn't close the window — close directly from the view.
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
