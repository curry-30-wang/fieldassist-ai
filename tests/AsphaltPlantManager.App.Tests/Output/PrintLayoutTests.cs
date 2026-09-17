using System.Printing;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using AsphaltPlantManager.App.Output;
using AsphaltPlantManager.Core.Output;
using AsphaltPlantManager.Infrastructure.Output;
using Xunit;

namespace AsphaltPlantManager.App.Tests.Output;

public sealed class PrintLayoutTests
{
    [Fact]
    public void Shared_layout_is_semantic_a4_landscape_content_without_guessed_pages()
    {
        var layout = new RecordLayoutBuilder().Build(TestSnapshots.Reconciliation(70));

        Assert.Equal(OutputOrientation.Landscape, layout.Orientation);
        Assert.InRange(layout.PageWidthPoints, 841.88, 841.90);
        Assert.InRange(layout.PageHeightPoints, 595.27, 595.29);
        Assert.Equal(36, layout.MarginPoints);
        Assert.Equal(70, layout.Rows.Count);
        Assert.Contains("客户", layout.ColumnHeaders);
        Assert.Contains("规格", layout.ColumnHeaders);
        Assert.Contains("DZ-202608-0001", layout.FooterMetadata);
        Assert.Contains("v3", layout.FooterMetadata);
        Assert.Contains("2026-08-07 08:00", layout.Metadata);
        Assert.Contains("2026-08-07 10:30", layout.Metadata);
    }

    [Fact]
    public void Flow_document_paginator_uses_actual_content_height_and_keeps_tail_content()
    {
        RunOnSta(() =>
        {
            var layout = new RecordLayoutBuilder().Build(TestSnapshots.Reconciliation(140, longText: true));
            var document = WpfPrintService.CreateFlowDocument(layout);
            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;

            paginator.ComputePageCount();

            Assert.True(paginator.PageCount > 1);
            var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
            Assert.Contains("合计", text, StringComparison.Ordinal);
            Assert.Contains("备注", text, StringComparison.Ordinal);
            Assert.Contains("制表", text, StringComparison.Ordinal);
            Assert.Contains("盖章区域", text, StringComparison.Ordinal);
            Assert.Contains("2026-08-07 08:00", text, StringComparison.Ordinal);
            Assert.Contains("2026-08-07 10:30", text, StringComparison.Ordinal);
            Assert.DoesNotContain(layout.FooterMetadata, text, StringComparison.Ordinal);
            Assert.IsType<System.Windows.Documents.Table>(document.Blocks.LastBlock);
        });
    }

    [Fact]
    public void Header_footer_paginator_adds_complete_physical_page_chrome_without_covering_content()
    {
        RunOnSta(() =>
        {
            var layout = new RecordLayoutBuilder().Build(TestSnapshots.Reconciliation(140, longText: true));
            var paginator = WpfPrintService.CreatePaginator(layout);

            paginator.ComputePageCount();

            Assert.IsType<HeaderFooterDocumentPaginator>(paginator);
            Assert.True(paginator.PageCount > 1);
            for (var index = 0; index < paginator.PageCount; index++)
            {
                var page = paginator.GetPage(index);
                var text = ReadVisualText(page.Visual);
                Assert.Contains("日期", text, StringComparison.Ordinal);
                Assert.Contains("金额", text, StringComparison.Ordinal);
                Assert.Contains("DZ-202608-0001", text, StringComparison.Ordinal);
                Assert.Contains("v3", text, StringComparison.Ordinal);
                Assert.Contains("2026-08-07 08:00", text, StringComparison.Ordinal);
                Assert.Contains("2026-08-07 10:30", text, StringComparison.Ordinal);
                Assert.Contains($"第 {index + 1} 页 / 共 {paginator.PageCount} 页", text, StringComparison.Ordinal);
                Assert.Equal(new Rect(paginator.PageSize), page.ContentBox);
                Assert.Equal(new Rect(paginator.PageSize), page.BleedBox);
            }
        });
    }

    [Fact]
    public void Header_footer_paginator_defensively_computes_page_count_before_drawing()
    {
        RunOnSta(() =>
        {
            var paginator = WpfPrintService.CreatePaginator(
                new RecordLayoutBuilder().Build(TestSnapshots.Reconciliation(80)));

            var firstPage = paginator.GetPage(0);

            Assert.True(paginator.IsPageCountValid);
            Assert.Contains($"第 1 页 / 共 {paginator.PageCount} 页", ReadVisualText(firstPage.Visual), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Wpf_signature_and_stamp_cells_have_room_for_handwriting()
    {
        RunOnSta(() =>
        {
            var document = WpfPrintService.CreateFlowDocument(new RecordLayoutBuilder().Build(TestSnapshots.Reconciliation(3)));
            var signatureTable = document.Blocks.OfType<System.Windows.Documents.Table>().Last();
            var writingAreas = signatureTable.RowGroups[0].Rows[0].Cells
                .SelectMany(cell => cell.Blocks.OfType<BlockUIContainer>())
                .Select(container => Assert.IsAssignableFrom<FrameworkElement>(container.Child))
                .ToArray();

            Assert.NotEmpty(writingAreas);
            Assert.All(writingAreas, area => Assert.True(area.MinHeight >= 80));
        });
    }

    [Fact]
    public async Task Accepted_print_runs_dialog_document_creation_and_adapter_on_sta_without_a_real_job()
    {
        var dialog = new RecordingPrintDialog(true);

        await RunOnStaAsync(() => new WpfPrintService(new RecordLayoutBuilder(), dialog)
            .PrintAsync(TestSnapshots.Reconciliation(80), new PrinterSettings(), CancellationToken.None));

        Assert.Equal(1, dialog.ShowCount);
        Assert.Equal(1, dialog.PrintCount);
        Assert.Equal(ApartmentState.STA, dialog.ApartmentState);
        Assert.True(dialog.PageCount > 1);
        Assert.True(dialog.AllPagesHaveChrome);
        Assert.True(dialog.PageCountWasValidAtPrint);
    }

    [Fact]
    public async Task Accepted_print_uses_the_printer_printable_area_and_safe_page_chrome()
    {
        var dialog = new RecordingPrintDialog(true, 760, 520);

        await RunOnStaAsync(() => new WpfPrintService(new RecordLayoutBuilder(), dialog)
            .PrintAsync(TestSnapshots.Reconciliation(80), new PrinterSettings(), CancellationToken.None));

        Assert.Equal(new Size(760, 520), dialog.PageSize);
        Assert.True(dialog.PageCountWasValidAtPrint);
        Assert.True(dialog.AllVisualBoundsAreInsidePage);
        Assert.True(dialog.AllContentStaysBetweenChrome);
        Assert.True(dialog.AllChromeUsesSafeMargin);
    }

    [Theory]
    [InlineData(73d, 125d, true)]
    [InlineData(760d, 198d, true)]
    [InlineData(double.NaN, 520d, true)]
    [InlineData(0d, 0d, true)]
    [InlineData(760d, 340d, false)]
    [InlineData(760d, 520d, false)]
    public async Task Print_uses_a4_when_the_printer_area_cannot_fit_safe_chrome_and_body(
        double printableAreaWidth,
        double printableAreaHeight,
        bool shouldUseA4)
    {
        var dialog = new RecordingPrintDialog(true, printableAreaWidth, printableAreaHeight);

        await RunOnStaAsync(() => new WpfPrintService(new RecordLayoutBuilder(), dialog)
            .PrintAsync(TestSnapshots.Reconciliation(3), new PrinterSettings(), CancellationToken.None));

        if (shouldUseA4)
        {
            Assert.InRange(dialog.PageSize.Width, 1122.5d, 1122.6d);
            Assert.InRange(dialog.PageSize.Height, 793.6d, 793.8d);
        }
        else
        {
            Assert.Equal(new Size(printableAreaWidth, printableAreaHeight), dialog.PageSize);
            var layout = new RecordLayoutBuilder().Build(TestSnapshots.Reconciliation(3));
            var usableBodyHeight = dialog.PageSize.Height
                - HeaderFooterDocumentPaginator.HeaderHeight
                - HeaderFooterDocumentPaginator.FooterHeight
                - (layout.MarginPoints * 96d / 72d * 2);
            Assert.True(usableBodyHeight >= HeaderFooterDocumentPaginator.MinimumBodyHeight);
        }
    }

    [Fact]
    public async Task Print_cancellation_during_pagination_does_not_call_the_print_adapter()
    {
        using var cancellationSource = new CancellationTokenSource();
        var dialog = new RecordingPrintDialog(true);
        var service = new WpfPrintService(
            new RecordLayoutBuilder(),
            dialog,
            (_, _) => new CancellingPaginator(cancellationSource));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunOnStaAsync(() =>
            service.PrintAsync(TestSnapshots.Reconciliation(3), new PrinterSettings(), cancellationSource.Token)));

        Assert.Equal(0, dialog.PrintCount);
    }

    [Fact]
    public async Task Cancelled_print_dialog_is_a_normal_no_op_on_sta()
    {
        var dialog = new RecordingPrintDialog(false);

        await RunOnStaAsync(() => new WpfPrintService(new RecordLayoutBuilder(), dialog)
            .PrintAsync(TestSnapshots.Reconciliation(3), new PrinterSettings(), CancellationToken.None));

        Assert.Equal(1, dialog.ShowCount);
        Assert.Equal(0, dialog.PrintCount);
    }

    [Fact]
    public async Task Printing_without_an_application_or_sta_context_has_a_chinese_actionable_error()
    {
        var service = new WpfPrintService(new RecordLayoutBuilder(), new RecordingPrintDialog(false));

        var error = await Assert.ThrowsAsync<OutputException>(() => Task.Run(() =>
            service.PrintAsync(TestSnapshots.Reconciliation(1), new PrinterSettings(), CancellationToken.None)));

        Assert.Contains("STA", error.Message, StringComparison.Ordinal);
        Assert.Contains("打印", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_failure_is_wrapped_for_the_user_and_keeps_the_cause()
    {
        var service = new WpfPrintService(new RecordLayoutBuilder(), new RecordingPrintDialog(false));

        var error = await Assert.ThrowsAsync<OutputException>(() =>
            service.PreviewAsync(TestSnapshots.Reconciliation(1, payloadJson: "{"), CancellationToken.None));

        Assert.Contains("预览", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void Shared_layout_only_totals_explicitly_summable_fields()
    {
        var layout = new RecordLayoutBuilder().Build(TestSnapshots.Reconciliation(3));

        Assert.Contains(layout.Totals, total => total.StartsWith("数量：", StringComparison.Ordinal));
        Assert.Contains(layout.Totals, total => total.StartsWith("金额：", StringComparison.Ordinal));
        Assert.DoesNotContain(layout.Totals, total => total.StartsWith("单价：", StringComparison.Ordinal));
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }

    private static Task RunOnStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action().GetAwaiter().GetResult();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static string ReadVisualText(Visual visual)
    {
        var text = new List<char>();
        VisitVisual(visual, text);
        return new string(text.ToArray());
    }

    private static void VisitVisual(Visual visual, ICollection<char> text)
    {
        if (visual is DrawingVisual drawingVisual)
        {
            VisitDrawing(drawingVisual.Drawing, text);
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
        {
            if (VisualTreeHelper.GetChild(visual, index) is Visual child)
            {
                VisitVisual(child, text);
            }
        }
    }

    private static void VisitDrawing(Drawing? drawing, ICollection<char> text)
    {
        switch (drawing)
        {
            case GlyphRunDrawing glyphs:
                foreach (var character in glyphs.GlyphRun.Characters)
                {
                    text.Add(character);
                }
                break;
            case DrawingGroup group:
                foreach (var child in group.Children)
                {
                    VisitDrawing(child, text);
                }
                break;
        }
    }

    private sealed class RecordingPrintDialog(
        bool accepted,
        double printableAreaWidth = 1000,
        double printableAreaHeight = 700) : IPrintDialogAdapter
    {
        public int ShowCount { get; private set; }
        public int PrintCount { get; private set; }
        public int PageCount { get; private set; }
        public ApartmentState ApartmentState { get; private set; }
        public bool AllPagesHaveChrome { get; private set; }
        public bool PageCountWasValidAtPrint { get; private set; }
        public bool AllVisualBoundsAreInsidePage { get; private set; }
        public bool AllContentStaysBetweenChrome { get; private set; }
        public bool AllChromeUsesSafeMargin { get; private set; }
        public Size PageSize { get; private set; }
        public double PrintableAreaWidth => printableAreaWidth;
        public double PrintableAreaHeight => printableAreaHeight;

        public bool? ShowDialog(PrinterSettings settings)
        {
            ShowCount++;
            ApartmentState = Thread.CurrentThread.GetApartmentState();
            return accepted;
        }

        public void PrintDocument(DocumentPaginator paginator, string description)
        {
            PrintCount++;
            ApartmentState = Thread.CurrentThread.GetApartmentState();
            PageCountWasValidAtPrint = paginator.IsPageCountValid;
            PageCount = paginator.PageCount;
            PageSize = paginator.PageSize;
            AllPagesHaveChrome = Enumerable.Range(0, PageCount).All(index =>
            {
                var text = ReadVisualText(paginator.GetPage(index).Visual);
                return text.Contains("日期", StringComparison.Ordinal) &&
                       text.Contains("DZ-202608-0001", StringComparison.Ordinal) &&
                       text.Contains($"第 {index + 1} 页 / 共 {PageCount} 页", StringComparison.Ordinal);
            });
            AllVisualBoundsAreInsidePage = Enumerable.Range(0, PageCount).All(index =>
            {
                var bounds = VisualTreeHelper.GetDescendantBounds(paginator.GetPage(index).Visual);
                return bounds.Left >= 0 && bounds.Top >= 0 &&
                       bounds.Right <= PageSize.Width && bounds.Bottom <= PageSize.Height;
            });
            AllContentStaysBetweenChrome = Enumerable.Range(0, PageCount).All(index =>
            {
                var visual = paginator.GetPage(index).Visual;
                var content = Assert.IsAssignableFrom<Visual>(VisualTreeHelper.GetChild(visual, 0));
                var bounds = VisualTreeHelper.GetDescendantBounds(content);
                return bounds.Top >= HeaderFooterDocumentPaginator.HeaderHeight &&
                       bounds.Bottom <= PageSize.Height - HeaderFooterDocumentPaginator.FooterHeight;
            });
            AllChromeUsesSafeMargin = Enumerable.Range(0, PageCount).All(index =>
            {
                var visual = paginator.GetPage(index).Visual;
                var chrome = Assert.IsAssignableFrom<Visual>(VisualTreeHelper.GetChild(visual, 1));
                var bounds = VisualTreeHelper.GetDescendantBounds(chrome);
                return bounds.Left >= HeaderFooterDocumentPaginator.SafeMargin &&
                       bounds.Top >= HeaderFooterDocumentPaginator.SafeMargin &&
                       bounds.Right <= PageSize.Width - HeaderFooterDocumentPaginator.SafeMargin &&
                       bounds.Bottom <= PageSize.Height - HeaderFooterDocumentPaginator.SafeMargin;
            });
        }
    }

    private sealed class CancellingPaginator(CancellationTokenSource cancellationSource) : DocumentPaginator
    {
        private bool _isPageCountValid;

        public override bool IsPageCountValid => _isPageCountValid;

        public override int PageCount => 1;

        public override IDocumentPaginatorSource? Source => null;

        public override Size PageSize { get; set; } = new(760, 520);

        public override void ComputePageCount()
        {
            _isPageCountValid = true;
            cancellationSource.Cancel();
        }

        public override DocumentPage GetPage(int pageNumber) => DocumentPage.Missing;
    }
}

internal static class TestSnapshots
{
    public static OutputRecordSnapshot Reconciliation(
        int rowCount,
        bool longText = false,
        string? payloadJson = null)
    {
        var template = new AsphaltPlantManager.Core.Templates.TemplateDefinition(
            "customer-reconciliation", "客户/工程对账单", "销售结算", 1, "DZ",
            [
                new("date", "日期", AsphaltPlantManager.Core.Templates.FieldDataType.Date, true),
                new("customer", "客户", AsphaltPlantManager.Core.Templates.FieldDataType.Text, true),
                new("specification", "规格", AsphaltPlantManager.Core.Templates.FieldDataType.Text, true),
                new("quantity", "数量", AsphaltPlantManager.Core.Templates.FieldDataType.Decimal, true, IsSummable: true),
                new("unitPrice", "单价", AsphaltPlantManager.Core.Templates.FieldDataType.Decimal, true),
                new("amount", "金额", AsphaltPlantManager.Core.Templates.FieldDataType.Decimal, IsSummable: true)
            ],
            [new("amount", AsphaltPlantManager.Core.Templates.FormulaOperator.Multiply, ["quantity", "unitPrice"])],
            ["customer"], new("A4", "Landscape"));
        var customer = longText
            ? "华东路桥\n" + string.Concat(Enumerable.Repeat("按实际内容高度自动换行分页", 5))
            : "华东路桥";
        var rows = Enumerable.Range(0, rowCount).Select(index => new
        {
            date = $"2026-08-{index % 28 + 1:00}",
            customer,
            specification = "AC-13",
            quantity = 238.14,
            unitPrice = 135.30
        });
        var payload = payloadJson ?? System.Text.Json.JsonSerializer.Serialize(new
        {
            rows,
            remarks = "本期数据已经双方核对。"
        });
        return new OutputRecordSnapshot(
            "DZ-202608-0001", 3, template, new(2026, 8, 1), new(2026, 8, 31),
            new Dictionary<string, string> { ["companyName"] = "华东沥青有限公司" },
            payload, "修正", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), DateTimeOffset.Parse("2026-08-07T10:30:00+08:00"));
    }
}
