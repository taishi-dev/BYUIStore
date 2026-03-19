using System.ComponentModel.DataAnnotations;

namespace CampusAdoptions.ViewModels;

public class SubmitRequestViewModel
{
    [Required(ErrorMessage = "Course name is required.")]
    [MaxLength(200)]
    [Display(Name = "Course Name")]
    public string CourseName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Course number is required.")]
    [MaxLength(50)]
    [Display(Name = "Course Number")]
    public string CourseNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Semester is required.")]
    [MaxLength(50)]
    public string Semester { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Section { get; set; }

    public List<ItemViewModel> Items { get; set; } = new();
}

public class ItemViewModel
{
    /// <summary>"Book" or "Supply"</summary>
    [Required]
    public string ItemType { get; set; } = "Book";

    // ── Book ──────────────────────────────────────────────────────────────
    public string? Title       { get; set; }
    public string? Author      { get; set; }

    /// <summary>Auto-filled by Open Library search — no manual ISBN entry needed.</summary>
    public string? Isbn        { get; set; }
    public string? Publisher   { get; set; }
    public string? Edition     { get; set; }
    public int?    PublicationYear { get; set; }

    // ── Supply ────────────────────────────────────────────────────────────
    public string? SupplyDescription { get; set; }

    // ── Common ────────────────────────────────────────────────────────────
    [Range(1, 9999)]
    public int  Quantity   { get; set; } = 1;
    public bool IsRequired { get; set; } = true;
    public string? Notes   { get; set; }
}
