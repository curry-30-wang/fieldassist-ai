using AsphaltPlantManager.Core.MasterData;
using AsphaltPlantManager.Core.Output;
using AsphaltPlantManager.Core.Records;
using AsphaltPlantManager.Core.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace AsphaltPlantManager.App;

public partial class FormEditorViewModel : ObservableObject
{
    private readonly ITemplateCatalog _templates;
    private readonly TemplateEngine _engine;
    private readonly IRecordRepository _records;
    private readonly ArchiveService _archives;
    private readonly IExcelFormWorkflow _excel;
    private readonly IMasterDataRepository _masterData;
    private readonly LocalFormPreferences _preferences;
    private FormRecord? _record;

    public FormEditorViewModel(
        ITemplateCatalog templates,
        TemplateEngine engine,
        IRecordRepository records,
        ArchiveService archives,
        IExcelFormWorkflow excel,
        IMasterDataRepository masterData,
        LocalFormPreferences preferences)
    {
        _templates = templates;
        _engine = engine;
        _records = records;
        _archives = archives;
        _excel = excel;
        _masterData = masterData;
        _preferences = preferences;
    }

    public ObservableCollection<TemplateDefinition> Templates { get; } = [];
    public ObservableCollection<FieldEditorViewModel> Fields { get; } = [];
    public ObservableCollection<string> ValidationMessages { get; } = [];
    public ObservableCollection<ExcelDraftFile> RecentDrafts { get; } = [];
    public ObservableCollection<string> SpecificationSuggestions { get; } = [];
    [ObservableProperty] private TemplateDefinition? _selectedTemplate;
    [ObservableProperty] private DateTime _periodStart = DateTime.Today;
    [ObservableProperty] private DateTime _periodEnd = DateTime.Today;
    [ObservableProperty] private string _changeNote = "首次封存";
    [ObservableProperty] private string _message = "请选择表单模板。";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isTemplateFavorite;
    [ObservableProperty] private bool _hasImportPreview;
    [ObservableProperty] private string _previewSummary = "";
    [ObservableProperty] private string _pendingImportPath = "";
    private ImportedExcelForm? _pendingImport;

    partial void OnSelectedTemplateChanged(TemplateDefinition? value)
    {
        if (value is not null)
        {
            ResetFields(value);
            _ = RefreshFavoriteStateAsync(value.Id);
        }
    }

    public async Task LoadTemplatesAsync()
    {
        if (Templates.Count > 0)
        {
            await RefreshDraftsAsync().ConfigureAwait(true);
            return;
        }
        IsBusy = true;
        try
        {
            var favorites = await _preferences.GetFavoriteTemplateIdsAsync(CancellationToken.None);
            foreach (var template in (await _templates.ListAsync(CancellationToken.None)).OrderByDescending(template => favorites.Contains(template.Id, StringComparer.OrdinalIgnoreCase)).ThenBy(template => template.Name)) Templates.Add(template);
            SpecificationSuggestions.Clear();
            foreach (var specification in await _preferences.GetSpecificationsAsync(CancellationToken.None)) SpecificationSuggestions.Add(specification);
            SelectedTemplate = Templates.FirstOrDefault();
            await RefreshDraftsAsync().ConfigureAwait(true);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Calculate()
    {
        if (SelectedTemplate is null) return;
        var calculated = _engine.Calculate(SelectedTemplate, Values());
        foreach (var field in Fields)
        {
            if (calculated.TryGetValue(field.Definition.Key, out var value)) field.Value = Format(value);
        }
        Message = "已按公式更新计算字段。";
    }

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        if (SelectedTemplate is null) { Message = "请先选择表单模板。"; return; }
        IsBusy = true;
        try
        {
            var now = DateTimeOffset.Now;
            var headers = HeaderSnapshot();
            var payload = JsonSerializer.Serialize(_engine.Calculate(SelectedTemplate, Values()));
            var search = string.Join(' ', headers.Values.Concat(Fields.Select(x => x.Value)));
            if (_record is null)
            {
                _record = FormRecord.CreateDraft(SelectedTemplate.Id, SelectedTemplate.Version,
                    DateOnly.FromDateTime(PeriodStart), DateOnly.FromDateTime(PeriodEnd), headers, payload, search, now);
            }
            else
            {
                _record.UpdateContent(headers, payload, search, now);
            }
            await _records.SaveAsync(_record, CancellationToken.None);
            Message = "草稿已保存。";
        }
        catch (Exception exception) { Message = $"保存草稿失败：{exception.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CompleteAndSealAsync()
    {
        if (SelectedTemplate is null) { Message = "请先选择表单模板。"; return; }
        ValidationMessages.Clear();
        var errors = _engine.Validate(SelectedTemplate, Values());
        foreach (var error in errors) ValidationMessages.Add(error.Message);
        if (errors.Count > 0) { Message = "请补全必填项后再完成。"; return; }

        await SaveDraftAsync();
        if (_record is null) return;
        try
        {
            _record.MarkCompleted();
            await _records.SaveAsync(_record, CancellationToken.None);
            var archived = await _archives.SealAsync(_record.Id, string.IsNullOrWhiteSpace(ChangeNote) ? "首次封存" : ChangeNote, DateTimeOffset.Now, CancellationToken.None);
            Message = $"已完成并首次封存：{archived.ArchiveNumber}（v{archived.Version}）。";
        }
        catch (Exception exception) { Message = $"封存失败：{exception.Message}"; }
    }

    [RelayCommand]
    private async Task CreateExcelDraftAsync()
    {
        if (SelectedTemplate is null) { Message = "请先选择表单模板。"; return; }
        IsBusy = true;
        try
        {
            var company = (await _masterData.GetAsync("公司", "default", CancellationToken.None))?.Value ?? string.Empty;
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "沥青拌合站管理系统", "Excel草稿");
            var draft = await _excel.CreateDraftAsync(
                SelectedTemplate,
                company,
                DateOnly.FromDateTime(PeriodStart),
                DateOnly.FromDateTime(PeriodEnd),
                directory,
                CancellationToken.None);
            Process.Start(new ProcessStartInfo { FileName = draft.Path, UseShellExecute = true });
            await RefreshDraftsAsync().ConfigureAwait(true);
            Message = $"Excel 模板已打开：{draft.Path}。填写并保存后，点击“导入 Excel 并封存”。";
        }
        catch (Exception exception) { Message = $"生成 Excel 模板失败：{exception.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ImportExcelAndSealAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择已填写的 Excel 表格",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var imported = await _excel.ImportAsync(dialog.FileName, CancellationToken.None);
            _pendingImport = imported;
            PendingImportPath = dialog.FileName;
            HasImportPreview = true;
            PreviewSummary = BuildPreviewSummary(imported);
            await RememberSpecificationsAsync(imported).ConfigureAwait(true);
            Message = "Excel 已通过校验，请核对预览后点击“确认并封存”。";
        }
        catch (Exception exception) { Message = $"读取 Excel 失败：{exception.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ConfirmImportAndSealAsync()
    {
        if (_pendingImport is null || string.IsNullOrWhiteSpace(PendingImportPath)) { Message = "请先导入 Excel 并查看预览。"; return; }
        IsBusy = true;
        try
        {
            if (_pendingImport.RecordId is not null)
            {
                Message = "这是封存档案的修改文件，请到“档案”页面导入。";
                return;
            }

            var imported = _pendingImport;
            var record = FormRecord.CreateDraft(
                imported.TemplateId,
                imported.TemplateVersion,
                imported.PeriodStart,
                imported.PeriodEnd,
                imported.HeaderSnapshot,
                imported.PayloadJson,
                imported.SearchText,
                DateTimeOffset.Now);
            record.MarkCompleted();
            await _records.SaveAsync(record, CancellationToken.None);
            var archived = await _archives.SealAsync(
                record.Id,
                string.IsNullOrWhiteSpace(ChangeNote) ? "首次封存" : ChangeNote,
                DateTimeOffset.Now,
                CancellationToken.None);

            var archivePath = CopyWorkbookToArchive(PendingImportPath, archived.ArchiveNumber);
            Message = archivePath is null
                ? $"已导入并封存：{archived.ArchiveNumber}（v{archived.Version}）。原 Excel 文件未移动。"
                : $"已导入并封存：{archived.ArchiveNumber}（v{archived.Version}）。Excel 副本：{archivePath}";
            ClearImportPreview();
        }
        catch (Exception exception) { Message = $"导入 Excel 失败：{exception.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ToggleTemplateFavoriteAsync()
    {
        if (SelectedTemplate is null) return;
        var favorites = (await _preferences.GetFavoriteTemplateIdsAsync(CancellationToken.None)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!favorites.Add(SelectedTemplate.Id)) favorites.Remove(SelectedTemplate.Id);
        await _preferences.SaveFavoriteTemplateIdsAsync(favorites, CancellationToken.None);
        IsTemplateFavorite = favorites.Contains(SelectedTemplate.Id);
        Message = IsTemplateFavorite ? "已收藏当前模板。" : "已取消收藏当前模板。";
    }

    [RelayCommand]
    private void OpenRecentDraft(ExcelDraftFile? draft)
    {
        if (draft is null || !File.Exists(draft.Path)) { Message = "找不到这个 Excel 草稿文件。"; return; }
        Process.Start(new ProcessStartInfo { FileName = draft.Path, UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenDraftFolder()
    {
        var directory = GetDraftDirectory();
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
    }

    private async Task RefreshFavoriteStateAsync(string templateId)
    {
        IsTemplateFavorite = (await _preferences.GetFavoriteTemplateIdsAsync(CancellationToken.None)).Contains(templateId, StringComparer.OrdinalIgnoreCase);
    }

    private Task RefreshDraftsAsync()
    {
        var directory = GetDraftDirectory();
        Directory.CreateDirectory(directory);
        RecentDrafts.Clear();
        foreach (var file in new DirectoryInfo(directory).GetFiles("*.xlsx").OrderByDescending(file => file.LastWriteTimeUtc).Take(8))
            RecentDrafts.Add(new ExcelDraftFile(file.FullName, file.Name, file.LastWriteTime));
        return Task.CompletedTask;
    }

    private async Task RememberSpecificationsAsync(ImportedExcelForm imported)
    {
        var specifications = ExtractSpecifications(imported.PayloadJson);
        if (specifications.Count == 0) return;
        var all = (await _preferences.GetSpecificationsAsync(CancellationToken.None)).Concat(specifications).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        await _preferences.RememberSpecificationsAsync(all, CancellationToken.None);
        SpecificationSuggestions.Clear();
        foreach (var specification in all) SpecificationSuggestions.Add(specification);
    }

    private static IReadOnlyList<string> ExtractSpecifications(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (!document.RootElement.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array) return [];
            return rows.EnumerateArray().Where(row => row.TryGetProperty("specification", out var value) && value.ValueKind == JsonValueKind.String).Select(row => row.GetProperty("specification").GetString()!).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (JsonException) { return []; }
    }

    private static string BuildPreviewSummary(ImportedExcelForm imported)
    {
        try
        {
            using var document = JsonDocument.Parse(imported.PayloadJson);
            var rowCount = document.RootElement.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array ? rows.GetArrayLength() : 0;
            var specifications = ExtractSpecifications(imported.PayloadJson);
            var specText = specifications.Count == 0 ? "未填写规格" : string.Join("、", specifications.Take(5));
            return $"模板：{imported.TemplateId} v{imported.TemplateVersion}　期间：{imported.PeriodStart:yyyy-MM-dd} 至 {imported.PeriodEnd:yyyy-MM-dd}\n明细：{rowCount} 行　规格：{specText}";
        }
        catch (JsonException) { return "Excel 已通过基础校验，但无法生成预览摘要。"; }
    }

    private void ClearImportPreview()
    {
        _pendingImport = null;
        PendingImportPath = string.Empty;
        PreviewSummary = string.Empty;
        HasImportPreview = false;
    }

    private static string GetDraftDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "沥青拌合站管理系统", "Excel草稿");

    private static string? CopyWorkbookToArchive(string sourcePath, string? archiveNumber)
    {
        if (string.IsNullOrWhiteSpace(archiveNumber) || !File.Exists(sourcePath)) return null;
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "沥青拌合站管理系统", "Excel归档");
        Directory.CreateDirectory(directory);
        var safeNumber = new string(archiveNumber!.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());
        var destination = Path.Combine(directory, $"{safeNumber}.xlsx");
        File.Copy(sourcePath, destination, overwrite: true);
        return destination;
    }

    private void ResetFields(TemplateDefinition template)
    {
        _record = null;
        ValidationMessages.Clear();
        Fields.Clear();
        foreach (var field in template.Fields)
            Fields.Add(new FieldEditorViewModel(field, Format(field.DefaultValue)));
        Message = $"已新建：{template.Name}";
    }

    private Dictionary<string, object?> Values() => Fields.ToDictionary(x => x.Definition.Key, x => x.ToValue(), StringComparer.Ordinal);
    private IReadOnlyDictionary<string, string> HeaderSnapshot() => Fields
        .Where(x => x.Definition.Key is "customer" or "project" or "projectPart" or "specification" or "company")
        .ToDictionary(x => x.Definition.Key, x => x.Value, StringComparer.Ordinal);
    private static string Format(object? value) => value switch { null => "", JsonElement element => element.ToString(), _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "" };
}

public sealed record ExcelDraftFile(string Path, string Name, DateTime LastWriteTime);

public partial class FieldEditorViewModel : ObservableObject
{
    public FieldEditorViewModel(FieldDefinition definition, string value) { Definition = definition; _value = value; }
    public FieldDefinition Definition { get; }
    public bool IsSelect => Definition.DataType == FieldDataType.Select;
    public bool IsCalculated => false;
    [ObservableProperty] private string _value;
    public object? ToValue() => Definition.DataType switch
    {
        FieldDataType.Decimal when decimal.TryParse(Value, out var number) => number,
        FieldDataType.Integer when int.TryParse(Value, out var integer) => integer,
        FieldDataType.Date when DateOnly.TryParse(Value, out var date) => date,
        _ => Value
    };
}
