using System.Runtime.InteropServices;

namespace Broodling;

/// <summary>Owns a null-terminated vector of unmanaged UTF-8 strings.</summary>
internal sealed class Utf8Vector : IDisposable
{
    private readonly nint[] strings;
    internal nint Pointer { get; private set; }

    internal Utf8Vector(IEnumerable<string> values)
    {
        var items = values.ToArray();
        strings = new nint[items.Length];
        try
        {
            Pointer = Marshal.AllocHGlobal(checked((items.Length + 1) * nint.Size));
            for (var i = 0; i < items.Length; i++)
            {
                strings[i] = Marshal.StringToCoTaskMemUTF8(items[i]);
                Marshal.WriteIntPtr(Pointer, i * nint.Size, strings[i]);
            }
            Marshal.WriteIntPtr(Pointer, items.Length * nint.Size, 0);
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        for (var i = 0; i < strings.Length; i++)
        {
            Marshal.FreeCoTaskMem(strings[i]);
            strings[i] = 0;
        }
        if (Pointer != 0) Marshal.FreeHGlobal(Pointer);
        Pointer = 0;
    }
}
