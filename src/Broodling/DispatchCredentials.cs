using System.Collections.Immutable;
using Zeroshot.Native.Contracts;

namespace Broodling;

/// <summary>Current secrets are never part of the frozen invocation or a diagnostic.</summary>
public sealed class DispatchCredentials(string? githubToken, string? gatewayBaseUrl, string? gatewayApiKey)
{
    /// <summary>
    /// The stock run's separately supplied credentials: the approved asset's <c>gateway</c> and <c>github</c>
    /// connections and the outer source token.
    /// </summary>
    internal TargetRunCredentials TargetRun()
    {
        if (string.IsNullOrWhiteSpace(githubToken) || githubToken.Length > 4096
            || string.IsNullOrWhiteSpace(gatewayApiKey) || gatewayApiKey.Length > 4096
            || gatewayBaseUrl != DirectTargetBinding.GatewayBaseUrl)
            throw new UnsupportedRuntime("PR dispatch requires current GitHub/gateway credentials and the exact supported gateway URL.");
        return new()
        {
            Connections = ImmutableDictionary<string, ImmutableDictionary<string, string>>.Empty
                .Add("gateway", ImmutableDictionary<string, string>.Empty
                    .Add("GATEWAY_BASE_URL", gatewayBaseUrl).Add("GATEWAY_API_KEY", gatewayApiKey))
                .Add("github", ImmutableDictionary<string, string>.Empty.Add("GH_TOKEN", githubToken)),
            GithubToken = githubToken
        };
    }
    public override string ToString() => nameof(DispatchCredentials);
}
