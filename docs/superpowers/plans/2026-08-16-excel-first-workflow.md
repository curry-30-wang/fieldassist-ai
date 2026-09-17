# Excel-First Form Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make Excel the primary form editor while the desktop app only creates templates, opens them, imports completed workbooks, archives versions, searches records, and backs up data.

**Architecture:** Generate standard `.xlsx` workbooks with ClosedXML from the existing JSON template definitions. Each workbook contains a metadata sheet, a readable title/header area, editable detail rows, formula cells, validation hints, filters, frozen headers, and print settings. Import reads the metadata and non-empty detail rows back into the existing `FormRecord` payload as `{ "rows": [...] }`; the app validates the imported values, creates or updates the record, and uses the existing archive/version services. The old WPF cell editor is hidden from the main workflow but the existing archive, search, output, and backup services remain authoritative.

**Tech Stack:** C#/.NET 8 WPF, SQLite, ClosedXML, existing Core/Infrastructure/App projects, xUnit.

## Global Constraints

- Single Windows computer and one operator; no cloud, multi-user, or Excel add-in.
- Do not generate an installer during this feature; only source changes and tests.
- Excel is opened as a normal `.xlsx` file with `UseShellExecute=true`; Microsoft Excel is required for editing.
- Existing archived records and version history must remain readable.
- Imported rows must be validated before sealing; invalid workbooks must not create or overwrite records.

### Task 1: Define the Excel draft/import contract

**Files:**
- Create: `src/AsphaltPlantManager.Core/Output/ExcelFormDraft.cs`
- Create: `src/AsphaltPlantManager.Core/Output/IExcelFormWorkflow.cs`
- Test: `tests/AsphaltPlantManager.Core.Tests/Output/ExcelFormDraftTests.cs`

**Interfaces:**
- `IExcelFormWorkflow.CreateDraftAsync(TemplateDefinition template, string companyName, DateOnly periodStart, DateOnly periodEnd, string destinationDirectory, CancellationToken)` returns the created `.xlsx` path.
- `IExcelFormWorkflow.ImportAsync(string workbookPath, CancellationToken)` returns `ImportedExcelForm` containing template id/version, period dates, header snapshot, JSON payload, and search text.

- [x] Write tests for metadata round-trip shape and rejection of missing template metadata.
- [ ] Run the focused Core tests and observe the missing contract failure.
- [x] Add immutable records/interfaces with explicit argument validation.
- [ ] Run focused Core tests and commit the contract.

### Task 2: Generate editable Excel templates

**Files:**
- Create: `src/AsphaltPlantManager.Infrastructure/Output/ExcelFormWorkflow.cs`
- Modify: `src/AsphaltPlantManager.Infrastructure/Output/ExcelRecordExporter.cs` only for shared styling/formula helpers if extraction is required.
- Test: `tests/AsphaltPlantManager.Infrastructure.Tests/Output/ExcelFormWorkflowTests.cs`

**Interfaces:**
- Consumes `ITemplateCatalog`-compatible `TemplateDefinition` values and `IExcelFormWorkflow`.
- Produces a workbook with `__FormMeta` metadata, a visible form sheet, 30 editable detail rows, formulas copied down, editable column widths, filters, frozen heading row, and A4 print settings.

- [x] Write failing tests for metadata, template title, 30 editable rows, formula cells, and stable output path.
- [ ] Run focused tests and verify failure before implementation.
- [ ] Implement workbook creation with ClosedXML, typed cells, formulas, data validation only as a suggestion (not a hard restriction), and atomic file output.
- [x] Run focused tests including workbook reopen with ClosedXML and verify all assertions pass.
- [ ] Commit the generator.

### Task 3: Import completed Excel workbooks into the existing record model

**Files:**
- Modify: `src/AsphaltPlantManager.Infrastructure/Output/ExcelFormWorkflow.cs`
- Modify: `src/AsphaltPlantManager.Core/Templates/TemplateEngine.cs` only if imported Excel typed values need a shared conversion helper.
- Test: `tests/AsphaltPlantManager.Infrastructure.Tests/Output/ExcelFormWorkflowTests.cs`

**Interfaces:**
- Reads `__FormMeta` and the visible form sheet; ignores completely blank detail rows; preserves every non-empty row in payload `{ "rows": [...] }`.
- Uses the existing template field definitions and validation rules; rejects missing required cells, invalid date/number cells, wrong template version, or malformed metadata with a user-readable `OutputException`.

- [x] Add tests for two-row round-trip, custom specification text, and calculated values.
- [ ] Run focused tests and verify the expected failures.
- [x] Implement import conversion and search-text/header extraction without changing archived record APIs.
- [ ] Run focused tests and verify the invalid workbook never produces a record.
- [ ] Commit the importer.

### Task 4: Replace the WPF new-form editor with Excel actions

**Files:**
- Modify: `src/AsphaltPlantManager.App/CompositionRoot.cs`
- Modify: `src/AsphaltPlantManager.App/ViewModels/FormEditorViewModel.cs`
- Modify: `src/AsphaltPlantManager.App/MainWindow.xaml`
- Modify: `src/AsphaltPlantManager.App/AsphaltPlantManager.App.csproj` only if a dialog dependency is needed (prefer existing WPF dialogs).
- Test: `tests/AsphaltPlantManager.App.Tests/FormEditorViewModelTests.cs`

**Interfaces:**
- View model exposes `CreateExcelDraftCommand`, `OpenDraftCommand`, and `ImportExcelAndSealCommand`.
- `CreateExcelDraftCommand` creates a draft workbook and opens it with the Windows associated Excel application.
- `ImportExcelAndSealCommand` selects a saved workbook, imports it, validates it, then creates a draft record and calls the existing `ArchiveService.SealAsync`; an imported workbook is copied beside the archived output for later reference.

- [ ] Write failing ViewModel tests for command status messages, invalid import blocking, and successful import creating one archived record with multiple rows.
- [ ] Run App tests and observe missing command/DI failures.
- [x] Register the workflow service, wire commands, and simplify the page to template/date selection plus Excel buttons and instructions; remove the crowded WPF DataGrid from the normal path.
- [x] Run App build/tests and verify commands use injected abstractions rather than hard-coded Excel automation.
- [ ] Commit the Excel-first UI.

### Task 5: End-to-end verification and user documentation

**Files:**
- Modify: `docs/user-guide.md`
- Test: `tests/AsphaltPlantManager.Infrastructure.Tests/Acceptance/BusinessWorkflowTests.cs` or the existing acceptance test location.

- [ ] Add an acceptance test covering create workbook → import two rows → validate → seal → search → export.
- [x] Run `dotnet test AsphaltPlantManager.sln -c Release --no-restore` and confirm all tests pass.
- [x] Reopen generated workbooks with ClosedXML and verify editable rows, formulas, filters, and print settings.
- [x] Update the Chinese guide to state that Excel is required for editing and that the app archives the saved workbook.
- [x] Run `git diff --check`; no installer was generated.
