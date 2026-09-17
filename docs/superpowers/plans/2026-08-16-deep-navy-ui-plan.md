# 深蓝工业风前端实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改变业务逻辑的前提下，把 WPF 主窗口改成层级清楚、卡片化、适合沥青拌合站场景的深蓝工业风界面。

**Architecture:** 保留现有 `MainWindowViewModel`、各页面 ViewModel 和命令绑定，只新增导航项分组/激活状态，并在 `MainWindow.xaml` 内集中定义颜色、按钮、卡片、输入框和表格样式。页面数据仍全部来自现有绑定，避免新增服务和数据库字段。

**Tech Stack:** .NET 8 WPF、XAML、CommunityToolkit.Mvvm、现有 xUnit 测试。

## Global Constraints

- 只修改 WPF 前端视觉与导航展示，不修改 Excel、档案、搜索、备份和封存业务。
- 单机单用户，不增加登录、云端、多用户或第三方 UI 框架。
- 保留导航整行点击、现有页面名称、现有命令绑定和窗口最小尺寸。
- 不生成安装包；完成后运行 App build、现有 App tests 和 Release 全量测试。

---

### Task 1: 导航分组与当前页状态

**Files:**
- Modify: `src/AsphaltPlantManager.App/ViewModels/NavigationItem.cs`
- Modify: `src/AsphaltPlantManager.App/ViewModels/MainWindowViewModel.cs`
- Test: `tests/AsphaltPlantManager.App.Tests/CompositionRootTests.cs`

**Interfaces:**
- `NavigationItem` 增加 `Group`、`ShowGroupHeader` 和可通知的 `IsActive` 属性；原有 `Title`、`Icon` 保留。
- `MainWindowViewModel.NavigationItems` 仍包含 6 个可导航页面，导航命令继续接收页面标题字符串。

- [x] **Step 1: 先增加导航状态回归断言**：在现有 App 测试中断言 6 个页面仍存在，并验证初始“首页”处于激活状态。
- [x] **Step 2: 运行聚焦 App 测试确认断言先失败**。
- [x] **Step 3: 实现 `NavigationItem` 可通知状态和 4 个分组标题**：首页=工作台；新建表格/档案=表格与档案；基础资料=基础资料；备份/设置=系统管理；`NavigateAsync` 每次切换时更新所有 `IsActive`。
- [x] **Step 4: 运行聚焦 App 测试确认通过**。

### Task 2: 共享深蓝工业风样式

**Files:**
- Modify: `src/AsphaltPlantManager.App/MainWindow.xaml`

**Interfaces:**
- 新增资源键：`PageBackgroundBrush`、`SidebarBrush`、`SidebarHoverBrush`、`AccentBrush`、`SuccessBrush`、`WarningBrush`、`CardStyle`、`PrimaryButton`、`SecondaryButton`、`NavButton`、`InputBox`、`SectionTitle`。
- 不新增代码后置事件；现有 `PasswordBox_OnPasswordChanged` 保持不变。

- [x] **Step 1: 用 XAML 资源定义颜色、圆角卡片、阴影、按钮悬停/按下状态、输入框焦点状态和 DataGrid 表头样式**。
- [x] **Step 2: 重排左侧导航为品牌区、分组标题、整行可点击按钮和底部“数据保存在本机”提示；激活项绑定 `IsActive`。
- [x] **Step 3: 增加右侧顶部标题栏和底部状态栏，绑定 `CurrentPage`、`Settings.CompanyName`、`StatusMessage`，不增加新的业务状态。

### Task 3: 页面卡片化与视觉验证

**Files:**
- Modify: `src/AsphaltPlantManager.App/MainWindow.xaml`
- Modify: `docs/user-guide.md` only if the new navigation labels need documenting.

**Interfaces:**
- 首页、Excel 新建、档案、基础资料、备份、设置继续使用现有 ViewModel 属性和命令。

- [x] **Step 1: 将首页改为指标卡片、快捷操作卡片、提醒横幅和最近档案卡片；只使用现有 Dashboard 数据。
- [x] **Step 2: 将新建表格改为“选择模板/日期 → Excel 填写 → 导入封存”三步卡片，保留现有两个 Excel 命令和 ChangeNote 绑定。
- [x] **Step 3: 将档案查询、结果表格、版本/打印操作拆成三张卡片；将基础资料、备份、设置分别放入白色内容卡片。
- [x] **Step 4: 运行 `dotnet build src/AsphaltPlantManager.App/AsphaltPlantManager.App.csproj --no-restore`，修复所有 XAML 编译错误。
- [x] **Step 5: 运行 `dotnet test tests/AsphaltPlantManager.App.Tests/AsphaltPlantManager.App.Tests.csproj --no-restore` 和 `dotnet test AsphaltPlantManager.sln --no-restore --configuration Release`。
- [x] **Step 6: 运行 `git diff --check`，确认工作树只包含本次 UI 改动；不生成安装包。
