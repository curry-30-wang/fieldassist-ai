using System.Windows;
using AsphaltPlantManager.Infrastructure.Output;
using Microsoft.Extensions.DependencyInjection;

namespace AsphaltPlantManager.App;

public partial class App : Application
{
    private CompositionRoot? _compositionRoot;

    protected override void OnStartup(StartupEventArgs e)
    {
        PdfFontBootstrap.Initialize();
        try
        {
            _compositionRoot = new CompositionRoot();
            _compositionRoot.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            MainWindow = new MainWindow(_compositionRoot.Services.GetRequiredService<MainWindowViewModel>());
            MainWindow.Show();
            base.OnStartup(e);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"应用启动失败：{exception.Message}", "沥青拌合站经营管理系统", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _compositionRoot?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
