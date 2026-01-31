namespace ngwsp;

public interface IUpstreamReadiness
{
    bool IsReady { get; }
}

public sealed class AlwaysReadyUpstreamReadiness : IUpstreamReadiness
{
    public bool IsReady => true;
}
