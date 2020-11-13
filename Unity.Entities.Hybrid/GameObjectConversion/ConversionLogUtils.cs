using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace GameObjectConversion
{
    internal enum LogType
    {
        Exception,
        Error,
        Warning,
        Assert,
        Log,
        Line
    }

    internal static class ConversionLogUtils
    {
        public static IEnumerable<(LogType Type, string Content)> ParseConversionLog(string conversionLogPath)
        {
            (LogType logType, string)[] allLogTypes =
                Enum.GetValues(typeof(LogType)).Cast<LogType>().Select(logType => (logType, $"{logType.ToString()}:")).ToArray();

            LogType GetLogType(string currentLine)
            {
                foreach (var (logType, logString) in allLogTypes)
                {
                    if (currentLine.StartsWith(logString))
                    {
                        return logType;
                    }
                }
                return LogType.Line;
            }

            var conversionLogLines = File.ReadLines(conversionLogPath);

            var currentLog = new StringBuilder();
            LogType currentLogType = LogType.Line;

            foreach (string line in conversionLogLines)
            {
                LogType logType = GetLogType(line);

                if (logType == LogType.Line)
                {
                    currentLog.AppendLine(line);
                }
                else
                {
                    if (currentLog.Length > 0)
                    {
                        yield return (currentLogType, currentLog.ToString());
                        currentLog.Clear();
                    }
                    currentLogType = logType;
                    currentLog.AppendLine(line);
                }
            }

            if (currentLog.Length > 0 && currentLogType != LogType.Line)
            {
                yield return (currentLogType, currentLog.ToString());
            }
        }

        public static (bool HasError, bool HasException) PrintConversionLogToUnityConsole(string conversionLogPath)
        {
            bool hasException = false;
            bool hasError = false;
            foreach (var (type, content) in ParseConversionLog(conversionLogPath))
            {
                switch (type)
                {
                    case LogType.Log:
                        Debug.Log(content);
                        break;
                    case LogType.Warning:
                        Debug.LogWarning(content);
                        break;
                    case LogType.Error:
                        Debug.LogError(content);
                        hasError = true;
                        break;
                    case LogType.Exception:
                        Debug.LogError(content);
                        hasException = true;
                        break;
                }
            }
            return (hasError, hasException);
        }
    }
}
