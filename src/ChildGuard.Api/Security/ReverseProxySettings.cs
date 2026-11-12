using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace ChildGuard.Api.Security;

/// <summary>
/// How the API learns the real client address and scheme behind a reverse proxy. Nothing configured
/// means forwarded headers are ignored (a client could otherwise forge them to dodge rate limits).
/// <list type="bullet">
/// <item><c>KnownProxies</c> / <c>KnownNetworks</c>: only those hops are believed (fixed infrastructure).</item>
/// <item><c>TrustAll</c>: any peer is accepted as the proxy. For platforms such as Render, where the
/// proxy's address is not fixed and the service is reachable only through it.</item>
/// <item><c>ForwardLimit</c> (default 1): how many forwarded hops are read. One means the address the
/// nearest proxy saw; a client-supplied X-Forwarded-For further left is not believed.</item>
/// </list>
/// </summary>
public sealed record ReverseProxySettings(string[] KnownProxies, string[] KnownNetworks, bool TrustAll, int ForwardLimit)
{
    public bool Enabled => TrustAll || KnownProxies.Length > 0 || KnownNetworks.Length > 0;

    public static ReverseProxySettings From(IConfiguration config)
    {
        var section = config.GetSection("ReverseProxy");
        return new ReverseProxySettings(
            section.GetSection("KnownProxies").Get<string[]>() ?? [],
            section.GetSection("KnownNetworks").Get<string[]>() ?? [],
            section.GetValue<bool>("TrustAll"),
            Math.Max(1, section.GetValue("ForwardLimit", 1)));
    }

    public void Apply(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = ForwardLimit;
        // The framework trusts loopback only until these are cleared.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        if (TrustAll) return;
        foreach (var proxy in KnownProxies) options.KnownProxies.Add(IPAddress.Parse(proxy));
        foreach (var network in KnownNetworks) options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
}
