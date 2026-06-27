using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace DynaCall;

/// <summary>
/// A managed equivalent of the classic DynamicWrapperX COM object.
/// Loads native DLLs on demand and exposes: dynamic function calls, native callbacks,
/// inline machine code, and a full suite of memory / string / number helpers — without
/// any static <c>[DllImport]</c> declarations.
/// </summary>
///
/// <remarks>
/// <para><b>COM registration (one-time, run as Administrator)</b></para>
/// <code>
/// RegAsm.exe /tlb /codebase DynaCall.dll
/// </code>
///
/// <para><b>WSH JScript usage</b></para>
/// <code>
/// var DWX = new ActiveXObject("DynaCall.DynamicWrapper");
/// DWX.Register("user32.dll", "MessageBoxW", "i=hwwu", "f=s", "r=l");
/// DWX.Call("MessageBoxW", 0, "Hello!", "Caption", 0);
/// </code>
///
/// <para><b>Thread safety</b></para>
/// Not thread-safe — synchronise concurrent access externally.
/// </remarks>
[ComVisible(true)]
[Guid("B8C3D4E5-F6A7-4901-BCDE-F12345678902")]
[ClassInterface(ClassInterfaceType.None)]
[ProgId("DynaCall.DynamicWrapper")]
public sealed class DynamicWrapper : IDynamicWrapper, IDisposable
{
    // ── State ─────────────────────────────────────────────────────────────────

    private readonly Dictionary<string, FunctionRegistration> _registrations =
        new Dictionary<string, FunctionRegistration>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, IntPtr> _loadedLibs =
        new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

    private readonly List<Delegate> _callbacks  = new List<Delegate>();
    private readonly List<IntPtr>   _codeBlocks = new List<IntPtr>();

    private bool _disposed;

    // ── Register — by DLL + export name ──────────────────────────────────────

    /// <summary>
    /// Registers a native function so it can be called via <see cref="Call"/>.
    /// </summary>
    /// <param name="dllName">The DLL — e.g. <c>"user32.dll"</c>.</param>
    /// <param name="functionName">The exported symbol — e.g. <c>"MessageBoxW"</c>. Casing is significant.</param>
    /// <param name="inputParams">
    ///   Type characters — e.g. <c>"i=hwwu"</c>. Uppercase = output parameter. <c>i=</c> prefix optional.
    /// </param>
    /// <param name="callingConvention"><c>"f=s"</c> stdcall (default) or <c>"f=c"</c> cdecl.</param>
    /// <param name="returnType">Return type — e.g. <c>"r=l"</c>. Use <c>"r=v"</c> for void.</param>
    /// <param name="flags">
    ///   <c>"l"</c> — capture Win32 last-error after each call (retrieve with <see cref="LastError"/>).
    /// </param>
    public void Register(
        string dllName,
        string functionName,
        [Optional] object inputParams,
        [Optional] object callingConvention,
        [Optional] object returnType,
        [Optional] object flags)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(dllName))      throw new ArgumentNullException(nameof(dllName));
        if (string.IsNullOrWhiteSpace(functionName)) throw new ArgumentNullException(nameof(functionName));

        var paramSpecs       = SignatureParser.ParseInputParams(CoerceString(inputParams));
        var returnSpec       = SignatureParser.ParseReturnType(CoerceString(returnType, "r=l"));
        var convention       = SignatureParser.ParseCallingConvention(CoerceString(callingConvention, "f=s"));
        var flagStr          = CoerceString(flags) ?? string.Empty;
        var captureLastError = flagStr.IndexOf('l') >= 0;

        var libHandle = EnsureLibraryLoaded(dllName);
        var funcPtr   = NativeMemory.GetProcAddress(libHandle, functionName);

        if (funcPtr == IntPtr.Zero)
            throw new EntryPointNotFoundException(
                "'" + functionName + "' was not found in " + dllName + ". " +
                "ANSI variants typically end in 'A', Unicode in 'W'.");

        BindRegistration(dllName, functionName, paramSpecs, returnSpec, convention, captureLastError, funcPtr);
    }

    // ── RegisterAddr — by raw function pointer ────────────────────────────────

    /// <summary>
    /// Registers a function by its memory address. All other parameters match <see cref="Register"/>.
    /// </summary>
    public void RegisterAddr(
        IntPtr address,
        string functionName,
        [Optional] object inputParams,
        [Optional] object callingConvention,
        [Optional] object returnType,
        [Optional] object flags)
    {
        ThrowIfDisposed();

        if (address == IntPtr.Zero)                  throw new ArgumentNullException(nameof(address));
        if (string.IsNullOrWhiteSpace(functionName)) throw new ArgumentNullException(nameof(functionName));

        var paramSpecs       = SignatureParser.ParseInputParams(CoerceString(inputParams));
        var returnSpec       = SignatureParser.ParseReturnType(CoerceString(returnType, "r=l"));
        var convention       = SignatureParser.ParseCallingConvention(CoerceString(callingConvention, "f=s"));
        var flagStr          = CoerceString(flags) ?? string.Empty;
        var captureLastError = flagStr.IndexOf('l') >= 0;

        BindRegistration("(address)", functionName, paramSpecs, returnSpec, convention, captureLastError, address);
    }

    // ── RegisterCode — inline machine code ───────────────────────────────────

    /// <summary>
    /// Writes machine code to executable memory and optionally registers it as a callable function.
    /// Check <see cref="Bitness"/> first — opcodes differ between x86 and x64.
    /// </summary>
    /// <returns>Address of the allocated executable memory.</returns>
    public IntPtr RegisterCode(
        string hexCode,
        [Optional] object functionName,
        [Optional] object inputParams,
        [Optional] object callingConvention,
        [Optional] object returnType)
    {
        ThrowIfDisposed();

        var bytes = HexParser.Parse(hexCode);
        if (bytes.Length == 0)
            throw new ArgumentException("Hex code string is empty.", nameof(hexCode));

        var size = new IntPtr(bytes.Length);
        var mem  = NativeMemory.VirtualAlloc(
            IntPtr.Zero, size,
            NativeMemory.MEM_COMMIT | NativeMemory.MEM_RESERVE,
            NativeMemory.PAGE_EXECUTE_READWRITE);

        if (mem == IntPtr.Zero)
            throw new OutOfMemoryException(
                "VirtualAlloc failed for " + bytes.Length.ToString() + " bytes. " +
                "Error: " + Marshal.GetLastWin32Error().ToString());

        Marshal.Copy(bytes, 0, mem, bytes.Length);
        _codeBlocks.Add(mem);

        var name = CoerceString(functionName);
        if (!string.IsNullOrWhiteSpace(name))
            RegisterAddr(mem, name!, inputParams, callingConvention, returnType);

        return mem;
    }

    // ── RegisterCallback ──────────────────────────────────────────────────────

    /// <summary>
    /// Wraps a managed delegate — or a JScript function reference — in a native-callable
    /// thunk and returns the function pointer.
    /// </summary>
    /// <param name="callback">
    ///   A .NET <see cref="Delegate"/>, or a JScript function reference (passed as
    ///   <c>IDispatch</c>).
    /// </param>
    /// <param name="inputParams">
    ///   Type characters describing the parameters the API will pass to the callback —
    ///   same format as <see cref="Register"/>.
    /// </param>
    /// <param name="returnType">
    ///   Type character for the value the callback must return to the API — e.g. <c>"r=l"</c>.
    /// </param>
    /// <param name="callingConvention">
    ///   Convention used by the API when calling the callback — almost always <c>"f=s"</c>.
    /// </param>
    /// <returns>Native function pointer valid until <see cref="Dispose"/> is called.</returns>
    public IntPtr RegisterCallback(
        object callback,
        [Optional] object inputParams,
        [Optional] object returnType,
        [Optional] object callingConvention)
    {
        ThrowIfDisposed();

        if (callback == null) throw new ArgumentNullException(nameof(callback));

        var paramSpecs = SignatureParser.ParseInputParams(CoerceString(inputParams));
        var returnSpec = SignatureParser.ParseReturnType(CoerceString(returnType, "r=l"));
        var convention = SignatureParser.ParseCallingConvention(CoerceString(callingConvention, "f=s"));
        var name       = "Callback_" + _callbacks.Count.ToString();

        // Accept either a .NET Delegate or a COM IDispatch (JScript function reference)
        Delegate managedCallback;
        if (callback is Delegate del)
        {
            managedCallback = del;
        }
        else
        {
            var comTarget = callback;
            var comType   = comTarget.GetType();
            managedCallback = new Func<object?[], object?>(
                args => comType.InvokeMember(
                    string.Empty,
                    BindingFlags.InvokeMethod,
                    null,
                    comTarget,
                    args));
        }

        var (nativeDelegate, ptr) =
            CallbackBuilder.Build(managedCallback, name, paramSpecs, returnSpec, convention);

        _callbacks.Add(nativeDelegate);
        return ptr;
    }

    /// <inheritdoc/>
    public bool IsRegistered(string functionName)
        => !_disposed && _registrations.ContainsKey(functionName);

    // ── Call — up to 12 arguments ─────────────────────────────────────────────

    /// <summary>
    /// Calls a previously registered function. Supports up to 12 arguments.
    /// Unused trailing arguments may be omitted.
    /// </summary>
    public object Call(
        string functionName,
        [Optional] object a0,  [Optional] object a1,  [Optional] object a2,
        [Optional] object a3,  [Optional] object a4,  [Optional] object a5,
        [Optional] object a6,  [Optional] object a7,  [Optional] object a8,
        [Optional] object a9,  [Optional] object a10, [Optional] object a11)
    {
        ThrowIfDisposed();

        var reg    = GetRegistration(functionName);
        var args   = CollectArgs(a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11);
        var coerced = CoerceArgs(args, reg.ParameterSpecs);
        return reg.BoundDelegate.DynamicInvoke(coerced)!;
    }

    // ── Delegate access (managed callers only) ────────────────────────────────

    /// <summary>
    /// Returns the live delegate for a registered function — cast to
    /// <see cref="GetDelegateType"/> for direct (low-overhead) invocation.
    /// Not accessible from COM scripting hosts.
    /// </summary>
    public Delegate GetDelegate(string functionName)
    {
        ThrowIfDisposed();
        return GetRegistration(functionName).BoundDelegate;
    }

    /// <summary>Returns the dynamically-emitted delegate <see cref="Type"/>.</summary>
    public Type GetDelegateType(string functionName)
    {
        ThrowIfDisposed();
        return GetRegistration(functionName).DelegateType;
    }

    // ── Memory ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Allocates a block of unmanaged memory and returns a pointer.
    /// Free with <see cref="MemFree"/> when done.
    /// </summary>
    public IntPtr MemAlloc(int bytes, [Optional] object zeroMem)
    {
        ThrowIfDisposed();
        var ptr = Marshal.AllocHGlobal(bytes);
        if (CoerceInt(zeroMem) != 0) NativeMemory.RtlZeroMemory(ptr, new IntPtr(bytes));
        return ptr;
    }

    /// <summary>Frees memory previously allocated by <see cref="MemAlloc"/>.</summary>
    public void MemFree(IntPtr memPtr)
    {
        ThrowIfDisposed();
        Marshal.FreeHGlobal(memPtr);
    }

    /// <summary>Fills a block of memory with binary zeros.</summary>
    public void MemZero(IntPtr address, int bytes)
    {
        ThrowIfDisposed();
        NativeMemory.RtlZeroMemory(address, new IntPtr(bytes));
    }

    /// <summary>
    /// Copies <paramref name="bytes"/> bytes between memory blocks.
    /// Overlapping blocks are handled correctly.
    /// </summary>
    /// <returns>Address immediately after the last written byte.</returns>
    public IntPtr MemCopy(IntPtr srcAddr, IntPtr destAddr, int bytes)
    {
        ThrowIfDisposed();
        NativeMemory.RtlMoveMemory(destAddr, srcAddr, new IntPtr(bytes));
        return IntPtr.Add(destAddr, bytes);
    }

    /// <summary>Reads a memory block into an upper-case hex string.</summary>
    public string MemRead(IntPtr address, int bytes, [Optional] object bytesPerGroup, [Optional] object groupsPerLine)
    {
        ThrowIfDisposed();
        return NativeMemory.HexDump(address, bytes, CoerceInt(bytesPerGroup), CoerceInt(groupsPerLine));
    }

    /// <summary>
    /// Writes a hex string to memory in binary form.
    /// Pass <see cref="IntPtr.Zero"/> as <paramref name="destAddr"/> to query the required size.
    /// </summary>
    public IntPtr MemWrite(string hexStr, IntPtr destAddr, [Optional] object bytes)
    {
        ThrowIfDisposed();
        var data  = HexParser.Parse(hexStr);
        var count = CoerceInt(bytes);
        count = (count > 0 && count < data.Length) ? count : data.Length;
        if (destAddr == IntPtr.Zero) return new IntPtr(count);
        Marshal.Copy(data, 0, destAddr, count);
        return IntPtr.Add(destAddr, count);
    }

    // ── NumGet / NumPut ───────────────────────────────────────────────────────

    /// <summary>
    /// Reads a typed number from memory.
    /// </summary>
    /// <param name="address">Base address.</param>
    /// <param name="offsetOrType">
    ///   Either a numeric offset (e.g. <c>4</c>) or a type character (e.g. <c>"m"</c>).
    ///   Omit for offset 0 with default type <c>"l"</c>.
    /// </param>
    /// <param name="type">Type character when <paramref name="offsetOrType"/> is an offset.</param>
    public object NumGet(IntPtr address, [Optional] object offsetOrType, [Optional] object type)
    {
        ThrowIfDisposed();
        ParseOffsetAndType(offsetOrType, type, "l", out var offset, out var typeStr);
        return NativeMemory.NumGet(IntPtr.Add(address, offset), typeStr);
    }

    /// <summary>Writes a typed number to memory.</summary>
    /// <returns>Address immediately after the last written byte.</returns>
    public IntPtr NumPut(object value, IntPtr address, [Optional] object offsetOrType, [Optional] object type)
    {
        ThrowIfDisposed();
        ParseOffsetAndType(offsetOrType, type, "l", out var offset, out var typeStr);
        return NativeMemory.NumPut(value, IntPtr.Add(address, offset), typeStr);
    }

    // ── StrGet / StrPut ───────────────────────────────────────────────────────

    /// <summary>Reads a null-terminated string from memory.</summary>
    /// <param name="address">Address of the string.</param>
    /// <param name="offsetOrType">Numeric offset or type character (<c>"w"</c>, <c>"s"</c>, <c>"z"</c>).</param>
    /// <param name="type">Type character when <paramref name="offsetOrType"/> is an offset.</param>
    public string StrGet(IntPtr address, [Optional] object offsetOrType, [Optional] object type)
    {
        ThrowIfDisposed();
        ParseOffsetAndType(offsetOrType, type, "w", out var offset, out var typeStr);
        return ReadString(IntPtr.Add(address, offset), typeStr) ?? string.Empty;
    }

    /// <summary>
    /// Writes a null-terminated string to memory.
    /// Pass <see cref="IntPtr.Zero"/> as <paramref name="address"/> to query the required buffer size.
    /// </summary>
    /// <returns>Address after the null terminator, or required buffer size in bytes.</returns>
    public IntPtr StrPut(string value, IntPtr address, [Optional] object offsetOrType, [Optional] object type)
    {
        ThrowIfDisposed();
        ParseOffsetAndType(offsetOrType, type, "w", out var offset, out var typeStr);
        return WriteString(value, IntPtr.Add(address, offset == 0 ? 0 : offset), typeStr);
    }

    // ── Misc ──────────────────────────────────────────────────────────────────

    /// <summary>Process bitness — 32 or 64.</summary>
    public int Bitness => IntPtr.Size * 8;

    /// <summary>
    /// Returns the Win32 last-error code captured after the most recent call to a
    /// function registered with the <c>"l"</c> flag.
    /// </summary>
    /// <param name="flag">0 = numeric code (default); 1 = human-readable description.</param>
    public object LastError([Optional] object flag)
    {
        var code = Marshal.GetLastWin32Error();
        if (CoerceInt(flag) == 1) return new Win32Exception(code).Message;
        return code;
    }

    /// <summary>Returns a field from this assembly's four-part version number.</summary>
    /// <param name="field">
    /// 0 = full string; 1 = major; 2 = minor; 3 = build; 4 = revision;
    /// 5 = major&lt;&lt;16|minor; 6 = build&lt;&lt;16|revision; 7 = full 64-bit packed value.
    /// </param>
    public object Version([Optional] object field)
    {
        var v = typeof(DynamicWrapper).Assembly.GetName().Version
                ?? new System.Version(2, 0, 0, 0);
        switch (CoerceInt(field))
        {
            case 1: return v.Major;
            case 2: return v.Minor;
            case 3: return v.Build;
            case 4: return v.Revision;
            case 5: return (v.Major << 16) | v.Minor;
            case 6: return (v.Build << 16) | v.Revision;
            case 7: return ((long)v.Major << 48) | ((long)v.Minor << 32) |
                            ((long)v.Build << 16) |  (long)v.Revision;
            default: return v.ToString();
        }
    }

    /// <summary>
    /// Creates a string of <paramref name="count"/> characters.
    /// Default fill is space. Pass an empty string for null characters.
    /// </summary>
    public string Space(int count, [Optional] object fillChar)
    {
        // Omitted / null / missing → spaces (VBScript Space() default behaviour)
        // Explicit empty string    → null characters
        // Any other string         → first character of that string
        char c;
        if (fillChar is Missing || fillChar == Type.Missing || fillChar == null)
        {
            c = ' ';
        }
        else
        {
            var s = fillChar as string ?? fillChar.ToString();
            c = (s == null || s.Length == 0) ? '\0' : s[0];
        }
        return new string(c, count);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <summary>
    /// Unloads all native libraries, frees executable code blocks, and releases all
    /// registrations. All previously obtained function pointers become invalid.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registrations.Clear();
        _callbacks.Clear();

        foreach (var handle in _loadedLibs.Values)
            NativeMemory.FreeLibrary(handle);
        _loadedLibs.Clear();

        foreach (var block in _codeBlocks)
            NativeMemory.VirtualFree(block, IntPtr.Zero, NativeMemory.MEM_RELEASE);
        _codeBlocks.Clear();
    }

    // ── Private — shared registration logic ──────────────────────────────────

    private void BindRegistration(
        string            dllName,
        string            functionName,
        ArgSpec[]         paramSpecs,
        ArgSpec           returnSpec,
        CallingConvention convention,
        bool              captureLastError,
        IntPtr            funcPtr)
    {
        var delegateType = DelegateBuilder.Build(
            functionName, paramSpecs, returnSpec, convention, captureLastError);

        var bound = Marshal.GetDelegateForFunctionPointer(funcPtr, delegateType);

        _registrations[functionName] = new FunctionRegistration
        {
            DllName          = dllName,
            FunctionName     = functionName,
            ParameterSpecs   = paramSpecs,
            ReturnSpec       = returnSpec,
            Convention       = convention,
            CaptureLastError = captureLastError,
            DelegateType     = delegateType,
            BoundDelegate    = bound,
        };
    }

    private IntPtr EnsureLibraryLoaded(string dllName)
    {
        if (_loadedLibs.TryGetValue(dllName, out var existing))
            return existing;

        var handle = NativeMemory.LoadLibraryA(dllName);

        if (handle == IntPtr.Zero)
            throw new DllNotFoundException(
                "Could not load '" + dllName + "'. " +
                "Error: " + Marshal.GetLastWin32Error().ToString());

        _loadedLibs[dllName] = handle;
        return handle;
    }

    private FunctionRegistration GetRegistration(string functionName)
    {
        if (!_registrations.TryGetValue(functionName, out var reg))
            throw new InvalidOperationException(
                "Function not registered: '" + functionName + "'. " +
                "Call Register(), RegisterAddr(), or RegisterCode() first.");
        return reg;
    }

    // ── Private — argument helpers ────────────────────────────────────────────

    private static object?[] CollectArgs(
        object? a0, object? a1, object? a2, object? a3,
        object? a4, object? a5, object? a6, object? a7,
        object? a8, object? a9, object? a10, object? a11)
    {
        var all    = new[] { a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11 };
        var result = new List<object?>(12);
        foreach (var a in all)
        {
            if (a is Missing || a == Type.Missing) break;
            result.Add(a);
        }
        return result.ToArray();
    }

    /// <summary>
    /// Coerces each collected argument to the CLR type the registered delegate expects.
    /// Necessary because JScript sends all integers as VT_I4 (Int32) regardless of the
    /// target type — IntPtr in particular has no implicit conversion from Int32 through
    /// reflection, causing DynamicInvoke to throw without this step.
    /// </summary>
    private static object?[] CoerceArgs(object?[] args, ArgSpec[] specs)
    {
        var result = new object?[args.Length];
        for (var i = 0; i < args.Length; i++)
            result[i] = i < specs.Length ? CoerceArg(args[i], specs[i].ClrType) : args[i];
        return result;
    }

    private static object? CoerceArg(object? value, Type target)
    {
        if (value == null)                            return null;
        if (target.IsAssignableFrom(value.GetType())) return value;

        // IntPtr / UIntPtr have no Convert.ChangeType support — handle explicitly
        if (target == typeof(IntPtr))  return new IntPtr(Convert.ToInt64(value));
        if (target == typeof(UIntPtr)) return new UIntPtr(Convert.ToUInt64(value));

        // Byref targets (ref int, ref long, …) — unwrap to the element type for the coercion;
        // DynamicInvoke will handle the ref boxing internally
        if (target.IsByRef)
        {
            var elem = target.GetElementType()!;
            if (elem == typeof(IntPtr))  return new IntPtr(Convert.ToInt64(value));
            if (elem == typeof(UIntPtr)) return new UIntPtr(Convert.ToUInt64(value));
            try { return Convert.ChangeType(value, elem); } catch { return value; }
        }

        try { return Convert.ChangeType(value, target); }
        catch { return value; }   // leave unconverted — DynamicInvoke will surface the real error
    }

    private static void ParseOffsetAndType(
        object? offsetOrType,
        object? type,
        string  defaultType,
        out int    offset,
        out string typeStr)
    {
        offset  = 0;
        typeStr = defaultType;

        var missing1 = offsetOrType is Missing || offsetOrType == Type.Missing || offsetOrType == null;
        var missing2 = type         is Missing || type         == Type.Missing || type         == null;

        if (missing1) return;

        if (offsetOrType is string s1)
        {
            typeStr = s1;
        }
        else
        {
            offset = Convert.ToInt32(offsetOrType);
            if (!missing2 && type is string s2) typeStr = s2;
        }
    }

    // ── Private — string / int coercion helpers ───────────────────────────────

    /// <summary>
    /// Normalises a COM <c>[Optional]</c> parameter that is typed as <c>object</c>:
    /// returns <paramref name="fallback"/> for Missing / null / empty,
    /// otherwise the string value.
    /// </summary>
    private static string? CoerceString(object? value, string? fallback = null)
    {
        if (value is Missing || value == Type.Missing || value == null) return fallback;
        var s = value as string ?? value.ToString();
        return string.IsNullOrEmpty(s) ? fallback : s;
    }

    /// <summary>
    /// Normalises a COM <c>[Optional]</c> parameter that is typed as <c>object</c>:
    /// returns <paramref name="fallback"/> for Missing / null, otherwise
    /// converts the value to <c>int</c>.
    /// </summary>
    private static int CoerceInt(object? value, int fallback = 0)
    {
        if (value is Missing || value == Type.Missing || value == null) return fallback;
        return Convert.ToInt32(value);
    }

    // ── Private — string read / write helpers ─────────────────────────────────

    private static string? ReadString(IntPtr ptr, string type)
    {
        switch (type)
        {
            case "w":  return Marshal.PtrToStringUni(ptr);
            case "s":
            case "z":  return Marshal.PtrToStringAnsi(ptr);
            default:   return Marshal.PtrToStringUni(ptr);
        }
    }

    private static IntPtr WriteString(string value, IntPtr dest, string type)
    {
        if (dest == IntPtr.Zero)
        {
            if (type == "w") return new IntPtr((value.Length + 1) * 2);
            return new IntPtr(value.Length + 1);
        }

        if (type == "w")
        {
            var bytes = Encoding.Unicode.GetBytes(value + "\0");
            Marshal.Copy(bytes, 0, dest, bytes.Length);
            return IntPtr.Add(dest, bytes.Length);
        }
        else
        {
            var bytes = Encoding.Default.GetBytes(value + "\0");
            Marshal.Copy(bytes, 0, dest, bytes.Length);
            return IntPtr.Add(dest, bytes.Length);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DynamicWrapper));
    }
}