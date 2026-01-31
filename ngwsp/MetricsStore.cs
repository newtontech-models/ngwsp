using System.Text;

namespace ngwsp;

public sealed class MetricsStore
{
    private long _activeSessions;
    private long _bytesIn;
    private long _bytesOut;
    private long _upstreamErrors;

    public void SessionStarted() => Interlocked.Increment(ref _activeSessions);
    public void SessionEnded() => Interlocked.Decrement(ref _activeSessions);
    public void AddBytesIn(long count) => Interlocked.Add(ref _bytesIn, count);
    public void AddBytesOut(long count) => Interlocked.Add(ref _bytesOut, count);
    public void AddUpstreamError() => Interlocked.Increment(ref _upstreamErrors);

    public string RenderPrometheus()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# HELP ngwsp_active_sessions Active WebSocket sessions");
        builder.AppendLine("# TYPE ngwsp_active_sessions gauge");
        builder.Append("ngwsp_active_sessions ").AppendLine(Interlocked.Read(ref _activeSessions).ToString());

        builder.AppendLine("# HELP ngwsp_bytes_in Total bytes received by the proxy");
        builder.AppendLine("# TYPE ngwsp_bytes_in counter");
        builder.Append("ngwsp_bytes_in ").AppendLine(Interlocked.Read(ref _bytesIn).ToString());

        builder.AppendLine("# HELP ngwsp_bytes_out Total bytes sent by the proxy");
        builder.AppendLine("# TYPE ngwsp_bytes_out counter");
        builder.Append("ngwsp_bytes_out ").AppendLine(Interlocked.Read(ref _bytesOut).ToString());

        builder.AppendLine("# HELP ngwsp_upstream_errors Total upstream errors observed");
        builder.AppendLine("# TYPE ngwsp_upstream_errors counter");
        builder.Append("ngwsp_upstream_errors ").AppendLine(Interlocked.Read(ref _upstreamErrors).ToString());

        return builder.ToString();
    }
}
