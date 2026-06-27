using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace DynaCall;

/// <summary>
/// Produces a native-callable delegate from any managed delegate — the implementation
/// behind <see cref="DynamicWrapper.RegisterCallback"/>.
/// </summary>
/// <remarks>
/// <para><b>Mechanism</b></para>
/// <para>
/// For each callback, a <see cref="DynamicMethod"/> is emitted whose signature
/// exactly matches the native function pointer the API expects. The method:
/// </para>
/// <list type="number">
///   <item>Boxes all incoming native arguments into an <c>object[]</c>.</item>
///   <item>
///     Calls <see cref="CallbackDispatcher.Dispatch"/> on a <see cref="CallbackDispatcher"/>
///     instance that is bound as the hidden first argument of the dynamic method.
///   </item>
///   <item>Unboxes (or casts) the return value back to the native type.</item>
/// </list>
/// <para>
/// The <see cref="CallbackDispatcher"/> holds a strong reference to the user's delegate,
/// preventing collection while the function pointer is live. The caller is responsible for
/// keeping the returned <see cref="Delegate"/> alive — typically by storing it in
/// <see cref="DynamicWrapper"/>'s callback list until <see cref="DynamicWrapper.Dispose"/>
/// is called.
/// </para>
/// </remarks>
internal static class CallbackBuilder
{
    private static readonly MethodInfo s_dispatch =
        typeof(CallbackDispatcher).GetMethod("Dispatch", new[] { typeof(object?[]) })!;

    /// <summary>
    /// Wraps <paramref name="userCallback"/> in a native-compatible delegate, then returns
    /// both the wrapper delegate (which must be kept alive) and the native function pointer.
    /// </summary>
    public static (Delegate NativeDelegate, IntPtr FunctionPointer) Build(
        Delegate          userCallback,
        string            name,
        ArgSpec[]         paramSpecs,
        ArgSpec           returnSpec,
        CallingConvention convention)
    {
        var nativeDelegateType = DelegateBuilder.Build(name, paramSpecs, returnSpec, convention);
        var clrParamTypes      = Array.ConvertAll(paramSpecs, s => s.ClrType);

        // The DynamicMethod has one extra first parameter — the bound CallbackDispatcher.
        // When CreateDelegate(type, target) is called with the dispatcher as the target,
        // that first parameter becomes invisible to callers; the delegate's public
        // signature then matches nativeDelegateType exactly.
        var dmParamTypes    = new Type[clrParamTypes.Length + 1];
        dmParamTypes[0]     = typeof(object);   // bound target — compatible with any object
        Array.Copy(clrParamTypes, 0, dmParamTypes, 1, clrParamTypes.Length);

        var dm = new DynamicMethod(
            "Callback_" + name + "_" + Guid.NewGuid().ToString("N"),
            returnSpec.ClrType,
            dmParamTypes,
            typeof(CallbackBuilder).Module,
            skipVisibility: true);

        EmitDispatchBody(dm.GetILGenerator(), clrParamTypes, returnSpec);

        var dispatcher     = new CallbackDispatcher(userCallback);
        var nativeDelegate = dm.CreateDelegate(nativeDelegateType, dispatcher);
        var funcPtr        = Marshal.GetFunctionPointerForDelegate(nativeDelegate);

        return (nativeDelegate, funcPtr);
    }

    // ── Private — IL emission ─────────────────────────────────────────────────

    private static void EmitDispatchBody(
        ILGenerator il,
        Type[]      clrParamTypes,
        ArgSpec     returnSpec)
    {
        var argsLocal = il.DeclareLocal(typeof(object?[]));

        // object?[] args = new object?[paramCount]
        il.Emit(OpCodes.Ldc_I4, clrParamTypes.Length);
        il.Emit(OpCodes.Newarr, typeof(object));
        il.Emit(OpCodes.Stloc,  argsLocal);

        // args[i] = (object)argN  — box value types
        for (var i = 0; i < clrParamTypes.Length; i++)
        {
            il.Emit(OpCodes.Ldloc,  argsLocal);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldarg,  i + 1);    // +1 — arg0 is the bound dispatcher

            if (clrParamTypes[i].IsValueType)
                il.Emit(OpCodes.Box, clrParamTypes[i]);

            il.Emit(OpCodes.Stelem_Ref);
        }

        // ((CallbackDispatcher)arg0).Dispatch(args)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(CallbackDispatcher));
        il.Emit(OpCodes.Ldloc,     argsLocal);
        il.Emit(OpCodes.Callvirt,  s_dispatch);

        // Adapt the object? return from Dispatch to the native return type
        if (returnSpec.ClrType == typeof(void))
        {
            il.Emit(OpCodes.Pop);               // discard the boxed return
        }
        else if (returnSpec.ClrType.IsValueType)
        {
            il.Emit(OpCodes.Unbox_Any, returnSpec.ClrType);
        }
        else
        {
            il.Emit(OpCodes.Castclass, returnSpec.ClrType);
        }

        il.Emit(OpCodes.Ret);
    }
}
