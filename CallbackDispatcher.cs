using System;

namespace DynaCall;

/// <summary>
/// Internal bridge between a native callback thunk and a managed user delegate —
/// the mechanism behind <see cref="DynamicWrapper.RegisterCallback"/>.
/// </summary>
/// <remarks>
/// An instance of this class is bound as the hidden first argument of the
/// <c>DynamicMethod</c> stub emitted by <see cref="CallbackBuilder"/>.
/// Holding a reference here prevents the GC from collecting the user's delegate
/// while the native function pointer is live.
/// </remarks>
internal sealed class CallbackDispatcher
{
    private readonly Delegate _target;

    public CallbackDispatcher(Delegate target) => _target = target;

    /// <summary>
    /// Called by the emitted stub — boxes the native arguments into an
    /// <c>object[]</c> and forwards them to the user delegate via
    /// <see cref="Delegate.DynamicInvoke"/>.
    /// </summary>
    public object? Dispatch(object?[] args) => _target.DynamicInvoke(args);
}
