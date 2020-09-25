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
            context.RegisterSyntaxNodeActionInNonGenerated(ProcessMethod, SyntaxKind.MethodDeclaration);
            //FIXME: Expressions a dalsi syntaxe, ktere muzeme generovat
        }

        private void ProcessMethod(SyntaxNodeAnalysisContext c)
        {
            var method = (MethodDeclarationSyntax)c.Node;
            var sourceFileName = Path.GetFileNameWithoutExtension(c.Node.GetLocation().GetLineSpan().Path);
            var languageVersion = c.Compilation.GetLanguageVersion().ToString();
            var root = Path.GetFullPath(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + @$"\..\..\..\..\RoslynData\{sourceFileName}\{method.Identifier.ValueText}\");
            Directory.CreateDirectory(root);
            File.WriteAllText(root + "Source.cs.txt", method.ToString());
            if (CSharpControlFlowGraph.TryGet(method.Body, c.SemanticModel, out var cfg))
            {
                File.WriteAllText(root + $"CFG.{languageVersion}.SonarAnalyzer.txt", CfgSerializer.Serialize("SonarAnalyzer." + method.Identifier.ValueText, cfg));
            }
            var roslynCfg = RoslynCFG.Create(c.Node, c.SemanticModel);
            File.WriteAllText(root + $"CFG.{languageVersion}.Roslyn.txt", Serialize("Roslyn." + method.Identifier.ValueText, roslynCfg));
        }

        private string Serialize(string methodName, RoslynCFG cfg)
        {
            var stringBuilder = new StringBuilder();
            using (var writer = new StringWriter(stringBuilder))
            {
                new RoslynCfgWalker(new DotWriter(writer)).Visit(methodName, cfg);
            }
            return stringBuilder.ToString();
        }

        private class RoslynCfgWalker
        {
            private readonly DotWriter writer;

            public RoslynCfgWalker(DotWriter writer)
            {
                this.writer = writer;
            }

            public void Visit(string methodName, RoslynCFG cfg)
            {
                writer.WriteGraphStart(methodName);
                foreach (var block in cfg.Blocks)
                {
                    Visit(block);
                }
                writer.WriteGraphEnd();
            }

            private void Visit(BasicBlock block)
            {
                WriteNode(block);
                WriteEdges(block);
            }

            private void WriteNode(BasicBlock block, SyntaxNode terminator = null)
            {
                var header = block.Kind.ToString().ToUpperInvariant();
                if (terminator != null)
                {
                    // shorten the text
                    var terminatorType = terminator.Kind().ToString().Replace("Syntax", string.Empty);
                    header += ":" + terminatorType;
                }
                header += " #" + block.Ordinal;
                writer.WriteNode(block.Ordinal.ToString(), header, block.Operations.SelectMany(Serialize).Concat(SerializeBranchValue(block.BranchValue)).ToArray());
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
                    writer.WriteEdge(predecessor.Source.Ordinal.ToString(), block.Ordinal.ToString(), label);
                }
            }
        }
    }
}
