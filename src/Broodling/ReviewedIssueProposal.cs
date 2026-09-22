using System.Text.Json;

namespace Broodling;

/// <summary>
/// Caller proposal for a self-contained request whose complete issue snapshot an operator has reviewed.
/// It does not infer that prerequisites are satisfied or that omitted external obligations are permitted.
/// </summary>
public sealed class ReviewedIssueProposal(byte[] reviewedIssue)
{
    private readonly byte[] reviewed = (byte[])reviewedIssue.Clone();

    public Contract Propose(ContractProposalInput inputs)
    {
        if (inputs.Sources.Count != 1 || inputs.Sources[0].Kind != "primary_issue")
            throw new InvalidContractProposal("The reviewed-issue proposer requires exactly one primary issue and no supplementary sources.");
        if (!inputs.Sources[0].Content.SequenceEqual(reviewed))
            throw new InvalidContractProposal("Issue changed since operator review; review the current snapshot.");
        try
        {
            using var document = JsonDocument.Parse(reviewed);
            var issue = document.RootElement;
            var title = issue.GetProperty("title").GetString();
            var body = issue.GetProperty("body").GetString();
            if (string.IsNullOrWhiteSpace(title))
                throw new InvalidContractProposal("The reviewed issue must have a title.");
            return new(inputs.WorkUnit.WorkUnitId, inputs.SourceAttribution,
                [new("reviewed-request", title + "\n\n" + (body ?? ""))],
                requiredEffects: inputs.RequiredEffects, constructedBy: inputs.ConstructedBy);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidContractProposal("The reviewed issue must contain valid title and body JSON.");
        }
    }
}
