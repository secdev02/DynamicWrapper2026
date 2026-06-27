using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DynaCall;

/// <summary>
/// Thin wrappers around kernel32 memory management routines, plus helpers for
/// reading, writing, and hex-formatting raw memory blocks.
/// </summary>
internal static class NativeMemory
{
    // ── kernel32 — library loading ────────────────────────────────────────────

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false)]
    internal static extern IntPtr LoadLibraryA(string lpFileName);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false)]
    internal static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    // ── kernel32 — virtual memory (for executable code blocks) ───────────────

    [DllImport("kernel32", SetLastError = true)]
    internal static extern IntPtr VirtualAlloc(
        IntPtr lpAddress,
        IntPtr dwSize,
        uint   flAllocationType,
        uint   flProtect);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool VirtualFree(
        IntPtr lpAddress,
        IntPtr dwSize,
        uint   dwFreeType);

    // ── kernel32 — block memory operations ───────────────────────────────────

    /// <summary>Copies a block of memory — handles overlapping regions correctly.</summary>
    [DllImport("kernel32")]
    internal static extern void RtlMoveMemory(IntPtr dst, IntPtr src, IntPtr length);

    /// <summary>Fills a block of memory with binary zeros.</summary>
    [DllImport("kernel32")]
    internal static extern void RtlZeroMemory(IntPtr dst, IntPtr length);

    // ── VirtualAlloc flags ────────────────────────────────────────────────────

    internal const uint MEM_COMMIT             = 0x00001000;
    internal const uint MEM_RESERVE            = 0x00002000;
    internal const uint MEM_RELEASE            = 0x00008000;
    internal const uint PAGE_EXECUTE_READWRITE = 0x00000040;

    // ── Hex dump helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Reads <paramref name="byteCount"/> bytes from <paramref name="address"/> and
    /// formats them as an upper-case hex string, optionally split into groups and lines.
    /// </summary>
    internal static string HexDump(IntPtr address, int byteCount, int bytesPerGroup, int groupsPerLine)
    {
        var data = new byte[byteCount];
        Marshal.Copy(address, data, 0, byteCount);

        if (bytesPerGroup <= 0)
        {
            // Plain continuous string — e.g. "4889C848F7EAC3"
            var sb = new StringBuilder(byteCount * 2);
            foreach (var b in data)
                sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        // Grouped and optionally wrapped — groups separated by spaces, lines by CRLF
        var result     = new StringBuilder(byteCount * 3);
        var byteInGroup = 0;
        var groupsOnLine = 0;

        for (var i = 0; i < byteCount; i++)
        {
            result.Append(data[i].ToString("X2"));
            byteInGroup++;

            if (byteInGroup >= bytesPerGroup)
            {
                byteInGroup = 0;
                groupsOnLine++;

                if (i < byteCount - 1)
                {
                    if (groupsPerLine > 0 && groupsOnLine >= groupsPerLine)
                    {
                        result.Append("\r\n");
                        groupsOnLine = 0;
                    }
                    else
                    {
                        result.Append(' ');
                    }
                }
            }
        }

        return result.ToString();
    }

    // ── NumGet / NumPut helpers ───────────────────────────────────────────────

    /// <summary>Reads a typed number from <paramref name="ptr"/>.</summary>
    internal static object NumGet(IntPtr ptr, string type)
    {
        switch (type)
        {
            case "m": return Marshal.ReadInt64(ptr);
            case "q": return (ulong)Marshal.ReadInt64(ptr);
            case "l": return Marshal.ReadInt32(ptr);
            case "u": return (uint)Marshal.ReadInt32(ptr);
            case "h":
            case "p": return Marshal.ReadIntPtr(ptr);
            case "n": return Marshal.ReadInt16(ptr);
            case "t": return (ushort)Marshal.ReadInt16(ptr);
            case "c": return (sbyte)Marshal.ReadByte(ptr);
            case "b": return Marshal.ReadByte(ptr);
            case "f":
                var fb = new byte[4];
                Marshal.Copy(ptr, fb, 0, 4);
                return BitConverter.ToSingle(fb, 0);
            case "d":
                var db = new byte[8];
                Marshal.Copy(ptr, db, 0, 8);
                return BitConverter.ToDouble(db, 0);
            default: return Marshal.ReadInt32(ptr);
        }
    }

    /// <summary>
    /// Writes a typed number to <paramref name="ptr"/> and returns the address
    /// immediately after the last written byte.
    /// </summary>
    internal static IntPtr NumPut(object value, IntPtr ptr, string type)
    {
        switch (type)
        {
            case "m":
                Marshal.WriteInt64(ptr, Convert.ToInt64(value));
                return IntPtr.Add(ptr, 8);
            case "q":
                Marshal.WriteInt64(ptr, (long)Convert.ToUInt64(value));
                return IntPtr.Add(ptr, 8);
            case "l":
                Marshal.WriteInt32(ptr, Convert.ToInt32(value));
                return IntPtr.Add(ptr, 4);
            case "u":
                Marshal.WriteInt32(ptr, (int)Convert.ToUInt32(value));
                return IntPtr.Add(ptr, 4);
            case "h":
            case "p":
                Marshal.WriteIntPtr(ptr, (IntPtr)value);
                return IntPtr.Add(ptr, IntPtr.Size);
            case "n":
                Marshal.WriteInt16(ptr, Convert.ToInt16(value));
                return IntPtr.Add(ptr, 2);
            case "t":
                Marshal.WriteInt16(ptr, (short)Convert.ToUInt16(value));
                return IntPtr.Add(ptr, 2);
            case "c":
                Marshal.WriteByte(ptr, (byte)Convert.ToSByte(value));
                return IntPtr.Add(ptr, 1);
            case "b":
                Marshal.WriteByte(ptr, Convert.ToByte(value));
                return IntPtr.Add(ptr, 1);
            case "f":
                var fb = BitConverter.GetBytes(Convert.ToSingle(value));
                Marshal.Copy(fb, 0, ptr, 4);
                return IntPtr.Add(ptr, 4);
            case "d":
                var db = BitConverter.GetBytes(Convert.ToDouble(value));
                Marshal.Copy(db, 0, ptr, 8);
                return IntPtr.Add(ptr, 8);
            default:
                Marshal.WriteInt32(ptr, Convert.ToInt32(value));
                return IntPtr.Add(ptr, 4);
        }
    }
}
