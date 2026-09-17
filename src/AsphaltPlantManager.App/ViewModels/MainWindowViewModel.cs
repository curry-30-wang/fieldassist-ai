using AsphaltPlantManager.Core.Backup;
using AsphaltPlantManager.Core.Dashboard;
using AsphaltPlantManager.Core.MasterData;
using AsphaltPlantManager.Core.Output;
using AsphaltPlantManager.Core.Records;
using AsphaltPlantManager.Core.Receivables;
using AsphaltPlantManager.Core.Search;
using AsphaltPlantManager.Core.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AsphaltPlantManager.App;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ITemplateCatalog _templates;
    private readonly TemplateEngine _templateEngine;
    private readonly IRecordRepository _records;
    private readonly ArchiveService _archives;
    private readonly DashboardService _dashboard;
    private readonly IMasterDataRepository _masterData;
    private readonly IBackupService _backup;
    private readonly IEnumerable<IRecordExporter> _exporters;
    private readonly IPrintService _printService;

    public MainWindowViewModel(
        ITemplateCatalog templates,
        TemplateEngine templateEngine,
        IRecordRepository records,
        ArchiveService archives,
        CustomerReceivableService receivables,
        ICustomerReceivableExporter receivableExporter,
        ArchiveSessionService archiveSession,
        DashboardService dashboard,
        IMasterDataRepository masterData,
        IExcelFormWorkflow excel,
        LocalFormPreferences preferences,
        IUnlockPasswordStore passwords,
        IBackupService backup,
        IEnumerable<IRecordExporter> exporters,
        IPrintService printService)
    {
        _templates = templates;
        _templateEngine = templateEngine;
        _records = records;
        _archives = archives;
        _dashboard = dashboard;
        _masterData = masterData;
        _backup = backup;
        _exporters = exporters;
        _printService = printService;
        NavigationItems = new ObservableCollection<NavigationItem>(
        [
            new("首页", "⌂", "工作台", true),
            new("新建表格", "＋", "表格与档案", true),
            new("档案", "▣", "表格与档案", false),
            new("客户欠款", "￥", "销售结算", true),
            new("基础资料", "☷", "基础资料", true),
            new("备份", "▤", "系统管理", true),
            new("设置", "⚙", "系统管理", false)
        ]);
        CurrentPage = "首页";
        SetActiveNavigation("首页");
        NewForm = new FormEditorViewModel(_templates, _templateEngine, _records, _archives, excel, _masterData, preferences);
        Archive = new ArchiveViewModel(_records, _archives, archiveSession, _templates, _exporters, _printService, excel);
        Receivables = new ReceivablesViewModel(receivables, receivableExporter);
        Dashboard = new DashboardViewModel(_dashboard);
        MasterData = new MasterDataViewModel(_masterData);
        Backup = new BackupViewModel(_backup);
        Settings = new SettingsViewModel(masterData, passwords);
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }
    public FormEditorViewModel NewForm { get; }
    public ArchiveViewModel Archive { get; }
    public ReceivablesViewModel Receivables { get; }
    public DashboardViewModel Dashboard { get; }
    public MasterDataViewModel MasterData { get; }
    public BackupViewModel Backup { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private string _currentPage;
    [ObservableProperty] private string _statusMessage = "准备就绪";

    [RelayCommand]
    private async Task NavigateAsync(string? page)
    {
        if (string.IsNullOrWhiteSpace(page)) return;
        CurrentPage = page;
        SetActiveNavigation(page);
        try
        {
            switch (page)
            {
                case "首页": await Dashboard.RefreshAsync(); break;
                case "新建表格": await NewForm.LoadTemplatesAsync(); break;
                case "档案": await Archive.SearchAsync(); break;
                case "客户欠款": await Receivables.LoadAsync(); break;
                case "基础资料": await MasterData.LoadAsync(); break;
            }
            StatusMessage = "准备就绪";
        }
        catch (Exception exception)
        {
            StatusMessage = $"操作失败：{exception.Message}";
        }
    }

    private void SetActiveNavigation(string page)
    {
        foreach (var item in NavigationItems) item.IsActive = string.Equals(item.Title, page, StringComparison.Ordinal);
    }
}
