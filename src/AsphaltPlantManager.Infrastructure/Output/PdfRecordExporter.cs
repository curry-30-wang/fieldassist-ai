using AsphaltPlantManager.Core.Output;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace AsphaltPlantManager.Infrastructure.Output;

public sealed class PdfRecordExporter : IRecordExporter
{
    public string Format => "pdf";

    public async Task<string> ExportAsync(
        OutputRecordSnapshot snapshot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            PdfFontBootstrap.EnsureInitialized();
            var layout = new RecordLayoutBuilder().Build(snapshot);
            var path = ArchiveFileNamer.GetPath(snapshot, destinationRoot, Format);
            await AtomicOutputFile.WriteAsync(
                path,
                (stream, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var renderer = new PdfDocumentRenderer { Document = BuildDocument(layout) };
                    renderer.RenderDocument();
                    token.ThrowIfCancellationRequested();
                    renderer.PdfDocument.Save(stream, false);
                    return Task.CompletedTask;
                },
                cancellationToken).ConfigureAwait(false);
            return path;
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
            throw new OutputException("PDF 导出失败，请检查输出数据和目标目录后重试。", exception);
        }
    }

    internal static Document BuildDocument(PrintPreviewDocument layout)
    {
        var document = new Document();
        document.Info.Title = layout.Title;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Microsoft YaHei";
        normal.Font.Size = layout.Orientation == OutputOrientation.Landscape ? 6.5 : 8;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = layout.Orientation == OutputOrientation.Landscape
            ? Orientation.Landscape
            : Orientation.Portrait;
        section.PageSetup.TopMargin = Unit.FromPoint(layout.MarginPoints);
        section.PageSetup.BottomMargin = Unit.FromPoint(layout.MarginPoints);
        section.PageSetup.LeftMargin = Unit.FromPoint(layout.MarginPoints);
        section.PageSetup.RightMargin = Unit.FromPoint(layout.MarginPoints);
        section.PageSetup.HeaderDistance = Unit.FromPoint(12);
        section.PageSetup.FooterDistance = Unit.FromPoint(12);
        section.PageSetup.DifferentFirstPageHeaderFooter = false;
        section.PageSetup.OddAndEvenPagesHeaderFooter = false;

        ComposeFooter(section.Footers.Primary, layout);
        ComposeIntro(section, layout);
        ComposeTable(section, layout);
        ComposeTail(section, layout);
        return document;
    }

    private static void ComposeFooter(HeaderFooter footer, PrintPreviewDocument layout)
    {
        var paragraph = footer.AddParagraph();
        paragraph.Format.Alignment = ParagraphAlignment.Center;
        paragraph.Format.Font.Size = 6.5;
        paragraph.Format.Font.Color = Colors.DimGray;
        paragraph.AddText(layout.FooterMetadata + "    第 ");
        paragraph.AddPageField();
        paragraph.AddText(" 页 / 共 ");
        paragraph.AddNumPagesField();
        paragraph.AddText(" 页");
    }

    private static void ComposeIntro(Section section, PrintPreviewDocument layout)
    {
        var title = section.AddParagraph(layout.Title);
        title.Format.Alignment = ParagraphAlignment.Center;
        title.Format.Font.Size = 16;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Color.Parse("#17365D");

        var company = section.AddParagraph(layout.CompanyName);
        company.Format.Alignment = ParagraphAlignment.Center;
        company.Format.Font.Size = 10;
        company.Format.Font.Bold = true;

        var period = section.AddParagraph(layout.Period);
        period.Format.Font.Size = 7;
        period.Format.SpaceBefore = Unit.FromPoint(2);
        var metadata = section.AddParagraph(layout.Metadata);
        metadata.Format.Font.Size = 7;
        metadata.Format.SpaceAfter = Unit.FromPoint(6);
    }

    private static void ComposeTable(Section section, PrintPreviewDocument layout)
    {
        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.35);
        table.Borders.Color = Color.Parse("#CBD5E1");
        table.Rows.LeftIndent = Unit.Zero;
        var usableWidth = layout.PageWidthPoints - layout.MarginPoints * 2;
        var columnWidth = Unit.FromPoint(usableWidth / Math.Max(1, layout.ColumnHeaders.Count));
        var cellLineLength = Math.Max(4, (int)Math.Floor(columnWidth.Point / NormalCellFontSize(layout)));
        foreach (var _ in layout.ColumnHeaders)
        {
            table.AddColumn(columnWidth);
        }

        var heading = table.AddRow();
        heading.HeadingFormat = true;
        heading.Format.Font.Bold = true;
        heading.Format.Font.Color = Colors.White;
        heading.Shading.Color = Color.Parse("#1F4E78");
        heading.VerticalAlignment = VerticalAlignment.Center;
        for (var index = 0; index < layout.ColumnHeaders.Count; index++)
        {
            heading.Cells[index].AddParagraph(layout.ColumnHeaders[index]).Format.Alignment = ParagraphAlignment.Center;
        }

        foreach (var values in layout.Rows)
        {
            var row = table.AddRow();
            row.VerticalAlignment = VerticalAlignment.Center;
            for (var index = 0; index < values.Count; index++)
            {
                var text = values[index].Kind == OutputValueKind.Text
                    ? WrapLongLine(values[index].DisplayText, cellLineLength)
                    : values[index].DisplayText;
                var paragraph = row.Cells[index].AddParagraph(text);
                paragraph.Format.Alignment = values[index].Kind == OutputValueKind.Text
                    ? ParagraphAlignment.Left
                    : ParagraphAlignment.Center;
                paragraph.Format.SpaceBefore = Unit.FromPoint(2);
                paragraph.Format.SpaceAfter = Unit.FromPoint(2);
            }
        }
    }

    private static void ComposeTail(Section section, PrintPreviewDocument layout)
    {
        var totals = section.AddParagraph("合计：" + string.Join("；", layout.Totals));
        totals.Format.Font.Bold = true;
        totals.Format.SpaceBefore = Unit.FromPoint(7);
        totals.Format.SpaceAfter = Unit.FromPoint(4);

        var remarkLineLength = layout.Orientation == OutputOrientation.Landscape ? 100 : 60;
        var remarks = section.AddParagraph(WrapLongLine("备注：" + layout.Remarks, remarkLineLength));
        remarks.Format.Shading.Color = Color.Parse("#F3F4F6");
        remarks.Format.SpaceBefore = Unit.FromPoint(2);
        remarks.Format.SpaceAfter = Unit.FromPoint(10);

        var signatures = section.AddTable();
        signatures.Rows.LeftIndent = Unit.Zero;
        var usableWidth = layout.PageWidthPoints - layout.MarginPoints * 2;
        var columnCount = layout.SignatureLabels.Count + 1;
        for (var index = 0; index < columnCount; index++)
        {
            signatures.AddColumn(Unit.FromPoint(usableWidth / columnCount));
        }

        var signatureRow = signatures.AddRow();
        signatureRow.Height = Unit.FromMillimeter(20);
        signatureRow.HeightRule = RowHeightRule.AtLeast;
        for (var index = 0; index < layout.SignatureLabels.Count; index++)
        {
            signatureRow.Cells[index].AddParagraph(layout.SignatureLabels[index]);
        }

        var stamp = signatureRow.Cells[columnCount - 1];
        stamp.Borders.Width = Unit.FromPoint(0.5);
        stamp.Borders.Color = Colors.Gray;
        stamp.VerticalAlignment = VerticalAlignment.Center;
        stamp.AddParagraph(layout.StampLabel).Format.Alignment = ParagraphAlignment.Center;
    }

    private static double NormalCellFontSize(PrintPreviewDocument layout) =>
        layout.Orientation == OutputOrientation.Landscape ? 6.5 : 8;

    private static string WrapLongLine(string value, int maximumCharacters)
    {
        var output = new List<string>();
        foreach (var sourceLine in value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (sourceLine.Length == 0)
            {
                output.Add(string.Empty);
                continue;
            }

            for (var offset = 0; offset < sourceLine.Length; offset += maximumCharacters)
            {
                output.Add(sourceLine.Substring(offset, Math.Min(maximumCharacters, sourceLine.Length - offset)));
            }
        }

        return string.Join('\n', output);
    }

}
