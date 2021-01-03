/*
 * SonarAnalyzer for .NET
 * Copyright (C) 2015-2020 SonarSource SA
 * mailto: contact AT sonarsource DOT com
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program; if not, write to the Free Software Foundation,
 * Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 */

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SonarAnalyzer.ControlFlowGraph;
using SonarAnalyzer.Helpers;
using SonarAnalyzer.Rules.SymbolicExecution;
using SonarAnalyzer.SymbolicExecution;
using SonarAnalyzer.SymbolicExecution.Constraints;

namespace SonarAnalyzer.Rules.CSharp
{
    internal sealed class NullPointerDereference : ISymbolicExecutionAnalyzer
    {
        internal const string NullDiagnosticId = "Null";
        internal const string NotNullDiagnosticId = "NotNull";

        private static readonly DiagnosticDescriptor NullRule = new DiagnosticDescriptor(NullDiagnosticId, string.Empty, string.Empty, string.Empty, DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor NotNullRule = new DiagnosticDescriptor(NotNullDiagnosticId, string.Empty, string.Empty, string.Empty, DiagnosticSeverity.Error, true);

        public IEnumerable<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(NullRule, NotNullRule);

        internal const string DiagnosticId = "Dummy";

        public ISymbolicExecutionAnalysisContext AddChecks(CSharpExplodedGraph explodedGraph, SyntaxNodeAnalysisContext context) =>
            new AnalysisContext(explodedGraph, context);

        private sealed class AnalysisContext : ISymbolicExecutionAnalysisContext
        {
            private readonly CSharpExplodedGraph explodedGraph;
            private readonly SyntaxNodeAnalysisContext context;
            private readonly Dictionary<ExpressionSyntax, bool> identifiers = new Dictionary<ExpressionSyntax, bool>();

            public AnalysisContext(CSharpExplodedGraph explodedGraph, SyntaxNodeAnalysisContext context)
            {
                this.explodedGraph = explodedGraph;
                this.context = context;

                explodedGraph.MemberAccessed += MemberAccessedHandler;
            }

            public bool SupportsPartialResults => true;

            public IEnumerable<Diagnostic> GetDiagnostics() =>
                identifiers.Select(item => Diagnostic.Create(
                    item.Value ? NullRule : NotNullRule,
                    item.Key.GetLocation(),
                    item.Key.ToString()));

            public void Dispose()
            {
                explodedGraph.MemberAccessed -= MemberAccessedHandler;
            }

            private void MemberAccessedHandler(object sender, MemberAccessedEventArgs args) =>
                CollectMemberAccesses(args, context.SemanticModel);

            private void CollectMemberAccesses(MemberAccessedEventArgs args, SemanticModel semanticModel)
            {
                if (!semanticModel.IsExtensionMethod(args.Identifier.Parent))
                {
                    var existing = identifiers.TryGetValue(args.Identifier, out var maybeNull);
                    if (!existing || !maybeNull)
                    {
                        identifiers[args.Identifier] = args.MaybeNull;
                    }
                }
            }
        }
    }
}
