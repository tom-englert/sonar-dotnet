using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.CodeAnalysis
{
    internal static class ImmutableExtensions
    {
        public static ImmutableArray<T> NullToEmpty<T>(this ImmutableArray<T> instance) => instance;
    }
}
