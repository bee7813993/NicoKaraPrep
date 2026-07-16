using System.Runtime.InteropServices;

namespace TimeTagTool.App.Services;

/// <summary>
/// 初期フォルダを指定できる保存ダイアログ。
/// （WinUI の FileSavePicker は任意の初期フォルダを指定できないため、
/// 　Win32 の IFileSaveDialog を直接使う）
/// </summary>
public static class SaveFileDialog
{
    /// <summary>
    /// 保存ダイアログを表示する。キャンセル時は null。
    /// </summary>
    /// <param name="hwnd">親ウィンドウ。</param>
    /// <param name="initialFolder">初期フォルダ（null なら OS 既定）。</param>
    /// <param name="suggestedFileName">初期ファイル名（拡張子なし）。</param>
    /// <param name="fileTypes">(表示名, パターン) の配列。例 ("歌詞ファイル (*.lrc)", "*.lrc")。</param>
    /// <param name="defaultExtension">既定の拡張子（ドットなし）。</param>
    public static string? Show(
        IntPtr hwnd,
        string? initialFolder,
        string suggestedFileName,
        IReadOnlyList<(string Label, string Pattern)> fileTypes,
        string defaultExtension)
    {
        var dialog = (IFileDialog)new FileSaveDialogRcw();
        try
        {
            var specs = new COMDLG_FILTERSPEC[fileTypes.Count];
            for (int i = 0; i < fileTypes.Count; i++)
            {
                specs[i] = new COMDLG_FILTERSPEC { pszName = fileTypes[i].Label, pszSpec = fileTypes[i].Pattern };
            }
            dialog.SetFileTypes((uint)specs.Length, specs);
            dialog.SetFileTypeIndex(1);
            dialog.SetDefaultExtension(defaultExtension.TrimStart('.'));
            dialog.SetFileName(suggestedFileName);

            if (!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder))
            {
                Guid shellItemIid = typeof(IShellItem).GUID;
                if (SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, ref shellItemIid, out IShellItem folder) == 0)
                {
                    dialog.SetFolder(folder);
                    Marshal.ReleaseComObject(folder);
                }
            }

            int hr = dialog.Show(hwnd);
            if (hr != 0) return null; // キャンセル（ERROR_CANCELLED）

            dialog.GetResult(out IShellItem item);
            try
            {
                item.GetDisplayName(SIGDN_FILESYSPATH, out string path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    // ------------------------------------------------------------ COM interop

    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        out IShellItem ppv);

    [ComImport]
    [Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")] // CLSID_FileSaveDialog
    private class FileSaveDialogRcw
    {
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")] // IShellItem
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")] // IFileDialog
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        // IModalWindow
        [PreserveSig] int Show(IntPtr hwndOwner);

        // IFileDialog（vtable 順を維持すること）
        void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }
}
