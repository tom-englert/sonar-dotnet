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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Diagnostics;
using SonarAnalyzer.Helpers;

namespace SonarAnalyzer.CBDE
{
    public class CbdeHandler
    {
        private const int ProcessStatPeriodMs = 1000;
        private const string CbdeOutputFileName = "cbdeSEout.xml";

        private static bool initialized;
        // this is the place where the cbde executable is unpacked. It is in a temp folder
        private static string extractedCbdeBinaryPath;
        private static readonly object logFileLock = new object();
        private static readonly object metricsFileLock = new object();
        private static readonly object perfFileLock = new object();
        private static readonly object staticInitLock = new object();

        private readonly Action<string, string, Location, CompilationAnalysisContext> raiseIssue;
        private readonly Func<CompilationAnalysisContext, bool> shouldRunInContext;
        private readonly Func<string> getOutputDirectory;

        // This is used by unit tests that want to check the log (whose path is the parameter of this action) contains
        // what is expected
        private readonly Action<string> onCbdeExecution;
        protected HashSet<string> csSourceFileNames= new HashSet<string>();
        protected Dictionary<string, int> fileNameDuplicateNumbering = new Dictionary<string, int>();
        private StringBuilder logStringBuilder;

        // cbdePath is inside .sonarqube/out/<n>/
        // cbdeDirectoryRoot contains mlir files and results for each assembly
        // cbdeProcessSpecificPath is the $"{cbdePath}/CBDE_{pid}/" folder
        // cbdeLogFile, cbdeMetricsLogFile and cbdePerfLogFile are inside cbdeProcessSpecificPath
        private string cbdePath;
        // the cbdeExecutablePath is normally the extractedCbdeBinaryPath, but can be different in tests
        private readonly string cbdeExecutablePath;
        private string cbdeDirectoryRoot;
        private string cbdeDirectoryAssembly;
        private string cbdeResultsPath;
        private string cbdeLogFile;
        private string cbdeMetricsLogFile;
        private string cbdePerfLogFile;
        private string moreDetailsMessage;
        private readonly bool emitLog;

        public CbdeHandler(Action<string, string, Location, CompilationAnalysisContext> raiseIssue,
            Func<CompilationAnalysisContext, bool> shouldRunInContext,
            Func<string> getOutputDirectory,
            string testCbdeBinaryPath = null, //  Used by unit tests
            Action<string> onCbdeExecution = null) // Used by unit tests
        {
            this.raiseIssue = raiseIssue;
            this.shouldRunInContext = shouldRunInContext;
            this.getOutputDirectory = getOutputDirectory;
            this.onCbdeExecution = onCbdeExecution;
            lock (staticInitLock)
            {
                if(!initialized)
                {
                    Initialize();
                    initialized = true;
                }
            }
            if (testCbdeBinaryPath == null)
            {
                emitLog = Environment.GetEnvironmentVariables().Contains("SONAR_DOTNET_INTERNAL_LOG_CBDE");
                cbdeExecutablePath = extractedCbdeBinaryPath;
            }
            else
            {
                // we are in test mode
                emitLog = true;
                cbdeExecutablePath = testCbdeBinaryPath;
            }
        }

        public void RegisterMlirAndCbdeInOneStep(SonarAnalysisContext context)
        {
            if (cbdeExecutablePath == null)
            {
                return;
            }

            context.RegisterCompilationAction(
                c =>
                {
                    if (!shouldRunInContext(c))
                    {
                        return;
                    }
                    var compilationHash = c.Compilation.GetHashCode();
                    InitializePathsAndLog(c.Compilation.Assembly.Name, compilationHash);
                    Log("CBDE: Compilation phase");
                    var exporterMetrics = new MlirExporterMetrics();
                    try
                    {
                        var watch = Stopwatch.StartNew();
                        var cpuWatch = ThreadCpuStopWatch.StartNew();
                        foreach (var tree in c.Compilation.SyntaxTrees)
                        {
                            csSourceFileNames.Add(tree.FilePath);
                            Log($"CBDE: Generating MLIR for source file {tree.FilePath} in context {compilationHash}");
                            var mlirFileName = ManglePath(tree.FilePath) + ".mlir";
                            ExportFunctionMlir(tree, c.Compilation.GetSemanticModel(tree), exporterMetrics, mlirFileName);
                            LogIfFailure($"- generated mlir file {mlirFileName}");
                            Log($"CBDE: Done with file {tree.FilePath} in context {compilationHash}");
                        }
                        Log($"CBDE: MLIR generation time: {watch.ElapsedMilliseconds} ms");
                        Log($"CBDE: MLIR generation cpu time: {cpuWatch.ElapsedMilliseconds} ms");
                        watch.Restart();
                        RunCbdeAndRaiseIssues(c);
                        Log($"CBDE: CBDE execution and reporting time: {watch.ElapsedMilliseconds} ms");
                        Log($"CBDE: CBDE execution and reporting cpu time: {cpuWatch.ElapsedMilliseconds} ms");
                        Log("CBDE: End of compilation");
                        lock (metricsFileLock)
                        {
                            File.AppendAllText(cbdeMetricsLogFile, exporterMetrics.Dump());
                        }
                    }
                    catch (Exception e)
                    {
                        Log("An exception has occured: " + e.Message + "\n" + e.StackTrace);
                        var message = $@"Top level error in CBDE handling: {e.Message}
Details: {moreDetailsMessage}
Inner exception: {e.InnerException}
Stack trace: {e.StackTrace}";
                        // Roslyn/MSBuild is currently cutting exception message at the end of the line instead
                        // of displaying the full message. As a workaround, we replace the line ending with ' ## '.
                        // See https://github.com/dotnet/roslyn/issues/1455 and https://github.com/dotnet/roslyn/issues/24346
                        throw new CbdeException(message.Replace("\n", " ## ").Replace("\r", ""));
                    }
                });
        }

        private void LogIfFailure(string s)
        {
            if (emitLog)
            {
                logStringBuilder.AppendLine(s);
            }
        }

        private void Log(string s)
        {
            if (emitLog)
            {
                lock (logFileLock)
                {
                    var message = $"{DateTime.Now} ({Thread.CurrentThread.ManagedThreadId,5}): {s}\n";
                    File.AppendAllText(cbdeLogFile, message);
                }
            }
        }

        private void PerformanceLog(string s)
        {
            lock (perfFileLock)
            {
                File.AppendAllText(cbdePerfLogFile, s);
            }
        }

        private static void Initialize()
        {
            extractedCbdeBinaryPath = Path.Combine(Path.GetTempPath(), $"CBDE_{Process.GetCurrentProcess().Id}");
            Directory.CreateDirectory(extractedCbdeBinaryPath);
            lock (logFileLock)
            {
                if (File.Exists(extractedCbdeBinaryPath))
                {
                    File.Delete(extractedCbdeBinaryPath);
                }
            }
            UnpackCbdeExe();
        }

        private static void UnpackCbdeExe()
        {
            var assembly = typeof(CbdeHandler).Assembly;
            const string res = "SonarAnalyzer.dotnet-symbolic-execution.exe";
            extractedCbdeBinaryPath = Path.Combine(extractedCbdeBinaryPath, "windows/dotnet-symbolic-execution.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(extractedCbdeBinaryPath));
            var stream = assembly.GetManifestResourceStream(res);
            var fileStream = File.Create(extractedCbdeBinaryPath);
            stream.Seek(0, SeekOrigin.Begin);
            stream.CopyTo(fileStream);
            fileStream.Close();
        }

        // In big projects, multiple source files can have the same name.
        // We need to convert all of them to mlir. Mangling the full pathname of each file would be too long.
        // We just give a number to each file haviong the same name.
        private string ManglePath(string path)
        {
            path = Path.GetFileNameWithoutExtension(path);
            fileNameDuplicateNumbering.TryGetValue(path, out var count);
            fileNameDuplicateNumbering[path] = ++count;
            path += "_" + count.ToString();
            return path;
        }

        private void InitializePathsAndLog(string assemblyName, int compilationHash)
        {
            SetupMlirRootDirectory();
            cbdeDirectoryAssembly = Path.Combine(cbdeDirectoryRoot, assemblyName, compilationHash.ToString());
            if (Directory.Exists(cbdeDirectoryAssembly))
            {
                Directory.Delete(cbdeDirectoryAssembly, true);
            }
            Directory.CreateDirectory(cbdeDirectoryAssembly);
            cbdeResultsPath = Path.Combine(cbdeDirectoryAssembly, CbdeOutputFileName);
            logStringBuilder = new StringBuilder();
            LogIfFailure($">> New Cbde Run triggered at {DateTime.Now.ToShortTimeString()}");
        }

        private void SetupMlirRootDirectory()
        {
            if((getOutputDirectory() != null) && (getOutputDirectory().Length != 0))
            {
                cbdePath = Path.Combine(getOutputDirectory(), "cbde");
                Directory.CreateDirectory(cbdePath);
            }
            else
            {
                // used only when doing the unit test
                cbdePath = Path.GetTempPath();
            }
            var cbdeProcessSpecificPath = Path.Combine(cbdePath, $"CBDE_{Process.GetCurrentProcess().Id}");
            Directory.CreateDirectory(cbdeProcessSpecificPath);
            cbdeLogFile = Path.Combine(cbdeProcessSpecificPath, "cbdeHandler.log");
            moreDetailsMessage = emitLog ? $", more details in {cbdeProcessSpecificPath}" : "";
            cbdeMetricsLogFile = Path.Combine(cbdeProcessSpecificPath, "metrics.log");
            cbdePerfLogFile = Path.Combine(cbdeProcessSpecificPath, "performances.log");
            cbdeDirectoryRoot = Path.Combine(cbdePath, "assemblies");
            Directory.CreateDirectory(cbdeDirectoryRoot);
        }

        private void ExportFunctionMlir(SyntaxTree tree, SemanticModel model, MlirExporterMetrics exporterMetrics, string mlirFileName)
        {
            using var mlirStreamWriter = new StreamWriter(Path.Combine(cbdeDirectoryAssembly, mlirFileName));
            var perfLog = new StringBuilder();
            perfLog.AppendLine(tree.GetRoot().GetLocation().GetLineSpan().Path);
            var mlirExporter = new MlirExporter(mlirStreamWriter, model, exporterMetrics, true);
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var cpuWatch = ThreadCpuStopWatch.StartNew();
                mlirExporter.ExportFunction(method);
                perfLog.AppendLine(method.Identifier + " " + watch.ElapsedMilliseconds);
                perfLog.AppendLine(method.Identifier + " " + cpuWatch.ElapsedMilliseconds);
            }
            perfLog.AppendLine();
            PerformanceLog(perfLog.ToString());
        }

        private void RunCbdeAndRaiseIssues(CompilationAnalysisContext c)
        {
            Log("Running CBDE");
            using (var cbdeProcess = new Process())
            {
                LogIfFailure("- Cbde process");
                cbdeProcess.StartInfo.FileName = cbdeExecutablePath;
                cbdeProcess.StartInfo.WorkingDirectory = cbdeDirectoryAssembly;
                var cbdeExePerfLogFile = Path.Combine(cbdeDirectoryAssembly, "perfLogFile.log");
                cbdeProcess.StartInfo.Arguments = $"-i \"{cbdeDirectoryAssembly}\" -o \"{cbdeResultsPath}\" -s \"{cbdeExePerfLogFile}\"";

                LogIfFailure($"  * binary_location: '{cbdeProcess.StartInfo.FileName}'");
                LogIfFailure($"  * arguments: '{cbdeProcess.StartInfo.Arguments}'");

                cbdeProcess.StartInfo.UseShellExecute = false;
                cbdeProcess.StartInfo.RedirectStandardError = true;
                long totalProcessorTime = 0;
                long peakPagedMemory = 0;
                long peakWorkingSet = 0;
                try
                {
                    cbdeProcess.Start();
                    while (!cbdeProcess.WaitForExit(ProcessStatPeriodMs))
                    {
                        try
                        {
                            cbdeProcess.Refresh();
                            totalProcessorTime = (long)cbdeProcess.TotalProcessorTime.TotalMilliseconds;
                            peakPagedMemory = cbdeProcess.PeakPagedMemorySize64;
                            peakWorkingSet = cbdeProcess.PeakWorkingSet64;
                        }
                        catch (InvalidOperationException)
                        {
                            // the process might have exited during the loop
                        }
                    }
                }
                catch (Exception e)
                {
                    Log("Running CBDE: Cannot start process");
                    ReportEndOfCbdeExecution();
                    throw new CbdeException($"Exception while running CBDE process: {e.Message}{moreDetailsMessage}");
                }

                var logString = $@" *exit code: {cbdeProcess.ExitCode}
  * cpu_time: {totalProcessorTime} ms
  * peak_paged_mem: {peakPagedMemory >> 20} MB
  * peak_working_set: {peakWorkingSet >> 20} MB";

                Log(logString);
                LogIfFailure(logString);

                if (cbdeProcess.ExitCode == 0)
                {
                    Log("Running CBDE: Success");
                    RaiseIssuesFromResultFile(c);
                    Cleanup();
                    Log("Running CBDE: Issues reported");
                }
                else
                {
                    Log("Running CBDE: Failure");
                    LogFailedCbdeRunAndThrow(cbdeProcess);
                }
            }
            ReportEndOfCbdeExecution();
        }

        private void ReportEndOfCbdeExecution() =>
            onCbdeExecution?.Invoke(cbdeLogFile);

        private void Cleanup() =>
            logStringBuilder.Clear();

        private void RaiseIssueFromXElement(XElement issue, CompilationAnalysisContext context)
        {
            var key = issue.Attribute("key").Value;
            var message = issue.Attribute("message").Value;
            var line = int.Parse(issue.Attribute("l").Value);
            var col = int.Parse(issue.Attribute("c").Value);
            var file = issue.Attribute("f").Value;

            var begin = new LinePosition(line, col);
            var end = new LinePosition(line, col + 1);
            var loc = Location.Create(file, TextSpan.FromBounds(0, 0), new LinePositionSpan(begin, end));

            raiseIssue(key, message, loc, context);
        }

        private void LogFailedCbdeRunAndThrow(Process pProcess)
        {
            var failureString = new StringBuilder("CBDE Failure Report :\n  C# souces files involved are:\n");
            foreach (var fileName in csSourceFileNames)
            {
                failureString.Append("  - " + fileName + "\n");
            }
            // we dispose the StreamWriter to unlock the log file
            LogIfFailure($"- parsing json file {cbdeResultsPath}");
            failureString.Append("  content of stderr is:\n" + pProcess.StandardError.ReadToEnd());
            failureString.Append("  content of the CBDE handler log file is :\n" + logStringBuilder);
            Log(failureString.ToString());
            ReportEndOfCbdeExecution();
            throw new CbdeException($"CBDE external process reported an error{moreDetailsMessage}");
        }

        private void RaiseIssuesFromResultFile(CompilationAnalysisContext context)
        {
            LogIfFailure($"- parsing file {cbdeResultsPath}");
            try
            {
                var document = XDocument.Load(cbdeResultsPath);
                foreach (var i in document.Descendants("Issue"))
                {
                    RaiseIssueFromXElement(i, context);
                }
            }
            catch(Exception exception)
            {
                if (exception is XmlException || exception is NullReferenceException)
                {
                    LogIfFailure($"- error parsing result file {cbdeResultsPath}: {exception}");
                    Log(logStringBuilder.ToString());
                    ReportEndOfCbdeExecution();
                    throw new CbdeException($"Error parsing output from CBDE: {exception.Message}{moreDetailsMessage}");
                }
                ReportEndOfCbdeExecution();
                throw;
            }
        }

        private class ThreadCpuStopWatch
        {
            private double totalMsStart;

            private readonly ProcessThread currentProcessThread;

            public void Reset() =>
                totalMsStart = currentProcessThread?.TotalProcessorTime.TotalMilliseconds ?? 0;

            public long ElapsedMilliseconds =>
                (long)((currentProcessThread?.TotalProcessorTime.TotalMilliseconds ?? -1) - totalMsStart);

            private static ProcessThread GetCurrentProcessThread()
            {
                // we need the physical thread id to get the cpu time.
                // contrary to what the deprecation warning says, in this case,
                // it cannot be replaced with the ManagedThreadId property on Thread
#pragma warning disable CS0618 // Type or member is obsolete
                var currentId = AppDomain.GetCurrentThreadId();
#pragma warning restore CS0618 // Type or member is obsolete
                              // this is not a generic collection, so there is no linq way of doing that
                foreach (ProcessThread p in Process.GetCurrentProcess().Threads)
                {
                    if (p.Id == currentId)
                    {
                        return p;
                    }
                }
                return null;
            }

            private ThreadCpuStopWatch() =>
                currentProcessThread = GetCurrentProcessThread();

            // We are copying the interface of the class StopWatch
            public static ThreadCpuStopWatch StartNew()
            {
                var instance = new ThreadCpuStopWatch();
                instance.Reset();
                return instance;
            }
        }
    }
}
