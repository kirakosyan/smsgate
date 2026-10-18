#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
    [System.AttributeUsage(System.AttributeTargets.Parameter)]
    internal sealed class CallerArgumentExpressionAttribute : System.Attribute
    {
        public CallerArgumentExpressionAttribute(string parameterName) { ParameterName = parameterName; }
        public string ParameterName { get; }
    }
}
#endif
