using System.Collections.Concurrent;
using AsphaltPlantManager.Core.Output;
using AsphaltPlantManager.Infrastructure.Output;
using PdfSharp.Fonts;
using Xunit;

namespace AsphaltPlantManager.Infrastructure.Tests.Output;

public sealed class PdfFontBootstrapTests : IDisposable
{
    private readonly string _fontDirectory = Path.Combine(Path.GetTempPath(), $"pdf-fonts-{Guid.NewGuid():N}");

    [Fact]
    public void Initialize_is_thread_safe_and_idempotent()
    {
        var errors = new ConcurrentBag<Exception>();

        Parallel.For(0, 32, _ =>
        {
            try
            {
                PdfFontBootstrap.Initialize();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        });

        Assert.Empty(errors);
        Assert.NotNull(GlobalFontSettings.FontResolver);
        Assert.Equal(1, PdfFontBootstrap.InitializationCount);
    }

    [Fact]
    public void ResolveFontFiles_uses_an_ordered_windows_fallback()
    {
        Directory.CreateDirectory(_fontDirectory);
        File.WriteAllBytes(Path.Combine(_fontDirectory, "simhei.ttf"), [1, 2, 3]);

        var files = PdfFontBootstrap.ResolveFontFiles(_fontDirectory);

        Assert.EndsWith("simhei.ttf", files.RegularPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(files.RegularPath, files.BoldPath);
    }

    [Fact]
    public void ResolveFontFiles_reports_a_chinese_error_when_no_supported_font_exists()
    {
        Directory.CreateDirectory(_fontDirectory);

        var error = Assert.Throws<OutputException>(() => PdfFontBootstrap.ResolveFontFiles(_fontDirectory));

        Assert.Contains("中文字体", error.Message, StringComparison.Ordinal);
        Assert.Contains("Windows", error.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_fontDirectory))
        {
            Directory.Delete(_fontDirectory, true);
        }
    }
}
