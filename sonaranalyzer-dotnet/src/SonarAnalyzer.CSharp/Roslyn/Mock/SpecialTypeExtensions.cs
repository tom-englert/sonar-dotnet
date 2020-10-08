
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.CodeAnalysis
{
    internal static class SpecialTypeExtensions
    {
        public static int VBForToShiftBits(this SpecialType specialType) => 0;
        public static bool IsSignedIntegralType(this SpecialType special) => false;
        public static bool IsUnsignedIntegralType(this SpecialType special) => false;
        public static bool IsNumericType(this SpecialType special) => false;
    }
}
