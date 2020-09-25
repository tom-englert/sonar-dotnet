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
            File.WriteAllText(root + "Source.cs", method.ToString());
            if(CSharpControlFlowGraph.TryGet(method.Body, c.SemanticModel, out var cfg))
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
                //if (block is BinaryBranchBlock binaryBranchBlock)
                //{
                //    WriteNode(block, binaryBranchBlock.BranchingNode);
                //    // Add labels to the binary branch block successors
                //    getLabel = b =>
                //    {
                //        if (b == binaryBranchBlock.TrueSuccessorBlock)
                //        {
                //            return bool.TrueString;
                //        }
                //        else if (b == binaryBranchBlock.FalseSuccessorBlock)
                //        {
                //            return bool.FalseString;
                //        }
                //        return string.Empty;
                //    };
                //}
                //else if (block is BranchBlock branchBlock)
                //{
                //    WriteNode(block, branchBlock.BranchingNode);
                //}
                //else if (block is ExitBlock exitBlock)
                //{
                //    WriteNode(block);
                //}
                //else if (block is ForeachCollectionProducerBlock foreachBlock)
                //{
                //    WriteNode(foreachBlock, foreachBlock.ForeachNode);
                //}
                //else if (block is ForInitializerBlock forBlock)
                //{
                //    WriteNode(forBlock, forBlock.ForNode);
                //}
                //else if (block is JumpBlock jumpBlock)
                //{
                //    WriteNode(jumpBlock, jumpBlock.JumpNode);
                //}
                //else if (block is LockBlock lockBlock)
                //{
                //    WriteNode(lockBlock, lockBlock.LockNode);
                //}
                //else if (block is UsingEndBlock usingBlock)
                //{
                //    WriteNode(usingBlock, usingBlock.UsingStatement);
                //}
                //else
                {
                    WriteNode(block);
                }
                WriteEdges(block);
            }

            private void WriteNode(BasicBlock block, SyntaxNode terminator = null)
            {
                var header = block.GetType().Name.SplitCamelCaseToWords().First().ToUpperInvariant();
                if (terminator != null)
                {
                    // shorten the text
                    var terminatorType = terminator.Kind().ToString().Replace("Syntax", string.Empty);
                    header += ":" + terminatorType;
                }
                writer.WriteNode(block.Ordinal.ToString(), header, block.Operations.Select(i => i.ToString()).ToArray());
            }

            private void WriteEdges(BasicBlock block)
            {
                foreach (var predecessor in block.Predecessors)
                {
                    writer.WriteEdge(predecessor.Source.Ordinal.ToString(), block.Ordinal.ToString(), predecessor.Source.ConditionKind.ToString());
                }
            }
        }
    }
}
