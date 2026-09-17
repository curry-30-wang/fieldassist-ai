using AsphaltPlantManager.Core.Output;
using AsphaltPlantManager.Core.Records;
using AsphaltPlantManager.Core.Search;
using AsphaltPlantManager.Core.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AsphaltPlantManager.App;

public partial class ArchiveViewModel : ObservableObject
{
    private readonly IRecordRepository _records;
    private readonly ArchiveService _archives;
    private readonly ArchiveSessionService _session;
    private readonly ITemplateCatalog _templates;
    private readonly IEnumerable<IRecordExporter> _exporters;
    private readonly IPrintService _printer;
    private readonly IExcelFormWorkflow _excel;

    public ArchiveViewModel(IRecordRepository records, ArchiveService archives, ArchiveSessionService session, ITemplateCatalog templates, IEnumerable<IRecordExporter> exporters, IPrintService printer, IExcelFormWorkflow excel)
    { _records = records; _archives = archives; _session = session; _templates = templates; _exporters = exporters; _printer = printer; _excel = excel; }

    public ObservableCollection<SearchResult> Results { get; } = [];
    public ObservableCollection<ArchiveVersion> Versions { get; } = [];
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;
    [ObservableProperty] private string _templateId = "";
    [ObservableProperty] private string _customer = "";
    [ObservableProperty] private string _project = "";
    [ObservableProperty] private string _specification = "";
    [ObservableProperty] private string _keyword = "";
    [ObservableProperty] private SearchResult? _selectedResult;
    [ObservableProperty] private ArchiveVersion? _selectedVersion;
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _editedPayload = "";
    [ObservableProperty] private string _editChangeNote = "修正已封存档案";
    [ObservableProperty] private string _message = "可按日期、模板、客户、工程、规格和关键词查询。";
    [ObservableProperty] private string _outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "沥青拌合站管理系统输出");

    partial void OnSelectedResultChanged(SearchResult? value) { if (value is not null) _ = OpenAsync(); }

    [RelayCommand]
    public async Task SearchAsync()
    {
        try
        {
            var result = await _records.SearchAsync(new RecordQuery(
                From: FromDate is null ? null : DateOnly.FromDateTime(FromDate.Value),
                To: ToDate is null ? null : DateOnly.FromDateTime(ToDate.Value),
                TemplateId: EmptyToNull(TemplateId), Customer: EmptyToNull(Customer), Project: EmptyToNull(Project),
                Specification: EmptyToNull(Specification), Keyword: EmptyToNull(Keyword)), CancellationToken.None);
            Results.Clear(); foreach (var item in result) Results.Add(item);
            Message = $"找到 {result.TotalCount} 条档案。";
        }
        catch (Exception exception) { Message = $"查询失败：{exception.Message}"; }
    }

    [RelayCommand]
    public async Task OpenAsync()
    {
        if (SelectedResult is null) return;
        Versions.Clear();
        try
        {
            foreach (var version in await _archives.GetVersionsAsync(SelectedResult.Id, CancellationToken.None)) Versions.Add(version);
            SelectedVersion = Versions.LastOrDefault();
            EditedPayload = SelectedVersion?.PayloadJson ?? "";
            Message = SelectedVersion is null ? "此记录尚未封存。" : $"已打开档案，共 {Versions.Count} 个版本。";
        }
        catch (Exception exception) { Message = $"打开档案失败：{exception.Message}"; }
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        try
        {
            var unlocked = await _session.UnlockAsync(Password, CancellationToken.None);
            Message = unlocked ? "本次会话已解锁。" : "解锁密码不正确。";
        }
        catch { Message = "无法解锁：请在设置中确认已设置解锁密码。"; }
    }

    [RelayCommand]
    private async Task SaveArchivedEditAsync()
    {
        if (SelectedResult is null || SelectedVersion is null) { Message = "请先打开一个已封存档案版本。"; return; }
        if (!_session.IsUnlocked) { Message = "请先输入本次会话解锁密码。"; return; }
        try
        {
            using var document = JsonDocument.Parse(EditedPayload);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("表单数据必须是对象。");
            var next = await _archives.SaveArchivedEditAsync(SelectedResult.Id, EditedPayload, SelectedVersion.HeaderSnapshot,
                string.IsNullOrWhiteSpace(EditChangeNote) ? "修正已封存档案" : EditChangeNote, DateTimeOffset.Now, CancellationToken.None);
            Versions.Add(next); SelectedVersion = next;
            Message = $"已保存修订版本 v{next.Version}。";
        }
        catch (Exception exception) { Message = $"保存修订失败：{exception.Message}"; }
    }

    [RelayCommand]
    private async Task ExportExcelAsync() => await ExportAsync("xlsx");
    [RelayCommand]
    private async Task ExportEditableExcelAsync()
    {
        var snapshot = await GetSnapshotAsync();
        if (snapshot is null) return;
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "沥青拌合站管理系统", "Excel草稿");
            var draft = await _excel.CreateEditableDraftAsync(snapshot, directory, CancellationToken.None);
            Process.Start(new ProcessStartInfo { FileName = draft.Path, UseShellExecute = true });
            Message = $"已打开可编辑 Excel：{draft.Path}";
        }
        catch (Exception exception) { Message = $"打开可编辑 Excel 失败：{exception.Message}"; }
    }
    [RelayCommand]
    private async Task ImportEditableExcelAsync()
    {
        if (SelectedResult is null || SelectedVersion is null) { Message = "请先打开一个已封存档案版本。"; return; }
        if (!_session.IsUnlocked) { Message = "请先输入本次会话解锁密码。"; return; }
        var dialog = new OpenFileDialog
        {
            Title = "选择修改后的档案 Excel",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var imported = await _excel.ImportAsync(dialog.FileName, CancellationToken.None);
            if (imported.RecordId != SelectedResult.Id)
            {
                Message = "这不是当前档案导出的 Excel，已拒绝导入。";
                return;
            }
            if (imported.ArchiveVersion != SelectedVersion.Version)
            {
                Message = $"Excel 对应的是 v{imported.ArchiveVersion?.ToString() ?? "未知"}，当前打开的是 v{SelectedVersion.Version}，请重新导出当前版本。";
                return;
            }
            var next = await _archives.SaveArchivedEditAsync(
                SelectedResult.Id,
                imported.PayloadJson,
                imported.HeaderSnapshot,
                string.IsNullOrWhiteSpace(EditChangeNote) ? "Excel 修改" : EditChangeNote,
                DateTimeOffset.Now,
                CancellationToken.None);
            await OpenAsync();
            SelectedVersion = Versions.FirstOrDefault(version => version.Version == next.Version) ?? next;
            EditedPayload = next.PayloadJson;
            Message = $"Excel 修改已保存为新版本 v{next.Version}。旧版本仍保留最近一版。";
        }
        catch (Exception exception) { Message = $"导入修改失败：{exception.Message}"; }
    }
    [RelayCommand]
    private async Task ExportPdfAsync() => await ExportAsync("pdf");
    [RelayCommand]
    private async Task PreviewAsync()
    {
        var snapshot = await GetSnapshotAsync(); if (snapshot is null) return;
        try { var preview = await _printer.PreviewAsync(snapshot, CancellationToken.None); Message = $"预览已生成：{preview.Title}，{preview.Rows.Count} 行。"; }
        catch (Exception exception) { Message = $"预览失败：{exception.Message}"; }
    }
    [RelayCommand]
    private async Task PrintAsync()
    {
        var snapshot = await GetSnapshotAsync(); if (snapshot is null) return;
        try { await _printer.PrintAsync(snapshot, new PrinterSettings(), CancellationToken.None); Message = "已发送打印任务。"; }
        catch (Exception exception) { Message = $"打印失败：{exception.Message}"; }
    }

    private async Task ExportAsync(string format)
    {
        var snapshot = await GetSnapshotAsync(); if (snapshot is null) return;
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            var exporter = _exporters.First(x => string.Equals(x.Format, format, StringComparison.OrdinalIgnoreCase));
            Message = $"已导出：{await exporter.ExportAsync(snapshot, OutputDirectory, CancellationToken.None)}";
        }
        catch (Exception exception) { Message = $"导出失败：{exception.Message}"; }
    }

    private async Task<OutputRecordSnapshot?> GetSnapshotAsync()
    {
        if (SelectedResult is null || SelectedVersion is null) { Message = "请先打开一个已封存档案版本。"; return null; }
        var record = SelectedResult.Record;
        if (record.ArchiveNumber is null) { Message = "草稿不能导出，请先完成并封存。"; return null; }
        var template = await _templates.GetAsync(SelectedVersion.TemplateId, SelectedVersion.TemplateVersion, CancellationToken.None);
        return new OutputRecordSnapshot(SelectedVersion.ArchiveNumber, SelectedVersion.Version, template,
            record.PeriodStart, record.PeriodEnd, SelectedVersion.HeaderSnapshot, SelectedVersion.PayloadJson,
            SelectedVersion.ChangeNote, SelectedVersion.CreatedAt, DateTimeOffset.Now, SelectedResult.Id);
    }
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
