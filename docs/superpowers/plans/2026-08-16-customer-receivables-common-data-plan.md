# 客户欠款与常用资料 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在单机 Excel 工作流中增加一个独立的客户欠款清单，并让客户、供应商、车牌和司机可以从本机常用资料下拉选择。

**Architecture:** 扩展现有 SQLite 基础资料仓储以支持按类别列出和删除；新增独立 `customer_receivables` 表和 Core 服务保存当前欠款。Excel 工作流在生成工作簿时读取常用资料并给对应字段加可编辑下拉验证，WPF 页面提供简单的欠款增删改查和基础资料维护。

**Tech Stack:** .NET 8、WPF、SQLite/Dapper、ClosedXML、CommunityToolkit.Mvvm、xUnit。

## Global Constraints

- 欠款不自动关联生产记录或历史档案，每个客户只维护一条当前记录。
- 剩余欠款为应收金额减已收金额，显示不低于 0；状态为未收、部分收款或已结清。
- 继续使用本机 SQLite，不增加联网、多用户或复杂权限。
- 保留 Excel 直接输入新名称的能力，下拉选择只是快捷方式。

### Task 1: 扩展常用资料仓储

**Files:**
- Modify: `src/AsphaltPlantManager.Core/MasterData/IMasterDataRepository.cs`
- Modify: `src/AsphaltPlantManager.Infrastructure/Database/SqliteMasterDataRepository.cs`
- Modify: `src/AsphaltPlantManager.App/ViewModels/MasterDataViewModel.cs`
- Test: `tests/AsphaltPlantManager.Infrastructure.Tests/Database/SqliteMasterDataRepositoryTests.cs`

- [ ] **Step 1:** 添加 `ListAsync(category)` 和 `DeleteAsync(category, key)` 接口，并用参数化 SQL 实现。
- [ ] **Step 2:** 将基础资料类别扩展为“公司、客户、工程、规格、配合比、供应商、车辆、司机”，增加列表、删除和编辑当前资料的 UI 状态。
- [ ] **Step 3:** 先写仓储测试覆盖按类别列出、更新和删除，再运行该测试项目确认通过。

### Task 2: 新增独立客户欠款数据

**Files:**
- Create: `src/AsphaltPlantManager.Core/Receivables/CustomerReceivable.cs`
- Create: `src/AsphaltPlantManager.Core/Receivables/ICustomerReceivableRepository.cs`
- Create: `src/AsphaltPlantManager.Core/Receivables/CustomerReceivableService.cs`
- Modify: `src/AsphaltPlantManager.Infrastructure/Database/DatabaseInitializer.cs`
- Create: `src/AsphaltPlantManager.Infrastructure/Database/SqliteCustomerReceivableRepository.cs`
- Test: `tests/AsphaltPlantManager.Core.Tests/Receivables/CustomerReceivableTests.cs`
- Test: `tests/AsphaltPlantManager.Infrastructure.Tests/Database/SqliteCustomerReceivableRepositoryTests.cs`

- [ ] **Step 1:** 先写失败测试：金额校验、剩余欠款不低于 0、三种状态、保存/修改/删除/查询。
- [ ] **Step 2:** 添加 `CustomerReceivable`，字段为 `Id、CustomerName、ReceivableAmount、PaidAmount、DueDate、Note、UpdatedAt`，并提供 `RemainingAmount` 与 `Status` 只读计算。
- [ ] **Step 3:** 添加 `customer_receivables` 表和参数化 SQLite 仓储；保存按客户唯一，重复客户更新当前记录。
- [ ] **Step 4:** 运行 Core/Infrastructure 聚焦测试，确认通过。

### Task 3: 给 Excel 模板增加常用资料下拉

**Files:**
- Modify: `src/AsphaltPlantManager.Core/Output/IExcelFormWorkflow.cs`
- Modify: `src/AsphaltPlantManager.Infrastructure/Output/ExcelFormWorkflow.cs`
- Modify: `src/AsphaltPlantManager.App/CompositionRoot.cs`
- Test: `tests/AsphaltPlantManager.Infrastructure.Tests/Output/ExcelFormWorkflowTests.cs`

- [ ] **Step 1:** 将一个只读的 `ICommonDataProvider` 接入 Excel 工作流，按类别提供客户、供应商、车辆、司机名称。
- [ ] **Step 2:** 在生成新模板和可编辑档案模板时，对键为 `customer、supplier、vehiclePlate、vehicle、driver` 的文字列添加 ClosedXML 下拉验证；没有资料时不添加验证，仍可直接输入。
- [ ] **Step 3:** 测试生成工作簿的验证范围和列表内容，并确认导入流程不受影响。

### Task 4: 接入欠款和资料页面

**Files:**
- Create: `src/AsphaltPlantManager.App/ViewModels/ReceivablesViewModel.cs`
- Modify: `src/AsphaltPlantManager.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/AsphaltPlantManager.App/CompositionRoot.cs`
- Modify: `src/AsphaltPlantManager.App/MainWindow.xaml`
- Test: `tests/AsphaltPlantManager.App.Tests/CompositionRootTests.cs`

- [ ] **Step 1:** 添加欠款页面 ViewModel 的加载、保存、删除和客户筛选命令，错误信息用自然语言显示。
- [ ] **Step 2:** 在首页或独立“客户欠款”页面显示清单、合计欠款和编辑区域，删除前弹出确认。
- [ ] **Step 3:** 在基础资料页面增加列表和删除操作，并将供应商、车辆、司机类别接入。
- [ ] **Step 4:** 在应用组合根注册服务和 ViewModel，补充启动解析测试。

### Task 5: 文档、全套验证和提交

**Files:**
- Modify: `docs/user-guide.md`

- [ ] **Step 1:** 写明客户欠款是独立手工清单，以及 Excel 下拉资料的使用方法。
- [ ] **Step 2:** 运行 `dotnet test AsphaltPlantManager.sln --configuration Release`、Release 构建和 `git diff --check`。
- [ ] **Step 3:** 只提交本轮功能文件，不生成安装包，不触碰用户的 `outputs/` 和 `work/`。
