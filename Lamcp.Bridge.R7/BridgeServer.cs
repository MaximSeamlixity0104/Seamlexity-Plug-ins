using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Rhino.Runtime;

namespace Lamcp.Bridge.R7
{
    /// <summary>
    /// Minimal HTTP/1.1 server bound to 127.0.0.1 only. Wire-compatible with the Rhino 8
    /// Lamcp_Bridge component: GET /health and POST /exec {"code": str, "timeout": float}
    /// returning {"stdout", "stderr", "result", "error"}.
    /// One server per Rhino process; Python execution is serialized.
    /// </summary>
    internal sealed class BridgeServer
    {
        private const int MaxHeaderBytes = 64 * 1024;
        private const int MaxBodyBytes = 64 * 1024 * 1024;

        private static readonly object InstanceLock = new object();
        private static BridgeServer _instance;

        private readonly object _execLock = new object();
        private readonly TcpListener _listener;
        private readonly Thread _acceptThread;
        private readonly PythonScript _python;
        private volatile bool _running;

        public int Port { get; }
        public bool IsRunning => _running;
        public string LastError { get; private set; }
        public long RequestCount;

        private BridgeServer(int port, PythonScript python)
        {
            Port = port;
            _python = python;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "lamcp-bridge-r7" };
        }

        public static BridgeServer Current
        {
            get { lock (InstanceLock) { return _instance; } }
        }

        /// <summary>Must be called on the Rhino UI thread (creates the IronPython scope).</summary>
        public static BridgeServer Start(int port)
        {
            lock (InstanceLock)
            {
                if (_instance != null && _instance.IsRunning && _instance.Port == port)
                {
                    return _instance;
                }
                StopInternal();

                PythonScript python = PythonScript.Create();
                if (python == null)
                {
                    throw new InvalidOperationException("Rhino IronPython engine is not available (PythonScript.Create returned null).");
                }
                python.ExecuteScript(LoadDriver());

                var server = new BridgeServer(port, python);
                server._listener.Start();
                server._running = true;
                server._acceptThread.Start();
                _instance = server;
                return server;
            }
        }

        public static void Stop()
        {
            lock (InstanceLock) { StopInternal(); }
        }

        private static void StopInternal()
        {
            if (_instance == null) return;
            _instance._running = false;
            try { _instance._listener.Stop(); } catch { /* already stopped */ }
            _instance = null;
        }

        private static string LoadDriver()
        {
            Assembly assembly = typeof(BridgeServer).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream("Lamcp.Bridge.R7.BridgeDriver.py"))
            {
                if (stream == null) throw new InvalidOperationException("Embedded BridgeDriver.py is missing.");
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception ex)
                {
                    if (_running) LastError = ex.Message;
                    break;
                }
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 30000;
                    NetworkStream stream = client.GetStream();
                    if (!TryReadRequest(stream, out string method, out string path, out byte[] body))
                    {
                        WriteJson(stream, 400, new Dictionary<string, object> { ["error"] = "bad request" });
                        return;
                    }
                    Interlocked.Increment(ref RequestCount);
                    Route(stream, method, path, body);
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
            }
        }

        private void Route(NetworkStream stream, string method, string path, byte[] body)
        {
            int query = path.IndexOf('?');
            if (query >= 0) path = path.Substring(0, query);

            if (method == "GET" && path == "/health")
            {
                WriteJson(stream, 200, new Dictionary<string, object>
                {
                    ["status"] = "ok",
                    ["runtime"] = "Rhino " + Rhino.RhinoApp.ExeVersion + " / IronPython 2.7",
                });
                return;
            }

            if (method == "POST" && path == "/exec")
            {
                string code;
                try
                {
                    var serializer = NewSerializer();
                    var request = serializer.DeserializeObject(Encoding.UTF8.GetString(body)) as IDictionary<string, object>;
                    code = request != null && request.TryGetValue("code", out object c) ? c as string ?? string.Empty : string.Empty;
                }
                catch (Exception ex)
                {
                    WriteJson(stream, 400, new Dictionary<string, object> { ["error"] = "bad request: " + ex.Message });
                    return;
                }
                WriteJson(stream, 200, Execute(code));
                return;
            }

            WriteJson(stream, 404, new Dictionary<string, object> { ["error"] = "not found" });
        }

        private Dictionary<string, object> Execute(string code)
        {
            lock (_execLock)
            {
                try
                {
                    _python.SetVariable("_lamcp_code", code);
                    _python.ExecuteScript("_lamcp_out = _lamcp_exec(_lamcp_code)");
                    var output = _python.GetVariable("_lamcp_out") as IList;
                    if (output == null || output.Count != 4)
                    {
                        throw new InvalidOperationException("Bridge driver returned an unexpected value.");
                    }
                    return new Dictionary<string, object>
                    {
                        ["stdout"] = output[0]?.ToString() ?? string.Empty,
                        ["stderr"] = output[1]?.ToString() ?? string.Empty,
                        ["result"] = output[2]?.ToString(),
                        ["error"] = output[3]?.ToString(),
                    };
                }
                catch (Exception ex)
                {
                    return new Dictionary<string, object>
                    {
                        ["stdout"] = string.Empty,
                        ["stderr"] = string.Empty,
                        ["result"] = null,
                        ["error"] = "Bridge host error: " + ex,
                    };
                }
            }
        }

        private static JavaScriptSerializer NewSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        }

        private static bool TryReadRequest(NetworkStream stream, out string method, out string path, out byte[] body)
        {
            method = path = null;
            body = new byte[0];

            var header = new MemoryStream();
            int matched = 0;
            byte[] terminator = { 13, 10, 13, 10 };
            while (matched < 4)
            {
                int b = stream.ReadByte();
                if (b < 0) return false;
                header.WriteByte((byte)b);
                matched = b == terminator[matched] ? matched + 1 : (b == 13 ? 1 : 0);
                if (header.Length > MaxHeaderBytes) return false;
            }

            string[] lines = Encoding.ASCII.GetString(header.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2) return false;
            method = requestLine[0].ToUpperInvariant();
            path = requestLine[1];

            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                string name = lines[i].Substring(0, colon).Trim();
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(lines[i].Substring(colon + 1).Trim(), out contentLength)) return false;
                }
                else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                {
                    return false; // chunked bodies are not supported; httpx sends Content-Length for json=
                }
            }
            if (contentLength < 0 || contentLength > MaxBodyBytes) return false;

            body = new byte[contentLength];
            int read = 0;
            while (read < contentLength)
            {
                int n = stream.Read(body, read, contentLength - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        private static void WriteJson(NetworkStream stream, int status, object payload)
        {
            byte[] body = Encoding.UTF8.GetBytes(NewSerializer().Serialize(payload));
            string reason = status == 200 ? "OK" : status == 404 ? "Not Found" : "Bad Request";
            string head = "HTTP/1.1 " + status + " " + reason + "\r\n" +
                          "Content-Type: application/json\r\n" +
                          "Content-Length: " + body.Length + "\r\n" +
                          "Connection: close\r\n\r\n";
            byte[] headBytes = Encoding.ASCII.GetBytes(head);
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }
    }
}
