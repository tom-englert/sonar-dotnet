
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.CodeAnalysis
{
    internal static class CompilationExtensions
    {
        public static ISymbol CommonGetWellKnownTypeMember(this Compilation compilation, WellKnownMember member) => null;
        public static ITypeSymbol CommonGetWellKnownType(this Compilation compilation, WellKnownType type) => null;
        public static IConvertibleConversion ClassifyConvertibleConversion(this Compilation compilation, IOperation condition, ITypeSymbol type, out ConstantValue constantValue)
        {
            constantValue = null;
            return null;
        }
        public static ISymbol CommonGetSpecialTypeMember(this Compilation compilation, SpecialMember member) => null;
    }
}
