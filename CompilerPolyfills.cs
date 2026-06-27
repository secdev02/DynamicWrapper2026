// CompilerPolyfills.cs
//
// Marker types that enable C# 9–11 features (init, required, nullable) on
// .NET Framework 4.8 without a runtime upgrade. The compiler checks for their
// presence; the runtime never inspects them.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }

    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct |
        AttributeTargets.Field | AttributeTargets.Property,
        Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public string FeatureName { get; }
        public bool   IsOptional  { get; init; }
        public CompilerFeatureRequiredAttribute(string featureName)
            => FeatureName = featureName;
    }
}
