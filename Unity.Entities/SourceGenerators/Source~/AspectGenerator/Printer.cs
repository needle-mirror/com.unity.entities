using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.CompilerServices;

namespace Unity.Entities.SourceGen.Aspect
{
    public ref struct Printer
    {
        public StringBuilder Builder;
        public string CurrentIndent;

        public void Clear()
        {
            Builder.Clear();
            CurrentIndent = "";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PrintLine(string text)
        {
            Builder.Append(CurrentIndent);
            Builder.AppendLine(text);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PrintLines(IEnumerable<string> texts)
        {
            foreach (var text in texts)
            {
                Builder.Append(CurrentIndent);
                Builder.AppendLine(text);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PrintBeginLine(string text)
        {
            Builder.Append(CurrentIndent);
            Builder.Append(text);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Print(string text)
        {
            Builder.Append(CurrentIndent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PrintEndLine(string text)
        {
            Builder.AppendLine(text);
        }

        public string Result => Builder.ToString();
    }

    public static class PrinterExt
    {
        public static ref Printer WithIndent(this ref Printer self, string indent)
        {
            self.CurrentIndent = indent;
            return ref self;
        }
        public static ref Printer WithIndentCleared(this ref Printer self, string indent)
        {
            self.Builder.Clear();
            self.CurrentIndent = indent;
            return ref self;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string PrintLinesToString(this ref Printer self, IEnumerable<string> texts)
        {
            self.PrintLines(texts);
            return self.Result;
        }
    }
}
