namespace BGCS.Core.Logging
{
    /// <summary>
    /// Defines the public struct <c>LogMessage</c>.
    /// </summary>
    public struct LogMessage
    {
        /// <summary>
        /// Exposes public member <c>Severtiy</c>.
        /// </summary>
        public LogSeverity Severtiy;
        /// <summary>
        /// Exposes public member <c>Message</c>.
        /// </summary>
        public string Message;

        /// <summary>
        /// Initializes a new instance of <see cref="LogMessage"/>.
        /// </summary>
        public LogMessage(LogSeverity severtiy, string message)
        {
            Severtiy = severtiy;
            Message = message;
        }

        private static string GetServertiyString(LogSeverity severtiy)
        {
            return severtiy switch
            {
                LogSeverity.Trace => "[Trc]",
                LogSeverity.Debug => "[Dbg]",
                LogSeverity.Information => "[Inf]",
                LogSeverity.Warning => "[Wrn]",
                LogSeverity.Error => "[Err]",
                LogSeverity.Critical => "[Crt]",
                _ => throw new NotImplementedException(),
            };
        }

        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override readonly string ToString()
        {
            return $"{GetServertiyString(Severtiy)}\t{Message}";
        }
    }
}
