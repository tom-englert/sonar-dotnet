using System;

namespace Microsoft.CodeAnalysis
{
    internal static class ExceptionUtilities
    {
        public static Exception UnexpectedValue(object arg) => null;
        public static Exception Unreachable;
    }
}
