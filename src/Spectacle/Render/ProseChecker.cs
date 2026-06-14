using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spectacle.Render;

/// <summary>One vague or hedging phrase found in a spec, with its 1-based line.</summary>
public sealed record ProseFinding(string Rule, int Line, string Phrase, string Message);

/// <summary>
/// Advisory prose check for AI-authored specs: it flags the hedging and vague filler
/// language that is the signature defect of agent output — wording that <em>looks</em>
/// like a requirement but commits to nothing, so a reader (or the next agent) cannot tell
/// what to build. Three rules:
/// <list type="bullet">
///   <item><c>hedge</c> — uncertainty that signals an undecided spec
///     (<c>should probably</c>, <c>may need to</c>, <c>perhaps</c>).</item>
///   <item><c>weasel</c> — vague fillers and open-ended quantifiers with no concrete
///     meaning (<c>etc.</c>, <c>and so on</c>, <c>various</c>, <c>a number of</c>).</item>
///   <item><c>vague-directive</c> — instructions that defer the actual decision
///     (<c>as appropriate</c>, <c>where applicable</c>, <c>to be determined</c>).</item>
/// </list>
///
/// Unlike every other check, this one is <strong>advisory</strong>: it never gates a
/// pipeline (always exits 0). Hedging is a judgement call — a curated, deliberately tight
/// word list keeps the noise low, but a confident exit code would be wrong for advice, so
/// it follows the same report-don't-fail precedent as <see cref="FenceChecker"/>'s
/// <c>no-language</c> rule. Fenced code is skipped (a code sample saying <c>etc.</c> is not
/// spec prose), mirroring <see cref="SpecLinter"/>'s placeholder scan.
/// </summary>
public static class ProseChecker
{
    public const string HedgeRule = "hedge";
    public const string WeaselRule = "weasel";
    public const string VagueDirectiveRule = "vague-directive";

    // Each entry pairs a rule with a canonical phrase label and the pattern that matches it.
    // The list is intentionally conservative: multi-word phrases and unambiguous fillers,
    // not common single words ("many", "often", "some") that have legitimate spec uses.
    private static readonly (string Rule, string Phrase, Regex Pattern)[] Phrases = Build();

    public static IReadOnlyList<ProseFinding> Check(string? markdown)
    {
        var lines = (markdown ?? string.Empty).Split('\n');
        var findings = new List<ProseFinding>();
        var inCodeFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                inCodeFence = !inCodeFence;
                continue;
            }
            if (inCodeFence) continue;

            foreach (var (rule, phrase, pattern) in Phrases)
            {
                if (pattern.IsMatch(lines[i]))
                    findings.Add(new ProseFinding(rule, i + 1, phrase, MessageFor(rule, phrase)));
            }
        }

        // A phrase that appears more than once on the same line is one finding, not several;
        // order by line, then by the order the phrase appears in the curated list above.
        return findings
            .GroupBy(f => (f.Line, f.Phrase))
            .Select(g => g.First())
            .OrderBy(f => f.Line)
            .ToList();
    }

    private static string MessageFor(string rule, string phrase) => rule switch
    {
        HedgeRule => $"hedging language '{phrase}'",
        WeaselRule => $"vague wording '{phrase}'",
        VagueDirectiveRule => $"non-actionable directive '{phrase}'",
        _ => $"vague language '{phrase}'",
    };

    private static (string, string, Regex)[] Build()
    {
        const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Compiled;
        (string Rule, string Phrase, string Pattern)[] specs =
        {
            (HedgeRule, "should probably", @"\bshould probably\b"),
            (HedgeRule, "might want to", @"\bmight want to\b"),
            (HedgeRule, "may want to", @"\bmay want to\b"),
            (HedgeRule, "may need to", @"\bmay need to\b"),
            (HedgeRule, "could potentially", @"\bcould potentially\b"),
            (HedgeRule, "perhaps", @"\bperhaps\b"),
            (HedgeRule, "presumably", @"\bpresumably\b"),
            (HedgeRule, "arguably", @"\barguably\b"),
            (HedgeRule, "hopefully", @"\bhopefully\b"),
            (HedgeRule, "more or less", @"\bmore or less\b"),
            (HedgeRule, "sort of", @"\bsort of\b"),
            (HedgeRule, "kind of", @"\bkind of\b"),

            (WeaselRule, "etc.", @"\betc\.?(?![a-z])"),
            (WeaselRule, "and so on", @"\band so on\b"),
            (WeaselRule, "and/or", @"\band/or\b"),
            (WeaselRule, "various", @"\bvarious\b"),
            (WeaselRule, "a number of", @"\ba number of\b"),
            (WeaselRule, "a couple of", @"\ba couple of\b"),
            (WeaselRule, "and the like", @"\band the like\b"),
            (WeaselRule, "among other things", @"\bamong other things\b"),

            (VagueDirectiveRule, "as appropriate", @"\bas appropriate\b"),
            (VagueDirectiveRule, "as needed", @"\bas needed\b"),
            (VagueDirectiveRule, "as necessary", @"\bas necessary\b"),
            (VagueDirectiveRule, "as required", @"\bas required\b"),
            (VagueDirectiveRule, "where applicable", @"\bwhere applicable\b"),
            (VagueDirectiveRule, "if applicable", @"\bif applicable\b"),
            (VagueDirectiveRule, "handle accordingly", @"\bhandle\w* accordingly\b"),
            (VagueDirectiveRule, "to be determined", @"\bto be determined\b"),
            (VagueDirectiveRule, "to be defined", @"\bto be defined\b"),
        };

        return specs.Select(s => (s.Rule, s.Phrase, new Regex(s.Pattern, Opts))).ToArray();
    }
}
