using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace AfterburnerStudio;

public partial class App : Application
{
    public override void Initialize()
    {
        // App.axaml only defines the light Fluent theme. Configure it directly
        // so startup does not depend on a generated XAML loader for App itself.
        // MainWindow.axaml is still compiled and loaded normally by Avalonia.
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
