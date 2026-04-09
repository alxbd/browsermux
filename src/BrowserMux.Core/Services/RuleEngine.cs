using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using BrowserMux.Core.Models;

namespace BrowserMux.Core.Services;

public class RuleEngine
{
    private readonly IReadOnlyList<Rule> _rules;

    // Compiled regex cache: pattern → Regex instance
    private static readonly ConcurrentDictionary<string, Regex?> _regexCache = new();

    public RuleEngine(IReadOnlyList<Rule> rules) => _rules = rules;

    /// <summary>
    /// Returns the target browser name, or null if no rule matches.
    /// "_picker" = force showing the picker.
    /// </summary>
    public string? Match(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        foreach (var rule in _rules.Where(r => r.IsEnabled))
        {
            if (Matches(rule, uri))
                return rule.BrowserName;
        }

        return null;
    }

    /// <summary>
    /// Returns the target BrowserId by checking DomainRules first (highest priority),
    /// then classic Rules (via BrowserName). Returns null if no rule matches.
    /// </summary>
    public string? MatchToBrowserId(string url, IReadOnlyList<DomainRule> domainRules)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        // 1. DomainRules first (highest priority)
        foreach (var dr in domainRules)
        {
            if (MatchesDomainRule(dr, uri))
                return dr.BrowserId;
        }

        // 2. Classic rules (BrowserName used as BrowserId fallback)
        foreach (var rule in _rules.Where(r => r.IsEnabled))
        {
            if (Matches(rule, uri))
                return rule.BrowserName;
        }

        return null;
    }

    private static bool MatchesDomainRule(DomainRule rule, Uri uri) => rule.MatchType switch
    {
        RuleMatchType.Domain => MatchDomain(rule.Pattern, uri.Host),
        RuleMatchType.Glob   => MatchGlob(rule.Pattern, uri.Host),
        RuleMatchType.Regex  => MatchRegex(rule.Pattern, uri.ToString()),
        _ => false
    };

    private static bool Matches(Rule rule, Uri uri) => rule.MatchType switch
    {
        RuleMatchType.Domain => MatchDomain(rule.Pattern, uri.Host),
        RuleMatchType.Glob   => MatchGlob(rule.Pattern, uri.Host),
        RuleMatchType.Regex  => MatchRegex(rule.Pattern, uri.ToString()),
        _ => false
    };

    private static bool MatchDomain(string pattern, string host)
        => host.Equals(pattern, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + pattern, StringComparison.OrdinalIgnoreCase);

    private static bool MatchGlob(string pattern, string host)
    {
        // Convert glob to regex: * → .*, ? → .
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";
        return GetOrCreateRegex(regexPattern)?.IsMatch(host) ?? false;
    }

    private static bool MatchRegex(string pattern, string url)
    {
        return GetOrCreateRegex(pattern)?.IsMatch(url) ?? false;
    }

    private static Regex? GetOrCreateRegex(string pattern)
    {
        return _regexCache.GetOrAdd(pattern, p =>
        {
            try { return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled); }
            catch { return null; }
        });
    }
}
