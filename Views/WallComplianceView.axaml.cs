using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using FenCalc2.ViewModels;

namespace FenCalc2.Views;

// The walls page: a UserControl hosted in MainWindow's "External Walls" tab (it was a
// modal window until the tab conversion — everything below is element-relative, so the
// drag and clipboard code needed no changes).
public partial class WallComplianceView : UserControl
{
    private WallLayerRow? _dragRow;

    public WallComplianceView()
    {
        InitializeComponent();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WallComplianceViewModel vm
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(vm.OutputText);
            vm.StatusMessage = "Report copied to clipboard.";
        }
    }

    // ---- drag-to-reorder (started on the ⠿ handle only) -------------------------
    // Row-level commands and the drag live here: a DataTemplate's DataContext is the
    // row, and compiled binding paths cannot reach the window's commands from it.

    private void OnHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control c && c.DataContext is WallLayerRow row)
        {
            _dragRow = row;
            e.Pointer.Capture(c);
        }
    }

    private void OnHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragRow is null || DataContext is not WallComplianceViewModel vm) return;
        if (LayersList.ContainerFromIndex(0) is not { } first) return;
        var rowHeight = first.Bounds.Height;
        if (rowHeight <= 0) return;

        // Position relative to the list; rows are uniform, so the index is a division.
        var y = e.GetPosition(LayersList).Y;
        var target = Math.Clamp((int)(y / rowHeight), 0, vm.Layers.Count - 1);
        var from = vm.Layers.IndexOf(_dragRow);
        if (from >= 0 && target != from)
            vm.MoveLayer(from, target);
    }

    private void OnHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragRow is null) return;
        e.Pointer.Capture(null);
        _dragRow = null;
    }

    private void OnRowMoveUpClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WallComplianceViewModel vm && sender is Control c && c.DataContext is WallLayerRow row)
            vm.MoveRow(row, -1);
    }

    private void OnRowMoveDownClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WallComplianceViewModel vm && sender is Control c && c.DataContext is WallLayerRow row)
            vm.MoveRow(row, +1);
    }

    // Preset chips render strings, so their click is handled here too (the command
    // lives on the window DataContext, not on the string item).
    private void OnPresetClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WallComplianceViewModel vm && sender is Button b && b.Content is string preset)
            vm.SetThicknessCommand.Execute(preset);
    }
}
