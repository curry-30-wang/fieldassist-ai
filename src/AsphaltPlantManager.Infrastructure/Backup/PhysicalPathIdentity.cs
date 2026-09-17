using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32.SafeHandles;

namespace AsphaltPlantManager.Infrastructure.Backup;

internal readonly record struct PhysicalPathIdentity(ulong Volume, ulong FileId)
{
    internal static ProtectedDirectory ProtectDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows())
        {
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("路径不能是指向其他位置的链接。");
            }

            return new ProtectedDirectory([]);
        }

        var handles = new List<SafeFileHandle>();
        try
        {
            foreach (var ancestor in EnumeratePathFromRoot(fullPath))
            {
                var handle = NativeMethods.CreateFileW(
                    ancestor,
                    NativeMethods.FileReadAttributes | NativeMethods.FileReadData,
                    FileShare.ReadWrite,
                    IntPtr.Zero,
                    FileMode.Open,
                    NativeMethods.FileFlagBackupSemantics | NativeMethods.FileFlagOpenReparsePoint,
                    IntPtr.Zero);
                handles.Add(handle);
                _ = ReadWindowsIdentity(handle, ancestor);
            }

            return new ProtectedDirectory(handles);
        }
        catch
        {
            for (var index = handles.Count - 1; index >= 0; index--)
            {
                handles[index].Dispose();
            }

            throw;
        }
    }

    internal static void MoveDirectChildDirectory(
        ProtectedDirectory protectedRoot,
        string sourcePath,
        string destinationName,
        PhysicalPathIdentity expectedIdentity)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (Read(sourcePath) != expectedIdentity)
            {
                throw new IOException("目录身份在移动前已发生变化。");
            }

            Directory.Move(sourcePath, Path.Combine(Path.GetDirectoryName(sourcePath)!, destinationName));
            return;
        }

        using var source = NativeMethods.CreateFileW(
            Path.GetFullPath(sourcePath),
            NativeMethods.Delete | NativeMethods.FileReadAttributes,
            FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            NativeMethods.FileFlagBackupSemantics | NativeMethods.FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (ReadWindowsIdentity(source, sourcePath) != expectedIdentity)
        {
            throw new IOException("目录身份在移动前已发生变化。");
        }

        RenameByHandle(source, Path.Combine(Path.GetDirectoryName(sourcePath)!, destinationName));
        GC.KeepAlive(protectedRoot);
    }

    internal static PhysicalPathIdentity Read(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows())
        {
            var info = new FileInfo(fullPath);
            return new PhysicalPathIdentity(
                unchecked((ulong)info.CreationTimeUtc.Ticks),
                unchecked((ulong)info.LastWriteTimeUtc.Ticks));
        }

        using var handle = NativeMethods.CreateFileW(
            fullPath,
            NativeMethods.FileReadAttributes,
            FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            NativeMethods.FileFlagBackupSemantics | NativeMethods.FileFlagOpenReparsePoint,
            IntPtr.Zero);
        return ReadWindowsIdentity(handle, fullPath);
    }

    private static PhysicalPathIdentity ReadWindowsIdentity(SafeFileHandle handle, string fullPath)
    {
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法安全打开路径：{fullPath}");
        }

        if (!NativeMethods.GetFileInformationByHandle(handle, out var information))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法读取路径身份：{fullPath}");
        }

        if (((FileAttributes)information.FileAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("路径不能是指向其他位置的链接。");
        }

        return new PhysicalPathIdentity(
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
    }

    private static IEnumerable<string> EnumeratePathFromRoot(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath)
            ?? throw new IOException("无法确定受保护目录的根路径。");
        yield return root;
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".")
        {
            yield break;
        }

        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            yield return current;
        }
    }

    private static void RenameByHandle(SafeFileHandle source, string destinationPath)
    {
        var nameBytes = System.Text.Encoding.Unicode.GetBytes(Path.GetFullPath(destinationPath));
        var nameOffset = IntPtr.Size == 8 ? 20 : 12;
        var bufferSize = nameOffset + nameBytes.Length + sizeof(char);
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (var index = 0; index < bufferSize; index++)
            {
                Marshal.WriteByte(buffer, index, 0);
            }

            Marshal.WriteInt32(buffer, 0, 0);
            Marshal.WriteIntPtr(buffer, IntPtr.Size == 8 ? 8 : 4, IntPtr.Zero);
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 16 : 8, nameBytes.Length);
            Marshal.Copy(nameBytes, 0, IntPtr.Add(buffer, nameOffset), nameBytes.Length);
            if (!NativeMethods.SetFileInformationByHandle(
                    source,
                    NativeMethods.FileInfoByHandleClass.FileRenameInfo,
                    buffer,
                    checked((uint)bufferSize)))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法在受保护根目录内安全移动目录。");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal sealed class ProtectedDirectory : IDisposable
    {
        private readonly IReadOnlyList<SafeFileHandle> _handles;

        internal ProtectedDirectory(IReadOnlyList<SafeFileHandle> handles) => _handles = handles;

        public void Dispose()
        {
            for (var index = _handles.Count - 1; index >= 0; index--)
            {
                _handles[index].Dispose();
            }
        }
    }

    private static class NativeMethods
    {
        internal const uint FileReadAttributes = 0x0080;
        internal const uint FileReadData = 0x0001;
        internal const uint Delete = 0x00010000;
        internal const uint FileFlagBackupSemantics = 0x02000000;
        internal const uint FileFlagOpenReparsePoint = 0x00200000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            FileShare shareMode,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(
            SafeFileHandle file,
            out ByHandleFileInformation fileInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetFileInformationByHandle(
            SafeFileHandle file,
            FileInfoByHandleClass fileInformationClass,
            IntPtr fileInformation,
            uint bufferSize);

        internal enum FileInfoByHandleClass
        {
            FileRenameInfo = 3
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ByHandleFileInformation
        {
            internal uint FileAttributes;
            internal FILETIME CreationTime;
            internal FILETIME LastAccessTime;
            internal FILETIME LastWriteTime;
            internal uint VolumeSerialNumber;
            internal uint FileSizeHigh;
            internal uint FileSizeLow;
            internal uint NumberOfLinks;
            internal uint FileIndexHigh;
            internal uint FileIndexLow;
        }
    }
}
