using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AppDataCleaner {
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem {}
    [ComVisible(true), Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IProgressSink {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IntPtr item, IntPtr name);
        [PreserveSig] int PostRenameItem(uint flags, IntPtr item, IntPtr name, int result, IntPtr created);
        [PreserveSig] int PreMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name);
        [PreserveSig] int PostMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr created);
        [PreserveSig] int PreCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name);
        [PreserveSig] int PostCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr created);
        [PreserveSig] int PreDeleteItem(uint flags, IntPtr item);
        [PreserveSig] int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr created);
        [PreserveSig] int PreNewItem(uint flags, IntPtr destination, IntPtr name);
        [PreserveSig] int PostNewItem(uint flags, IntPtr destination, IntPtr name, IntPtr template, uint attributes, int result, IntPtr created);
        [PreserveSig] int UpdateProgress(uint total, uint done);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class RecycleGuard : IProgressSink {
        public int Failure;
        public int StartOperations() { return 0; }
        public int FinishOperations(int result) { if(result < 0) Failure=result; return 0; }
        public int PreRenameItem(uint f,IntPtr i,IntPtr n) { return 0; }
        public int PostRenameItem(uint f,IntPtr i,IntPtr n,int r,IntPtr c) { return 0; }
        public int PreMoveItem(uint f,IntPtr i,IntPtr d,IntPtr n) { return 0; }
        public int PostMoveItem(uint f,IntPtr i,IntPtr d,IntPtr n,int r,IntPtr c) { return 0; }
        public int PreCopyItem(uint f,IntPtr i,IntPtr d,IntPtr n) { return 0; }
        public int PostCopyItem(uint f,IntPtr i,IntPtr d,IntPtr n,int r,IntPtr c) { return 0; }
        public int PreDeleteItem(uint flags,IntPtr item) {
            // TSF_DELETE_RECYCLE_IF_POSSIBLE: abort any non-recycling callback.
            if((flags & 0x80) != 0) return 0;
            return Failure=unchecked((int)0x80004004);
        }
        public int PostDeleteItem(uint f,IntPtr i,int r,IntPtr c) { if(r < 0) Failure=r; return 0; }
        public int PreNewItem(uint f,IntPtr d,IntPtr n) { return 0; }
        public int PostNewItem(uint f,IntPtr d,IntPtr n,IntPtr t,uint a,int r,IntPtr c) { return 0; }
        public int UpdateProgress(uint total,uint done) { return 0; }
        public int ResetTimer() { return 0; }
        public int PauseTimer() { return 0; }
        public int ResumeTimer() { return 0; }
    }
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOperation {
        [PreserveSig] int Advise(IProgressSink sink,out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(IntPtr window);
        [PreserveSig] int ApplyPropertiesToItem(IntPtr item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IntPtr item,IntPtr name,IntPtr sink);
        [PreserveSig] int RenameItems(IntPtr items,IntPtr name);
        [PreserveSig] int MoveItem(IntPtr item,IntPtr destination,IntPtr name,IntPtr sink);
        [PreserveSig] int MoveItems(IntPtr items,IntPtr destination);
        [PreserveSig] int CopyItem(IntPtr item,IntPtr destination,IntPtr name,IntPtr sink);
        [PreserveSig] int CopyItems(IntPtr items,IntPtr destination);
        [PreserveSig] int DeleteItem(IShellItem item,IProgressSink sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IntPtr destination,uint attributes,IntPtr name,IntPtr template,IntPtr sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
    public static class Recycler {
        [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]
        static extern int SHCreateItemFromParsingName(string name,IntPtr context,ref Guid iid,out IShellItem item);
        static void Check(int code) { if(code < 0) Marshal.ThrowExceptionForHR(code); }
        public static void Move(string path) {
            string full = Path.GetFullPath(path).TrimEnd('\\');
            if(full.StartsWith(@"\\") || full.Length < 4 || full[1] != ':')
                throw new IOException("只支持本地盘上的文件夹。");
            Exception failure = null;
            Thread thread = new Thread(delegate() {
                IFileOperation operation = null; IShellItem item = null;
                try {
                    operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09")));
                    // RECYCLEONDELETE, ALLOWUNDO, WANTNUKEWARNING, NO_UI, EARLYFAILURE.
                    Check(operation.SetOperationFlags(0x00080000 | 0x40 | 0x4000 | 0x614 | 0x00100000));
                    Guid iid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
                    Check(SHCreateItemFromParsingName(full,IntPtr.Zero,ref iid,out item));
                    var guard = new RecycleGuard();
                    Check(operation.DeleteItem(item,guard));
                    Check(operation.PerformOperations());
                    Check(guard.Failure);
                    bool aborted; Check(operation.GetAnyOperationsAborted(out aborted));
                    if(aborted || Directory.Exists(full) || File.Exists(full))
                        throw new IOException("回收站不可用、项目被占用或操作被取消；未请求永久删除。");
                    GC.KeepAlive(guard);
                } catch(Exception e) { failure=e; }
                finally {
                    if(item != null) Marshal.FinalReleaseComObject(item);
                    if(operation != null) Marshal.FinalReleaseComObject(operation);
                }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if(failure != null) throw new IOException("无法移入回收站：" + failure.Message,failure);
        }
    }
}
