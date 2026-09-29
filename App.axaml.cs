using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using FenCalc2.Data;
using FenCalc2.ViewModels;
using FenCalc2.Views;

namespace FenCalc2;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Initialize database and run migrations
        using (var conn = Database.CreateConnection())
        {
            Migrations.Run(conn);
            // Fresh install? Load the curated catalogue (glazing/frames/climate zones/
            // window ranges) so the app is usable without the legacy import.
            CatalogueSeeder.SeedIfEmpty(conn);
        }

        // Apply the persisted View > Theme choice before any window parses XAML;
        // no setting yet = Default (follows the OS).
        var repo = new ProjectRepository(Database.CreateConnection);
        RequestedThemeVariant = repo.GetSetting("Theme") switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            var custom => AppThemes.FromName(custom) ?? ThemeVariant.Default,
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
