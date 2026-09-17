using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

namespace AsphaltPlantManager.SetupBootstrapper;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "AsphaltPlantManager-Setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
                    ?? throw new InvalidOperationException("安装文件不完整，请重新下载安装包。");
                using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
                ExtractSafely(archive, tempRoot);

                var scriptPath = Path.Combine(tempRoot, "Install-Local.ps1");
                if (!File.Exists(scriptPath))
                {
                    throw new InvalidOperationException("安装文件不完整，缺少安装脚本。");
                }

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    WorkingDirectory = tempRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                }) ?? throw new InvalidOperationException("无法启动安装程序。");

                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "安装失败。" : error.Trim());
                }
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }

            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "AsphaltPlantManager-Setup-error.log"),
                    ex.ToString());
            }
            catch
            {
                // Keep the user-facing message even if the diagnostic file cannot be written.
            }
            MessageBox.Show(
                ex.Message,
                "沥青拌合站经营管理系统安装失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void ExtractSafely(ZipArchive archive, string destinationRoot)
    {
        var fullRoot = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var relativeName = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, relativeName));
            if (!destination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("安装文件包含无效路径。");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Open();
            using var target = File.Create(destination);
            source.CopyTo(target);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Temporary cleanup must not hide a successful installation.
        }
    }
}
