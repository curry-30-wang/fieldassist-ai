using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using AsphaltPlantManager.Core.Output;
using AsphaltPlantManager.Infrastructure.Output;

namespace AsphaltPlantManager.App.Output;

public interface IPrintDialogAdapter
{
    double PrintableAreaWidth { get; }

    double PrintableAreaHeight { get; }

    bool? ShowDialog(PrinterSettings settings);

    void PrintDocument(DocumentPaginator paginator, string description);
}

public sealed class WpfPrintService : IPrintService
{
    private const double DipsPerPoint = 96d / 72d;
    private const double MinimumPrintableAreaWidth = 240d;
    private readonly RecordLayoutBuilder _layoutBuilder;
    private readonly IPrintDialogAdapter _dialog;
    private readonly Func<PrintPreviewDocument, Size, DocumentPaginator> _paginatorFactory;

    public WpfPrintService()
        : this(new RecordLayoutBuilder(), new WindowsPrintDialogAdapter())
    {
    }

    public WpfPrintService(RecordLayoutBuilder layoutBuilder, IPrintDialogAdapter dialog)
        : this(layoutBuilder, dialog, (layout, pageSize) => CreatePaginator(layout, pageSize))
    {
    }

    public WpfPrintService(
        RecordLayoutBuilder layoutBuilder,
        IPrintDialogAdapter dialog,
        Func<PrintPreviewDocument, Size, DocumentPaginator> paginatorFactory)
    {
        _layoutBuilder = layoutBuilder ?? throw new ArgumentNullException(nameof(layoutBuilder));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        _paginatorFactory = paginatorFactory ?? throw new ArgumentNullException(nameof(paginatorFactory));
    }

    public Task<PrintPreviewDocument> PreviewAsync(OutputRecordSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_layoutBuilder.Build(snapshot));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OutputException("打印预览生成失败，请检查输出数据后重试。", exception);
        }
    }

    public async Task PrintAsync(
        OutputRecordSnapshot snapshot,
        PrinterSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var layout = _layoutBuilder.Build(snapshot);
            await InvokeOnWpfDispatcherAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_dialog.ShowDialog(settings) != true)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var printableArea = GetPrintableArea(layout, _dialog.PrintableAreaWidth, _dialog.PrintableAreaHeight);
                var paginator = _paginatorFactory(layout, printableArea);
                paginator.ComputePageCount();
                cancellationToken.ThrowIfCancellationRequested();
                if (!paginator.IsPageCountValid)
                {
                    throw new OutputException(
                        "打印分页失败，无法确定总页数，请检查打印机可打印区域后重试。",
                        new InvalidOperationException("The wrapped paginator did not produce a valid page count."));
                }

                _dialog.PrintDocument(paginator, $"{snapshot.Template.Name} {snapshot.ArchiveNumber} v{snapshot.Version}");
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OutputException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OutputException("打印失败，请检查打印机设置后重试。", exception);
        }
    }

    public static FlowDocument CreateFlowDocument(PrintPreviewDocument layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var document = new FlowDocument
        {
            PageWidth = layout.PageWidthPoints * DipsPerPoint,
            PageHeight = layout.PageHeightPoints * DipsPerPoint,
            PagePadding = new Thickness(layout.MarginPoints * DipsPerPoint),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = layout.Orientation == OutputOrientation.Landscape ? 8.5 : 10.5,
            Background = Brushes.White
        };

        document.Blocks.Add(CreateParagraph(layout.Title, 20, FontWeights.Bold, TextAlignment.Center, Brushes.MidnightBlue));
        document.Blocks.Add(CreateParagraph(layout.CompanyName, 13, FontWeights.SemiBold, TextAlignment.Center));
        document.Blocks.Add(CreateParagraph(layout.Period, 9, FontWeights.Normal, TextAlignment.Left));
        document.Blocks.Add(CreateParagraph(layout.Metadata, 9, FontWeights.Normal, TextAlignment.Left));
        document.Blocks.Add(CreateTable(layout));

        var totals = CreateParagraph("合计：" + string.Join("；", layout.Totals), 9, FontWeights.Bold, TextAlignment.Left);
        totals.Margin = new Thickness(0, 8, 0, 4);
        document.Blocks.Add(totals);

        var remarks = CreateParagraph("备注：" + layout.Remarks, 9, FontWeights.Normal, TextAlignment.Left);
        remarks.Background = Brushes.WhiteSmoke;
        remarks.Padding = new Thickness(5);
        document.Blocks.Add(remarks);
        document.Blocks.Add(CreateSignatureTable(layout));

        return document;
    }

    public static DocumentPaginator CreatePaginator(PrintPreviewDocument layout, Size? pageSize = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var document = CreateFlowDocument(layout);
        var paginator = new HeaderFooterDocumentPaginator(
            ((IDocumentPaginatorSource)document).DocumentPaginator,
            layout);
        if (pageSize is not null)
        {
            paginator.PageSize = pageSize.Value;
        }

        return paginator;
    }

    private static Size GetPrintableArea(PrintPreviewDocument layout, double width, double height)
    {
        if (double.IsFinite(width) && double.IsFinite(height) &&
            width >= MinimumPrintableAreaWidth &&
            height >= GetMinimumPrintableAreaHeight(layout))
        {
            return new Size(width, height);
        }

        return new Size(
            layout.PageWidthPoints * DipsPerPoint,
            layout.PageHeightPoints * DipsPerPoint);
    }

    private static double GetMinimumPrintableAreaHeight(PrintPreviewDocument layout) =>
        HeaderFooterDocumentPaginator.HeaderHeight +
        HeaderFooterDocumentPaginator.FooterHeight +
        (layout.MarginPoints * DipsPerPoint * 2) +
        HeaderFooterDocumentPaginator.MinimumBodyHeight;

    private static System.Windows.Documents.Table CreateTable(PrintPreviewDocument layout)
    {
        var table = new System.Windows.Documents.Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 8, 0, 0)
        };
        foreach (var _ in layout.ColumnHeaders)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        }

        var rows = new TableRowGroup();
        table.RowGroups.Add(rows);
        var header = new TableRow { Background = Brushes.SteelBlue };
        foreach (var label in layout.ColumnHeaders)
        {
            header.Cells.Add(CreateCell(label, true));
        }
        rows.Rows.Add(header);

        foreach (var values in layout.Rows)
        {
            var row = new TableRow();
            foreach (var value in values)
            {
                row.Cells.Add(CreateCell(value.DisplayText, false));
            }
            rows.Rows.Add(row);
        }

        return table;
    }

    private static TableCell CreateCell(string text, bool header)
    {
        var paragraph = CreateParagraph(
            text,
            header ? 8.5 : 8,
            header ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment.Center,
            header ? Brushes.White : Brushes.Black);
        paragraph.Margin = new Thickness(0);
        return new TableCell(paragraph)
        {
            BorderBrush = header ? Brushes.White : Brushes.LightGray,
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(2)
        };
    }

    private static System.Windows.Documents.Table CreateSignatureTable(PrintPreviewDocument layout)
    {
        var table = new System.Windows.Documents.Table { CellSpacing = 0, Margin = new Thickness(0, 12, 0, 0) };
        for (var index = 0; index <= layout.SignatureLabels.Count; index++)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        }

        var group = new TableRowGroup();
        var row = new TableRow();
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        foreach (var signature in layout.SignatureLabels)
        {
            row.Cells.Add(CreateWritingCell(signature, false));
        }

        var stamp = CreateWritingCell(layout.StampLabel, true);
        stamp.BorderBrush = Brushes.SlateGray;
        stamp.BorderThickness = new Thickness(1);
        row.Cells.Add(stamp);
        return table;
    }

    private static TableCell CreateWritingCell(string label, bool centered)
    {
        var writingArea = new Border
        {
            MinHeight = 80,
            Padding = new Thickness(4)
        };
        var labelParagraph = CreateParagraph(
            label,
            8,
            FontWeights.Normal,
            centered ? TextAlignment.Center : TextAlignment.Left);
        var cell = new TableCell(labelParagraph)
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(2)
        };
        cell.Blocks.Add(new BlockUIContainer(writingArea));
        return cell;
    }

    private static Paragraph CreateParagraph(
        string text,
        double fontSize,
        FontWeight weight,
        TextAlignment alignment,
        Brush? foreground = null) => new(new Run(text))
        {
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = fontSize,
            FontWeight = weight,
            TextAlignment = alignment,
            Foreground = foreground ?? Brushes.Black,
            Margin = new Thickness(0, 1, 0, 1)
        };

    private static async Task InvokeOnWpfDispatcherAsync(Action action, CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                await dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task.ConfigureAwait(false);
            }
            return;
        }

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
            return;
        }

        throw new OutputException(
            "打印操作需要在 WPF UI（STA）线程中执行；当前没有可用的 Application.Dispatcher。",
            new InvalidOperationException("No WPF dispatcher or STA thread is available."));
    }
}

public sealed class WindowsPrintDialogAdapter : IPrintDialogAdapter
{
    private PrintDialog? _dialog;

    public double PrintableAreaWidth => _dialog?.PrintableAreaWidth ?? double.NaN;

    public double PrintableAreaHeight => _dialog?.PrintableAreaHeight ?? double.NaN;

    public bool? ShowDialog(PrinterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _dialog = new PrintDialog();
        if (!string.IsNullOrWhiteSpace(settings.PrinterName))
        {
            using var server = new LocalPrintServer();
            _dialog.PrintQueue = server.GetPrintQueue(settings.PrinterName);
        }

        _dialog.PrintTicket ??= new PrintTicket();
        _dialog.PrintTicket.CopyCount = Math.Max(1, settings.Copies);
        return _dialog.ShowDialog();
    }

    public void PrintDocument(DocumentPaginator paginator, string description)
    {
        if (_dialog is null)
        {
            throw new InvalidOperationException("尚未显示打印对话框。");
        }

        _dialog.PrintDocument(paginator, description);
    }
}
