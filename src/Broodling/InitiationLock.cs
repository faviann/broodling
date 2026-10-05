using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Broodling;

/// <summary>
/// The store-wide initiation lock: a shared <c>flock</c> on the SQLite file itself, held by every dispatch
/// from before its intent commits until its send returns. Drain status and stopped-target retirement probe
/// it exclusively. It fences local initiation only; the file is never written through this description.
/// </summary>
internal sealed class InitiationLock : SafeHandleMinusOneIsInvalid
{
    private const string Library = "broodling_git";
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int broodling_open_lock([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out int fd);
    [DllImport("libc", SetLastError = true)]
    private static extern int flock(int fd, int operation);
    [DllImport("libc")]
    private static extern int close(int fd);

    private InitiationLock(int fd) : base(true) => SetHandle(fd);
    protected override bool ReleaseHandle() => close(handle.ToInt32()) == 0; // Never LOCK_UN: close releases this description.

    internal static InitiationLock AcquireExisting(string store, bool shared)
    {
        Check(broodling_open_lock(store, out var fd), "Cannot open the initiation lock");
        var result = new InitiationLock(fd);
        while (flock(fd, shared ? 1 /* LOCK_SH */ : 2 /* LOCK_EX */) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 4 /* EINTR */) continue;
            result.Dispose();
            Check(error, "Cannot acquire the initiation lock");
        }
        return result;
    }

    /// <summary>True when no open description, in any process, holds the lock.</summary>
    internal static bool IsFree(string store)
    {
        Check(broodling_open_lock(store, out var fd), "Cannot open the initiation lock");
        try
        {
            while (true)
            {
                if (flock(fd, 6 /* LOCK_EX | LOCK_NB */) == 0) return true;
                var error = Marshal.GetLastPInvokeError();
                if (error == 11 /* EWOULDBLOCK */) return false;
                if (error != 4 /* EINTR */) Check(error, "Cannot probe the initiation lock");
            }
        }
        finally { close(fd); }
    }

    private static void Check(int error, string message)
    {
        if (error != 0) throw new InitiationLockError($"{message}: {new Win32Exception(error).Message} ({error}).");
    }
}
