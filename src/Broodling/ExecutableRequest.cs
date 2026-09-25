using System.Globalization;
using System.Text.RegularExpressions;

namespace Broodling;

/// <summary>
/// One normalized v1 reference target. Its reference ID is the bundle identity
/// used for deduplication; issue URLs normalize owner/repository case.
/// </summary>
internal sealed record RequestTarget(string ReferenceId, string? Path, WorkReference? Issue, long? CommentId)
{
    public static RequestTarget RepositoryFile(string path) => new("repo:" + path, path, null, null);

    public static RequestTarget? GitHub(string owner, string repository, string issue, string? comment)
    {
        if (owner is "." or ".." || repository is "." or "..")
            return null;
        WorkReference reference;
        try { reference = WorkReference.Parse(owner + "/" + repository, issue); }
        catch (InvalidWorkReference) { return null; }
        long? commentId = null;
        if (comment is not null)
        {
            if (!long.TryParse(comment, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                return null;
            commentId = parsed;
        }
        var number = reference.IssueNumber.ToString(CultureInfo.InvariantCulture);
        var id = $"github:{reference.Owner}/{reference.Repository}/issues/{number}"
            + (commentId is { } c ? "#issuecomment-" + c.ToString(CultureInfo.InvariantCulture) : "");
        return new(id, null, reference, commentId);
    }
}

internal sealed record RequestDeclaration(string Label, string Target, RequestTarget Selected);

internal sealed record ExecutableRequestSection(string Text, IReadOnlyList<RequestDeclaration> Declarations);

/// <summary>
/// The version-one Executable Request convention: one marked issue-body section
/// with an optional Available references subsection of labeled declarations.
/// Rules are syntactic; nothing here judges relevance.
/// </summary>
internal static partial class ExecutableRequest
{
    internal const string Convention = "broodling-request:v1";

    // Only a whole, visible, unindented line is a marker; an inline mention or
    // indented code is prose.
    [GeneratedRegex(@"\A {0,3}<!--[ \t]*broodling-request:([^\s>]*)[ \t]*-->[ \t]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Marker();

    [GeneratedRegex(@"\A {0,3}<!--(?:(?!-->).)*-->[ \t]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Comment();

    [GeneratedRegex(@"\A {0,3}(#{1,6})(?:[ \t]+(.*?))?[ \t]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\A {0,3}(`{3,}|~{3,})(.*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex Fence();

    [GeneratedRegex(@"\A {0,3}-[ \t]+([A-Za-z0-9][A-Za-z0-9._-]*):[ \t]+(\S+)[ \t]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"\Ahttps://(?i:github\.com)/([A-Za-z0-9-]+)/([A-Za-z0-9._-]+)/issues/([0-9]+)(?:#issuecomment-([0-9]+))?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex DeclaredGitHub();

    // A link is not embedded in another URL, and ends at anything other than
    // URL-continuing text: sentence punctuation may follow it, but other paths,
    // queries, fragments or file extensions may not.
    [GeneratedRegex(@"(?<![A-Za-z0-9_/?=&%.:+-])https://(?i:github\.com)/([A-Za-z0-9-]+)/([A-Za-z0-9._-]+)/issues/([0-9]+)(?:#issuecomment-([0-9]+))?(?![A-Za-z0-9_/#?=&%-]|\.\w)",
        RegexOptions.CultureInvariant)]
    private static partial Regex BodyLink();

    private enum LineKind { Blank, Text, Heading, Marker, Fenced, Comment }

    /// <summary>Label is the heading text or the marker version.</summary>
    private sealed record Line(int Start, string Text, LineKind Kind, int Level = 0, string Label = "");

    /// <summary>Select the one marked section, or explain why none can be selected.</summary>
    internal static ExecutableRequestSection? Parse(string body, List<RequestCaptureFinding> findings)
    {
        var lines = Lines(body);
        var markers = Enumerable.Range(0, lines.Count).Where(index => lines[index].Kind == LineKind.Marker).ToArray();
        if (markers.Length == 0)
            findings.Add(new("request_section_missing", "request", $"No <!-- {Convention} --> section marker line was found."));
        foreach (var marker in markers.Where(marker => lines[marker].Label != "v1"))
            findings.Add(new("request_section_unsupported", "line " + (marker + 1),
                $"The request section convention '{lines[marker].Label}' is not supported."));
        if (markers.Length > 1)
            findings.Add(new("request_section_multiple", "request", "More than one request section marker was found."));
        if (findings.Count > 0) return null;

        var headingIndex = markers[0] - 1;
        while (headingIndex >= 0 && lines[headingIndex].Kind == LineKind.Blank)
            headingIndex--;
        if (headingIndex < 0 || lines[headingIndex].Kind != LineKind.Heading)
        {
            findings.Add(new("request_section_ambiguous", "line " + (markers[0] + 1),
                "The request section marker must be directly beneath a Markdown heading."));
            return null;
        }

        var section = lines[headingIndex];
        var end = SectionEnd(lines, headingIndex, lines.Count);
        var text = body[section.Start..(end < lines.Count ? lines[end].Start : body.Length)];

        var subsections = Enumerable.Range(headingIndex + 1, end - headingIndex - 1)
            .Where(index => lines[index].Kind == LineKind.Heading
                && string.Equals(lines[index].Label, "Available references", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (subsections.Length > 1)
        {
            findings.Add(new("invalid_reference_declaration", "line " + (subsections[1] + 1),
                "The request section has more than one Available references subsection."));
            return null;
        }
        var declarations = new List<RequestDeclaration>();
        if (subsections.Length == 1)
        {
            var start = subsections[0];
            for (var index = start + 1; index < SectionEnd(lines, start, end); index++)
            {
                if (lines[index].Kind is LineKind.Blank or LineKind.Comment) continue;
                var subject = "line " + (index + 1);
                var match = lines[index].Kind == LineKind.Text ? Declaration().Match(lines[index].Text) : Match.Empty;
                var target = match.Success ? Target(match.Groups[2].Value) : null;
                if (target is null)
                    findings.Add(new("invalid_reference_declaration", subject,
                        "Available references accepts only '- label: repo:path' or a GitHub issue/comment URL."));
                else if (declarations.Any(existing =>
                        string.Equals(existing.Label, match.Groups[1].Value, StringComparison.OrdinalIgnoreCase)))
                    findings.Add(new("invalid_reference_declaration", subject, "The reference label is declared more than once."));
                else if (declarations.Any(existing => existing.Selected.ReferenceId == target.ReferenceId))
                    findings.Add(new("invalid_reference_declaration", subject, "The reference target is declared more than once."));
                else
                    declarations.Add(new(match.Groups[1].Value, match.Groups[2].Value, target));
            }
        }
        return findings.Count > 0 ? null : new(text, declarations);
    }

    /// <summary>Supported GitHub issue/comment links in a captured reference body, in first-occurrence order.</summary>
    internal static IEnumerable<(RequestTarget Target, string Url)> Links(string body)
    {
        foreach (Match match in BodyLink().Matches(body))
            if (RequestTarget.GitHub(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value,
                    match.Groups[4].Success ? match.Groups[4].Value : null) is { } target)
                yield return (target, match.Value);
    }

    private static RequestTarget? Target(string target)
    {
        if (target.StartsWith("repo:", StringComparison.Ordinal))
            return target.Length > "repo:".Length ? RequestTarget.RepositoryFile(target["repo:".Length..]) : null;
        var match = DeclaredGitHub().Match(target);
        return match.Success
            ? RequestTarget.GitHub(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value,
                match.Groups[4].Success ? match.Groups[4].Value : null)
            : null;
    }

    private static int SectionEnd(IReadOnlyList<Line> lines, int heading, int limit)
    {
        var index = heading + 1;
        while (index < limit && (lines[index].Kind != LineKind.Heading || lines[index].Level > lines[heading].Level))
            index++;
        return index;
    }

    // Fenced code and HTML comments are content, never structure: a shell
    // comment in an example or a hidden heading cannot end the section.
    private static List<Line> Lines(string body)
    {
        var lines = new List<Line>();
        char fenceChar = '\0';
        var fenceLength = 0;
        var inComment = false;
        var start = 0;
        while (start < body.Length)
        {
            var newline = body.IndexOf('\n', start);
            var next = newline < 0 ? body.Length : newline + 1;
            var text = body[start..(newline < 0 ? body.Length : newline)].TrimEnd('\r');
            var fence = Fence().Match(text);
            if (inComment)
            {
                inComment = !text.Contains("-->");
                lines.Add(new(start, text, LineKind.Comment));
            }
            else if (fenceLength > 0)
            {
                if (fence.Success && fence.Groups[1].Value[0] == fenceChar && fence.Groups[1].Length >= fenceLength
                    && fence.Groups[2].Value.Trim().Length == 0)
                    fenceLength = 0;
                lines.Add(new(start, text, LineKind.Fenced));
            }
            else if (fence.Success && !(fence.Groups[1].Value[0] == '`' && fence.Groups[2].Value.Contains('`')))
            {
                fenceChar = fence.Groups[1].Value[0];
                fenceLength = fence.Groups[1].Length;
                lines.Add(new(start, text, LineKind.Fenced));
            }
            else if (Marker().Match(text) is { Success: true } marker)
                lines.Add(new(start, text, LineKind.Marker, Label: marker.Groups[1].Value));
            else
            {
                // An opener hides the following lines, but visible text before it
                // still classifies this line. Indented code opens nothing.
                var indent = text.Length - text.TrimStart(' ', '\t').Length;
                var opener = text.LastIndexOf("<!--", StringComparison.Ordinal);
                inComment = opener > text.LastIndexOf("-->", StringComparison.Ordinal)
                    && indent < 4 && !text[..indent].Contains('\t');
                if (Heading().Match(text) is { Success: true } heading)
                    lines.Add(new(start, text, LineKind.Heading, heading.Groups[1].Length,
                        Regex.Replace(heading.Groups[2].Value, @"(?:\A|[ \t]+)#+\z", "").Trim()));
                else if (inComment && text[..opener].Trim().Length == 0 || Comment().IsMatch(text))
                    lines.Add(new(start, text, LineKind.Comment));
                else
                    lines.Add(new(start, text, text.Trim().Length == 0 ? LineKind.Blank : LineKind.Text));
            }
            start = next;
        }
        return lines;
    }
}
