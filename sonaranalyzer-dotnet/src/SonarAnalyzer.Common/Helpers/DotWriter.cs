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

using System.Linq;
using System.Text;

namespace SonarAnalyzer.Helpers
{
    internal class DotWriter
    {
        private readonly StringBuilder stringBuilder;

        public DotWriter(StringBuilder stringBuilder)
        {
            this.stringBuilder = stringBuilder;
        }

        public void WriteGraphStart(string graphName, bool subgraph)
        {
            stringBuilder.AppendLine(subgraph ? $"subgraph \"cluster_{Encode(graphName)}\" {{\nlabel = \"{Encode(graphName)}\"" : $"digraph \"{Encode(graphName)}\" {{");
        }

        public void WriteGraphEnd()
        {
            stringBuilder.AppendLine("}");
        }

        public void WriteNode(string id, string header, params string[] items)
        {
            // Curly braces in the label reverse the orientation of the columns/rows
            // Columns/rows are created with pipe
            // New lines are inserted with \n; \r\n does not work well.
            // ID [shape=record label="{<header>|<line1>\n<line2>\n...}"]
            stringBuilder.Append(id);
            stringBuilder.Append(" [shape=record label=\"{" + header);
            if (items.Length > 0)
            {
                stringBuilder.Append("|");
                stringBuilder.Append(string.Join("|", items.Select(Encode)));
            }
            stringBuilder.Append("}\"");
            stringBuilder.AppendLine("]");
        }

        public void WriteEdge(string startId, string endId, string label)
        {
            stringBuilder.Append($"{startId} -> {endId}");
            if (!string.IsNullOrEmpty(label))
            {
                stringBuilder.Append($" [label=\"{label}\"]");
            }
            stringBuilder.AppendLine();
        }

        private static string Encode(string s) =>
            s.Replace("\r", string.Empty)
            .Replace("\n", "\\n")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace("|", "\\|")
            .Replace("<", "\\<")
            .Replace(">", "\\>")
            .Replace("\"", "\\\"");
    }
}
