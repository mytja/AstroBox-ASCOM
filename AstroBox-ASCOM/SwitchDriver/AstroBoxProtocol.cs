using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ASCOM.Utilities;

namespace ASCOM.mytjaAstroBox.Switch
{
    /// <summary>
    /// One response frame from the AstroBox Wire Protocol.
    /// </summary>
    internal sealed class ProtocolResponse
    {
        public uint Id { get; set; }
        public bool Ok { get; set; }
        public string Status { get; set; }
        public Dictionary<string, double> Values { get; } =
            new Dictionary<string, double>(StringComparer.Ordinal);
        public Dictionary<string, string> Strings { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
        public List<string> RawTokens { get; } = new List<string>();

        public double GetValue(string key, double fallback = 0.0)
        {
            double value;
            return Values.TryGetValue(key, out value) ? value : fallback;
        }

        public string GetString(string key, string fallback = "")
        {
            string value;
            return Strings.TryGetValue(key, out value) ? value : fallback;
        }
    }

    internal sealed class TelemetryEventArgs : EventArgs
    {
        public TelemetryEventArgs(long sequence, long uptimeMs,
                                  IReadOnlyDictionary<string, double> values)
        {
            Sequence = sequence;
            UptimeMs = uptimeMs;
            Values = values;
        }

        public long Sequence { get; private set; }
        public long UptimeMs { get; private set; }
        public IReadOnlyDictionary<string, double> Values { get; private set; }
    }

    /// <summary>
    /// One warning/error/info entry pushed by the device, or replayed from its
    /// on-device history ring via the LOG command.
    /// </summary>
    internal sealed class DeviceLogEntry
    {
        public DeviceLogEntry(long bootId, long sequence, string severity,
                              long uptimeMs, string source, string message)
        {
            BootId = bootId;
            Sequence = sequence;
            Severity = severity ?? "INFO";
            UptimeMs = uptimeMs;
            Source = source ?? "SYSTEM";
            Message = message ?? string.Empty;
            Timestamp = DateTime.Now;
        }

        public long BootId { get; private set; }
        public long Sequence { get; private set; }
        public string Severity { get; private set; }
        public long UptimeMs { get; private set; }
        public string Source { get; private set; }
        public string Message { get; private set; }
        public DateTime Timestamp { get; private set; }

        public string Key
        {
            get
            {
                return BootId.ToString(CultureInfo.InvariantCulture) + ":" +
                       Sequence.ToString(CultureInfo.InvariantCulture);
            }
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                                 "[{0:yyyy-MM-dd HH:mm:ss}] {1,-5} {2}: {3}",
                                 Timestamp, Severity, Source, Message);
        }
    }

    /// <summary>
    /// Asynchronous host-side implementation of the AstroBox Wire Protocol.
    ///
    /// A single background task owns the receive side of the serial port and
    /// dispatches:
    ///   T;...  pushed telemetry          -> TelemetryReceived event
    ///   R;...  responses to our requests -> waiting TaskCompletionSource
    ///   H;...  hello / protocol version
    ///   E;...  warning/error/info        -> DeviceLogReceived event
    ///
    /// Command callers never read from the port themselves, so unsolicited
    /// telemetry can never be mistaken for a command response.
    /// </summary>
    internal sealed class AstroBoxProtocol : IDisposable
    {
        private readonly Serial serial;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<uint, TaskCompletionSource<ProtocolResponse>> pending =
            new ConcurrentDictionary<uint, TaskCompletionSource<ProtocolResponse>>();

        private Task readerTask;
        private int nextRequestId = 1000;
        private volatile bool running;

        public AstroBoxProtocol(Serial serial)
        {
            if (serial == null)
                throw new ArgumentNullException("serial");
            this.serial = serial;
        }

        public event EventHandler<TelemetryEventArgs> TelemetryReceived;
        public event EventHandler<DeviceLogEntry> DeviceLogReceived;
        public event EventHandler<string> ProtocolMessage;

        public bool IsRunning
        {
            get { return running; }
        }

        public int ProtocolVersion { get; private set; }
        public string FirmwareInfo { get; private set; }
        public DateTime LastTelemetryUtc { get; private set; }

        public void Start()
        {
            if (running)
                return;

            running = true;
            // Dedicated thread: ReceiveTerminated blocks for up to the serial
            // timeout, so this must not occupy a thread-pool worker.
            readerTask = Task.Factory.StartNew(ReadLoop,
                                               TaskCreationOptions.LongRunning);
        }

        public void Stop()
        {
            running = false;

            try { cancellation.Cancel(); } catch { }

            foreach (KeyValuePair<uint, TaskCompletionSource<ProtocolResponse>> entry in pending)
            {
                entry.Value.TrySetException(
                    new IOException("AstroBox protocol reader stopped"));
            }
            pending.Clear();
        }

        public ProtocolResponse Send(string command, params string[] args)
        {
            return SendAsync(command, args).GetAwaiter().GetResult();
        }

        public ProtocolResponse SendWithTimeout(string command, int timeoutMs,
                                                params string[] args)
        {
            return SendAsync(command, timeoutMs, args).GetAwaiter().GetResult();
        }

        public Task<ProtocolResponse> SendAsync(string command, params string[] args)
        {
            return SendAsync(command, 5000, args);
        }

        public async Task<ProtocolResponse> SendAsync(string command, int timeoutMs,
                                                      params string[] args)
        {
            if (!running)
                throw new InvalidOperationException("AstroBox protocol is not running");

            uint id = (uint)Interlocked.Increment(ref nextRequestId);
            TaskCompletionSource<ProtocolResponse> completion =
                new TaskCompletionSource<ProtocolResponse>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            if (!pending.TryAdd(id, completion))
                throw new InvalidOperationException("Duplicate AstroBox request id");

            try
            {
                string frame = BuildCommandFrame(id, command, args);

                await writeLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    serial.Transmit(frame);
                }
                finally
                {
                    writeLock.Release();
                }

                Task timeout = Task.Delay(timeoutMs);
                Task completed = await Task.WhenAny(completion.Task, timeout)
                                           .ConfigureAwait(false);

                if (completed != completion.Task)
                    throw new TimeoutException(
                        string.Format("AstroBox command {0} timed out after {1} ms",
                                      command, timeoutMs));

                return await completion.Task.ConfigureAwait(false);
            }
            catch
            {
                TaskCompletionSource<ProtocolResponse> ignored;
                pending.TryRemove(id, out ignored);
                throw;
            }
        }

        private static string BuildCommandFrame(uint id, string command, string[] args)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("C;");
            builder.Append(id.ToString(CultureInfo.InvariantCulture));
            builder.Append(';');
            builder.Append(command);

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    builder.Append(';');
                    builder.Append(args[i] ?? string.Empty);
                }
            }

            builder.Append('\n');
            return builder.ToString();
        }

        private void ReadLoop()
        {
            while (!cancellation.IsCancellationRequested)
            {
                string line;
                try
                {
                    line = serial.ReceiveTerminated("\n");
                }
                catch (TimeoutException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    if (!cancellation.IsCancellationRequested)
                        RaiseProtocolMessage("Serial read failed: " + ex.Message);
                    break;
                }

                if (line == null)
                    continue;

                line = line.Trim();
                if (line.Length == 0)
                    continue;

                try
                {
                    HandleLine(line);
                }
                catch (Exception ex)
                {
                    RaiseProtocolMessage("Malformed frame ignored: " + ex.Message);
                }
            }
        }

        private void HandleLine(string line)
        {
            string[] parts = line.Split(';');
            if (parts.Length < 2)
                return;

            switch (parts[0])
            {
                case "T":
                    HandleTelemetry(parts);
                    break;
                case "R":
                    HandleResponse(parts);
                    break;
                case "H":
                    HandleHello(parts);
                    break;
                case "E":
                    HandleDeviceLog(parts);
                    break;
            }
        }

        private void HandleDeviceLog(string[] parts)
        {
            // E;<boot-id>;<sequence>;<severity>;<uptime-ms>;<source>;<message...>
            if (parts.Length < 7)
                return;

            long bootId;
            long sequence;
            long uptime;
            if (!long.TryParse(parts[1], NumberStyles.Integer,
                               CultureInfo.InvariantCulture, out bootId))
                return;
            if (!long.TryParse(parts[2], NumberStyles.Integer,
                               CultureInfo.InvariantCulture, out sequence))
                return;
            if (!long.TryParse(parts[4], NumberStyles.Integer,
                               CultureInfo.InvariantCulture, out uptime))
                return;

            string severity = parts[3];
            string source = parts[5];
            string message = string.Join(";", parts, 6, parts.Length - 6);

            DeviceLogEntry entry = new DeviceLogEntry(
                bootId, sequence, severity, uptime, source, message);

            EventHandler<DeviceLogEntry> handler = DeviceLogReceived;
            if (handler != null)
                handler(this, entry);
        }

        private void HandleHello(string[] parts)
        {
            int version;
            if (parts.Length >= 2 &&
                int.TryParse(parts[1], NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out version))
            {
                ProtocolVersion = version;
            }

            if (parts.Length >= 4)
                FirmwareInfo = parts[3];
        }

        private void HandleTelemetry(string[] parts)
        {
            if (parts.Length < 4)
                return;

            long sequence;
            long uptime;
            long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                          out sequence);
            long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture,
                          out uptime);

            Dictionary<string, double> values =
                new Dictionary<string, double>(StringComparer.Ordinal);

            for (int i = 3; i < parts.Length; i++)
            {
                string key;
                string value;
                if (!SplitPair(parts[i], out key, out value))
                    continue;

                double number;
                if (double.TryParse(value, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out number))
                {
                    values[key] = number;
                }
            }

            LastTelemetryUtc = DateTime.UtcNow;

            EventHandler<TelemetryEventArgs> handler = TelemetryReceived;
            if (handler != null)
                handler(this, new TelemetryEventArgs(sequence, uptime, values));
        }

        private void HandleResponse(string[] parts)
        {
            if (parts.Length < 3)
                return;

            uint id;
            if (!uint.TryParse(parts[1], NumberStyles.Integer,
                               CultureInfo.InvariantCulture, out id))
                return;

            ProtocolResponse response = new ProtocolResponse
            {
                Id = id,
                Status = parts[2],
                Ok = string.Equals(parts[2], "OK", StringComparison.Ordinal)
            };

            for (int i = 3; i < parts.Length; i++)
            {
                string token = parts[i];
                if (token.Length == 0)
                    continue;

                response.RawTokens.Add(token);

                string key;
                string value;
                if (!SplitPair(token, out key, out value))
                    continue;

                double number;
                if (double.TryParse(value, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out number))
                {
                    response.Values[key] = number;
                }
                else
                {
                    response.Strings[key] = value;
                }
            }

            TaskCompletionSource<ProtocolResponse> completion;
            if (pending.TryRemove(id, out completion))
                completion.TrySetResult(response);
        }

        private static bool SplitPair(string token, out string key, out string value)
        {
            key = null;
            value = null;

            int separator = token.IndexOf('=');
            if (separator <= 0)
                return false;

            key = token.Substring(0, separator);
            value = token.Substring(separator + 1);
            return true;
        }

        private void RaiseProtocolMessage(string message)
        {
            EventHandler<string> handler = ProtocolMessage;
            if (handler != null)
                handler(this, message);
        }

        public void Dispose()
        {
            Stop();
            writeLock.Dispose();
        }
    }
}
