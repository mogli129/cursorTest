using System.Runtime.InteropServices;

namespace SwCheckinConflictButtonAddin
{
    /// <summary>
    /// 复用官方 ISwAddin 的 IID，便于 SOLIDWORKS 对插件做 QueryInterface。
    /// 不要加 ComImport：由本程序集提供实现。
    /// </summary>
    [ComVisible(true)]
    [Guid("0ACE2441-4E71-4430-97D6-E116AF9305D6")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface ISwAddin
    {
        [DispId(1)]
        bool ConnectToSW([MarshalAs(UnmanagedType.IDispatch)] object thisSw, int cookie);

        [DispId(2)]
        bool DisconnectFromSW();
    }
}
