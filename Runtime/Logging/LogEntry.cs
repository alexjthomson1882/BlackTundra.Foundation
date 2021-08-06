using System;

using BlackTundra.Foundation.Utility;

namespace BlackTundra.Foundation.Logging {

    /// <summary>
    /// Describes a single log entry.
    /// </summary>
    public sealed class LogEntry {

        #region constant

        private static readonly string FormattedRichTextF1 = string.Concat("<color=#", ConsoleColour.Gray.hex, ">[</color><color=#");
        private static readonly string FormattedRichTextF2 = ">";
        private static readonly string FormattedRichTextF3 = string.Concat("</color><color=#", ConsoleColour.Gray.hex, ">]</color> ");

        /// <summary>
        /// A completely empty log entry.
        /// </summary>
        public static readonly LogEntry Empty = new LogEntry(LogLevel.None, DateTime.MinValue, string.Empty);

        #endregion

        #region variable

        /// <summary>
        /// <see cref="LogLevel"/> of the <see cref="LogEntry"/>.
        /// </summary>
        public readonly LogLevel logLevel;

        /// <summary>
        /// <see cref="DateTime"/> that the <see cref="LogEntry"/> was created.
        /// </summary>
        public readonly DateTime timestamp;

        /// <summary>
        /// Content of the <see cref="LogEntry"/>.
        /// </summary>
        public readonly string content;

        #endregion

        #region property

        /// <summary>
        /// Formatted text that describes the <see cref="LogEntry"/> used in logs.
        /// </summary>
        public string FormattedLogEntry => logLevel == LogLevel.None
            ? string.Concat(content, Environment.NewLine)
            : string.Concat(timestamp.ToFormattedString(), " [", logLevel.logName, "] ", content, Environment.NewLine);

        /// <summary>
        /// Formatted text that describes the <see cref="LogEntry"/> used in rich text GUI.
        /// </summary>
        public string FormattedRichTextEntry => logLevel == LogLevel.None
            ? content
            : string.Concat(
                FormattedRichTextF1, logLevel.colour.hex,
                FormattedRichTextF2, logLevel.distinctName,
                FormattedRichTextF3, ConsoleUtility.Escape(content)
            );

        /// <summary>
        /// Constructs a formatted timestamp.
        /// </summary>
        public string FormattedTimestamp => timestamp.ToFormattedString();

        /// <summary>
        /// Returns <c>true</c> if the <see cref="LogEntry"/> is empty.
        /// </summary>
        public bool IsEmpty => content.Length == 0;

        #endregion

        #region constructor

        private LogEntry() => throw new NotSupportedException();

        internal LogEntry(in string content) {
            this.content = content ?? throw new ArgumentNullException("content");
            logLevel = LogLevel.None;
            timestamp = DateTime.UtcNow;
        }

        internal LogEntry(in LogLevel logLevel, in string content) {
            this.logLevel = logLevel ?? throw new ArgumentNullException("logLevel");
            this.content = content ?? throw new ArgumentNullException("content");
            timestamp = DateTime.UtcNow;
        }

        internal LogEntry(in LogLevel logLevel, in DateTime timestamp, in string content) {
            this.logLevel = logLevel ?? throw new ArgumentNullException("logLevel");
            this.content = content ?? throw new ArgumentNullException("content");
            this.timestamp = timestamp;
        }

        #endregion

    }

}