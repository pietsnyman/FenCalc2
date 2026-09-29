using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FenCalc2.ViewModels;

// Help > About: logo, version and the MIT licence (same text as LICENSE at the repo
// root). Static info — nothing to hand back when it closes, so no
// OnAboutDialogClosed callback in MainViewModel.
public partial class AboutViewModel : ViewModelBase
{
    [ObservableProperty] private bool _dialogResult;

    public string AppName => "FenCalc2";

    public string Tagline => "Fenestration solar-gain calculations (SANS 10400-XA)";

    public string VersionText => "Version " +
        (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown");

    public string CopyrightText => "MIT License · Copyright (c) 2026 Piet Snyman";

    // Keep in sync with LICENSE at the repo root.
    public string LicenseText { get; } =
        """
        MIT License

        Copyright (c) 2026 Piet Snyman

        Permission is hereby granted, free of charge, to any person obtaining a copy
        of this software and associated documentation files (the "Software"), to deal
        in the Software without restriction, including without limitation the rights
        to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        copies of the Software, and to permit persons to whom the Software is
        furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all
        copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        SOFTWARE.
        """;

    // false -> true changes the value, so PropertyChanged fires and ShowDialogAsync
    // closes the window (no code-behind Click handler needed, unlike Cancel).
    [RelayCommand]
    private void Close() => DialogResult = true;
}
