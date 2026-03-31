namespace CampusAdoptions.Models;

public enum DiffType
{
    MaterialAdded,
    MaterialRemoved,
    MaterialChanged,        // Same course, different ISBN
    RequiredStatusChanged,  // Required ↔ Optional
    EditionChanged,         // Same title, different edition
    PriceIncreased          // Significant price jump
}

/// <summary>
/// Represents a single difference between the current semester's adoption
/// and a past semester's adoption for the same course.
/// </summary>
public class AdoptionDiff
{
    public DiffType Type { get; set; }
    public string CourseNumber { get; set; } = string.Empty;
    public string? Section { get; set; }
    public string CurrentSemester { get; set; } = string.Empty;
    public string PastSemester { get; set; } = string.Empty;

    // Current item (null for MaterialRemoved)
    public string? CurrentIsbn { get; set; }
    public string? CurrentTitle { get; set; }
    public string? CurrentEdition { get; set; }
    public bool? CurrentIsRequired { get; set; }

    // Past item (null for MaterialAdded)
    public string? PastIsbn { get; set; }
    public string? PastTitle { get; set; }
    public string? PastEdition { get; set; }
    public bool? PastIsRequired { get; set; }

    /// <summary>Human-readable summary of the change.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// The full result of comparing two semesters for a set of courses.
/// </summary>
public class AdoptionDiffResult
{
    public string CurrentSemester { get; set; } = string.Empty;
    public string PastSemester { get; set; } = string.Empty;
    public List<AdoptionDiff> Diffs { get; set; } = new();

    public int TotalChanges => Diffs.Count;
    public int MaterialsAdded => Diffs.Count(d => d.Type == DiffType.MaterialAdded);
    public int MaterialsRemoved => Diffs.Count(d => d.Type == DiffType.MaterialRemoved);
    public int MaterialsChanged => Diffs.Count(d => d.Type == DiffType.MaterialChanged);
    public int RequiredStatusChanges => Diffs.Count(d => d.Type == DiffType.RequiredStatusChanged);
    public int EditionChanges => Diffs.Count(d => d.Type == DiffType.EditionChanged);
}
