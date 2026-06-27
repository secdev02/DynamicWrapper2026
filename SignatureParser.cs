using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DynaCall;

/// <summary>
/// Parses DynaCall / DynamicWrapperX-style signature strings into strongly-typed
/// <see cref="ArgSpec"/> arrays.
/// </summary>
/// <remarks>
/// <para><b>Input types</b> (lower-case)</para>
/// <list type="table">
///   <listheader><term>Char</term><description>CLR type — native type</description></listheader>
///   <item><term>m</term><description><see cref="long"/>  — INT64, LONGLONG</description></item>
///   <item><term>q</term><description><see cref="ulong"/> — UINT64, ULONGLONG</description></item>
///   <item><term>l</term><description><see cref="int"/>   — LONG, INT, BOOL (32-bit)</description></item>
///   <item><term>u</term><description><see cref="uint"/>  — ULONG, UINT, DWORD</description></item>
///   <item><term>h</term><description><see cref="IntPtr"/> — HANDLE (size follows process bitness)</description></item>
///   <item><term>p</term><description><see cref="IntPtr"/> — pointer; also accepts objects / strings</description></item>
///   <item><term>n</term><description><see cref="short"/>  — SHORT (signed 16-bit)</description></item>
///   <item><term>t</term><description><see cref="ushort"/> — USHORT, WORD, WCHAR (unsigned 16-bit)</description></item>
///   <item><term>c</term><description><see cref="sbyte"/>  — CHAR (signed 8-bit)</description></item>
///   <item><term>b</term><description><see cref="byte"/>   — UCHAR, BYTE (unsigned 8-bit)</description></item>
///   <item><term>f</term><description><see cref="float"/>  — FLOAT (32-bit)</description></item>
///   <item><term>d</term><description><see cref="double"/> — DOUBLE (64-bit)</description></item>
///   <item><term>w</term><description><see cref="string"/> — LPWSTR (Unicode)</description></item>
///   <item><term>s</term><description><see cref="string"/> — LPSTR (ANSI)</description></item>
///   <item><term>z</term><description><see cref="string"/> — LPSTR (OEM; treated as ANSI in .NET)</description></item>
///   <item><term>v</term><description><see cref="IntPtr"/> — pointer to a VARIANT structure</description></item>
/// </list>
///
/// <para><b>Output / byref types</b></para>
/// <para>
/// An uppercase letter denotes an output parameter — the lower-case type made byref.
/// The <c>r</c> prefix is also supported for backward compatibility with the original DynaCall.
/// String output parameters (<c>W</c>, <c>S</c>, <c>Z</c>, <c>rw</c>, <c>rs</c>, <c>rz</c>)
/// map to <see cref="System.Text.StringBuilder"/>.
/// All other output types map to <c>ref T</c>.
/// </para>
///
/// <para><b>Calling-convention spec</b></para>
/// <list type="bullet">
///   <item><c>f=s</c> — __stdcall (default)</item>
///   <item><c>f=c</c> — __cdecl</item>
/// </list>
/// </remarks>
internal static class SignatureParser
{
    // ── Public entry points ───────────────────────────────────────────────────

    /// <summary>
    /// Parses an input-parameter spec — e.g. <c>"i=mwwLU"</c> — into an ordered
    /// <see cref="ArgSpec"/> array. Uppercase letters denote output parameters.
    /// </summary>
    public static ArgSpec[] ParseInputParams(string? spec)
    {
        if (string.IsNullOrEmpty(spec)) return Array.Empty<ArgSpec>();

        var eq    = spec!.IndexOf('=');
        var chars = eq >= 0 ? spec.Substring(eq + 1) : spec;

        var result = new List<ArgSpec>(chars.Length);
        var i      = 0;

        while (i < chars.Length)
        {
            var c     = chars[i];
            var byRef = false;

            if (c == 'r' && i + 1 < chars.Length)
            {
                // Original DynaCall 'r' prefix — next char is the actual type
                byRef = true;
                i++;
                c = chars[i];
            }
            else if (char.IsUpper(c))
            {
                // DynamicWrapperX convention — uppercase = output/byref
                byRef = true;
            }

            result.Add(BuildArgSpec(c, byRef));
            i++;
        }

        return result.ToArray();
    }

    /// <summary>
    /// Parses a return-type spec — e.g. <c>"r=l"</c>. Defaults to <c>int</c>.
    /// </summary>
    public static ArgSpec ParseReturnType(string? spec)
    {
        if (string.IsNullOrEmpty(spec)) return BuildArgSpec('l', false);

        var eq = spec!.IndexOf('=');
        var c  = (eq >= 0 && eq + 1 < spec.Length) ? spec[eq + 1] : spec[0];
        return BuildArgSpec(c, false);
    }

    /// <summary>
    /// Parses a calling-convention spec — <c>"f=s"</c> for __stdcall, <c>"f=c"</c> for __cdecl.
    /// </summary>
    public static CallingConvention ParseCallingConvention(string? spec)
    {
        if (string.IsNullOrEmpty(spec)) return CallingConvention.StdCall;

        var eq = spec!.IndexOf('=');
        var c  = (eq >= 0 && eq + 1 < spec.Length) ? spec[eq + 1] : spec[0];
        return c == 'c' ? CallingConvention.Cdecl : CallingConvention.StdCall;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static ArgSpec BuildArgSpec(char typeChar, bool byRef)
    {
        var lower             = char.ToLowerInvariant(typeChar);
        var (clrType, marshal) = MapTypeChar(lower);

        if (byRef && clrType != typeof(void))
        {
            if (clrType == typeof(string))
            {
                // String output buffers → StringBuilder; P/Invoke handles the marshaling
                clrType = typeof(System.Text.StringBuilder);
                marshal  = null;
            }
            else
            {
                clrType = clrType.MakeByRefType();
            }
        }

        return new ArgSpec
        {
            TypeChar  = lower,
            ClrType   = clrType,
            IsByRef   = byRef,
            MarshalAs = marshal,
        };
    }

    private static (Type ClrType, UnmanagedType? MarshalAs) MapTypeChar(char c)
    {
        return c switch
        {
            'm' => (typeof(long),    null),
            'q' => (typeof(ulong),   null),
            'l' => (typeof(int),     null),
            'u' => (typeof(uint),    null),
            'h' => (typeof(IntPtr),  null),
            'p' => (typeof(IntPtr),  null),
            'n' => (typeof(short),   null),
            't' => (typeof(ushort),  null),
            'c' => (typeof(sbyte),   null),
            'b' => (typeof(byte),    null),
            'f' => (typeof(float),   null),
            'd' => (typeof(double),  null),
            'w' => (typeof(string),  (UnmanagedType?)UnmanagedType.LPWStr),
            's' => (typeof(string),  (UnmanagedType?)UnmanagedType.LPStr),
            'z' => (typeof(string),  (UnmanagedType?)UnmanagedType.LPStr),  // OEM — mapped to ANSI
            'v' => (typeof(IntPtr),  null),                                  // pointer to VARIANT
            _   => (typeof(IntPtr),  null),
        };
    }
}
