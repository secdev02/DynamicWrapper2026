using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;

namespace DynaCall;

/// <summary>
/// Constructs delegate types at runtime — one per registered function — decorated
/// with the correct <see cref="UnmanagedFunctionPointerAttribute"/> and any
/// necessary per-parameter <see cref="MarshalAsAttribute"/> annotations.
/// </summary>
internal static class DelegateBuilder
{
    private static readonly ModuleBuilder s_module = CreateDynamicModule();
    private static readonly Dictionary<string, Type> s_typeCache =
        new Dictionary<string, Type>(StringComparer.Ordinal);
    private static readonly object s_cacheLock = new object();
    private static int s_nextTypeId;

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
        var cacheKey = BuildCacheKey(paramSpecs, returnSpec, convention, captureLastError);

        lock (s_cacheLock)
        {
            if (s_typeCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var typeName = "Fn_" + Sanitize(functionName) + "_" + (++s_nextTypeId).ToString();
            var typeBuilder = s_module.DefineType(
                typeName,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.AutoClass,
                typeof(MulticastDelegate));

            ApplyUnmanagedFunctionPointerAttr(typeBuilder, convention, captureLastError);
            EmitDelegateConstructor(typeBuilder);
            EmitInvokeMethod(typeBuilder, paramSpecs, returnSpec);

            var created = typeBuilder.CreateType()!;
            s_typeCache.Add(cacheKey, created);
            return created;
        }
    }

    // ── Private — type construction ───────────────────────────────────────────

    private static string BuildCacheKey(
        ArgSpec[] paramSpecs,
        ArgSpec returnSpec,
        CallingConvention convention,
        bool captureLastError)
    {
        var sb = new StringBuilder(96);
        sb.Append((int)convention).Append('|')
          .Append(captureLastError ? '1' : '0').Append('|');

        AppendSpecKey(sb, returnSpec);
        for (var i = 0; i < paramSpecs.Length; i++)
            AppendSpecKey(sb, paramSpecs[i]);

        return sb.ToString();
    }

    private static void AppendSpecKey(StringBuilder sb, ArgSpec spec)
    {
        sb.Append(spec.ClrType.AssemblyQualifiedName)
          .Append(':')
          .Append(spec.MarshalAs.HasValue ? ((int)spec.MarshalAs.Value).ToString() : "-")
          .Append(';');
    }

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
