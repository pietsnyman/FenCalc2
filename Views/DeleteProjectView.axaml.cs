using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FenCalc2.Views;

public partial class DeleteProjectView : Window
{
    public DeleteProjectView()
    {
        InitializeComponent();
    }

    // VM Cancel sets DialogResult=false (already false → no PropertyChanged) — close directly.
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
