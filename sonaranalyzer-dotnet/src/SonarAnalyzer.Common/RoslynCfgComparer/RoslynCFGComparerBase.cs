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
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SonarAnalyzer.Common;
using SonarAnalyzer.Helpers;
using RoslynCFG = Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph;

namespace SonarAnalyzer.Rules
{
    public abstract class RoslynCfgComparerBase : SonarDiagnosticAnalyzer
    {
        protected const string DiagnosticId = "S-COMPARE";
        private const string MessageFormat = "CFG Comparer";

        private static readonly DiagnosticDescriptor rule = new DiagnosticDescriptor(DiagnosticId, DiagnosticId, MessageFormat, "Debug", DiagnosticSeverity.Warning, true, null, null,
            DiagnosticDescriptorBuilder.MainSourceScopeTag);
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(rule);

        internal abstract void SerializeSonarCfg(SyntaxNodeAnalysisContext c, DotWriter writer, StringBuilder sb); // Ugly redundant argument
        internal abstract string LanguageVersion(Compilation c);
        internal abstract string MethodName(SyntaxNodeAnalysisContext c);

        protected void ProcessBaseMethod(SyntaxNodeAnalysisContext c)
        {
            var sourceFileName = Path.GetFileNameWithoutExtension(c.Node.GetLocation().GetLineSpan().Path);
            var languageVersion = LanguageVersion(c.Compilation);
            var root = Path.GetFullPath(Path.GetDirectoryName(GetType().Assembly.Location) + @$"\..\..\..\..\RoslynData\{sourceFileName}\");
            var methodName = MethodName(c);
            Directory.CreateDirectory(root);

            var sb = new StringBuilder();
            var writer = new DotWriter(sb);
            writer.WriteGraphStart(methodName, false);

            SerializeSonarCfg(c, writer, sb);
            new RoslynCfgWalker(writer).Visit("Roslyn." + methodName, RoslynCFG.Create(c.Node, c.SemanticModel), true);

            writer.WriteGraphEnd();
            File.WriteAllText(root + $"CFG.{languageVersion}.{methodName}.txt",
                $@"// http://viz-js.com/
// https://edotor.net/?engine=dot#{System.Net.WebUtility.UrlEncode(sb.ToString()).Replace("+", "%20")}

/*
{c.Node}
*/

{sb}");
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
