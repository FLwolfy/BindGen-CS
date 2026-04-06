namespace BGCS.Core
{
    using BGCS.Core.Logging;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Declares the callback signature <c>LogEventHandler</c>.
    /// </summary>
    public delegate void LogEventHandler(LogSeverity severity, string message);

    /// <summary>
    /// Defines the public class <c>LoggerBase</c>.
    /// </summary>
    public class LoggerBase
    {
        private readonly List<LogMessage> messages = new();

        /// <summary>
        /// Exposes public member <c>messages</c>.
        /// </summary>
        public IReadOnlyList<LogMessage> Messages => messages;

        /// <summary>
        /// Raises notifications for this component.
        /// </summary>
        public event LogEventHandler? LogEvent;

        /// <summary>
        /// Gets or sets <c>LogLevel</c>.
        /// </summary>
        public LogSeverity LogLevel { get; set; } = LogSeverity.Information;

        /// <summary>
        /// Executes public operation <c>DisplayMessages</c>.
        /// </summary>
        public void DisplayMessages()
        {
            int warns = 0;
            int errors = 0;
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                switch (msg.Severtiy)
                {
                    case LogSeverity.Trace:
                        Console.ForegroundColor = ConsoleColor.Gray;
                        break;

                    case LogSeverity.Debug:
                        Console.ForegroundColor = ConsoleColor.Gray;
                        break;

                    case LogSeverity.Information:
                        Console.ForegroundColor = ConsoleColor.White;
                        break;

                    case LogSeverity.Warning:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        warns++;
                        break;

                    case LogSeverity.Error:
                        Console.ForegroundColor = ConsoleColor.Red;
                        errors++;
                        break;

                    case LogSeverity.Critical:
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        errors++;
                        break;
                }
                Console.WriteLine(messages[i]);
                Console.ForegroundColor = ConsoleColor.White;
            }

            Console.WriteLine();
            Console.Write($"summary: ");
            if (warns > 0)
                Console.ForegroundColor = ConsoleColor.Yellow;
            else
                Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"warnings: {warns}, ");
            if (errors > 0)
                Console.ForegroundColor = ConsoleColor.Red;
            else
                Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"errors: {errors}\n");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();
            messages.Clear();
        }

        /// <summary>
        /// Executes public operation <c>Log</c>.
        /// </summary>
        public void Log(LogSeverity severtiy, string message)
        {
            if (severtiy < LogLevel) return;
            messages.Add(new LogMessage(severtiy, message));
            LogEvent?.Invoke(severtiy, message);
        }

        /// <summary>
        /// Executes public operation <c>LogCritical</c>.
        /// </summary>
        public void LogCritical(string message)
        {
            Log(LogSeverity.Critical, message);
        }

        /// <summary>
        /// Executes public operation <c>LogDebug</c>.
        /// </summary>
        public void LogDebug(string message)
        {
            Log(LogSeverity.Debug, message);
        }

        /// <summary>
        /// Executes public operation <c>LogError</c>.
        /// </summary>
        public void LogError(string message)
        {
            Log(LogSeverity.Error, message);
        }

        /// <summary>
        /// Executes public operation <c>LogInfo</c>.
        /// </summary>
        public void LogInfo(string message)
        {
            Log(LogSeverity.Information, message);
        }

        /// <summary>
        /// Executes public operation <c>LogTrace</c>.
        /// </summary>
        public void LogTrace(string message)
        {
            Log(LogSeverity.Trace, message);
        }

        /// <summary>
        /// Executes public operation <c>LogWarn</c>.
        /// </summary>
        public void LogWarn(string message)
        {
            Log(LogSeverity.Warning, message);
        }

        /// <summary>
        /// Executes public operation <c>LogToConsole</c>.
        /// </summary>
        public void LogToConsole()
        {
            LogEvent += GeneratorLogEvent;
        }

        private static void GeneratorLogEvent(LogSeverity severity, string message)
        {
            switch (severity)
            {
                case LogSeverity.Trace:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    break;

                case LogSeverity.Debug:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    break;

                case LogSeverity.Information:
                    Console.ForegroundColor = ConsoleColor.White;
                    break;

                case LogSeverity.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;

                case LogSeverity.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;

                case LogSeverity.Critical:
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    break;
            }

            string type = severity switch
            {
                LogSeverity.Trace => "[Trc] ",
                LogSeverity.Debug => "[Dbg] ",
                LogSeverity.Information => "[Inf] ",
                LogSeverity.Warning => "[Wrn] ",
                LogSeverity.Error => "[Err] ",
                LogSeverity.Critical => "[Crt] ",
                _ => "[Unk] ",
            };

            Console.Write(type);
            Console.WriteLine(message);
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}
