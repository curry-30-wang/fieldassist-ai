# Excel 工作流效率改进实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不加入自动备份提醒的前提下，增加 Excel 导入预览、草稿管理、档案 Excel 修改、只保留最近两版、规格记忆、模板收藏和公司名称自动显示。

**Architecture:** 继续复用现有 `IExcelFormWorkflow`、`ArchiveService`、`IRecordRepository` 和 `IMasterDataRepository`。Excel 元数据增加可选的档案 ID/版本信息；预览只保存在 ViewModel 内存中，确认后才写数据库；版本清理在 SQLite 事务中完成；规格和收藏使用现有基础资料表保存，不新增数据库表。

**Tech Stack:** .NET 8 WPF、ClosedXML、SQLite/Dapper、CommunityToolkit.Mvvm、xUnit。

## Global Constraints

- 只实现用户选定的 7 项功能，不实现自动备份提醒。
- 修改封存档案时必须先解锁；每个档案只保留最新两版。
- Excel 导入失败不得写入 records 或 record_versions。
- 不增加云端、登录、多用户、第三方 UI 框架或复杂图表。
- 不生成安装包；Release 全量测试通过后再等待用户后续指示。

---

### Task 1: Excel 元数据与导入预览契约

**Files:**
- Modify: `src/AsphaltPlantManager.Core/Output/ExcelFormDraft.cs`
- Modify: `src/AsphaltPlantManager.Core/Output/IExcelFormWorkflow.cs`
- Test: `tests/AsphaltPlantManager.Core.Tests/Output/ExcelFormDraftTests.cs`

- [ ] 为 `ImportedExcelForm` 增加可选 `RecordId`、`ArchiveNumber`、`ArchiveVersion`；旧构造方式保持兼容。
- [ ] 为 `IExcelFormWorkflow` 增加 `CreateEditableDraftAsync(OutputRecordSnapshot snapshot, string destinationDirectory, CancellationToken)`。
- [ ] 增加契约测试：普通导入没有档案 ID，档案编辑导入能带回同一 ID 和版本。
- [ ] 运行 Core 聚焦测试，确认通过。

### Task 2: Excel 工作簿往返与档案编辑

**Files:**
- Modify: `src/AsphaltPlantManager.Infrastructure/Output/ExcelFormWorkflow.cs`
- Modify: `src/AsphaltPlantManager.Infrastructure/Output/ExcelRecordExporter.cs`（仅抽取/复用可编辑元数据，不改变既有导出格式）
- Test: `tests/AsphaltPlantManager.Infrastructure.Tests/Output/ExcelFormWorkflowTests.cs`

- [ ] 为可编辑档案工作簿写入 `__FormMeta` 的 `RecordId`、档案号和版本，同时保留现有字段、公式、筛选和打印设置。
- [ ] 从现有 `OutputRecordSnapshot.PayloadJson` 填充数据行，用户可修改后重新导入。
- [ ] 让 `ImportAsync` 读取这些可选元数据，并继续使用同一套必填项、数字和公式校验。
- [ ] 增加两行往返和损坏元数据测试；坏文件必须在返回前抛出 `OutputException`。
- [ ] 运行 Infrastructure 聚焦测试，确认通过。

### Task 3: 只保留最近两版

**Files:**
- Modify: `src/AsphaltPlantManager.Infrastructure/Database/SqliteArchiveRepository.cs`
- Test: `tests/AsphaltPlantManager.Infrastructure.Tests/Database/SqliteArchiveRepositoryTests.cs`

- [ ] 在 `SealAsync`、`SaveArchivedEditAsync`、`RestoreVersionAsync` 插入新版本后，事务内执行 `DELETE FROM record_versions WHERE record_id=@Id AND version < (SELECT MAX(version)-1 ...)`。
- [ ] 增加 v1→v2 保留两版、v3 删除 v1、恢复后仍只保留两版的测试。
- [ ] 确认删除旧版本不会删除当前 `records` 内容，且事务失败时版本和记录一起回滚。

### Task 4: 草稿、规格记忆、模板收藏和公司名称

**Files:**
- Create: `src/AsphaltPlantManager.App/Services/LocalFormPreferences.cs`
- Modify: `src/AsphaltPlantManager.App/ViewModels/FormEditorViewModel.cs`
- Modify: `src/AsphaltPlantManager.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AsphaltPlantManager.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/AsphaltPlantManager.App/CompositionRoot.cs`
- Test: `tests/AsphaltPlantManager.App.Tests/CompositionRootTests.cs`

- [ ] 用基础资料类别“系统偏好”保存收藏模板 ID 和规格建议 JSON；不新增数据库表。
- [ ] 在新建表格加载时扫描 Excel 草稿目录，暴露最近文件、打开文件和打开文件夹命令。
- [ ] 导入成功后把非空规格合并到规格建议；允许直接输入新规格。
- [ ] 增加收藏/取消收藏命令，收藏模板排在未收藏模板前面。
- [ ] 启动时异步读取公司名称，让顶部栏、Excel 生成和设置页显示同一名称。

### Task 5: 预览确认和档案 Excel 新版本流程

**Files:**
- Modify: `src/AsphaltPlantManager.App/ViewModels/FormEditorViewModel.cs`
- Modify: `src/AsphaltPlantManager.App/ViewModels/ArchiveViewModel.cs`
- Modify: `src/AsphaltPlantManager.App/MainWindow.xaml`
- Modify: `src/AsphaltPlantManager.App/ViewModels/MainWindowViewModel.cs`
- Test: `tests/AsphaltPlantManager.App.Tests/CompositionRootTests.cs`

- [ ] 将“导入 Excel 并封存”改为先选择并预览；显示模板、期间、行数和规格摘要，预览对象只留在内存。
- [ ] 增加“确认并封存”命令，确认后创建记录、首次封存并复制 Excel；取消或校验失败不写数据库。
- [ ] 档案页增加“导出可编辑 Excel”和“导入修改后的 Excel”命令；导入必须匹配当前档案 ID 并先通过会话解锁。
- [ ] 修改成功后调用 `SaveArchivedEditAsync` 生成新版本，并刷新版本列表；旧版本按 Task 3 规则清理。
- [ ] XAML 增加预览卡片、草稿列表、收藏按钮和档案编辑按钮，保持整行导航与现有业务绑定。

### Task 6: 验证、说明与收尾

**Files:**
- Modify: `docs/user-guide.md`

- [ ] 更新使用说明：导入先预览、档案 Excel 修改、只保留两版、草稿/收藏/规格建议位置。
- [ ] 运行 `dotnet build src/AsphaltPlantManager.App/AsphaltPlantManager.App.csproj --no-restore`。
- [ ] 运行 `dotnet test tests/AsphaltPlantManager.Core.Tests/AsphaltPlantManager.Core.Tests.csproj --no-restore`。
- [ ] 运行 `dotnet test tests/AsphaltPlantManager.Infrastructure.Tests/AsphaltPlantManager.Infrastructure.Tests.csproj --no-restore`。
- [ ] 运行 `dotnet test tests/AsphaltPlantManager.App.Tests/AsphaltPlantManager.App.Tests.csproj --no-restore`。
- [ ] 运行 `dotnet test AsphaltPlantManager.sln --configuration Release` 和 `git diff --check`；不生成安装包。
