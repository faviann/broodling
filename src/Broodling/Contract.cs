using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broodling;

public sealed record SourceAttribution(string SourceId, string ContentSha256);
public sealed record Obligation(string ObligationId, string Statement, string Kind);
public sealed record RequiredEffect(string EffectId, string Statement, string Kind, string TargetBranch = "");
public sealed record Prerequisite(string PrerequisiteId, string Statement, bool SatisfiedWithinProfile);
public sealed record FinalAssuranceMaterial(string Path, bool FinalCandidate = true, bool ComparisonBase = false);

public sealed class EvidencePopulation(string kind, IReadOnlyList<string>? members = null, string surface = "")
{
    public string Kind { get; } = kind;
    public IReadOnlyList<string> Members { get; } = ContractData.Copy(members ?? [], nameof(members));
    public string Surface { get; } = surface;
}

public sealed class MechanicalEvidence(IReadOnlyList<string> argv, string cwd = ".", IReadOnlyList<string>? materials = null)
{
    public IReadOnlyList<string> Argv { get; } = ContractData.Copy(argv, nameof(argv));
    public string Cwd { get; } = cwd;
    public IReadOnlyList<string> Materials { get; } = ContractData.Copy(materials ?? [], nameof(materials));
}

public sealed class Criterion(string criterionId, string statement, EvidencePopulation? evidencePopulation = null,
    string validationSeam = "", string validationAction = "", string falsifyingObservation = "",
    IReadOnlyList<string>? evidenceEffectDependencies = null, MechanicalEvidence? mechanicalEvidence = null)
{
    public string CriterionId { get; } = criterionId;
    public string Statement { get; } = statement;
    public EvidencePopulation? EvidencePopulation { get; } = evidencePopulation;
    public string ValidationSeam { get; } = validationSeam;
    public string ValidationAction { get; } = validationAction;
    public string FalsifyingObservation { get; } = falsifyingObservation;
    public IReadOnlyList<string> EvidenceEffectDependencies { get; } = ContractData.Copy(evidenceEffectDependencies ?? [], nameof(evidenceEffectDependencies));
    public MechanicalEvidence? MechanicalEvidence { get; } = mechanicalEvidence;
}

/// <summary>Complete immutable meaning. Unsupported requirements remain representable for refusal.</summary>
public sealed class Contract
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    public Contract(string workUnitId, IReadOnlyList<SourceAttribution> sourceAttribution, IReadOnlyList<Criterion> criteria,
        IReadOnlyList<Obligation>? obligations = null, IReadOnlyList<Prerequisite>? prerequisites = null,
        IReadOnlyList<RequiredEffect>? requiredEffects = null, IReadOnlyList<string>? hostAssumptions = null,
        string constructedBy = "broodling_policy", string notes = "",
        IReadOnlyList<FinalAssuranceMaterial>? finalAssuranceMaterials = null)
    {
        WorkUnitId = workUnitId;
        // Attribution is a set of exact pins, not precedence. Keep duplicates so
        // validation can reject them instead of silently changing the proposal.
        SourceAttribution = Array.AsReadOnly(ContractData.Copy(sourceAttribution, nameof(sourceAttribution))
            .OrderBy(pin => pin.SourceId, StringComparer.Ordinal)
            .ThenBy(pin => pin.ContentSha256, StringComparer.Ordinal).ToArray());
        Criteria = ContractData.Copy(criteria, nameof(criteria));
        Obligations = ContractData.Copy(obligations ?? [], nameof(obligations));
        Prerequisites = ContractData.Copy(prerequisites ?? [], nameof(prerequisites));
        RequiredEffects = ContractData.Copy(requiredEffects ?? [], nameof(requiredEffects));
        HostAssumptions = ContractData.Copy(hostAssumptions ?? [], nameof(hostAssumptions));
        ConstructedBy = constructedBy;
        Notes = notes;
        FinalAssuranceMaterials = finalAssuranceMaterials is null ? null : ContractData.Copy(finalAssuranceMaterials, nameof(finalAssuranceMaterials));
    }

    public string WorkUnitId { get; }
    public IReadOnlyList<SourceAttribution> SourceAttribution { get; }
    public IReadOnlyList<Criterion> Criteria { get; }
    public IReadOnlyList<Obligation> Obligations { get; }
    public IReadOnlyList<Prerequisite> Prerequisites { get; }
    public IReadOnlyList<RequiredEffect> RequiredEffects { get; }
    public IReadOnlyList<string> HostAssumptions { get; }
    public string ConstructedBy { get; }
    public string Notes { get; }
    public IReadOnlyList<FinalAssuranceMaterial>? FinalAssuranceMaterials { get; }

    [JsonIgnore]
    public string ContractSha256 => Digests.Bytes(CanonicalBytes());
    [JsonIgnore]
    public string ContractRevisionId => "cr-" + ContractSha256;

    public byte[] CanonicalBytes()
    {
        Validate();
        return JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
    }

    public static Contract FromCanonicalBytes(byte[] bytes)
    {
        try
        {
            var contract = JsonSerializer.Deserialize<Contract>(bytes, JsonOptions)
                ?? throw new InvalidContractProposal("Contract material must be an object.");
            // Stored material is our canonical format, not permissive model JSON.
            // Never silently discard unknown, omitted, or malformed meaning.
            if (!contract.CanonicalBytes().AsSpan().SequenceEqual(bytes))
                throw new InvalidContractProposal("Stored Contract material is not canonical.");
            return contract;
        }
        catch (JsonException)
        {
            throw new InvalidContractProposal("Contract material is malformed or has unrecognized fields.");
        }
    }

    public void Validate()
    {
        ContractData.Text(WorkUnitId, "Work Unit", required: true);
        ContractData.Text(Notes, "notes");
        if (ConstructedBy is not ("broodling_policy" or "caller" or "model_extraction"))
            throw new InvalidContractProposal("Unrecognized Contract producer.");
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pin in SourceAttribution)
        {
            ContractData.Text(pin.SourceId, "source identity", required: true);
            ContractData.Text(pin.ContentSha256, "source digest", required: true);
            if (!sources.Add(pin.SourceId))
                throw new SourceAttributionError("A source must be attributed once at its exact digest.");
        }
        ValidateStatements(Criteria.Select(item => (item.CriterionId, item.Statement)), "criterion");
        ValidateStatements(Obligations.Select(item => (item.ObligationId, item.Statement)), "obligation");
        ValidateStatements(Prerequisites.Select(item => (item.PrerequisiteId, item.Statement)), "prerequisite");
        ValidateStatements(RequiredEffects.Select(item => (item.EffectId, item.Statement)), "effect");
        foreach (var criterion in Criteria)
        {
            ContractData.Text(criterion.ValidationSeam, "validation seam");
            ContractData.Text(criterion.ValidationAction, "validation action");
            ContractData.Text(criterion.FalsifyingObservation, "falsifying observation");
            foreach (var dependency in criterion.EvidenceEffectDependencies)
                ContractData.Text(dependency, "evidence effect dependency");
            if (criterion.EvidencePopulation is { } population)
            {
                ContractData.Text(population.Kind, "evidence population kind");
                ContractData.Text(population.Surface, "evidence population surface");
                foreach (var member in population.Members)
                    ContractData.Text(member, "evidence population member");
            }
            if (criterion.MechanicalEvidence is { } mechanical)
            {
                ContractData.Text(mechanical.Cwd, "mechanical evidence directory");
                foreach (var argument in mechanical.Argv)
                    ContractData.Text(argument, "mechanical evidence argument");
                foreach (var material in mechanical.Materials)
                    ContractData.Text(material, "mechanical evidence material");
            }
        }
        foreach (var obligation in Obligations)
            ContractData.Text(obligation.Kind, "obligation kind");
        foreach (var effect in RequiredEffects)
        {
            ContractData.Text(effect.Kind, "effect kind");
            ContractData.Text(effect.TargetBranch, "target branch");
        }
        foreach (var assumption in HostAssumptions)
            ContractData.Text(assumption, "host assumption");
        foreach (var material in FinalAssuranceMaterials ?? [])
            ContractData.Text(material.Path, "selected material path");
    }

    private static void ValidateStatements(IEnumerable<(string Identity, string Statement)> items, string kind)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (identity, statement) in items)
        {
            ContractData.Text(identity, kind + " identity", required: true);
            ContractData.Text(statement, kind + " statement", required: true);
            if (!identities.Add(identity))
                throw new InvalidContractProposal($"{kind} identities must be unique.");
        }
    }
}

internal static class ContractData
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string name) where T : class
    {
        if (values is null)
            throw new InvalidContractProposal($"{name} must be a collection.");
        var copy = values.ToArray();
        if (copy.Any(value => value is null))
            throw new InvalidContractProposal($"{name} cannot contain null elements.");
        return Array.AsReadOnly(copy);
    }

    internal static void Text(string value, string name, bool required = false)
    {
        if (value is null || (required && string.IsNullOrWhiteSpace(value)))
            throw new InvalidContractProposal($"{name} must be {(required ? "nonempty text" : "text")}.");
        try { StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException)
        {
            throw new InvalidContractProposal($"{name} contains invalid Unicode.");
        }
    }
}
