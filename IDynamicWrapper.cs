using System;
using System.Runtime.InteropServices;

namespace DynaCall;

/// <summary>
/// COM-visible interface for <see cref="DynamicWrapper"/> — exposes the full API
/// to scripting hosts such as WSH JScript and VBScript via <c>IDispatch</c>.
/// </summary>
/// <remarks>
/// WSH JScript usage:
/// <code>
/// var DWX = new ActiveXObject("DynaCall.DynamicWrapper");
/// DWX.Register("user32.dll", "MessageBoxW", "i=hwwu", "f=s", "r=l");
/// DWX.Call("MessageBoxW", 0, "Hello!", "Title", 0);
/// </code>
/// Register with:
/// <code>
/// RegAsm.exe /tlb /codebase DynaCall.dll
/// </code>
/// </remarks>
[ComVisible(true)]
[Guid("A7B2C3D4-E5F6-4890-ABCD-EF1234567891")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IDynamicWrapper
{
    // ── Registration ──────────────────────────────────────────────────────────

    /// <summary>Loads a DLL and resolves an exported function so it can be called via <see cref="Call"/>.</summary>
    /// <param name="dllName">The DLL — e.g. <c>"user32.dll"</c>.</param>
    /// <param name="functionName">The exported symbol — e.g. <c>"MessageBoxW"</c>. Casing is significant.</param>
    /// <param name="inputParams">Type characters — e.g. <c>"i=hwwu"</c>. Uppercase = output parameter. <c>i=</c> prefix optional.</param>
    /// <param name="callingConvention"><c>"f=s"</c> __stdcall (default) or <c>"f=c"</c> __cdecl.</param>
    /// <param name="returnType">Return type — e.g. <c>"r=l"</c>. Use <c>"r=v"</c> for void.</param>
    /// <param name="flags">Optional flags: <c>"l"</c> captures Win32 last-error after each call.</param>
    void Register(
        string dllName,
        string functionName,
        [Optional] object inputParams,
        [Optional] object callingConvention,
        [Optional] object returnType,
        [Optional] object flags);

    /// <summary>Registers a function by its raw memory address rather than by DLL and symbol name.</summary>
    /// <param name="address">The function's address in memory.</param>
    /// <param name="functionName">Name to register the function under — used when calling via <see cref="Call"/>.</param>
    /// <param name="inputParams">Type characters — same format as <see cref="Register"/>.</param>
    /// <param name="callingConvention">Calling convention — same format as <see cref="Register"/>.</param>
    /// <param name="returnType">Return type — same format as <see cref="Register"/>.</param>
    /// <param name="flags">Optional flags — same as <see cref="Register"/>.</param>
    void RegisterAddr(
        IntPtr address,
        string functionName,
        [Optional] object inputParams,
        [Optional] object callingConvention,
        [Optional] object returnType,
        [Optional] object flags);

    /// <summary>
    /// Writes machine code to executable memory and optionally registers it as a callable function.
    /// Check <see cref="Bitness"/> first — x86 and x64 opcodes differ.
    /// </summary>
    /// <param name="hexCode">
    ///   Machine code as a hex string. Whitespace ignored. Parenthetical comments
    ///   and semicolon end-of-line comments are stripped automatically.
    /// </param>
    /// <param name="functionName">When provided, registers the code so it can be called via <see cref="Call"/>.</param>
    /// <param name="inputParams">Type characters — same format as <see cref="Register"/>.</param>
    /// <param name="callingConvention">Calling convention — same format as <see cref="Register"/>.</param>
    /// <param name="returnType">Return type — same format as <see cref="Register"/>.</param>
    /// <returns>Address of the allocated executable memory.</returns>
    IntPtr RegisterCode(
        string hexCode,
        [Optional] object functionName,
        [Optional] object inputParams,
        [Optional] object callingConvention,
        [Optional] object returnType);

    /// <summary>
    /// Wraps a managed delegate — or a JScript function reference — in a native-callable
    /// thunk and returns the function pointer. Pass the pointer to an API such as <c>EnumWindows</c>.
    /// </summary>
    /// <param name="callback">A .NET delegate or a JScript function reference (IDispatch).</param>
    /// <param name="inputParams">Type characters for the parameters the API will pass to the callback.</param>
    /// <param name="returnType">Type character for the value the callback returns to the API.</param>
    /// <param name="callingConvention">Convention used by the API when calling the callback — usually <c>"f=s"</c>.</param>
    /// <returns>Native function pointer valid until <see cref="DynamicWrapper.Dispose"/> is called.</returns>
    IntPtr RegisterCallback(
        object callback,
        [Optional] object inputParams,
        [Optional] object returnType,
        [Optional] object callingConvention);

    /// <summary>Returns <c>true</c> if the named function has been registered.</summary>
    /// <param name="functionName">The name used in the corresponding registration call.</param>
    bool IsRegistered(string functionName);

    // ── Invocation — up to 12 arguments ──────────────────────────────────────

    /// <summary>
    /// Calls a previously registered function with up to 12 arguments.
    /// Unused trailing arguments may be omitted.
    /// </summary>
    /// <param name="functionName">The name used in the corresponding registration call.</param>
    /// <param name="a0">Argument 1.</param>  <param name="a1">Argument 2.</param>
    /// <param name="a2">Argument 3.</param>  <param name="a3">Argument 4.</param>
    /// <param name="a4">Argument 5.</param>  <param name="a5">Argument 6.</param>
    /// <param name="a6">Argument 7.</param>  <param name="a7">Argument 8.</param>
    /// <param name="a8">Argument 9.</param>  <param name="a9">Argument 10.</param>
    /// <param name="a10">Argument 11.</param><param name="a11">Argument 12.</param>
    /// <returns>The native return value boxed as <see cref="object"/>, or null for void functions.</returns>
    object Call(string functionName,
        [Optional] object a0,  [Optional] object a1,  [Optional] object a2,
        [Optional] object a3,  [Optional] object a4,  [Optional] object a5,
        [Optional] object a6,  [Optional] object a7,  [Optional] object a8,
        [Optional] object a9,  [Optional] object a10, [Optional] object a11);

    // ── Memory ────────────────────────────────────────────────────────────────

    /// <summary>Allocates a block of unmanaged memory and returns a pointer. Free with <see cref="MemFree"/>.</summary>
    /// <param name="bytes">Number of bytes to allocate.</param>
    /// <param name="zeroMem">Pass 1 to fill the block with binary zeros.</param>
    IntPtr MemAlloc(int bytes, [Optional] object zeroMem);

    /// <summary>Frees memory previously allocated by <see cref="MemAlloc"/>.</summary>
    /// <param name="memPtr">Pointer returned by <see cref="MemAlloc"/>.</param>
    void   MemFree(IntPtr memPtr);

    /// <summary>Fills a block of memory with binary zeros.</summary>
    /// <param name="address">Start address of the block.</param>
    /// <param name="bytes">Size of the block in bytes.</param>
    void   MemZero(IntPtr address, int bytes);

    /// <summary>Copies bytes between memory blocks. Overlapping regions are handled correctly.</summary>
    /// <param name="srcAddr">Source address.</param>
    /// <param name="destAddr">Destination address.</param>
    /// <param name="bytes">Number of bytes to copy.</param>
    /// <returns>Address immediately after the last written byte.</returns>
    IntPtr MemCopy(IntPtr srcAddr, IntPtr destAddr, int bytes);

    /// <summary>Reads a block of memory into an upper-case hex string with optional grouping.</summary>
    /// <param name="address">Start address.</param>
    /// <param name="bytes">Number of bytes to read.</param>
    /// <param name="bytesPerGroup">Bytes per space-separated group. 0 = no grouping.</param>
    /// <param name="groupsPerLine">Groups per CRLF-separated line. 0 = no line breaks.</param>
    string MemRead(IntPtr address, int bytes, [Optional] object bytesPerGroup, [Optional] object groupsPerLine);

    /// <summary>
    /// Writes a hex string to memory in binary form. Pass <see cref="IntPtr.Zero"/> as
    /// <paramref name="destAddr"/> to query the required buffer size.
    /// </summary>
    /// <param name="hexStr">Source hex string — whitespace and comments are stripped.</param>
    /// <param name="destAddr">Destination address, or zero to query size.</param>
    /// <param name="bytes">Bytes to write. 0 = write all bytes from the hex string.</param>
    /// <returns>Address after the last written byte, or required buffer size when <paramref name="destAddr"/> is zero.</returns>
    IntPtr MemWrite(string hexStr, IntPtr destAddr, [Optional] object bytes);

    // ── Number I/O ────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a typed number from memory at <c>address [+ offset]</c>.
    /// </summary>
    /// <param name="address">Base address.</param>
    /// <param name="offsetOrType">
    ///   A numeric byte offset (e.g. <c>4</c>), or a type character (e.g. <c>"m"</c>) when
    ///   offset is 0. Omit both optional parameters for offset 0 with default type <c>"l"</c>.
    /// </param>
    /// <param name="type">Type character when <paramref name="offsetOrType"/> is an offset.</param>
    object NumGet(IntPtr address, [Optional] object offsetOrType, [Optional] object type);

    /// <summary>Writes a typed number to memory at <c>address [+ offset]</c>.</summary>
    /// <param name="value">The number to write.</param>
    /// <param name="address">Base address.</param>
    /// <param name="offsetOrType">Numeric offset or type character — see <see cref="NumGet"/>.</param>
    /// <param name="type">Type character when <paramref name="offsetOrType"/> is an offset.</param>
    /// <returns>Address immediately after the last written byte.</returns>
    IntPtr NumPut(object value, IntPtr address, [Optional] object offsetOrType, [Optional] object type);

    // ── String I/O ────────────────────────────────────────────────────────────

    /// <summary>Reads a null-terminated string from memory.</summary>
    /// <param name="address">Address of the string.</param>
    /// <param name="offsetOrType">Numeric offset or type character (<c>"w"</c>, <c>"s"</c>, <c>"z"</c>).</param>
    /// <param name="type">Type character when <paramref name="offsetOrType"/> is an offset.</param>
    string StrGet(IntPtr address, [Optional] object offsetOrType, [Optional] object type);

    /// <summary>
    /// Writes a null-terminated string to memory. Pass <see cref="IntPtr.Zero"/> as
    /// <paramref name="address"/> to query the required buffer size in bytes.
    /// </summary>
    /// <param name="value">The string to write.</param>
    /// <param name="address">Destination address, or zero to query required size.</param>
    /// <param name="offsetOrType">Numeric offset or type character — see <see cref="StrGet"/>.</param>
    /// <param name="type">Type character when <paramref name="offsetOrType"/> is an offset.</param>
    /// <returns>Address after the null terminator, or required buffer size in bytes.</returns>
    IntPtr StrPut(string value, IntPtr address, [Optional] object offsetOrType, [Optional] object type);

    // ── Misc ──────────────────────────────────────────────────────────────────

    /// <summary>Process bitness — 32 or 64. Check this before using <see cref="DynamicWrapper.RegisterCode"/>.</summary>
    int    Bitness  { get; }

    /// <summary>
    /// Returns the Win32 last-error code captured after the most recent call to a function
    /// registered with the <c>"l"</c> flag.
    /// </summary>
    /// <param name="flag">0 = numeric code (default); 1 = human-readable description.</param>
    object LastError([Optional] object flag);

    /// <summary>Returns a field from this assembly's four-part version number.</summary>
    /// <param name="field">0 = full string; 1 = major; 2 = minor; 3 = build; 4 = revision; 5–7 = packed integers.</param>
    object Version ([Optional] object field);

    /// <summary>
    /// Creates a string of <paramref name="count"/> characters filled with <paramref name="fillChar"/>
    /// (default space). Pass an empty string for null characters.
    /// </summary>
    /// <param name="count">Number of characters.</param>
    /// <param name="fillChar">Fill character — default space; empty string = null character.</param>
    string Space   (int count, [Optional] object fillChar);
}