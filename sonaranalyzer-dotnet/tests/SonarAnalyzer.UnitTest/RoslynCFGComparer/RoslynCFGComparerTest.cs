extern alias csharp;

using System.Linq;
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

using csharp::SonarAnalyzer.Rules.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SonarAnalyzer.UnitTest.TestFramework;

namespace SonarAnalyzer.UnitTest.Rules
{
    [TestClass]
    public class RoslynCfgComparerTest
    {
        [TestMethod]
        [DataRow("AnonymousFunctions")]
        [DataRow("LocalFunctions")]
        [DataRow("Branching")]
        [DataRow("Loop")]
        [DataRow("Nested")]
        [DataRow("PatternMatching")]
        [DataRow("Simple")]
        [DataRow("TryCatch")]
        public void RoslynCfgComparer_RenderCfgs_CS(string filename) =>
            Verifier.VerifyAnalyzer(@$"TestCases\RoslynCFGComparer\{filename}.cs", new RoslynCfgComparer());

        [TestMethod]
        [DataRow("Branching")]
        [DataRow("TryCatch")]
        public void RoslynCfgComparer_RenderCfgs_VB(string filename) =>
            Verifier.VerifyAnalyzer(@$"TestCases\RoslynCFGComparer\{filename}.vb", new SonarAnalyzer.Rules.VisualBasic.RoslynCfgComparer());
    }
}
