using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FenCalc2.Data;
using FenCalc2.ViewModels;

namespace FenCalc2.Views;

public partial class MainWindow : Window
{
    private bool _subscribed;

    public MainWindow()
    {
        InitializeComponent();

        var floorAreaBox = this.FindControl<TextBox>("FloorAreaBox");
        if (floorAreaBox is not null)
            floorAreaBox.LostFocus += OnFloorAreaLostFocus;

        var selWidthBox = this.FindControl<TextBox>("SelWidthBox");
        if (selWidthBox is not null)
            selWidthBox.LostFocus += OnEditorSizeLostFocus;

        var selHeightBox = this.FindControl<TextBox>("SelHeightBox");
        if (selHeightBox is not null)
            selHeightBox.LostFocus += OnEditorSizeLostFocus;

        // Theme submenu: reflect the variant App.axaml.cs applied from AppSettings
        var variant = Application.Current?.RequestedThemeVariant;
        ThemeLightItem.IsChecked = variant == ThemeVariant.Light;
        ThemeDarkItem.IsChecked = variant == ThemeVariant.Dark;
        ThemeSystemItem.IsChecked = variant is null || variant == ThemeVariant.Default;
        ThemeOliveItem.IsChecked = variant == AppThemes.Olive;
        ThemeBurntItem.IsChecked = variant == AppThemes.BurntOrange;
        ThemeMulberryItem.IsChecked = variant == AppThemes.Mulberry;
        ThemeSageItem.IsChecked = variant == AppThemes.Sage;
    }

    // View > Theme: switch the variant live and persist it (App.axaml.cs re-applies it
    // on next launch). Items are checkable, so re-assert the radio group after the
    // MenuItem's own toggle and re-tint bindings the theme system cannot reach.
    private void OnThemeLightClick(object? sender, RoutedEventArgs e) => SetTheme("Light");

    private void OnThemeDarkClick(object? sender, RoutedEventArgs e) => SetTheme("Dark");

    private void OnThemeSystemClick(object? sender, RoutedEventArgs e) => SetTheme("Default");

    private void OnThemeOliveClick(object? sender, RoutedEventArgs e) => SetTheme(AppThemes.OliveName);

    private void OnThemeBurntClick(object? sender, RoutedEventArgs e) => SetTheme(AppThemes.BurntOrangeName);

    private void OnThemeMulberryClick(object? sender, RoutedEventArgs e) => SetTheme(AppThemes.MulberryName);

    private void OnThemeSageClick(object? sender, RoutedEventArgs e) => SetTheme(AppThemes.SageName);

    // Project > Exit: closing the main window ends the app (classic desktop lifetime).
    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private void SetTheme(string mode)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = mode switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => AppThemes.FromName(mode) ?? ThemeVariant.Default,
            };
        }

        new ProjectRepository(Database.CreateConnection).SetSetting("Theme", mode);

        ThemeLightItem.IsChecked = mode == "Light";
        ThemeDarkItem.IsChecked = mode == "Dark";
        ThemeSystemItem.IsChecked = mode == "Default";
        ThemeOliveItem.IsChecked = mode == AppThemes.OliveName;
        ThemeBurntItem.IsChecked = mode == AppThemes.BurntOrangeName;
        ThemeMulberryItem.IsChecked = mode == AppThemes.MulberryName;
        ThemeSageItem.IsChecked = mode == AppThemes.SageName;

        (DataContext as MainViewModel)?.RefreshThemedBrushes();
    }

    // App creates the window via an object initializer (DataContext assigned after the
    // constructor runs), so subscriptions must react to DataContext instead of the ctor.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == DataContextProperty && !_subscribed && DataContext is MainViewModel vm)
        {
            _subscribed = true;
            vm.ShowAddWindowDialog += OnShowAddWindowDialog;
            vm.ShowNewProjectDialog += OnShowNewProjectDialog;
            vm.ShowOpenProjectDialog += OnShowOpenProjectDialog;
            vm.ShowNewFromExistingDialog += OnShowNewFromExistingDialog;
            vm.ShowDeleteProjectDialog += OnShowDeleteProjectDialog;
            vm.ShowDuplicateFloorDialog += OnShowDuplicateFloorDialog;
            vm.ShowRenameFloorDialog += OnShowRenameFloorDialog;
            vm.ShowDeleteFloorDialog += OnShowDeleteFloorDialog;
            vm.ShowAboutDialog += OnShowAboutDialog;
            vm.ShowGettingStartedDialog += OnShowGettingStartedDialog;
            vm.ShowCopyWindowDialog += OnShowCopyWindowDialog;
            vm.ShowAddRangeDialog += OnShowAddRangeDialog;
            vm.ShowDeleteRangeDialog += OnShowDeleteRangeDialog;
            vm.ShowAddWindowTypeDialog += OnShowAddWindowTypeDialog;
            vm.ShowDeleteWindowTypeDialog += OnShowDeleteWindowTypeDialog;
            vm.RequestCopyToClipboard += OnRequestCopyToClipboard;
            vm.RequestLoadFloorImage += OnLoadFloorImage;
            vm.PropertyChanged += OnVmPropertyChanged;
            vm.CheckFirstLaunch();
        }
    }

    private async void OnShowAddWindowDialog(AddWindowView dialog, AddWindowViewModel vm)
    {
        await ShowDialogAsync(dialog, vm);
        if (DataContext is MainViewModel mainVm)
            mainVm.OnAddWindowDialogClosed(vm);
    }

    private async void OnShowNewProjectDialog(NewProjectViewModel vm)
    {
        var dialog = new NewProjectView { DataContext = vm };
        await ShowDialogAsync(dialog, vm);
        if (DataContext is MainViewModel mainVm)
            mainVm.OnNewProjectDialogClosed(vm);
    }

    private async void OnShowOpenProjectDialog(OpenProjectViewModel vm)
    {
        var dialog = new OpenProjectView { DataContext = vm };
        await ShowDialogAsync(dialog, vm);
        if (DataContext is MainViewModel mainVm)
            mainVm.OnOpenProjectDialogClosed(vm);
    }

    private async void OnShowNewFromExistingDialog(NewFromExistingViewModel vm)
    {
        var dialog = new NewFromExistingView { DataContext = vm };
        await ShowDialogAsync(dialog, vm);
        if (DataContext is MainViewModel mainVm)
            mainVm.OnNewFromExistingDialogClosed(vm);
    }

    private async void OnShowDeleteProjectDialog(DeleteProjectViewModel vm)
    {
        var dialog = new DeleteProjectView { DataContext = vm };
        await ShowDialogAsync(dialog, vm);
        if (DataContext is MainViewModel mainVm)
            mainVm.OnDeleteProjectDialogClosed(vm);
    }

    private async void OnShowDuplicateFloorDialog(DuplicateFloorViewModel vm)
    {
        var dialog = new DuplicateFloorView { DataContext = vm };
        await ShowDialogAsync(dialog, vm);
        if (DataContext is MainViewModel mainVm)
            mainVm.OnDuplicateFloorDialogClosed(vm);
    }

    private async void OnShowRenameFloorDialog(RenameFloorViewModel vm)
    {
        var dialog = new RenameFloorView { DataContext = vm };
        await ShowDialogAsync(dialog, vm);
        if (DataContext is MainViewModel mainVm)
            mainVm.OnRenameFloorDialogClosed(vm);
    }

    private async void OnShowDeleteFloorDialog(DeleteFloorViewModel vm)
    {
        var dialog = new DeleteFloorView { DataContext = vm };
        await ShowDialogAsync(dialog, vm);
        if (DataContext is MainViewModel mainVm)
            mainVm.OnDeleteFloorDialogClosed(vm);
    }

    // Help > About — pure info dialog, so no closed-callback (nothing to apply).
    private async void OnShowAboutDialog(AboutViewModel vm)
    {
        var dialog = new AboutView { DataContext = vm };
        await ShowDialogAsync(dialog, vm);
    }

    // Help > Getting Started — same as About: static content, no callback.
    private async void OnShowGettingStartedDialog(GettingStartedViewModel vm)
    {
        var dialog = new GettingStartedView { DataContext = vm };
        await ShowDialogAsync(dialog, vm);
    }

    private async void OnShowCopyWindowDialog(CopyWindowViewModel vm)
    {
        var dialog = new CopyWindowView { DataContext = vm };
        await ShowDialogAsync(dialog, vm);
        if (DataContext is MainViewModel mainVm)
            mainVm.OnCopyWindowDialogClosed(vm);
    }

    private async void OnShowAddRangeDialog(AddRangeViewModel vm) =>
        await ShowDialogAsync(new AddRangeView { DataContext = vm }, vm);

    private async void OnShowDeleteRangeDialog(DeleteRangeViewModel vm) =>
        await ShowDialogAsync(new DeleteRangeView { DataContext = vm }, vm);

    private async void OnShowAddWindowTypeDialog(AddWindowTypeViewModel vm) =>
        await ShowDialogAsync(new AddWindowTypeView { DataContext = vm }, vm);

    private async void OnShowDeleteWindowTypeDialog(DeleteWindowTypeViewModel vm) =>
        await ShowDialogAsync(new DeleteWindowTypeView { DataContext = vm }, vm);

    // Dialogs signal completion by setting DialogResult; close the window when it changes
    // so the awaited ShowDialog returns and the closed-callbacks can run.
    private async Task ShowDialogAsync(Window dialog, ViewModelBase vm)
    {
        void OnDialogPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "DialogResult")
                dialog.Close();
        }

        vm.PropertyChanged += OnDialogPropertyChanged;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            vm.PropertyChanged -= OnDialogPropertyChanged;
        }
    }

    private void CompassTile_Tapped(object? sender, TappedEventArgs e)
    {
        // Taps that land on a listed window (inside a ListBoxItem) select that window;
        // taps on the empty part of a tile open the Add Window dialog.
        if (e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(true) is not null)
            return;

        if (sender is Border { Tag: string slot } && DataContext is MainViewModel vm)
        {
            // The dialog pre-selects the tile's current TRUE-direction label
            vm.AddWindowCommand.Execute(vm.LabelForSlot(slot));
        }
    }

    private void OnLoadFloorImage() => PickFloorPlanImage();

    private void FloorPlanBox_Tapped(object? sender, TappedEventArgs e) => PickFloorPlanImage();

    private async void PickFloorPlanImage()
    {
        if (DataContext is not MainViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storage) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select floor plan image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" }
                }
            }
        });

        if (files.Count > 0)
            vm.SetFloorPlanImagePath(files[0].TryGetLocalPath());
    }

    private void OnRequestCopyToClipboard()
    {
        if (DataContext is not MainViewModel vm) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            _ = clipboard.SetTextAsync(vm.OutputText);
    }

    private void OnFloorAreaLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.OnFloorAreaChanged();
    }

    private void OnEditorSizeLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.OnEditorSizeChanged();
    }

    // Type AutoCompleteBox is one-way bound (same pattern as NewProjectView): push the
    // raw text back into the VM so custom typed names reach ApplyChanges.
    private void OnSelWinTypeTextChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is AutoCompleteBox box)
            vm.SelWinType = box.Text ?? string.Empty;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsMirrored) && DataContext is MainViewModel vm)
        {
            var image = this.FindControl<Image>("FloorPlanImage");
            if (image is not null)
            {
                // Mirror in place: transform and center origin must both be set from code,
                // otherwise the image is flipped around its left edge and jumps out of the box.
                image.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                image.RenderTransform = vm.IsMirrored
                    ? new ScaleTransform(-1, 1)
                    : null;
            }
        }
    }

    // Wheel over the compass rotates the building: up = clockwise, down = anticlockwise.
    // Scrollable card lists consume the event first, so their own scrolling wins there.
    private void CompassWheel_Changed(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MainViewModel vm || e.Delta.Y == 0) return;
        (e.Delta.Y > 0 ? vm.RotateClockwiseCommand : vm.RotateAnticlockwiseCommand).Execute(null);
        e.Handled = true;
    }

    private void Compass_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && DataContext is MainViewModel vm)
        {
            var card = listBox.SelectedItem as WindowCard;
            vm.SelectWindowCommand.Execute(card);
        }
    }
}
