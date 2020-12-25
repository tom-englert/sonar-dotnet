﻿/*
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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SonarAnalyzer.CFG.Helpers
{
    internal static class CSharpSyntaxHelper
    {
        public static readonly string NameOfKeywordText =
            SyntaxFacts.GetText(SyntaxKind.NameOfKeyword);

        public static SyntaxNode RemoveParentheses(this SyntaxNode expression)
        {
            var currentExpression = expression;
            var parentheses = expression as ParenthesizedExpressionSyntax;
            while (parentheses != null)
            {
                currentExpression = parentheses.Expression;
                parentheses = currentExpression as ParenthesizedExpressionSyntax;
            }
            return currentExpression;
        }

        public static ExpressionSyntax RemoveParentheses(this ExpressionSyntax expression) =>
            (ExpressionSyntax)RemoveParentheses((SyntaxNode)expression);

        public static bool IsNameof(this InvocationExpressionSyntax expression, SemanticModel semanticModel)
        {
            if (semanticModel.GetSymbolOrCandidateSymbol(expression) is IMethodSymbol calledSymbol)
            {
                // NRT_EXTENSIONS => if any "ref" method is called, terminate and suppress nothing; this scenario is not properly handled.
                if (calledSymbol.ReturnsByRef || calledSymbol.ReturnsByRefReadonly)
                {
                    throw new NotSupportedException();
                }

                return false;
            }

            var nameofIdentifier = (expression?.Expression as IdentifierNameSyntax)?.Identifier;

            return nameofIdentifier.HasValue &&
                (nameofIdentifier.Value.ToString() == NameOfKeywordText);
        }

        public static bool IsCatchingAllExceptions(this CatchClauseSyntax catchClause)
        {
            if (catchClause.Declaration == null)
            {
                return true;
            }

            var exceptionTypeName = catchClause.Declaration.Type.GetText().ToString().Trim();

            return catchClause.Filter == null &&
                (exceptionTypeName == "Exception" || exceptionTypeName == "System.Exception");
        }
    }
}
