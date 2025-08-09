namespace Helios.Core.Interfaces
{
    public interface ILogger
    {
        bool IsTraceEnabled { get; }
        bool IsDebugEnabled { get; }
        bool IsInfoEnabled { get; }
        bool IsWarningEnabled { get; }
        bool IsErrorEnabled { get; }
        bool IsCriticalEnabled { get; }

        void Trace(string message, params object[] data);
        void Debug(string message, params object[] data);
        void Info(string message, params object[] data);
        void Warning(string message, params object[] data);
        void Error(string message, params object[] data);
        void Critical(string message, params object[] data);

        void Trace(System.Exception ex, string message, params object[] data);
        void Debug(System.Exception ex, string message, params object[] data);
        void Info(System.Exception ex, string message, params object[] data);
        void Warning(System.Exception ex, string message, params object[] data);
        void Error(System.Exception ex, string message, params object[] data);
        void Critical(System.Exception ex, string message, params object[] data);
    }
}