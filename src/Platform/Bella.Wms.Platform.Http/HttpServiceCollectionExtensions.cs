using Microsoft.Extensions.DependencyInjection;

namespace Bella.Wms.Platform.Http;

/// <summary>Registers the outbound HTTP transport.</summary>
public static class HttpServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IWmsHttpClient"/> and the payload archive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>TLS validation is deliberately left on.</b> Every ABL cURL invocation passes
    /// <c>--insecure</c> (<c>headerCurl.sh</c> lines 38-46, <c>globaleCurl.sh:15</c>).
    /// Nothing here disables certificate validation, and nothing should — if an endpoint
    /// fails, that is the finding, not a problem to configure away.
    /// </para>
    /// <para>
    /// The archive is a singleton because it holds no per-request state; it takes the
    /// context as a parameter on every call instead.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddWmsHttp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRequestArchive, FileRequestArchive>();
        services.AddHttpClient<IWmsHttpClient, WmsHttpClient>();

        return services;
    }
}
