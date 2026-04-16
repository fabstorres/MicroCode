using System.Text;
using System.Text.RegularExpressions;

namespace MicroCode.Skills;

/// <summary>
/// Holds the merged set of skills from the global and local directories and
/// provides lookup helpers for system-prompt assembly and per-message matching.
/// </summary>
public class SkillRegistry
{
    private readonly IReadOnlyList<Skill> _skills;

    private SkillRegistry(IReadOnlyList<Skill> skills)
    {
        _skills = skills;
    }

    /// <summary>Gets all registered skills.</summary>
    public IReadOnlyList<Skill> Skills => _skills;

    /// <summary>
    /// Loads global skills from `$HOME/.agents/skills` then local skills from
    /// `<paramref name="cwd"/>/.agents/skills`. Local entries shadow global ones
    /// by case-insensitive <see cref="Skill.Name"/>.
    /// </summary>
    public static SkillRegistry Load(string cwd)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var globalRoot = Path.Combine(home, ".agents", "skills");
        var localRoot = Path.Combine(cwd, ".agents", "skills");

        var merged = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in SkillLoader.LoadFrom(globalRoot, SkillSource.Global))
        {
            merged[skill.Name] = skill;
        }
        foreach (var skill in SkillLoader.LoadFrom(localRoot, SkillSource.Local))
        {
            merged[skill.Name] = skill;
        }

        return new SkillRegistry(merged.Values
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    /// <summary>
    /// Builds a block to append to the system prompt. Returns an empty string
    /// when no skills are registered.
    /// </summary>
    public string BuildSystemPromptSection()
    {
        if (_skills.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("<available_skills>");
        sb.AppendLine("Skills are reusable instruction sets. The lines below list each skill by name, source, and absolute path to its SKILL.md. If the user's request matches one, read the file via the bash tool to load its instructions.");
        foreach (var s in _skills)
        {
            var src = s.Source == SkillSource.Local ? "local" : "global";
            sb.Append("- name: ").Append(s.Name)
              .Append(" | source: ").Append(src)
              .Append(" | path: ").AppendLine(s.FilePath);
            if (!string.IsNullOrWhiteSpace(s.Description))
            {
                sb.Append("  description: ").AppendLine(s.Description);
            }
        }
        sb.Append("</available_skills>");
        return sb.ToString();
    }

    /// <summary>
    /// Returns the skills whose name or metadata values occur as whole-word
    /// matches (case-insensitive) in <paramref name="userInput"/>.
    /// </summary>
    public IReadOnlyList<Skill> Match(string userInput)
    {
        if (_skills.Count == 0 || string.IsNullOrWhiteSpace(userInput))
        {
            return Array.Empty<Skill>();
        }

        var input = userInput.ToLowerInvariant();
        var matches = new List<Skill>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in _skills)
        {
            if (SkillMatches(skill, input) && seen.Add(skill.Name))
            {
                matches.Add(skill);
            }
        }

        return matches;
    }

    private static bool SkillMatches(Skill skill, string lowerInput)
    {
        var nameCandidates = new List<string> { skill.Name.ToLowerInvariant() };
        if (skill.Name.Contains('-'))
        {
            nameCandidates.Add(skill.Name.Replace('-', ' ').ToLowerInvariant());
        }

        foreach (var candidate in nameCandidates)
        {
            if (WholeWordContains(lowerInput, candidate))
            {
                return true;
            }
        }

        foreach (var (key, value) in skill.Metadata)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var tokens = key.Equals("tags", StringComparison.OrdinalIgnoreCase)
                ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : new[] { value.Trim() };

            foreach (var token in tokens)
            {
                if (token.Length == 0)
                {
                    continue;
                }
                if (WholeWordContains(lowerInput, token.ToLowerInvariant()))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool WholeWordContains(string haystack, string needle)
    {
        if (needle.Length == 0)
        {
            return false;
        }

        var pattern = $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(needle)}(?![\p{{L}}\p{{N}}_])";
        return Regex.IsMatch(haystack, pattern);
    }
}
