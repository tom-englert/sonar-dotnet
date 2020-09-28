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
using SonarAnalyzer.Common;
using SonarAnalyzer.ControlFlowGraph;
using SonarAnalyzer.ControlFlowGraph.CSharp;
using SonarAnalyzer.Helpers;
using RoslynCFG = Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph;

namespace SonarAnalyzer.Rules.CSharp
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    [Rule(DiagnosticId)]
    public sealed class RoslynCFGComparer : SonarDiagnosticAnalyzer
    {
        private const string DiagnosticId = "S-COMPARE";
        private const string MessageFormat = "CFG Comparer";

        private static readonly DiagnosticDescriptor rule = new DiagnosticDescriptor(DiagnosticId, DiagnosticId, MessageFormat, "Debug", DiagnosticSeverity.Warning, true, null, null, DiagnosticDescriptorBuilder.MainSourceScopeTag);
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(rule);

        protected override void Initialize(SonarAnalysisContext context)
        {
            // Output is rendered to Solution/Tests/RoslynData project
            context.RegisterSyntaxNodeActionInNonGenerated(
                ProcessBaseMethod,
                SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration);
            //FIXME: Expressions a dalsi syntaxe, ktere muzeme generovat
        }

        private void ProcessBaseMethod(SyntaxNodeAnalysisContext c)
        {
            var method = (BaseMethodDeclarationSyntax)c.Node;
            var methodName = (method as MethodDeclarationSyntax)?.Identifier.ValueText ?? c.Node.FirstAncestorOrSelf<TypeDeclarationSyntax>().Identifier.ValueText + ".ctor";
            var sourceFileName = Path.GetFileNameWithoutExtension(c.Node.GetLocation().GetLineSpan().Path);
            var languageVersion = c.Compilation.GetLanguageVersion().ToString();
            var root = Path.GetFullPath(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + @$"\..\..\..\..\RoslynData\{sourceFileName}\");
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

        private string Serialize(IControlFlowGraph sonarCfg, RoslynCFG roslynCfg, string methodName)
        {
            var sb = new StringBuilder();
            var writer = new DotWriter(sb);
            writer.WriteGraphStart(methodName, false);
            if(sonarCfg == null)
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

            public RoslynCfgWalker(DotWriter writer)
            {
                this.writer = writer;
            }

            public void Visit(string methodName, RoslynCFG cfg, bool subgraph)
            {
                writer.WriteGraphStart(methodName, subgraph);
                foreach(var region in cfg.Root.NestedRegions)
                {
                    Visit(cfg, region);
                }
                foreach (var block in cfg.Blocks.Where(x => !visited.Contains(x)).ToArray())
                {
                    Visit(block);
                }
                writer.WriteGraphEnd();
            }

            private void Visit(RoslynCFG cfg, ControlFlowRegion region)
            {
                writer.WriteGraphStart(region.Kind + " region", true);
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
                writer.WriteNode(BlockId(block), header, block.Operations.SelectMany(Serialize).Concat(SerializeBranchValue(block.BranchValue)).ToArray());
            }

            private IEnumerable<string> SerializeBranchValue(IOperation operation) =>
                operation == null
                    ? Enumerable.Empty<string>()
                    : new[] { "## BranchValue ##" }.Concat(Serialize(operation));

            private IEnumerable<string> Serialize(IOperation operation) => Serialize(0, operation).Concat(new[] { new string('#', 10) });

            private IEnumerable<string> Serialize(int level, IOperation operation)
            {
                var ret = new List<string>();
                ret.AddRange(operation.Children.SelectMany(x => Serialize(level + 1, x)));
                ret.Add($"{level}# {operation.GetType().Name} / {operation.Syntax.GetType().Name}: {operation.Syntax}");
                return ret;
            }

            private void WriteEdges(BasicBlock block)
            {
                foreach (var predecessor in block.Predecessors)
                {
                    var label = "";
                    if (predecessor.Source.ConditionKind != ControlFlowConditionKind.None)
                    {
                        label = predecessor == predecessor.Source.ConditionalSuccessor
                            ? predecessor.Source.ConditionKind.ToString()
                            : "Else";
                    }
                    writer.WriteEdge(BlockId(predecessor.Source), BlockId(block), label);
                }
            }

            private string BlockId(BasicBlock block) =>
                "R" + block.Ordinal; // To prevent colision with CfgSerializer in common subgraph
        }
    }
}
