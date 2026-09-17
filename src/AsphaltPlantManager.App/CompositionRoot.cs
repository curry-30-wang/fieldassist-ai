using AsphaltPlantManager.App.Output;
using AsphaltPlantManager.Core.Backup;
using AsphaltPlantManager.Core.Dashboard;
using AsphaltPlantManager.Core.MasterData;
using AsphaltPlantManager.Core.Output;
using AsphaltPlantManager.Core.Records;
using AsphaltPlantManager.Core.Receivables;
using AsphaltPlantManager.Core.Templates;
using AsphaltPlantManager.Infrastructure.Backup;
using AsphaltPlantManager.Infrastructure.Database;
using AsphaltPlantManager.Infrastructure.Output;
using AsphaltPlantManager.Infrastructure.Security;
using AsphaltPlantManager.Infrastructure.Templates;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace AsphaltPlantManager.App;

/// <summary>Application composition root. It is intentionally the only place that knows infrastructure types.</summary>
public sealed class CompositionRoot : IAsyncDisposable
{
    private readonly DatabaseInitializer _databaseInitializer;
    private readonly ServiceProvider _services;

    public CompositionRoot(string? databasePath = null, string? templatesDirectory = null)
    {
        DatabasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AsphaltPlantManager", "Data", "asphalt-plant.db");
        TemplatesDirectory = templatesDirectory ?? Path.Combine(AppContext.BaseDirectory, "templates");
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        var connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();
        _databaseInitializer = new DatabaseInitializer(connectionString);
        var services = new ServiceCollection();
        services.AddSingleton<ITemplateCatalog>(_ => new JsonTemplateCatalog(TemplatesDirectory));
        services.AddSingleton<TemplateEngine>();
        services.AddSingleton<IExcelFormWorkflow, ExcelFormWorkflow>();
        services.AddSingleton<IRecordRepository>(_ => new SqliteRecordRepository(connectionString));
        services.AddSingleton<IArchiveRepository>(_ => new SqliteArchiveRepository(connectionString));
        services.AddSingleton<ICustomerReceivableRepository>(_ => new SqliteCustomerReceivableRepository(connectionString));
        services.AddSingleton<CustomerReceivableService>();
        services.AddSingleton<ICustomerReceivableExporter, CustomerReceivableExcelExporter>();
        services.AddSingleton<IMasterDataRepository>(_ => new SqliteMasterDataRepository(connectionString));
        services.AddSingleton<LocalFormPreferences>();
        services.AddSingleton<ISettingsReader>(_ => new SqliteSettingsReader(connectionString));
        services.AddSingleton<SqliteUnlockPasswordStore>(_ => new SqliteUnlockPasswordStore(connectionString));
        services.AddSingleton<IUnlockPasswordStore>(provider => provider.GetRequiredService<SqliteUnlockPasswordStore>());
        services.AddSingleton<ArchiveSessionService>();
        services.AddSingleton<ArchiveService>();
        services.AddSingleton<TrashService>();
        services.AddSingleton<DashboardService>();
        services.AddSingleton<IBackupService>(_ => new BackupService(DatabasePath));
        services.AddSingleton<IRecordExporter, ExcelRecordExporter>();
        services.AddSingleton<IRecordExporter, PdfRecordExporter>();
        services.AddSingleton<IPrintService, WpfPrintService>();
        services.AddSingleton<MainWindowViewModel>();
        _services = services.BuildServiceProvider();
    }

    public string DatabasePath { get; }
    public string TemplatesDirectory { get; }
    public IServiceProvider Services => _services;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(TemplatesDirectory))
        {
            throw new DirectoryNotFoundException($"未找到表单模板目录：{TemplatesDirectory}");
        }

        await _databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var templates = await _services.GetRequiredService<ITemplateCatalog>().ListAsync(cancellationToken).ConfigureAwait(false);
        if (templates.Count == 0)
        {
            throw new InvalidOperationException("未找到可用表单模板，请检查 templates 目录。");
        }
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
