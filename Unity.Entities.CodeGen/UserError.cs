using System;
#if !UNITY_DOTSRUNTIME
using System.IO;
#endif
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace Unity.Entities.CodeGen
{
    static class InternalCompilerError
    {
        public static DiagnosticMessage DCICE300(TypeReference producerReference, TypeReference jobStructType, Exception ex)
        {
            return UserError.MakeError(nameof(DCICE300), $"Unexpected error while generating automatic registration for job provider {producerReference.FullName} via job struct {jobStructType.FullName}. Please report this error.\nException: {ex.Message}", method: null, instruction: null);
        }
    }

    static class UserError
    {
        // !!! Needs to be non Entities.ForEach specific
        public static DiagnosticMessage DC0010(MethodDefinition method, Instruction instruction)
        {
            return MakeError(nameof(DC0010), $"The Entities.ForEach statement contains dynamic code that cannot be statically analyzed.", method, instruction);
        }

        public static DiagnosticMessage DC3001(TypeReference type)
        {
            return MakeError(nameof(DC3001), $"{type.FullName}: [RegisterGenericJobType] requires an instance of a generic value type", method: null, instruction: null);
        }

        static DiagnosticMessage MakeInternal(DiagnosticType type, string errorCode, string messageData, MethodDefinition method, Instruction instruction)
        {
            var result = new DiagnosticMessage {Column = 0, Line = 0, DiagnosticType = type, File = ""};

            var seq = instruction != null ? CecilHelpers.FindBestSequencePointFor(method, instruction) : null;

            if (errorCode.Contains("ICE"))
            {
                messageData = messageData + " Seeing this error indicates a bug in the dots compiler. We'd appreciate a bug report (About->Report a Bug...). Thnx! <3";
            }

            var errorType = type == DiagnosticType.Error ? "error" : "warning";
            messageData = $"{errorType} {errorCode}: {messageData}";
            if (seq != null)
            {
                result.File = seq.Document.Url;
                result.Column = seq.StartColumn;
                result.Line = seq.StartLine;
#if !UNITY_DOTSRUNTIME
                var shortenedFilePath = seq.Document.Url.Replace($"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}", "");
                result.MessageData = $"{shortenedFilePath}({seq.StartLine},{seq.StartColumn}): {messageData}";
#else
                result.MessageData = messageData;
#endif
            }
            else
            {
                result.MessageData = messageData;
            }

            return result;
        }

        public static DiagnosticMessage MakeError(string errorCode, string messageData, MethodDefinition method, Instruction instruction)
        {
            return MakeInternal(DiagnosticType.Error, errorCode, messageData, method, instruction);
        }

        public static DiagnosticMessage MakeWarning(string errorCode, string messageData, MethodDefinition method, Instruction instruction)
        {
            return MakeInternal(DiagnosticType.Warning, errorCode, messageData, method, instruction);
        }

        public static void Throw(this DiagnosticMessage dm)
        {
            if (dm.DiagnosticType != DiagnosticType.Error)
                throw new InvalidOperationException("We should never throw exceptions for non-error Entities.ForEach diagnostic messages.");
            throw new FoundErrorInUserCodeException(new[] { dm });
        }
    }
}
