# 沥青拌合站经营管理系统实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建一套可安装在单台 Windows 10/11 电脑、完全离线运行，支持可扩展表格模板、封存版本、精确查询、A4 打印、Excel/PDF 导出和备份恢复的沥青拌合站经营管理软件。

**Architecture:** 使用 WPF MVVM 桌面界面，领域与模板规则放在独立 Core 项目，SQLite 持久化和文件输出放在 Infrastructure 项目。表格实例保存模板版本和 JSON 数据快照，模板升级不会改变历史档案；打印、Excel、PDF、备份分别通过接口与业务层隔离。

**Tech Stack:** C# 12、.NET 8 WPF、CommunityToolkit.Mvvm、Microsoft.Data.Sqlite、Dapper、ClosedXML、PDFsharp/MigraDoc、xUnit、FluentAssertions、Microsoft.Extensions.DependencyInjection、Inno Setup。

## Global Constraints

- 目标平台为普通 Windows 10/11 x64，单机单用户，完全离线运行。
- 发布物必须自包含 .NET 8 运行时，用户电脑不需要预装 .NET、Excel 或数据库。
- 所有经营数据默认保存在 `%LOCALAPPDATA%\AsphaltPlantManager\Data`，备份默认保存在 `%USERPROFILE%\Documents\沥青拌合站管理系统备份`。
- 用户可修改软件名称、公司名称、项目名称和其他基础资料。
- 首版包含 12 种设计说明中列出的模板，并能在后续新增模板而不破坏历史档案。
- 单机单人模式下，每次应用会话首次编辑已封存表格时输入一次解封密码；当前会话内可随时编辑，且每次保存自动产生并保留历史版本，无需反复封存。
- 所有宽表默认 A4 横向，普通记录单默认 A4 纵向；打印和 PDF 必须包含档案编号、版本、打印时间和页码。
- 对日期、吨数、单价和金额做格式校验；打印、导出或恢复失败不得改变原始业务数据。
- 所有数据写入、版本封存和恢复操作使用 SQLite 事务。

---

## 文件结构

```text
AsphaltPlantManager.sln
src/
  AsphaltPlantManager.App/            WPF 启动、视图、ViewModel、导航和依赖注入
  AsphaltPlantManager.Core/           领域模型、模板定义、计算、校验和服务接口
  AsphaltPlantManager.Infrastructure/ SQLite、文件、导出、打印和备份实现
tests/
  AsphaltPlantManager.Core.Tests/     模板计算、状态流转、编号和校验测试
  AsphaltPlantManager.Infrastructure.Tests/ 数据库、查询、导出和备份集成测试
  AsphaltPlantManager.App.Tests/      ViewModel 和导航测试
templates/                             12 个内置模板的版本化 JSON 定义
installer/                             Inno Setup 安装脚本
docs/user-guide.md                     中文使用说明
```

### Task 1: 解决方案骨架、领域基础类型与测试基线

**Files:**
- Create: `global.json`
- Create: `AsphaltPlantManager.sln`
- Create: `src/AsphaltPlantManager.Core/AsphaltPlantManager.Core.csproj`
- Create: `src/AsphaltPlantManager.Core/Records/RecordStatus.cs`
- Create: `src/AsphaltPlantManager.Core/Records/FormRecord.cs`
- Create: `tests/AsphaltPlantManager.Core.Tests/AsphaltPlantManager.Core.Tests.csproj`
- Create: `tests/AsphaltPlantManager.Core.Tests/Records/FormRecordTests.cs`

**Interfaces:**
- Produces: `RecordStatus`, `FormRecord.CreateDraft(string templateId, int templateVersion, DateOnly periodStart, DateOnly periodEnd)`, `FormRecord.MarkCompleted()`, `FormRecord.Seal(string archiveNumber, string changeNote, DateTimeOffset now)`, `FormRecord.Unseal()`.

- [ ] **Step 1: 固定 .NET 8 SDK 并创建三个项目和测试项目**

```json
{"sdk":{"version":"8.0.100","rollForward":"latestPatch"}}
```

运行：`dotnet new sln -n AsphaltPlantManager`，创建 `Core`、`Infrastructure`、`App` 和三个测试项目并加入解决方案；WPF 项目目标框架使用 `net8.0-windows`。

- [ ] **Step 2: 写状态流转失败测试**

```csharp
[Fact]
public void Sealed_record_must_be_unsealed_before_editing()
{
    var record = FormRecord.CreateDraft("asphalt-inventory", 1, new(2026, 8, 1), new(2026, 8, 31));
    record.MarkCompleted();
    record.Seal("GHC-202608-0001", "首次封存", DateTimeOffset.Parse("2026-08-31T10:00:00+08:00"));
    record.CanEdit.Should().BeFalse();
    record.Unseal();
    record.CanEdit.Should().BeTrue();
}
```

- [ ] **Step 3: 运行测试并确认失败**

Run: `dotnet test tests/AsphaltPlantManager.Core.Tests --filter FullyQualifiedName~FormRecordTests`
Expected: FAIL，提示 `FormRecord` 未定义。

- [ ] **Step 4: 实现最小领域状态机**

```csharp
public enum RecordStatus { Draft, Completed, Sealed, Unsealed }

public sealed class FormRecord
{
    public Guid Id { get; private init; } = Guid.NewGuid();
    public RecordStatus Status { get; private set; } = RecordStatus.Draft;
    public bool CanEdit => Status is RecordStatus.Draft or RecordStatus.Unsealed;
    public void MarkCompleted() => Status = RecordStatus.Completed;
    public void Seal(string archiveNumber, string note, DateTimeOffset now) => Status = RecordStatus.Sealed;
    public void Unseal()
    {
        if (Status != RecordStatus.Sealed) throw new InvalidOperationException("只有已封存表格可以解封");
        Status = RecordStatus.Unsealed;
    }
}
```

- [ ] **Step 5: 运行全部基线测试并提交**

Run: `dotnet test AsphaltPlantManager.sln`
Expected: PASS。

```powershell
git add global.json AsphaltPlantManager.sln src tests
git commit -m "feat: establish domain and solution foundation"
```

### Task 2: 可扩展模板定义、校验与公式计算

**Files:**
- Create: `src/AsphaltPlantManager.Core/Templates/TemplateDefinition.cs`
- Create: `src/AsphaltPlantManager.Core/Templates/FieldDefinition.cs`
- Create: `src/AsphaltPlantManager.Core/Templates/FormulaDefinition.cs`
- Create: `src/AsphaltPlantManager.Core/Templates/TemplateEngine.cs`
- Create: `src/AsphaltPlantManager.Core/Templates/ITemplateCatalog.cs`
- Create: `src/AsphaltPlantManager.Infrastructure/Templates/JsonTemplateCatalog.cs`
- Create: `tests/AsphaltPlantManager.Core.Tests/Templates/TemplateEngineTests.cs`
- Create: `tests/AsphaltPlantManager.Infrastructure.Tests/Templates/JsonTemplateCatalogTests.cs`

**Interfaces:**
- Produces: `ITemplateCatalog.GetAsync(string id, int? version, CancellationToken)`, `ITemplateCatalog.ListAsync(CancellationToken)`, `TemplateEngine.Calculate(TemplateDefinition, IReadOnlyDictionary<string, object?>)`, `TemplateEngine.Validate(...)`.
- Consumes: JSON 模板定义目录。

- [ ] **Step 1: 写模板版本和购耗存公式失败测试**

```csharp
[Fact]
public void Inventory_balance_equals_opening_plus_purchase_minus_consumption()
{
    var data = new Dictionary<string, object?> { ["opening"] = 10m, ["purchase"] = 30m, ["consumption"] = 12.5m };
    var result = new TemplateEngine().Calculate(TestTemplates.Inventory, data);
    result["closing"].Should().Be(27.5m);
}
```

- [ ] **Step 2: 运行测试确认公式引擎缺失**

Run: `dotnet test tests/AsphaltPlantManager.Core.Tests --filter FullyQualifiedName~TemplateEngineTests`
Expected: FAIL。

- [ ] **Step 3: 实现受限公式操作和字段校验**

```csharp
public sealed record FormulaDefinition(string Target, FormulaOperator Operator, string[] Operands);
public enum FormulaOperator { Add, Subtract, Multiply, Sum }

public IReadOnlyDictionary<string, object?> Calculate(TemplateDefinition template, IReadOnlyDictionary<string, object?> input)
{
    var output = new Dictionary<string, object?>(input);
    foreach (var formula in template.Formulas)
        output[formula.Target] = EvaluateDecimal(formula, output);
    return output;
}
```

公式不得执行任意脚本，只允许加、减、乘和明细求和；校验返回字段名和中文错误信息。

- [ ] **Step 4: 实现 JSON 目录并测试指定历史版本仍可加载**

Run: `dotnet test tests/AsphaltPlantManager.Core.Tests tests/AsphaltPlantManager.Infrastructure.Tests --filter "FullyQualifiedName~Template"`
Expected: PASS。

- [ ] **Step 5: 提交模板引擎**

```powershell
git add src/AsphaltPlantManager.Core/Templates src/AsphaltPlantManager.Infrastructure/Templates tests
git commit -m "feat: add versioned template engine"
```

### Task 3: 12 个内置模板及业务计算规则

**Files:**
- Create: `templates/asphalt-inventory/v1.json`
- Create: `templates/customer-reconciliation/v1.json`
- Create: `templates/production-daily/v1.json`
- Create: `templates/material-receipt/v1.json`
- Create: `templates/inventory-count/v1.json`
- Create: `templates/finished-goods-dispatch/v1.json`
- Create: `templates/sales-collection/v1.json`
- Create: `templates/equipment-maintenance/v1.json`
- Create: `templates/fuel-inventory/v1.json`
- Create: `templates/quality-inspection/v1.json`
- Create: `templates/transport-settlement/v1.json`
- Create: `templates/safety-inspection/v1.json`
- Create: `tests/AsphaltPlantManager.Infrastructure.Tests/Templates/BuiltInTemplateTests.cs`

**Interfaces:**
- Produces: 12 个唯一模板 ID，全部由 `JsonTemplateCatalog` 加载。

- [ ] **Step 1: 写完整性和样例计算测试**

```csharp
[Fact]
public async Task Catalog_contains_all_twelve_valid_templates()
{
    var all = await _catalog.ListAsync(default);
    all.Select(x => x.Id).Should().BeEquivalentTo(ExpectedIds);
    all.Should().OnlyContain(x => x.Fields.Count > 0 && x.PrintLayout.Paper == "A4");
}

[Fact]
public void Reconciliation_total_is_material_plus_oil_plus_freight()
{
    var row = CalculateReconciliation(quantity: 238.14m, unitPrice: 135.30m, oil: 0m, freight: 0m);
    row.SupplyTotal.Should().Be(32220.34m);
}
```

- [ ] **Step 2: 运行测试确认模板不存在**

Run: `dotnet test tests/AsphaltPlantManager.Infrastructure.Tests --filter FullyQualifiedName~BuiltInTemplateTests`
Expected: FAIL，目录为空。

- [ ] **Step 3: 按批准设计和照片样例编写 12 个模板 JSON**

每个文件必须包含 `id`、`name`、`category`、`version: 1`、`archivePrefix`、`fields`、`formulas`、`searchFields` 和 `printLayout`。对账单金额使用 `quantity * unitPrice`，供货总额使用材料金额、油费、运费之和，累计欠款使用上期欠款加供货总额减回款。

- [ ] **Step 4: 运行模板、公式和 JSON 架构测试**

Run: `dotnet test tests/AsphaltPlantManager.Infrastructure.Tests --filter FullyQualifiedName~BuiltInTemplateTests`
Expected: PASS，12 个模板均能加载并计算。

- [ ] **Step 5: 提交模板包**

```powershell
git add templates tests/AsphaltPlantManager.Infrastructure.Tests/Templates
git commit -m "feat: add twelve asphalt plant form templates"
```

### Task 4: SQLite 数据库、基础资料和草稿自动保存

**Files:**
- Create: `src/AsphaltPlantManager.Core/Records/IRecordRepository.cs`
- Create: `src/AsphaltPlantManager.Core/MasterData/MasterDataItem.cs`
- Create: `src/AsphaltPlantManager.Core/MasterData/IMasterDataRepository.cs`
- Create: `src/AsphaltPlantManager.Infrastructure/Database/DatabaseInitializer.cs`
- Create: `src/AsphaltPlantManager.Infrastructure/Database/SqliteRecordRepository.cs`
- Create: `src/AsphaltPlantManager.Infrastructure/Database/SqliteMasterDataRepository.cs`
- Create: `src/AsphaltPlantManager.Infrastructure/Database/Migrations/001_initial.sql`
- Create: `tests/AsphaltPlantManager.Infrastructure.Tests/Database/SqliteRecordRepositoryTests.cs`

**Interfaces:**
- Produces: `IRecordRepository.SaveAsync(FormRecord, CancellationToken)`, `GetAsync(Guid, CancellationToken)`, `SearchAsync(RecordQuery, CancellationToken)`；`IMasterDataRepository.UpsertAsync(MasterDataItem, CancellationToken)`。

- [ ] **Step 1: 写保存快照和历史名称不联动失败测试**

```csharp
[Fact]
public async Task Saved_record_keeps_company_snapshot_after_setting_changes()
{
    await _records.SaveAsync(RecordFixtures.WithCompany("旧公司"), default);
    await _master.UpsertAsync(new("company", "default", "新公司"), default);
    (await _records.GetAsync(RecordFixtures.Id, default))!.HeaderSnapshot["company"].Should().Be("旧公司");
}
```

- [ ] **Step 2: 运行数据库测试确认失败**

Run: `dotnet test tests/AsphaltPlantManager.Infrastructure.Tests --filter FullyQualifiedName~SqliteRecordRepositoryTests`
Expected: FAIL。

- [ ] **Step 3: 实现迁移、事务仓储和 JSON 快照列**

数据库表至少包含 `records`、`record_versions`、`master_data`、`settings`、`archive_sequences` 和 `trash`；启用 `PRAGMA foreign_keys=ON`、WAL 和 `busy_timeout`。

- [ ] **Step 4: 实现 1 秒防抖的草稿保存服务并验证异常重开**

Run: `dotnet test tests/AsphaltPlantManager.Infrastructure.Tests --filter "FullyQualifiedName~Sqlite|FullyQualifiedName~Draft"`
Expected: PASS。

- [ ] **Step 5: 提交持久化层**

```powershell
git add src/AsphaltPlantManager.Core src/AsphaltPlantManager.Infrastructure/Database tests
git commit -m "feat: persist records and master data in sqlite"
```

### Task 5: 档案编号、会话解封、自动版本和回收站

**Files:**
- Create: `src/AsphaltPlantManager.Core/Records/ArchiveService.cs`
- Create: `src/AsphaltPlantManager.Core/Records/ArchiveVersion.cs`
- Create: `src/AsphaltPlantManager.Core/Records/ArchiveNumberService.cs`
- Create: `src/AsphaltPlantManager.Core/Records/ArchiveSessionService.cs`
- Create: `src/AsphaltPlantManager.Core/Records/TrashService.cs`
- Create: `tests/AsphaltPlantManager.Core.Tests/Records/ArchiveServiceTests.cs`

**Interfaces:**
- Produces: `ArchiveService.SealAsync(Guid recordId, string changeNote, DateTimeOffset now, CancellationToken)` for first archive, `SaveArchivedChangeAsync(Guid recordId, string changeNote, DateTimeOffset now, CancellationToken)` for automatic versions, `RestoreVersionAsync(Guid, int, CancellationToken)`，`ArchiveSessionService.UnlockAsync(string password, CancellationToken)` for one application-session unlock, and `TrashService.MoveAsync`/`RestoreAsync`。

- [ ] **Step 1: 写连续编号和版本保留失败测试**

```csharp
[Fact]
public async Task Saving_archived_change_creates_version_two_without_overwriting_one()
{
    var first = await _service.SealAsync(_id, "首次", _now, default);
    await _session.UnlockAsync("会话解封密码", default);
    await _records.UpdatePayloadAsync(_id, "{\"quantity\":20}", default);
    var second = await _service.SaveArchivedChangeAsync(_id, "修正数量", _now.AddHours(1), default);
    second.ArchiveNumber.Should().Be(first.ArchiveNumber);
    (await _records.GetVersionsAsync(_id, default)).Select(x => x.Version).Should().Equal(1, 2);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/AsphaltPlantManager.Core.Tests --filter FullyQualifiedName~ArchiveServiceTests`
Expected: FAIL。

- [ ] **Step 3: 在单一事务中实现编号和不可变版本快照**

编号格式为 `{模板前缀}-{yyyyMM}-{四位流水}`；首次封存生成编号。每个应用会话首次编辑已封存档案前验证一次解封密码；会话有效期间每次保存已封存档案均在同一事务中沿用编号并自动递增版本，无需重新封存。

- [ ] **Step 4: 实现回收站软删除和恢复测试**

Run: `dotnet test tests/AsphaltPlantManager.Core.Tests --filter "FullyQualifiedName~Archive|FullyQualifiedName~Trash"`
Expected: PASS。

- [ ] **Step 5: 提交档案生命周期**

```powershell
git add src/AsphaltPlantManager.Core/Records tests/AsphaltPlantManager.Core.Tests/Records
git commit -m "feat: add archive versioning and recycle bin"
```

### Task 6: 组合查询、经营汇总和提醒

**Files:**
- Create: `src/AsphaltPlantManager.Core/Search/RecordQuery.cs`
- Create: `src/AsphaltPlantManager.Core/Search/SearchResult.cs`
- Create: `src/AsphaltPlantManager.Core/Dashboard/DashboardService.cs`
- Modify: `src/AsphaltPlantManager.Infrastructure/Database/SqliteRecordRepository.cs`
- Create: `tests/AsphaltPlantManager.Infrastructure.Tests/Search/RecordSearchTests.cs`
- Create: `tests/AsphaltPlantManager.Core.Tests/Dashboard/DashboardServiceTests.cs`

**Interfaces:**
- Produces: `RecordQuery` 的日期、模板、客户、工程、规格、状态、欠款和关键词筛选；`DashboardService.GetAsync(DateOnly, CancellationToken)`。

- [ ] **Step 1: 写多条件检索失败测试**

```csharp
[Fact]
public async Task Search_combines_month_customer_spec_and_positive_receivable()
{
    var result = await _repository.SearchAsync(new RecordQuery(
        From: new(2026,8,1), To: new(2026,8,31), Customer:"甲公司", Specification:"AC-13", HasReceivable:true), default);
    result.Should().ContainSingle().Which.SearchText.Should().Contain("甲公司");
}
```

- [ ] **Step 2: 运行搜索测试确认失败**

Run: `dotnet test tests/AsphaltPlantManager.Infrastructure.Tests --filter FullyQualifiedName~RecordSearchTests`
Expected: FAIL。

- [ ] **Step 3: 实现参数化 SQL、分页和 JSON 索引字段冗余列**

所有用户输入通过 SQLite 参数传递；默认按业务日期和更新时间降序，每页 50 条。

- [ ] **Step 4: 实现首页产量、消耗、库存、欠款和提醒汇总**

Run: `dotnet test tests/AsphaltPlantManager.Infrastructure.Tests tests/AsphaltPlantManager.Core.Tests --filter "FullyQualifiedName~Search|FullyQualifiedName~Dashboard"`
Expected: PASS。

- [ ] **Step 5: 提交查询统计**

```powershell
git add src/AsphaltPlantManager.Core src/AsphaltPlantManager.Infrastructure tests
git commit -m "feat: add archive search and dashboard summaries"
```

### Task 7: Excel、PDF 和 Windows 打印输出

**Files:**
- Create: `src/AsphaltPlantManager.Core/Output/IRecordExporter.cs`
- Create: `src/AsphaltPlantManager.Core/Output/IPrintService.cs`
- Create: `src/AsphaltPlantManager.Infrastructure/Output/ExcelRecordExporter.cs`
- Create: `src/AsphaltPlantManager.Infrastructure/Output/PdfRecordExporter.cs`
- Create: `src/AsphaltPlantManager.Infrastructure/Output/WpfPrintService.cs`
- Create: `src/AsphaltPlantManager.Infrastructure/Output/ArchiveFileNamer.cs`
- Create: `tests/AsphaltPlantManager.Infrastructure.Tests/Output/ExportTests.cs`

**Interfaces:**
- Produces: `IRecordExporter.ExportAsync(RecordSnapshot, string destination, CancellationToken)`；`IPrintService.PreviewAsync` 和 `PrintAsync`。

- [ ] **Step 1: 写 Excel 内容和 PDF 元数据失败测试**

```csharp
[Fact]
public async Task Excel_contains_title_headers_totals_and_archive_metadata()
{
    var path = await _excel.ExportAsync(RecordFixtures.SealedReconciliation(), _temp.Path, default);
    using var book = new XLWorkbook(path);
    var sheet = book.Worksheet(1);
    sheet.Cell("A1").GetString().Should().Contain("对账单");
    sheet.CellsUsed().Select(c => c.GetString()).Should().Contain("DZ-202608-0001");
}
```

- [ ] **Step 2: 运行输出测试确认失败**

Run: `dotnet test tests/AsphaltPlantManager.Infrastructure.Tests --filter FullyQualifiedName~ExportTests`
Expected: FAIL。

- [ ] **Step 3: 实现 ClosedXML 和 PDFsharp/MigraDoc 输出**

文件目录固定为 `{目标目录}\{yyyy}\{MM}\{模板名称}\{档案编号}-v{版本}`；Excel 保留数字格式和公式，PDF 使用嵌入的中文字体并重复分页表头。

- [ ] **Step 4: 实现 WPF 打印预览和打印适配器**

打印失败抛出带中文说明的 `OutputException`，调用方显示错误且不更新记录状态。

- [ ] **Step 5: 运行导出测试并人工渲染两类样表**

Run: `dotnet test tests/AsphaltPlantManager.Infrastructure.Tests --filter FullyQualifiedName~ExportTests`
Expected: PASS；PDF 页尺寸为 A4，对账单横向，购耗存表纵向或按字段宽度横向且无截断。

- [ ] **Step 6: 提交输出功能**

```powershell
git add src/AsphaltPlantManager.Core/Output src/AsphaltPlantManager.Infrastructure/Output tests
git commit -m "feat: export and print archived forms"
```

### Task 8: 自动备份、手动备份和安全恢复

**Files:**
- Create: `src/AsphaltPlantManager.Core/Backup/IBackupService.cs`
- Create: `src/AsphaltPlantManager.Infrastructure/Backup/BackupService.cs`
- Create: `src/AsphaltPlantManager.Infrastructure/Backup/BackupManifest.cs`
- Create: `tests/AsphaltPlantManager.Infrastructure.Tests/Backup/BackupServiceTests.cs`

**Interfaces:**
- Produces: `CreateAsync(BackupReason, string destination, CancellationToken)`、`ValidateAsync(string backupPath, CancellationToken)`、`RestoreAsync(string backupPath, CancellationToken)`、`PruneAsync(int keepCount, CancellationToken)`。

- [ ] **Step 1: 写恢复失败不覆盖当前数据库的测试**

```csharp
[Fact]
public async Task Invalid_backup_never_replaces_current_database()
{
    var before = await File.ReadAllBytesAsync(_dbPath);
    await FluentActions.Invoking(() => _service.RestoreAsync(_corruptBackup, default)).Should().ThrowAsync<BackupValidationException>();
    (await File.ReadAllBytesAsync(_dbPath)).Should().Equal(before);
}
```

- [ ] **Step 2: 运行备份测试确认失败**

Run: `dotnet test tests/AsphaltPlantManager.Infrastructure.Tests --filter FullyQualifiedName~BackupServiceTests`
Expected: FAIL。

- [ ] **Step 3: 实现 SQLite 在线备份、SHA-256 清单和原子替换恢复**

恢复流程为校验备份、创建当前数据库安全副本、恢复到临时文件、运行 `PRAGMA integrity_check`、原子替换；任一步失败均回滚。

- [ ] **Step 4: 实现每日首次正常退出备份和保留最近 30 份**

Run: `dotnet test tests/AsphaltPlantManager.Infrastructure.Tests --filter FullyQualifiedName~BackupServiceTests`
Expected: PASS。

- [ ] **Step 5: 提交备份恢复**

```powershell
git add src/AsphaltPlantManager.Core/Backup src/AsphaltPlantManager.Infrastructure/Backup tests
git commit -m "feat: add validated backup and safe restore"
```

### Task 9: WPF 主界面、模板编辑器和档案查询界面

**Files:**
- Create: `src/AsphaltPlantManager.App/App.xaml.cs`
- Create: `src/AsphaltPlantManager.App/Shell/MainWindow.xaml`
- Create: `src/AsphaltPlantManager.App/Shell/MainWindowViewModel.cs`
- Create: `src/AsphaltPlantManager.App/Dashboard/DashboardView.xaml`
- Create: `src/AsphaltPlantManager.App/Records/TemplatePickerView.xaml`
- Create: `src/AsphaltPlantManager.App/Records/RecordEditorView.xaml`
- Create: `src/AsphaltPlantManager.App/Records/RecordEditorViewModel.cs`
- Create: `src/AsphaltPlantManager.App/Archives/ArchiveSearchView.xaml`
- Create: `src/AsphaltPlantManager.App/Archives/ArchiveSearchViewModel.cs`
- Create: `src/AsphaltPlantManager.App/MasterData/MasterDataView.xaml`
- Create: `src/AsphaltPlantManager.App/Settings/SettingsView.xaml`
- Create: `src/AsphaltPlantManager.App/Backup/BackupView.xaml`
- Create: `tests/AsphaltPlantManager.App.Tests/Records/RecordEditorViewModelTests.cs`
- Create: `tests/AsphaltPlantManager.App.Tests/Archives/ArchiveSearchViewModelTests.cs`

**Interfaces:**
- Consumes: 前述模板、档案、搜索、输出和备份接口。
- Produces: 左侧导航和可键盘操作的完整中文工作流。

- [ ] **Step 1: 写 ViewModel 自动计算和封存命令失败测试**

```csharp
[Fact]
public async Task Changing_quantity_recalculates_amount_and_seal_validates_required_fields()
{
    var vm = RecordEditorViewModelFixture.Create();
    vm.SetValue("quantity", 10m);
    vm.SetValue("unitPrice", 135.30m);
    vm.GetDecimal("materialAmount").Should().Be(1353m);
    await vm.SealCommand.ExecuteAsync(null);
    vm.ValidationErrors.Should().BeEmpty();
}
```

- [ ] **Step 2: 运行 App 测试确认失败**

Run: `dotnet test tests/AsphaltPlantManager.App.Tests`
Expected: FAIL。

- [ ] **Step 3: 实现主导航和动态表格编辑器**

编辑器按字段定义生成文本、日期、数字、下拉和多行明细控件；数字右对齐，必填项显示中文错误，支持增删行、复制上一张和 1 秒自动保存。

- [ ] **Step 4: 实现档案筛选、版本浏览、会话解封、自动版本保存、回收站和基础资料界面**

危险操作使用明确中文确认框；恢复版本和恢复备份必须显示来源、目标和影响。

- [ ] **Step 5: 实现打印预览、导出和设置入口并运行测试**

Run: `dotnet test AsphaltPlantManager.sln`
Expected: PASS。

- [ ] **Step 6: 手工键盘与 1366×768 分辨率检查并提交**

检查：所有核心操作可仅用键盘完成；窗口最小尺寸下无关键按钮被遮挡；表格横向滚动可用。

```powershell
git add src/AsphaltPlantManager.App tests/AsphaltPlantManager.App.Tests
git commit -m "feat: build desktop management workflow"
```

### Task 10: 设置、演示数据、端到端验收和安装包

**Files:**
- Create: `src/AsphaltPlantManager.App/CompositionRoot.cs`
- Create: `src/AsphaltPlantManager.Infrastructure/Seed/DemoDataSeeder.cs`
- Create: `tests/AsphaltPlantManager.Infrastructure.Tests/Acceptance/BusinessWorkflowTests.cs`
- Create: `installer/AsphaltPlantManager.iss`
- Create: `docs/user-guide.md`
- Create: `scripts/build-release.ps1`

**Interfaces:**
- Produces: `outputs/沥青拌合站经营管理系统-Setup.exe`、便携自包含版本、用户说明和演示数据库。

- [ ] **Step 1: 写完整经营流程验收测试**

```csharp
[Fact]
public async Task Draft_to_session_unlock_auto_version_search_export_backup_restore_round_trip()
{
    var record = await _scenario.CreateReconciliationAsync();
    await _scenario.SealAsync(record.Id, "首次封存");
    await _scenario.UnlockSessionAndSaveArchivedChangeAsync(record.Id, "修正吨数");
    (await _scenario.SearchAsync(customer:"演示客户", specification:"AC-13")).Should().ContainSingle();
    await _scenario.AssertExcelAndPdfAsync(record.Id);
    await _scenario.AssertBackupRestoreAsync();
}
```

- [ ] **Step 2: 运行验收测试确认尚未完成接线**

Run: `dotnet test tests/AsphaltPlantManager.Infrastructure.Tests --filter FullyQualifiedName~BusinessWorkflowTests`
Expected: FAIL。

- [ ] **Step 3: 完成依赖注入、首次启动目录、默认设置和可选演示数据**

首次启动创建数据目录和 12 个模板索引；演示数据只能由用户选择导入，不混入正式数据。

- [ ] **Step 4: 编写中文用户说明和发布脚本**

`build-release.ps1` 必须执行 `dotnet test`、`dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`，然后调用 Inno Setup 生成安装包并计算 SHA-256。

- [ ] **Step 5: 运行全部自动化和实际打印/导出/恢复验收**

Run: `dotnet test AsphaltPlantManager.sln -c Release`
Expected: 全部 PASS。

Run: `powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1`
Expected: 安装包、便携版、用户说明、演示数据库和校验值均生成到 `outputs`。

- [ ] **Step 6: 在干净的 Windows 用户配置中安装并验收**

检查安装、桌面快捷方式、无 Excel/.NET 环境启动、两张照片对应表格的数据录入、封存解封、查询、打印、Excel/PDF、备份恢复和卸载；卸载默认保留用户数据并明确提示。

- [ ] **Step 7: 提交发布内容**

```powershell
git add src tests installer docs scripts
git commit -m "release: package asphalt plant management system"
```

## 计划自检结论

- 设计说明中的 12 个模板、可扩展版本、基础资料快照、草稿、封存解封、历史版本、回收站、组合查询、统计提醒、打印、Excel/PDF、备份恢复、安装包和说明书均有对应任务。
- 首版不实现联网、多用户、手机端、云存储和用户自定义复杂打印设计器。
- 接口名称在产生任务与消费任务之间保持一致；持久化、恢复和封存均要求事务或原子替换。
- 计划未保留待定字段或未指定的错误处理步骤。
