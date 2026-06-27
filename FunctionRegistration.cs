using System;
using System.Runtime.InteropServices;

namespace DynaCall;

/// <summary>
/// Holds the fully-resolved metadata and bound delegate for a single registered
/// function — the runtime counterpart of one <c>DynamicWrapper.Register</c> call.
/// </summary>
internal sealed class FunctionRegistration
{
    /// <summary>The DLL that exports the function — "(address)" for RegisterAddr / RegisterCode.</summary>
    public required string            DllName          { get; init; }

    /// <summary>The exported symbol name.</summary>
    public required string            FunctionName     { get; init; }

    /// <summary>Ordered parameter descriptors — one per argument.</summary>
    public required ArgSpec[]         ParameterSpecs   { get; init; }

    /// <summary>Return-type descriptor.</summary>
    public required ArgSpec           ReturnSpec       { get; init; }

    /// <summary>Unmanaged calling convention — StdCall or Cdecl.</summary>
    public required CallingConvention Convention       { get; init; }

    /// <summary>
    /// When true, the delegate was built with <c>SetLastError = true</c> so the runtime
    /// saves the Win32 last-error code immediately after each call.
    /// </summary>
    public required bool              CaptureLastError { get; init; }

    /// <summary>
    /// The dynamically-emitted delegate <see cref="Type"/> — decorated with
    /// <see cref="UnmanagedFunctionPointerAttribute"/> and per-parameter
    /// <see cref="MarshalAsAttribute"/> annotations.
    /// </summary>
    public required Type              DelegateType     { get; init; }

    /// <summary>
    /// The live delegate wired to the native function pointer.
    /// Cast to <see cref="DelegateType"/> for direct invocation.
    /// </summary>
    public required Delegate          BoundDelegate    { get; init; }
}
