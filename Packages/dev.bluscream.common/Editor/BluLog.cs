using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Bluscream
{
    public enum BluLogLevel
    {
        /// <summary>Nothing at all, including errors. Only for benchmarking.</summary>
        Off = 0,
        Error = 1,
        Warning = 2,
        /// <summary>Step boundaries and per-operation results. The default.</summary>
        Info = 3,
        /// <summary>Per-object decisions: what was changed, skipped, kept, and why.</summary>
        Verbose = 4,
        /// <summary>Per-element detail. Very noisy; for diagnosing one specific failing case.</summary>
        Trace = 5
    }

    /// <summary>
    /// Tagged, verbosity-gated logger shared by every Bluscream package.
    ///
    /// Three things it does that plain <see cref="Debug"/> does not:
    ///
    /// <list type="bullet">
    /// <item><b>Progress doubles as logging.</b> <see cref="Step"/> writes the message to the log and pushes
    /// the same text to whatever progress UI the caller attached, so a long operation reports itself once
    /// instead of maintaining two parallel sets of strings that drift apart.</item>
    /// <item><b>Per-logger files.</b> <see cref="StartFile"/> tees a logger's output to its own file, so a
    /// run can be archived or parsed later without sifting it out of Unity's Editor.log.</item>
    /// <item><b>Stack-trace suppression.</b> Unity attaches a full managed stack trace to every
    /// <c>Debug.Log</c>, which inflates a few hundred messages into tens of thousands of lines.
    /// <see cref="SuppressStackTraces"/> turns that off for the duration of an operation.</item>
    /// </list>
    ///
    /// Usage is one static field per class:
    /// <code>
    /// private static readonly BluLog Log = BluLog.Get("MyOptimizer");
    /// Log.Info("Did the thing.");
    /// Log.Step("Processing X...", 0.4f);
    /// </code>
    /// </summary>
    public sealed class BluLog
    {
        private const string GlobalLevelPref = "Bluscream_BluLog_Level";

        private static readonly Dictionary<string, BluLog> Instances = new Dictionary<string, BluLog>(StringComparer.Ordinal);

        /// <summary>Verbosity applied to every logger that has no explicit override.</summary>
        public static BluLogLevel GlobalLevel
        {
            get => (BluLogLevel)EditorPrefs.GetInt(GlobalLevelPref, (int)BluLogLevel.Info);
            set => EditorPrefs.SetInt(GlobalLevelPref, (int)value);
        }

        /// <summary>The tag prefixed to every message, e.g. <c>[MyOptimizer] ...</c>.</summary>
        public string Tag { get; }

        /// <summary>Per-logger verbosity. Null defers to <see cref="GlobalLevel"/>.</summary>
        public BluLogLevel? LevelOverride { get; set; }

        public BluLogLevel EffectiveLevel => LevelOverride ?? GlobalLevel;

        /// <summary>
        /// Where <see cref="Step"/> sends progress. Signature is (message, 0..1 progress); a null progress
        /// means "message only, leave the bar where it is".
        /// </summary>
        public Action<string, float?> ProgressHandler { get; set; }

        private StreamWriter _file;
        private string _filePath;

        private BluLog(string tag)
        {
            Tag = string.IsNullOrEmpty(tag) ? "Bluscream" : tag;
        }

        /// <summary>Returns the logger for a tag, creating it on first use. Cached, so this is cheap.</summary>
        public static BluLog Get(string tag)
        {
            if (string.IsNullOrEmpty(tag)) tag = "Bluscream";

            lock (Instances)
            {
                if (!Instances.TryGetValue(tag, out BluLog log))
                    Instances[tag] = log = new BluLog(tag);
                return log;
            }
        }

        /// <summary>All loggers created so far, for settings UIs that want to list them.</summary>
        public static IEnumerable<BluLog> All
        {
            get { lock (Instances) return new List<BluLog>(Instances.Values); }
        }

        public bool IsEnabled(BluLogLevel level) => EffectiveLevel >= level;
        public bool IsVerbose => IsEnabled(BluLogLevel.Verbose);
        public bool IsTrace => IsEnabled(BluLogLevel.Trace);

        // ---- Writing ------------------------------------------------------------------------------

        public void Info(string message, UnityEngine.Object context = null) => Write(BluLogLevel.Info, message, context);
        public void Verbose(string message, UnityEngine.Object context = null) => Write(BluLogLevel.Verbose, message, context);
        public void Warn(string message, UnityEngine.Object context = null) => Write(BluLogLevel.Warning, message, context);
        public void Error(string message, UnityEngine.Object context = null) => Write(BluLogLevel.Error, message, context);

        /// <summary>
        /// Lazily-built message — the delegate only runs when the level is enabled, so expensive per-element
        /// strings cost nothing when Verbose/Trace are off.
        /// </summary>
        public void Verbose(Func<string> message, UnityEngine.Object context = null)
        {
            if (IsEnabled(BluLogLevel.Verbose) && message != null) Write(BluLogLevel.Verbose, message(), context);
        }

        public void Trace(Func<string> message, UnityEngine.Object context = null)
        {
            if (IsEnabled(BluLogLevel.Trace) && message != null) Write(BluLogLevel.Trace, message(), context);
        }

        public void Trace(string message, UnityEngine.Object context = null) => Write(BluLogLevel.Trace, message, context);

        /// <summary>
        /// Logs at Info and reports the same text as progress, so the user-facing status line and the log
        /// never disagree about what is happening.
        /// </summary>
        /// <param name="progress">0..1, or null to update the text without moving the bar.</param>
        public void Step(string message, float? progress = null, UnityEngine.Object context = null)
        {
            Write(BluLogLevel.Info, message, context);

            try
            {
                ProgressHandler?.Invoke(message, progress);
            }
            catch (Exception e)
            {
                // A broken progress UI must never take down the operation being reported.
                Debug.LogWarning($"[{Tag}] Progress handler threw: {e.Message}");
            }
        }

        private void Write(BluLogLevel level, string message, UnityEngine.Object context)
        {
            if (!IsEnabled(level)) return;

            string line = $"[{Tag}] {message}";

            switch (level)
            {
                case BluLogLevel.Error:
                    if (context != null) Debug.LogError(line, context); else Debug.LogError(line);
                    break;
                case BluLogLevel.Warning:
                    if (context != null) Debug.LogWarning(line, context); else Debug.LogWarning(line);
                    break;
                default:
                    if (context != null) Debug.Log(line, context); else Debug.Log(line);
                    break;
            }

            WriteToFile(level, message);
        }

        // ---- Per-logger file ----------------------------------------------------------------------

        /// <summary>
        /// Tees this logger's output to its own file. Directories are created as needed. Calling it again
        /// closes the previous file first.
        /// </summary>
        /// <param name="append">False (default) truncates, so each run starts clean.</param>
        public void StartFile(string path, bool append = false)
        {
            StopFile();

            if (string.IsNullOrEmpty(path)) return;

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                _file = new StreamWriter(path, append, Encoding.UTF8) { AutoFlush = true };
                _filePath = path;

                _file.WriteLine($"# {Tag} log opened {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }
            catch (Exception e)
            {
                _file = null;
                _filePath = null;
                Debug.LogWarning($"[{Tag}] Could not open log file '{path}': {e.Message}");
            }
        }

        /// <summary>Closes this logger's file, if one is open. Safe to call unconditionally.</summary>
        public void StopFile()
        {
            if (_file == null) return;

            try
            {
                _file.WriteLine($"# {Tag} log closed {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _file.Flush();
                _file.Dispose();
            }
            catch { /* closing a log must never throw into the caller */ }

            _file = null;
            _filePath = null;
        }

        public string FilePath => _filePath;

        /// <summary>
        /// Opens files for every logger under one directory, named by tag. Convenient for archiving a whole
        /// run rather than one subsystem.
        /// </summary>
        public static void StartFilesForAll(string directory, bool append = false)
        {
            if (string.IsNullOrEmpty(directory)) return;

            foreach (BluLog log in All)
            {
                string safeTag = string.Join("_", log.Tag.Split(Path.GetInvalidFileNameChars()));
                log.StartFile(Path.Combine(directory, safeTag + ".log"), append);
            }
        }

        public static void StopFilesForAll()
        {
            foreach (BluLog log in All) log.StopFile();
        }

        private void WriteToFile(BluLogLevel level, string message)
        {
            if (_file == null) return;

            try
            {
                // Timestamped and level-prefixed so the file can be parsed later, unlike the console form.
                _file.WriteLine($"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} [{level}] {message}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{Tag}] Log file write failed, closing it: {e.Message}");
                StopFile();
            }
        }

        // ---- Stack trace suppression --------------------------------------------------------------

        private static StackTraceLogType _savedLog, _savedWarning;
        private static int _suppressDepth;

        /// <summary>
        /// Turns off Unity's stack traces for Log and Warning until the returned handle is disposed. Errors
        /// keep theirs, since those are the ones worth tracing. Nesting is reference-counted, so an inner
        /// scope cannot restore traces an outer scope still wants suppressed.
        /// </summary>
        public static IDisposable SuppressStackTraces() => new StackTraceScope();

        private sealed class StackTraceScope : IDisposable
        {
            private bool _disposed;

            public StackTraceScope()
            {
                if (_suppressDepth++ != 0) return;

                _savedLog = Application.GetStackTraceLogType(LogType.Log);
                _savedWarning = Application.GetStackTraceLogType(LogType.Warning);

                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                if (--_suppressDepth != 0) return;

                Application.SetStackTraceLogType(LogType.Log, _savedLog);
                Application.SetStackTraceLogType(LogType.Warning, _savedWarning);
            }
        }
    }
}
