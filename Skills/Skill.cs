namespace MicroCode.Skills;

/// <summary>
/// Where a skill was discovered.
/// </summary>
public enum SkillSource
{
    /// <summary>Loaded from the user's global agents directory.</summary>
    Global,
    /// <summary>Loaded from the current working directory's agents folder.</summary>
    Local,
}

/// <summary>
/// Represents a single discovered skill and its parsed frontmatter metadata.
/// </summary>
/// <param name="Name">Skill name (from the frontmatter `name:` field).</param>
/// <param name="Description">Short description shown to the model.</param>
/// <param name="FilePath">Absolute path to the SKILL.md file.</param>
/// <param name="Source">Whether the skill came from the global or local directory.</param>
/// <param name="Metadata">Flattened metadata values (leaf-key to string value).</param>
public record Skill(
    string Name,
    string Description,
    string FilePath,
    SkillSource Source,
    IReadOnlyDictionary<string, string> Metadata);
