using System.Net;
using System.Text;

namespace Telemetry.Present;

/// <summary>Локальний сервер сторінки: лише localhost; будь-який збій запиту не зупиняє сервер.</summary>
internal sealed class Server(LiveRun live, string overviewJson, string preflightJson)
{
    private HttpListener? listener;

    public int Port { get; private set; }

    public bool Start(int firstPort)
    {
        for (var port = firstPort; port < firstPort + 10; port++)
        {
            var candidate = new HttpListener();
            candidate.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                candidate.Start();
            }
            catch (HttpListenerException)
            {
                candidate.Close();
                continue;
            }

            listener = candidate;
            Port = port;
            return true;
        }

        return false;
    }

    public async Task RunAsync(CancellationToken stop)
    {
        using var registration = stop.Register(() => listener?.Close());
        while (!stop.IsCancellationRequested && listener is { IsListening: true })
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                break;
            }

            _ = Task.Run(() => Handle(context), stop);
        }
    }

    private void Handle(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            var method = context.Request.HttpMethod;
            if (method == "GET" && path is "/" or "/index.html")
            {
                Send(context, 200, "text/html; charset=utf-8", File.ReadAllText(IndexPath()));
            }
            else if (method == "GET" && path == "/api/overview")
            {
                Send(context, 200, "application/json; charset=utf-8", overviewJson);
            }
            else if (method == "GET" && path == "/api/preflight")
            {
                Send(context, 200, "application/json; charset=utf-8", preflightJson);
            }
            else if (method == "GET" && path == "/api/state")
            {
                Send(context, 200, "application/json; charset=utf-8", live.State().ToJsonString());
            }
            else if (method == "GET" && path == "/api/report")
            {
                var report = live.ResultJson();
                Send(context, report is null ? 404 : 200, "application/json; charset=utf-8", report ?? "{\"error\":\"немає результатів\"}");
            }
            else if (method == "POST" && path == "/api/run")
            {
                if (!SameOrigin(context))
                {
                    Send(context, 403, "application/json; charset=utf-8", "{\"error\":\"чужий origin\"}");
                    return;
                }

                var started = live.TryStart();
                Send(context, started ? 202 : 409, "application/json; charset=utf-8", $"{{\"started\":{(started ? "true" : "false")}}}");
            }
            else if (path == "/favicon.ico")
            {
                Send(context, 204, "image/x-icon", "");
            }
            else
            {
                Send(context, 404, "text/plain; charset=utf-8", "не знайдено");
            }
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            try
            {
                Send(context, 500, "application/json; charset=utf-8", "{\"error\":\"внутрішня помилка сторінки\"}");
            }
            catch (Exception inner) when (inner is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                // клієнт уже закрив з'єднання
            }
        }
    }

    private bool SameOrigin(HttpListenerContext context)
    {
        var origin = context.Request.Headers["Origin"];
        return string.IsNullOrEmpty(origin) || origin == $"http://localhost:{Port}";
    }

    private static string IndexPath()
    {
        var inRepo = Repo.Path("tools", "Telemetry.Present", "wwwroot", "index.html");
        return File.Exists(inRepo) ? inRepo : Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
    }

    private static void Send(HttpListenerContext context, int status, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
    }
}
