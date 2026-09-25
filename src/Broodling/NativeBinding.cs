using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>The frozen PR source selectors: GitHub owner/repository, authorized target branch and original B1.</summary>
public sealed record NativeSource(string Repository, string Branch, string Revision);

/// <summary>
/// The retained facts that identify one correlated native run. Read and stop receive these
/// instead of adapter settings; no credentials, workspace or client-state configuration.
/// </summary>
public sealed record NativeRunBinding(NativeLocator Locator, string RunId, string Title, string Size, NativeSource? Source);

/// <summary>The one interpretation of a retained request's facts. It never rewrites the saved bytes.</summary>
internal sealed record FrozenSubmission(string Delivery, NativeLocator Locator, string Title, string Size,
    NativeSource? Source, string Repository, string OriginUrl)
{
    internal static FrozenSubmission Read(string requestJson)
    {
        var request = JsonNode.Parse(requestJson)!;
        var source = request["source"];
        return new((string)request["preset"]!["delivery"]!, NativeLocator.Read(request["target"]!["locator"]!),
            (string)request["title"]!, (string)request["runtime"]!["size"]!,
            source is null ? null : new((string)source["repository"]!, (string)source["branch"]!, (string)source["revision"]!),
            (string)request["repository"]!, (string)request["originUrl"]!);
    }

    internal NativeRunBinding Run(string runId) => new(Locator, runId, Title, Size, Source);
}
