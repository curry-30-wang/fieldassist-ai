using AsphaltPlantManager.Core.Output;
using System.Text.RegularExpressions;

namespace AsphaltPlantManager.Infrastructure.Output;

public static class ArchiveFileNamer
{
    private static readonly Regex ReservedDeviceName = new(
        @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string GetPath(OutputRecordSnapshot snapshot, string destinationRoot, string extension)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        var cleanExtension = extension.Trim().TrimStart('.');
        if (cleanExtension.Length > 10 || cleanExtension.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("导出文件扩展名无效。", nameof(extension));
        }

        var root = Path.GetFullPath(destinationRoot);
        var template = SanitizeSegment(snapshot.Template.Name, "未命名模板");
        var archive = SanitizeSegment(snapshot.ArchiveNumber, "未编号档案");
        var candidate = Path.GetFullPath(Path.Combine(
            root,
            snapshot.VersionedAt.ToString("yyyy"),
            snapshot.VersionedAt.ToString("MM"),
            template,
            $"{archive}-v{snapshot.Version}.{cleanExtension.ToLowerInvariant()}"));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("导出路径超出指定目录。", nameof(destinationRoot));
        }

        return candidate;
    }

    private static string SanitizeSegment(string value, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var characters = value.Trim().Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character).ToArray();
        var sanitized = new string(characters).Trim(' ', '.');
        while (sanitized.Contains("..", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("..", "_", StringComparison.Ordinal);
        }

        sanitized = sanitized.Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return fallback;
        }

        return ReservedDeviceName.IsMatch(sanitized) ? $"_{sanitized}" : sanitized;
    }
}
