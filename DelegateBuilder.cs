using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace DynaCall;

/// <summary>
/// Constructs delegate types at runtime — one per registered function — decorated
/// with the correct <see cref="UnmanagedFunctionPointerAttribute"/> and any
/// necessary per-parameter <see cref="MarshalAsAttribute"/> annotations.
/// </summary>
internal static class DelegateBuilder
{
    private static readonly ModuleBuilder s_module = CreateDynamicModule();

    private static ModuleBuilder CreateDynamicModule()
    {
        var asmName = new AssemblyName("DynaCall.Dynamic." + Guid.NewGuid().ToString("N").Substring(0, 8));
        var asm     = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
        return asm.DefineDynamicModule("DynaCall.Dynamic");
    }

    /// <summary>
    /// Emits a sealed delegate type whose <c>Invoke</c> signature matches
    /// <paramref name="paramSpecs"/> → <paramref name="returnSpec"/>.
    /// </summary>
    /// <param name="functionName">Used to produce a readable (though unique) type name.</param>
    /// <param name="paramSpecs">Ordered parameter descriptors.</param>
    /// <param name="returnSpec">Return-type descriptor.</param>
    /// <param name="convention">Unmanaged calling convention.</param>
    /// <param name="captureLastError">
    ///   When true, sets <c>SetLastError = true</c> on the
    ///   <see cref="UnmanagedFunctionPointerAttribute"/> so the runtime saves the Win32
    ///   last-error code after each call. Retrieve it with
    ///   <see cref="System.Runtime.InteropServices.Marshal.GetLastWin32Error"/>.
    /// </param>
    public static Type Build(
        string            functionName,
        ArgSpec[]         paramSpecs,
        ArgSpec           returnSpec,
        CallingConvention convention,
        bool              captureLastError = false)
    {
        var typeName    = "Fn_" + Sanitize(functionName) + "_" + Guid.NewGuid().ToString("N");
        var typeBuilder = s_module.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.AutoClass,
            typeof(MulticastDelegate));

        ApplyUnmanagedFunctionPointerAttr(typeBuilder, convention, captureLastError);
        EmitDelegateConstructor(typeBuilder);
        EmitInvokeMethod(typeBuilder, paramSpecs, returnSpec);

        return typeBuilder.CreateType()!;
    }

    // ── Private — type construction ───────────────────────────────────────────

    private static void ApplyUnmanagedFunctionPointerAttr(
        TypeBuilder        tb,
        CallingConvention  convention,
        bool               setLastError)
    {
        var ctor = typeof(UnmanagedFunctionPointerAttribute)
            .GetConstructor(new[] { typeof(CallingConvention) })!;

        CustomAttributeBuilder attr;

        if (setLastError)
        {
            var prop    = typeof(UnmanagedFunctionPointerAttribute).GetProperty("SetLastError")!;
            attr = new CustomAttributeBuilder(
                ctor,
                new object[] { convention },
                new[] { prop },
                new object[] { true });
        }
        else
        {
            attr = new CustomAttributeBuilder(ctor, new object[] { convention });
        }

        tb.SetCustomAttribute(attr);
    }

    private static void EmitDelegateConstructor(TypeBuilder tb)
    {
        var ctor = tb.DefineConstructor(
            MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { typeof(object), typeof(IntPtr) });

        ctor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
    }

    private static void EmitInvokeMethod(TypeBuilder tb, ArgSpec[] paramSpecs, ArgSpec returnSpec)
    {
        var clrParamTypes = Array.ConvertAll(paramSpecs, s => s.ClrType);

        var invoke = tb.DefineMethod(
            "Invoke",
            MethodAttributes.Public | MethodAttributes.HideBySig |
            MethodAttributes.NewSlot | MethodAttributes.Virtual,
            returnSpec.ClrType,
            clrParamTypes);

        invoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        // ── Per-parameter MarshalAs ─────────────────────────────────────────
        for (var i = 0; i < paramSpecs.Length; i++)
        {
            if (!paramSpecs[i].MarshalAs.HasValue) continue;

            var pb = invoke.DefineParameter(i + 1, ParameterAttributes.None, "arg" + i.ToString());
            pb.SetCustomAttribute(MakeMarshalAsAttr(paramSpecs[i].MarshalAs!.Value));
        }

        // ── Return-type MarshalAs ───────────────────────────────────────────
        if (returnSpec.MarshalAs.HasValue)
        {
            var ret = invoke.DefineParameter(0, ParameterAttributes.Retval, null);
            ret.SetCustomAttribute(MakeMarshalAsAttr(returnSpec.MarshalAs!.Value));
        }
    }

    // ── Private — attribute helpers ───────────────────────────────────────────

    private static CustomAttributeBuilder MakeMarshalAsAttr(UnmanagedType unmanagedType)
    {
        var ctor = typeof(MarshalAsAttribute)
            .GetConstructor(new[] { typeof(UnmanagedType) })!;

        return new CustomAttributeBuilder(ctor, new object[] { unmanagedType });
    }

    private static string Sanitize(string name)
    {
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                chars[i] = '_';
        }
        return new string(chars);
    }
}
