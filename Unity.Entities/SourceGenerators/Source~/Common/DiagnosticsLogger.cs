using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGenerator.Common
{
    public static class DiagnosticsLogger
    {
        public static void LogError(this ISourceGeneratorDiagnosable diagnosable, string errorCode, string title, string errorMessage, Location location, string description = "")
        {
            if (errorCode.Contains("ICE"))
                errorMessage = SourceGenHelpers.CompilerError.WithMessage(errorMessage);

            Log(diagnosable, DiagnosticSeverity.Error, errorCode, title, errorMessage, location, description);
        }

        public static void LogWarning(this ISourceGeneratorDiagnosable diagnosable, string errorCode, string title, string errorMessage, Location location, string description = "")
            => Log(diagnosable, DiagnosticSeverity.Warning, errorCode, title, errorMessage, location, description);

        public static void LogInfo(this ISourceGeneratorDiagnosable diagnosable, string errorCode, string title, string errorMessage, Location location, string description = "")
            => Log(diagnosable, DiagnosticSeverity.Info, errorCode, title, errorMessage, location, description);

        static void Log(this ISourceGeneratorDiagnosable diagnosable, DiagnosticSeverity diagnosticSeverity, string errorCode, string title, string errorMessage, Location location, string description = "")
        {
            if (location.IsInMetadata)
                throw new InvalidOperationException(
                    "Errors thrown by source generators must point to locations in source code, NOT locations in metadata. " +
                    $"Please ensure that the `{errorCode}` error points to a valid location in source code.");

            SourceOutputHelpers.LogInfoToSourceGenLog($"{diagnosticSeverity}: {errorCode}, {title}, {errorMessage}");
            var rule = new DiagnosticDescriptor(errorCode, title, errorMessage, "Source Generator", diagnosticSeverity, true, description);
            diagnosable.Diagnostics?.Add(Diagnostic.Create(rule, location));
        }
    }

    public interface ISourceGeneratorDiagnosable
    {
        public List<Diagnostic> Diagnostics { get; }
    }
}
