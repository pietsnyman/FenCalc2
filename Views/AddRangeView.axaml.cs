using Avalonia.Interactivity;
using Avalonia.Controls;
namespace FenCalc2.Views;
public partial class AddRangeView : Window
{
    public AddRangeView() { InitializeComponent(); }

    // VM Cancel sets DialogResult=false, but it is already false, so no PropertyChanged
    // fires and ShowDialogAsync wouldn't close the window — close directly from the view.
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}

