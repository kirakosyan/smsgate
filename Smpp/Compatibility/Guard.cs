using System;
using System.Runtime.CompilerServices;

namespace Smpp
{
    internal static class Guard
    {
        internal static void ThrowIfNull(object argument, [CallerArgumentExpression("argument")] string name = null)
        {
            if (argument == null) throw new ArgumentNullException(name);
        }
        internal static void ThrowIfNegative(int argument, [CallerArgumentExpression("argument")] string name = null)
        {
            if (argument < 0) throw new ArgumentOutOfRangeException(name, argument, "Value must not be negative.");
        }
        internal static void ThrowIfNegativeOrZero(int argument, [CallerArgumentExpression("argument")] string name = null)
        {
            if (argument <= 0) throw new ArgumentOutOfRangeException(name, argument, "Value must be positive.");
        }
        internal static void ThrowIfDisposed(bool disposed, object instance)
        {
            if (disposed) throw new ObjectDisposedException(instance.GetType().FullName);
        }
    }
}
