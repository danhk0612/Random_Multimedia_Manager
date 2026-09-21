using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using RandomMultimediaManager.App.Sessions;

namespace RandomMultimediaManager.App.Deletion;

public static class WindowsFileDeletion
{
    public static Task<FileDeletionResult> DeleteAsync(DeletionRecord record)
    {
        var completion = new TaskCompletionSource<FileDeletionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Delete(record)); }
            catch (Exception ex) { completion.SetResult(new(DeletionOutcome.Unknown, ex.Message)); }
        }) { IsBackground = true, Name = "Media file deletion" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
    private static FileDeletionResult Delete(DeletionRecord record)
    {
        try
        {
            // Reparse paths must not turn a file deletion into a different target or a tree.
            for (string? p = record.Path; p is not null; p = System.IO.Path.GetDirectoryName(p))
                if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    return new(DeletionOutcome.Failed, "Reparse 경로는 삭제하지 않습니다.");
            if ((File.GetAttributes(record.Path) & FileAttributes.Directory) != 0)
                return new(DeletionOutcome.Failed, "폴더는 삭제하지 않습니다.");
        }
        catch (FileNotFoundException ex) { return new(DeletionOutcome.Failed, ex.Message, true); }
        catch (DirectoryNotFoundException ex) { return new(DeletionOutcome.Failed, ex.Message, true); }
        catch (Exception ex) { return new(DeletionOutcome.Failed, ex.Message); }
        if (record.Mode == DeletionMode.Permanent)
        {
            // Unlike File.Delete, DeleteFileW does not report an already absent file as success.
            if (DeleteFile(record.Path)) return new(DeletionOutcome.Succeeded);
            int error = Marshal.GetLastWin32Error();
            return new(DeletionOutcome.Failed, new Win32Exception(error).Message, error is 2 or 3);
        }
        IFileOperation? operation = null;
        IShellItem? item = null;
        var sink = new RecycleSink();
        bool started = false;
        try
        {
            operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new("3AD05575-8857-4850-9277-11B85BDB8E09"))!)!;
            Guid iid = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(record.Path, IntPtr.Zero, ref iid, out item));
            // Silent, no error UI, early failure, recycle only. The progress sink additionally
            // vetoes a non-recycle operation; no permanent fallback or elevation prompt.
            operation.SetOperationFlags(0x0004 | 0x0010 | 0x0400 | 0x00080000 | 0x00100000);
            operation.DeleteItem(item, sink);
            started = true;
            int hr = operation.PerformOperations();
            operation.GetAnyOperationsAborted(out bool aborted);
            if (sink.Result == 0 && sink.Recycling) return new(DeletionOutcome.Succeeded);
            if (sink.Vetoed) return new(DeletionOutcome.Failed, "휴지통으로 이동할 수 없습니다. 영구 삭제로 전환하지 않았습니다.");
            if (sink.Result is < 0) return new(DeletionOutcome.Failed, Marshal.GetExceptionForHR(sink.Result.Value)?.Message);
            if (aborted) return new(DeletionOutcome.Cancelled, "휴지통 이동이 취소되었습니다.");
            return new(DeletionOutcome.Unknown, $"휴지통 작업 결과를 확인할 수 없습니다 (0x{hr:X8}).");
        }
        catch (Exception ex) { return new(started ? DeletionOutcome.Unknown : DeletionOutcome.Failed, ex.Message); }
        finally
        {
            if (item is not null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item);
            if (operation is not null) Marshal.FinalReleaseComObject(operation);
            GC.KeepAlive(sink);
        }
    }
    [DllImport("kernel32.dll", EntryPoint = "DeleteFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteFile(string path);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid,
        out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint kind, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IFileOperationProgressSink sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog([MarshalAs(UnmanagedType.Interface)] object dialog);
        void SetProperties([MarshalAs(UnmanagedType.Interface)] object properties);
        void SetOwnerWindow(uint owner);
        void ApplyPropertiesToItem([MarshalAs(UnmanagedType.Interface)] object item);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.Interface)] object items);
        void RenameItem([MarshalAs(UnmanagedType.Interface)] object item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
        void RenameItems([MarshalAs(UnmanagedType.Interface)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem([MarshalAs(UnmanagedType.Interface)] object item, [MarshalAs(UnmanagedType.Interface)] object destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
        void MoveItems([MarshalAs(UnmanagedType.Interface)] object items, [MarshalAs(UnmanagedType.Interface)] object destination);
        void CopyItem([MarshalAs(UnmanagedType.Interface)] object item, [MarshalAs(UnmanagedType.Interface)] object destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
        void CopyItems([MarshalAs(UnmanagedType.Interface)] object items, [MarshalAs(UnmanagedType.Interface)] object destination);
        void DeleteItem(IShellItem item, IFileOperationProgressSink sink);
        void DeleteItems([MarshalAs(UnmanagedType.Interface)] object items);
        void NewItem([MarshalAs(UnmanagedType.Interface)] object destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IFileOperationProgressSink sink);
        [PreserveSig] int PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags, IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IntPtr created);
        [PreserveSig] int PreMoveItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostMoveItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IntPtr created);
        [PreserveSig] int PreCopyItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostCopyItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IntPtr created);
        [PreserveSig] int PreDeleteItem(uint flags, IntPtr item);
        [PreserveSig] int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr created);
        [PreserveSig] int PreNewItem(uint flags, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int result, IntPtr created);
        [PreserveSig] int UpdateProgress(uint total, uint soFar);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class RecycleSink : IFileOperationProgressSink
    {
        public int? Result { get; private set; }
        public bool Recycling { get; private set; }
        public bool Vetoed { get; private set; }
        public int PreDeleteItem(uint flags, IntPtr item)
        {
            Recycling = (flags & 0x80) != 0; // TSF_DELETE_RECYCLE_IF_POSSIBLE
            Vetoed = !Recycling;
            return Recycling ? 0 : unchecked((int)0x80004005);
        }
        public int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr created) { Result = result; return 0; }
        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreRenameItem(uint f, IntPtr i, string n) => 0;
        public int PostRenameItem(uint f, IntPtr i, string n, int r, IntPtr c) => 0;
        public int PreMoveItem(uint f, IntPtr i, IntPtr d, string n) => 0;
        public int PostMoveItem(uint f, IntPtr i, IntPtr d, string n, int r, IntPtr c) => 0;
        public int PreCopyItem(uint f, IntPtr i, IntPtr d, string n) => 0;
        public int PostCopyItem(uint f, IntPtr i, IntPtr d, string n, int r, IntPtr c) => 0;
        public int PreNewItem(uint f, IntPtr d, string n) => 0;
        public int PostNewItem(uint f, IntPtr d, string n, string t, uint a, int r, IntPtr c) => 0;
        public int UpdateProgress(uint t, uint s) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}
