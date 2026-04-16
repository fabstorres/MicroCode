namespace MicroCode.Skills;

/// <summary>
/// Discovers SKILL.md files under a root directory and parses their frontmatter.
/// </summary>
public static class SkillLoader
{
    /// <summary>
    /// Enumerates `*/SKILL.md` files one directory level under <paramref name="root"/>
    /// and returns successfully-parsed skills. Missing or unreadable roots yield an empty list.
    /// </summary>
    public static IEnumerable<Skill> LoadFrom(string root, SkillSource source)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            yield break;
        }

        IEnumerable<string> skillDirs;
        try
        {
            skillDirs = Directory.EnumerateDirectories(root);
        }
        catch
        {
            yield break;
        }

        foreach (var dir in skillDirs)
        {
            var path = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(path))
            {
                continue;
            }

            Skill? skill = null;
            try
            {
                skill = ParseFile(path, source);
            }
            catch
            {
                // malformed skills are silently skipped
            }

            if (skill is not null)
            {
                yield return skill;
            }
        }
    }

    private static Skill? ParseFile(string path, SkillSource source)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return null;
        }

        int end = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            return null;
        }

        string? name = null;
        string description = "";
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        bool inMetadata = false;
        int metadataIndent = -1;
        string? pendingListKey = null;
        var pendingListValues = new List<string>();

        void FlushPendingList()
        {
            if (pendingListKey is not null)
            {
                metadata[pendingListKey] = string.Join(", ", pendingListValues);
                pendingListKey = null;
                pendingListValues.Clear();
            }
        }

        for (int i = 1; i < end; i++)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            int indent = 0;
            while (indent < raw.Length && raw[indent] == ' ')
            {
                indent++;
            }
            var trimmed = raw[indent..];

            if (pendingListKey is not null && trimmed.StartsWith("- "))
            {
                pendingListValues.Add(Unquote(trimmed[2..].Trim()));
                continue;
            }
            FlushPendingList();

            if (inMetadata && indent <= metadataIndent)
            {
                inMetadata = false;
            }

            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx <= 0)
            {
                continue;
            }

            var key = trimmed[..colonIdx].Trim();
            var value = colonIdx + 1 < trimmed.Length
                ? trimmed[(colonIdx + 1)..].Trim()
                : "";

            if (!inMetadata && key.Equals("metadata", StringComparison.OrdinalIgnoreCase) && value.Length == 0)
            {
                inMetadata = true;
                metadataIndent = indent;
                continue;
            }

            if (!inMetadata)
            {
                if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    name = Unquote(value);
                }
                else if (key.Equals("description", StringComparison.OrdinalIgnoreCase))
                {
                    description = Unquote(value);
                }
                else if (value.Length > 0)
                {
                    metadata[key] = ParseInlineValue(value);
                }
                else
                {
                    pendingListKey = key;
                }
            }
            else
            {
                if (value.Length > 0)
                {
                    metadata[key] = ParseInlineValue(value);
                }
                else
                {
                    pendingListKey = key;
                }
            }
        }
        FlushPendingList();

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new Skill(name!, description, Path.GetFullPath(path), source, metadata);
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[^1] == '"') ||
                (value[0] == '\'' && value[^1] == '\''))
            {
                return value[1..^1];
            }
        }
        return value;
    }

    private static string ParseInlineValue(string value)
    {
        var v = value.Trim();
        if (v.StartsWith('[') && v.EndsWith(']'))
        {
            var inner = v[1..^1];
            var parts = inner
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Unquote);
            return string.Join(", ", parts);
        }
        return Unquote(v);
    }
}
