using System;
using System.Runtime.InteropServices;

namespace DynaCall;

/// <summary>
/// Describes a single argument position or return type — mapping a DynaCall
/// type character to its CLR equivalent and any required P/Invoke marshaling.
/// </summary>
internal sealed class ArgSpec
{
    /// <summary>The raw DynaCall character, always lower-case — 'l', 's', 'w', etc.</summary>
    public char           TypeChar  { get; init; }

    /// <summary>
    /// The CLR type used in the dynamically-emitted delegate signature.
    /// For byref strings this will be <see cref="System.Text.StringBuilder"/>;
    /// for other byref types it will be <c>T.MakeByRefType()</c>.
    /// </summary>
    public Type           ClrType   { get; init; } = typeof(IntPtr);

    /// <summary>True when the position is an output / byref parameter.</summary>
    public bool           IsByRef   { get; init; }

    /// <summary>
    /// Explicit P/Invoke marshaling directive — null means no attribute is emitted
    /// and the runtime default applies.
    /// </summary>
    public UnmanagedType? MarshalAs { get; init; }
}
