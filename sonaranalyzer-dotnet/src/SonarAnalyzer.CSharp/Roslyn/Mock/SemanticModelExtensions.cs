
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.CodeAnalysis
{
    internal static class SemanticModelExtensions
    {
        public static SemanticModel ContainingModelOrSelf(this SemanticModel semanticModel) => semanticModel;
        public static SyntaxNode Root(this SemanticModel semanticModel) => null;
    }
}
