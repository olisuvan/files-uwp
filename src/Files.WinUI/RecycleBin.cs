using System.Runtime.InteropServices;
using Files.Core;

namespace Files.WinUI;

internal static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileOperation
    {
        public IntPtr Owner;
        public uint Function;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        public IntPtr To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Aborted;
        public IntPtr Mappings;
        public IntPtr ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref FileOperation operation);

    // The Windows shell owns any "cannot recycle" confirmation; do not suppress it.
    // Run synchronously on the window's STA thread so shell dialogs have the right owner.
    public static void Move(IReadOnlyList<string> paths, IntPtr owner)
    {
        if (paths.Count == 0) return;
        var absolute = paths.Select(PathRules.Normalize).ToArray();
        foreach (var path in absolute)
        {
            if (Directory.GetParent(path) is null) throw new IOException("不能删除磁盘根目录。");
            PathRules.RejectReparsePoints(path);
        }
        var operation = new FileOperation
        {
            Owner = owner,
            Function = 3, // FO_DELETE
            From = string.Join('\0', absolute) + "\0\0",
            Flags = 0x0040, // FOF_ALLOWUNDO; retain shell error and permanent-delete prompts
        };
        var result = SHFileOperationW(ref operation);
        if (operation.Aborted) throw new OperationCanceledException("删除已取消；请检查列表中的实际结果。");
        if (result != 0) throw new IOException($"系统未能完成回收站操作（错误码 0x{result:X}）。");
    }
}
