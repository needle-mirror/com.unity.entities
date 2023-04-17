using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

using static Unity.Entities.SourceGen.Common.SourceGenHelpers;

namespace Unity.Entities.SourceGen.Common
{
    // Only throw this exception if the parsing cannot continue, in most cases we should try to continue and collect all valid errors
    public class InvalidDescriptionException : Exception { }

    /// <summary>
    /// Logger interface of anything related to diagnostics of incoming code errors and warnings.
    /// </summary>
    public interface IDiagnosticLogger : IDisposable
    {
        void LogError(string errorCode, string title, string errorMessage, Location location, string description = "");
        void LogWarning(string errorCode, string title, string errorMessage, Location location, string description = "");
        void LogInfo(string errorCode, string title, string errorMessage, Location location, string description = "");
    }

    /// <summary>
    /// Implementation of IDiagnosticLogger that reports all previously logged
    /// diagnostics to a GeneratorExecutionContext instance upon IDisposable.Dispose() being called
    /// </summary>
    public class DiagnosticLogger : IDiagnosticLogger
    {
        public GeneratorExecutionContext ExecutionContext;
        public List<Diagnostic> Diagnostics = new List<Diagnostic>();
        public DiagnosticLogger(GeneratorExecutionContext executionContext)
        {
            ExecutionContext = executionContext;
        }
        public void LogError(string errorCode, string title, string errorMessage, Location location, string description = "")
        {
            if (errorCode.Contains("ICE"))
                errorMessage = CompilerError.WithMessage(errorMessage);

            Log(DiagnosticSeverity.Error, errorCode, title, errorMessage, location, description);
        }

        public void LogWarning(string errorCode, string title, string errorMessage, Location location, string description = "")
            => Log(DiagnosticSeverity.Warning, errorCode, title, errorMessage, location, description);

        public void LogInfo(string errorCode, string title, string errorMessage, Location location, string description = "")
            => Log(DiagnosticSeverity.Info, errorCode, title, errorMessage, location, description);

        void Log(DiagnosticSeverity diagnosticSeverity, string errorCode, string title, string errorMessage, Location location, string description = "")
        {
            if (Service<IDiagnosticFrame>.Available)
            {
                var sbMessage = new StringBuilder();
                sbMessage.Append(errorMessage);
                sbMessage.Append(" (");

                var sbDescription = new StringBuilder();
                if (description != "")
                {
                    sbDescription.AppendLine(description);
                }

                foreach (var frame in Service<IDiagnosticFrame>.Registry.LIFO)
                {
                    sbMessage.Append(frame.GetBrief());
                    sbDescription.Append(frame.GetMessage());
                }
                sbMessage.Append(")");
                errorMessage = sbMessage.ToString();
                description = sbDescription.ToString();
            }
            SourceOutputHelpers.LogInfoToSourceGenLog($"{diagnosticSeverity}: {errorCode}, {title}, {errorMessage}");
            var rule = new DiagnosticDescriptor(errorCode, title, errorMessage, "Source Generator", diagnosticSeverity, true, description);
            Diagnostics.Add(Diagnostic.Create(rule, location));
        }

        public void Dispose()
        {
            foreach (var diagnostic in Diagnostics)
                ExecutionContext.ReportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// Provide a diagnostic message.
    /// Used with Service<IDiagnosticFrame>, it provides a diagnostic message stack
    /// </summary>
    public interface IDiagnosticFrame
    {
        string GetBrief();
        string GetMessage();
    }
}
