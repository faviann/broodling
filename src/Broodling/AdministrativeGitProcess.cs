using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Broodling;

/// <summary>Linux local administrative children only; never execution supervision.</summary>
internal static class AdministrativeGitProcess
{
    private const string Library = "broodling_git";
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int broodling_check_host();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int broodling_open_lock([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out int fd);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int broodling_spawn_git([MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        nint argv, nint envp, int lockFd, out int pid, out int stdout, out int stderr);
    [DllImport("libc", SetLastError = true)]
    private static extern int flock(int fd, int operation);
    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);
    [DllImport("libc")]
    private static extern int close(int fd);

    internal static void RequireSupportedHost()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new WorktreeProvisioningError("Administrative Git requires a Linux x64 host.");
        Check(broodling_check_host(), "Administrative Git requires a non-PID-1 host with waitable children and no competing reaper");
    }

    internal sealed class EnclosureLock : SafeHandleMinusOneIsInvalid
    {
        private EnclosureLock(int fd) : base(true) => SetHandle(fd);
        protected override bool ReleaseHandle() => close(handle.ToInt32()) == 0; // Never LOCK_UN: a Git child may still own this description.
        internal static EnclosureLock Acquire(string path)
        {
            RequireSupportedHost();
            Check(broodling_open_lock(path, out var fd), "Cannot open the stable provisioning lock");
            var result = new EnclosureLock(fd);
            while (flock(fd, 2 /* LOCK_EX */) != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == 4 /* EINTR */) continue;
                result.Dispose();
                Check(error, "Cannot acquire provisioning exclusion");
            }
            return result;
        }
    }

    internal static GitCustody.Result Run(ProcessStartInfo start, EnclosureLock enclosureLock)
    {
        RequireSupportedHost();
        var executable = ResolveExecutable(start.FileName, start.Environment["PATH"]);
        using var arguments = new Utf8Vector(new[] { executable }.Concat(start.ArgumentList));
        using var environment = new Utf8Vector(start.Environment.Where(pair => pair.Value is not null).Select(pair => pair.Key + "=" + pair.Value));
        var held = false;
        int pid, stdout, stderr;
        try
        {
            enclosureLock.DangerousAddRef(ref held);
            Check(broodling_spawn_git(executable, arguments.Pointer, environment.Pointer,
                enclosureLock.DangerousGetHandle().ToInt32(), out pid, out stdout, out stderr), "Cannot spawn administrative Git");
        }
        finally { if (held) enclosureLock.DangerousRelease(); }

        // Own each pipe and exactly this PID until completion, including output failures.
        // This synchronous seam has no cancellation/detach path that could drop its waiter.
        using var outputHandle = new SafeFileHandle(stdout, ownsHandle: true);
        using var errorHandle = new SafeFileHandle(stderr, ownsHandle: true);
        var output = Task.Run(() => Read(outputHandle));
        var errorOutput = Task.Run(() => Read(errorHandle));
        var status = 0;
        var waitError = 0;
        while (true)
        {
            if (waitpid(pid, out status, 0) == pid) break;
            var error = Marshal.GetLastPInvokeError();
            if (error == 4 /* EINTR */) continue;
            waitError = error;
            break;
        }
        Task.WhenAll(output, errorOutput).GetAwaiter().GetResult();
        Check(waitError, "Cannot establish administrative Git exit status; host wait ownership was lost");
        if ((status & 0x7f) != 0)
            throw new WorktreeProvisioningError($"Administrative Git terminated by signal {status & 0x7f}.");
        return new((status >> 8) & 0xff, output.Result, System.Text.Encoding.UTF8.GetString(errorOutput.Result));
    }

    private static byte[] Read(SafeFileHandle handle)
    {
        using var stream = new FileStream(handle, FileAccess.Read);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static string ResolveExecutable(string executable, string? path)
    {
        if (!OperatingSystem.IsLinux()) throw new WorktreeProvisioningError("Linux is required.");
        if (executable.Contains('/')) return executable;
        foreach (var directory in (path ?? "").Split(':'))
        {
            var candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory.Length == 0 ? "." : directory, executable));
            if (File.Exists(candidate) && (File.GetUnixFileMode(candidate) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                return candidate;
        }
        throw new WorktreeProvisioningError("Administrative Git executable is unavailable on PATH.");
    }

    private static void Check(int error, string message)
    {
        if (error != 0) throw new WorktreeProvisioningError($"{message}: {new Win32Exception(error).Message} ({error}).");
    }

    private sealed class Utf8Vector : IDisposable
    {
        private readonly List<nint> strings = [];
        internal nint Pointer { get; private set; }
        internal Utf8Vector(IEnumerable<string> values)
        {
            try
            {
                foreach (var value in values)
                {
                    if (value.Contains('\0')) throw new WorktreeProvisioningError("Git arguments/environment cannot contain NUL.");
                    strings.Add(Marshal.StringToCoTaskMemUTF8(value));
                }
                Pointer = Marshal.AllocHGlobal((strings.Count + 1) * nint.Size);
                Marshal.Copy(strings.Append(0).ToArray(), 0, Pointer, strings.Count + 1);
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            foreach (var item in strings) Marshal.FreeCoTaskMem(item);
            if (Pointer != 0) Marshal.FreeHGlobal(Pointer);
        }
    }
}
