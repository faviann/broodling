using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>The frozen PR source selectors: GitHub owner/repository, authorized target branch and original B1.</summary>
public sealed record NativeSource(string Repository, string Branch, string Revision);

/// <summary>
/// The retained facts that identify one correlated native run. Read and stop receive these
/// instead of adapter settings; no credentials, workspace or client-state configuration.
/// </summary>
public sealed record NativeRunBinding(NativeLocator Locator, string RunId, string Title, string Size, NativeSource? Source);

/// <summary>
/// Which run identity a network operation used: the intended ID of a dispatched but unacknowledged
/// submission, or the confirmed ID of a correlated one. A snapshot, never a correlation fact.
/// </summary>
public enum NativeRunIdentity { Intended, Confirmed }

/// <summary>The one interpretation of a retained request's facts. It never rewrites the saved bytes.</summary>
internal sealed record FrozenSubmission(string Delivery, NativeLocator Locator, string Title, string Size,
    NativeSource? Source, string Repository, string OriginUrl)
{
    /// <summary>The bridge facts: no-effect LocalTarget work has no PR source.</summary>
    internal static FrozenSubmission Read(string requestJson)
    {
        var request = JsonNode.Parse(requestJson)!;
        return new((string)request["preset"]!["delivery"]!, NativeLocator.Read(request["target"]!["locator"]!),
            (string)request["title"]!, (string)request["runtime"]!["size"]!, null,
            (string)request["repository"]!, (string)request["originUrl"]!);
    }

    /// <summary>
    /// The HTTP facts: the stock request names title, size and source; the Broodling-only
    /// binding names the target origin, shared Git custody and the frozen result-fetch origin.
    /// </summary>
    internal static FrozenSubmission ReadHttp(string requestJson, string bindingJson)
    {
        var submission = JsonNode.Parse(requestJson)!["submission"]!;
        var binding = JsonNode.Parse(bindingJson)!;
        var source = submission["source"]!;
        return new("pull_request", new NativeLocator("direct", (string)binding["origin"]!, null),
            (string)submission["title"]!, (string)submission["runtime"]!["size"]!,
            new((string)source["repository"]!, (string)source["branch"]!, (string)source["revision"]!),
            (string)binding["repository"]!, (string)binding["resultOrigin"]!);
    }

    internal NativeRunBinding Run(string runId) => new(Locator, runId, Title, Size, Source);
}
