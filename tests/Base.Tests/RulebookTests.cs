using System.Reflection;
using System.Text.RegularExpressions;

namespace Base.Tests;

/// <summary>The rulebook itself: numbers are well formed and unique, every test it names exists, and every number cited in the code is a rule.</summary>
public partial class RulebookTests
{
    private static readonly string[] Areas = ["CODE", "DATA", "AUTH", "SEC", "OPS", "SHELL", "FORM", "LIST", "DIALOG", "COLOR", "MOBILE", "A11Y", "I18N", "KIT", "TEST"];

    [GeneratedRegex(@"^- \*\*([A-Z0-9]+-\d\d) \((MUST|SHOULD|MAY)\)\*\* (.+)$", RegexOptions.Multiline)]
    private static partial Regex RuleLine();

    [GeneratedRegex(@"`([A-Za-z]+Tests\.[A-Za-z0-9_]+)`")]
    private static partial Regex TestName();

    private static IReadOnlyList<(string Id, string Level, string Text)> Rules() =>
        [.. RuleLine().Matches(Repo.Read("docs", "RULES.md")).Select(m => (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value))];

    [Fact]
    public void Rule_numbers_are_unique_well_formed_and_every_rule_says_why()
    {
        var rules = Rules();
        Assert.InRange(rules.Count, 30, 60);
        Assert.Equal(rules.Count, rules.Select(r => r.Id).Distinct().Count());
        Assert.All(rules, r => Assert.Contains(r.Id.Split('-')[0], Areas));
        Assert.All(rules, r => Assert.Contains("*Why:*", r.Text));
        // every line that looks like a rule is one (a typo in the format would silently drop it)
        Assert.Equal(Regex.Matches(Repo.Read("docs", "RULES.md"), @"^- \*\*", RegexOptions.Multiline).Count, rules.Count);
        // within an area the numbers run 01, 02, 03 ... without a gap
        foreach (var area in rules.GroupBy(r => r.Id.Split('-')[0]))
            Assert.Equal(Enumerable.Range(1, area.Count()).Select(n => $"{area.Key}-{n:00}"), area.Select(r => r.Id));
    }

    [Fact]
    public void Every_test_named_in_the_rulebook_exists()
    {
        var tests = typeof(RulebookTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.GetCustomAttribute<FactAttribute>() is not null).Select(m => $"{t.Name}.{m.Name}"))
            .ToHashSet();
        var named = Rules().SelectMany(r => TestName().Matches(r.Text).Select(m => (r.Id, Test: m.Groups[1].Value))).ToArray();
        Assert.True(named.Length >= 40, "the rulebook names too few tests: did its format change?");
        var missing = named.Where(n => !tests.Contains(n.Test)).Select(n => $"{n.Id}: {n.Test}").ToArray();
        Assert.True(missing.Length == 0, "tests named in docs/RULES.md that do not exist:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Every_rule_number_cited_in_the_code_is_a_rule()
    {
        var ids = Rules().Select(r => r.Id).ToHashSet();
        var cite = new Regex(@"\b(" + string.Join('|', Areas) + @")-\d\d\b");
        var unknown = new List<string>();
        foreach (var file in RuleFileTests.Files(["src", "ui", "samples", "scripts", "templates", "tests", ".github"], "*"))
            foreach (Match m in cite.Matches(File.ReadAllText(file)))
                if (!ids.Contains(m.Value)) unknown.Add($"{Path.GetRelativePath(Repo.Root, file)}: {m.Value}");
        foreach (var name in new[] { "README.md", "CLAUDE.md", "AGENTS.md" })
            foreach (Match m in cite.Matches(Repo.Read(name)))
                if (!ids.Contains(m.Value)) unknown.Add($"{name}: {m.Value}");
        Assert.True(unknown.Count == 0, "rule numbers that are not in docs/RULES.md:\n" + string.Join("\n", unknown.Distinct()));
    }
}
