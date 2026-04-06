namespace BGCS.Cpp2C
{
    using BGCS.Core.Logging;

    /// <summary>
    /// Defines the public class <c>BaseGenerator</c> used by the generation pipeline.
    /// </summary>
    public class BaseGenerator
    {
        protected readonly Cpp2CGeneratorConfig config;
        private readonly List<LogMessage> messages = new();

        /// <summary>
        /// Initializes a new instance of <see cref="BaseGenerator"/>.
        /// </summary>
        public BaseGenerator(Cpp2CGeneratorConfig settings)
        {
            this.config = settings;
        }

        /// <summary>
        /// Exposes public member <c>messages</c>.
        /// </summary>
        public IReadOnlyList<LogMessage> Messages => messages;

        /// <summary>
        /// Performs the operation implemented by <c>Log</c>.
        /// </summary>
        public void Log(LogSeverity severtiy, string message)
        {
            if (severtiy < config.LogLevel) return;
            LogMessage msg = new(severtiy, message);
            messages.Add(msg);
            WriteMessage(msg);
        }

        /// <summary>
        /// Performs the operation implemented by <c>LogTrace</c>.
        /// </summary>
        public void LogTrace(string message)
        {
            Log(LogSeverity.Trace, message);
        }

        /// <summary>
        /// Performs the operation implemented by <c>LogDebug</c>.
        /// </summary>
        public void LogDebug(string message)
        {
            Log(LogSeverity.Debug, message);
        }

        /// <summary>
        /// Performs the operation implemented by <c>LogInfo</c>.
        /// </summary>
        public void LogInfo(string message)
        {
            Log(LogSeverity.Information, message);
        }

        /// <summary>
        /// Performs the operation implemented by <c>LogWarn</c>.
        /// </summary>
        public void LogWarn(string message)
        {
            Log(LogSeverity.Warning, message);
        }

        /// <summary>
        /// Performs the operation implemented by <c>LogError</c>.
        /// </summary>
        public void LogError(string message)
        {
            Log(LogSeverity.Error, message);
        }

        /// <summary>
        /// Performs the operation implemented by <c>LogCritical</c>.
        /// </summary>
        public void LogCritical(string message)
        {
            Log(LogSeverity.Critical, message);
        }

        /// <summary>
        /// Performs the operation implemented by <c>DisplayMessages</c>.
        /// </summary>
        public void DisplayMessages()
        {
            int warns = 0;
            int errors = 0;
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                WriteMessage(msg);
                if (msg.Severtiy == LogSeverity.Critical || msg.Severtiy == LogSeverity.Error) ++errors;
                else if (msg.Severtiy == LogSeverity.Warning) ++warns;
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

        private void WriteMessage(in LogMessage msg)
        {
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
                    break;

                case LogSeverity.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;

                case LogSeverity.Critical:
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    break;
            }
            Console.WriteLine(msg.Message);
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}
