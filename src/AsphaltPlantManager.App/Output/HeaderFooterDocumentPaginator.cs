using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using AsphaltPlantManager.Core.Output;

namespace AsphaltPlantManager.App.Output;

/// <summary>
/// Adds invariant physical-page chrome around a flowing document paginator.
/// </summary>
public sealed class HeaderFooterDocumentPaginator : DocumentPaginator
{
    public const double SafeMargin = 36;
    public const double HeaderHeight = 60;
    public const double FooterHeight = 64;
    public const double MinimumBodyHeight = 120;
    private const double ChromeInset = 1;

    private readonly DocumentPaginator _inner;
    private readonly PrintPreviewDocument _layout;
    private Size _pageSize;
    private bool _computingPageCount;

    public HeaderFooterDocumentPaginator(DocumentPaginator inner, PrintPreviewDocument layout)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        PageSize = new Size(
            layout.PageWidthPoints * 96d / 72d,
            layout.PageHeightPoints * 96d / 72d);
    }

    public override bool IsPageCountValid => _inner.IsPageCountValid;

    public override int PageCount => _inner.PageCount;

    public override IDocumentPaginatorSource? Source => _inner.Source;

    public override Size PageSize
    {
        get => _pageSize;
        set
        {
            if (value.Width <= 0 || value.Height <= HeaderHeight + FooterHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "页面尺寸不足以容纳页眉、正文和页脚。");
            }

            _pageSize = value;
            _inner.PageSize = new Size(value.Width, value.Height - HeaderHeight - FooterHeight);
        }
    }

    public override void ComputePageCount()
    {
        if (_computingPageCount)
        {
            return;
        }

        try
        {
            _computingPageCount = true;
            _inner.ComputePageCount();
        }
        finally
        {
            _computingPageCount = false;
        }
    }

    public override DocumentPage GetPage(int pageNumber)
    {
        EnsurePageCount();
        var innerPage = _inner.GetPage(pageNumber);
        var root = new ContainerVisual();
        var content = CreateContentVisual(innerPage);
        root.Children.Add(content);
        root.Children.Add(CreateChrome(pageNumber));

        var fullPage = new Rect(PageSize);
        return new DocumentPage(root, PageSize, fullPage, fullPage);
    }

    private DrawingVisual CreateContentVisual(DocumentPage innerPage)
    {
        var content = new DrawingVisual();
        using var drawing = content.RenderOpen();
        drawing.DrawRectangle(
            new VisualBrush(innerPage.Visual),
            null,
            new Rect(0, HeaderHeight, PageSize.Width, PageSize.Height - HeaderHeight - FooterHeight));
        return content;
    }

    private void EnsurePageCount()
    {
        if (!IsPageCountValid && !_computingPageCount)
        {
            ComputePageCount();
        }

        if (!IsPageCountValid)
        {
            throw new InvalidOperationException("无法在绘制物理页眉和页脚前确定总页数。");
        }
    }

    private DrawingVisual CreateChrome(int pageNumber)
    {
        var visual = new DrawingVisual();
        using var drawing = visual.RenderOpen();
        var separator = new Pen(Brushes.SlateGray, 0.5);
        drawing.DrawLine(
            separator,
            new Point(SafeMargin + ChromeInset, HeaderHeight - 3),
            new Point(PageSize.Width - SafeMargin - ChromeInset, HeaderHeight - 3));
        drawing.DrawLine(
            separator,
            new Point(SafeMargin + ChromeInset, PageSize.Height - FooterHeight + 3),
            new Point(PageSize.Width - SafeMargin - ChromeInset, PageSize.Height - FooterHeight + 3));

        var columns = string.Join(" | ", _layout.ColumnHeaders);
        DrawText(drawing, columns, SafeMargin + ChromeInset, SafeMargin + ChromeInset, 7.5, FontWeights.SemiBold);

        var pageText = $"第 {pageNumber + 1} 页 / 共 {PageCount} 页";
        var metadata = CreateText(_layout.FooterMetadata, 7, FontWeights.Normal);
        var metadataY = PageSize.Height - SafeMargin - metadata.Height - 10;
        drawing.DrawText(metadata, new Point(SafeMargin + ChromeInset, metadataY));
        var page = CreateText(pageText, 7, FontWeights.Normal);
        drawing.DrawText(
            page,
            new Point(
                PageSize.Width - SafeMargin - ChromeInset - page.WidthIncludingTrailingWhitespace,
                PageSize.Height - SafeMargin - ChromeInset - page.Height));
        return visual;
    }

    private static void DrawText(
        DrawingContext drawing,
        string text,
        double x,
        double y,
        double fontSize,
        FontWeight weight) =>
        drawing.DrawText(CreateText(text, fontSize, weight), new Point(x, y));

    private static FormattedText CreateText(string text, double fontSize, FontWeight weight) =>
        new(
            text,
            CultureInfo.GetCultureInfo("zh-CN"),
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, weight, FontStretches.Normal),
            fontSize,
            Brushes.Black,
            1);
}
