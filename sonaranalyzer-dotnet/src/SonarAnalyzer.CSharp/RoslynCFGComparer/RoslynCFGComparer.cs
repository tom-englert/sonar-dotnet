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
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SonarAnalyzer.Common;
using SonarAnalyzer.ControlFlowGraph;
using SonarAnalyzer.ControlFlowGraph.CSharp;
using SonarAnalyzer.Helpers;
using RoslynCFG = Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph;

namespace SonarAnalyzer.Rules.CSharp
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    [Rule(DiagnosticId)]
    public sealed class RoslynCfgComparer : SonarDiagnosticAnalyzer
    {
        private const string DiagnosticId = "S-COMPARE";
        private const string MessageFormat = "CFG Comparer";

        private static readonly DiagnosticDescriptor rule = new DiagnosticDescriptor(DiagnosticId, DiagnosticId, MessageFormat, "Debug", DiagnosticSeverity.Warning, true, null, null,
            DiagnosticDescriptorBuilder.MainSourceScopeTag);
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(rule);

        protected override void Initialize(SonarAnalysisContext context)
        {
            // Output is rendered to Solution/Tests/RoslynData project
            context.RegisterSyntaxNodeActionInNonGenerated(
                ProcessBaseMethod,
                SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration);
        }

        private void ProcessBaseMethod(SyntaxNodeAnalysisContext c)
        {
            var method = (BaseMethodDeclarationSyntax)c.Node;
            var methodName = (method as MethodDeclarationSyntax)?.Identifier.ValueText ?? c.Node.FirstAncestorOrSelf<TypeDeclarationSyntax>().Identifier.ValueText + ".ctor";
            var sourceFileName = Path.GetFileNameWithoutExtension(c.Node.GetLocation().GetLineSpan().Path);
            var languageVersion = c.Compilation.GetLanguageVersion().ToString();
            var root = Path.GetFullPath(Path.GetDirectoryName(GetType().Assembly.Location) + @$"\..\..\..\..\RoslynData\{sourceFileName}\");
            Directory.CreateDirectory(root);
            var graph = Serialize(CSharpControlFlowGraph.Create((CSharpSyntaxNode)method.Body ?? method.ExpressionBody, c.SemanticModel), RoslynCFG.Create(c.Node, c.SemanticModel), methodName);
            File.WriteAllText(root + $"CFG.{languageVersion}.{methodName}.txt",
                $@"// http://viz-js.com/
// http://magjac.com/graphviz-visual-editor/?dot={System.Net.WebUtility.UrlEncode(graph)}

/*
{method}
*/

{graph}");
        }

        private static string Serialize(IControlFlowGraph sonarCfg, RoslynCFG roslynCfg, string methodName)
        {
            var sb = new StringBuilder();
            var writer = new DotWriter(sb);
            writer.WriteGraphStart(methodName, false);
            if (sonarCfg == null)
            {
                writer.WriteGraphStart("Sonar." + methodName, true);
                writer.WriteNode("0", "N/A");
                writer.WriteGraphEnd();
            }
            else
            {
                CfgSerializer.Serialize("Sonar." + methodName, sonarCfg, sb, true);
            }

            new RoslynCfgWalker(writer).Visit("Roslyn." + methodName, roslynCfg, true);

            writer.WriteGraphEnd();
            return sb.ToString();
        }

        private class RoslynCfgWalker
        {
            private readonly DotWriter writer;
            private readonly HashSet<BasicBlock> visited = new HashSet<BasicBlock>();
            private readonly char blockPrefix= '@';
            private readonly int nestingLevel;

            public RoslynCfgWalker(DotWriter writer, int nestingLevel = 0)
            {
                this.writer = writer;
                this.nestingLevel = nestingLevel;

                blockPrefix = (char)(blockPrefix + nestingLevel);
            }

            public void Visit(string methodName, RoslynCFG cfg, bool subgraph)
            {
                writer.WriteGraphStart(methodName, subgraph);

                foreach (var region in cfg.Root.NestedRegions)
                {
                    Visit(cfg, region);
                }

                foreach (var block in cfg.Blocks.Where(x => !visited.Contains(x)).ToArray())
                {
                    Visit(block);
                }

                foreach (var localFunction in cfg.LocalFunctions)
                {
                    var localFunctionCfg = cfg.GetLocalFunctionControlFlowGraph(localFunction);

                    new RoslynCfgWalker(writer, nestingLevel + 1).Visit($"{methodName}.{localFunction.Name}", localFunctionCfg, true);
                }

                foreach (var anonymousFunction in GetAnonymousFunctions(cfg))
                {
                    var anonymousFunctionCfg = cfg.GetAnonymousFunctionControlFlowGraph(anonymousFunction);

                    new RoslynCfgWalker(writer, nestingLevel + 1).Visit($"{methodName}.anonymous", anonymousFunctionCfg, true);
                }

                writer.WriteGraphEnd();
            }

            private void Visit(RoslynCFG cfg, ControlFlowRegion region)
            {
                writer.WriteGraphStart(region.Kind + " region" + (region.ExceptionType == null ? null : " " + region.ExceptionType), true);
                foreach (var nested in region.NestedRegions)
                {
                    Visit(cfg, nested);
                }
                foreach (var block in cfg.Blocks.Where(x => x.EnclosingRegion == region))
                {
                    Visit(block);
                }
                writer.WriteGraphEnd();
            }

            private void Visit(BasicBlock block)
            {
                visited.Add(block);
                WriteNode(block);
                WriteEdges(block);
            }

            private void WriteNode(BasicBlock block)
            {
                var header = block.Kind.ToString().ToUpperInvariant() + " #" + BlockId(block);
                writer.WriteNode(BlockId(block), header, block.Operations.SelectMany(SerializeOperation).Concat(SerializeBranchValue(block.BranchValue)).ToArray());
            }

            private static IEnumerable<string> SerializeBranchValue(IOperation operation) =>
                operation == null
                    ? Enumerable.Empty<string>()
                    : new[] { "## BranchValue ##" }.Concat(SerializeOperation(operation));

            private static IEnumerable<string> SerializeOperation(IOperation operation) => SerializeOperation(0, operation).Concat(new[] { new string('#', 10) });

            private static IEnumerable<string> SerializeOperation(int level, IOperation operation)
            {
                var ret = new List<string>();
                ret.AddRange(operation.Children.SelectMany(x => SerializeOperation(level + 1, x)));
                ret.Add($"{level}# {operation.GetType().Name}{OperationSuffix(operation)} / {operation.Syntax.GetType().Name}: {operation.Syntax}");
                return ret;
            }

            private static string OperationSuffix(IOperation op) =>
                op switch
                {
                    IInvocationOperation invocation => "." + invocation.TargetMethod.Name,
                    _ => null
                };

            private void WriteEdges(BasicBlock block)
            {
                foreach (var predecessor in block.Predecessors)
                {
                    var condition = "";
                    if (predecessor.Source.ConditionKind != ControlFlowConditionKind.None)
                    {
                        condition = predecessor == predecessor.Source.ConditionalSuccessor
                            ? predecessor.Source.ConditionKind.ToString()
                            : "Else";
                    }
                    var semantics = predecessor.Semantics == ControlFlowBranchSemantics.Regular ? null : predecessor.Semantics.ToString();
                    writer.WriteEdge(BlockId(predecessor.Source), BlockId(block), $"{semantics} {condition}".Trim());
                }
                if (block.FallThroughSuccessor != null && block.FallThroughSuccessor.Destination == null)
                {
                    writer.WriteEdge(BlockId(block), "NoDestination" + BlockId(block), block.FallThroughSuccessor.Semantics.ToString());
                }
            }

            private string BlockId(BasicBlock block) =>
                // To prevent collision with CfgSerializer in common subgraph
                blockPrefix == '@'
                    ? "Root" + block.Ordinal
                    : blockPrefix.ToString() + block.Ordinal;

            private static IEnumerable<IFlowAnonymousFunctionOperation> GetAnonymousFunctions(RoslynCFG cfg) =>
                cfg.Blocks
                   .SelectMany(block => block.Operations)
                   .Concat(cfg.Blocks.Select(block => block.BranchValue).Where(op => op != null))
                   .SelectMany(operation => operation.DescendantsAndSelf())
                   .OfType<IFlowAnonymousFunctionOperation>();
        }
    }
}
