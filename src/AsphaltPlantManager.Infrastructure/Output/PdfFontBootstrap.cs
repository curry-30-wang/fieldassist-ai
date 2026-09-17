using AsphaltPlantManager.Core.Output;
using PdfSharp.Fonts;

namespace AsphaltPlantManager.Infrastructure.Output;

/// <summary>
/// Configures PDFsharp exactly once with an installed Chinese-capable font.
/// </summary>
public static class PdfFontBootstrap
{
    private const string RegularFace = "AsphaltPlant-Chinese-Regular";
    private const string BoldFace = "AsphaltPlant-Chinese-Bold";
    private static readonly object Sync = new();
    private static bool _initialized;
    private static int _initializationCount;

    internal static int InitializationCount => Volatile.Read(ref _initializationCount);

    public static void Initialize()
    {
        if (Volatile.Read(ref _initialized))
        {
            return;
        }

        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            // Respect a resolver installed by the host application or another library.
            if (GlobalFontSettings.FontResolver is null)
            {
                var windowsFonts = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "Fonts");
                GlobalFontSettings.FontResolver = new ChineseFontResolver(ResolveFontFiles(windowsFonts));
            }

            Interlocked.Increment(ref _initializationCount);
            Volatile.Write(ref _initialized, true);
        }
    }

    internal static void EnsureInitialized() => Initialize();

    internal static PdfFontFiles ResolveFontFiles(string fontDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontDirectory);
        var candidates = new[]
        {
            new PdfFontFiles(
                Path.Combine(fontDirectory, "Deng.ttf"),
                Path.Combine(fontDirectory, "Dengb.ttf")),
            new PdfFontFiles(
                Path.Combine(fontDirectory, "simhei.ttf"),
                Path.Combine(fontDirectory, "simhei.ttf")),
            new PdfFontFiles(
                Path.Combine(fontDirectory, "simkai.ttf"),
                Path.Combine(fontDirectory, "simkai.ttf"))
        };

        var match = candidates.FirstOrDefault(candidate =>
            File.Exists(candidate.RegularPath) && File.Exists(candidate.BoldPath));
        if (match is not null)
        {
            return match;
        }

        throw new OutputException(
            "未找到可用于 PDF 导出的中文字体。请确认 Windows 10/11 已安装等线、黑体或楷体后重试。",
            new FileNotFoundException($"No supported Chinese font was found in '{fontDirectory}'."));
    }

    internal sealed record PdfFontFiles(string RegularPath, string BoldPath);

    private sealed class ChineseFontResolver(PdfFontFiles files) : IFontResolver
    {
        private readonly Lazy<byte[]> _regular = new(
            () => File.ReadAllBytes(files.RegularPath),
            LazyThreadSafetyMode.ExecutionAndPublication);
        private readonly Lazy<byte[]> _bold = new(
            () => File.ReadAllBytes(files.BoldPath),
            LazyThreadSafetyMode.ExecutionAndPublication);

        public byte[]? GetFont(string faceName) => faceName switch
        {
            RegularFace => _regular.Value,
            BoldFace => _bold.Value,
            _ => null
        };

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new(isBold ? BoldFace : RegularFace, false, isItalic);
    }
}
