using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CampusAdoptions.Models;

public enum ItemType { Book, Supply }

public class RequestItem
{
    public int Id { get; set; }

    public int CourseRequestId { get; set; }
    [ForeignKey(nameof(CourseRequestId))]
    public CourseRequest CourseRequest { get; set; } = null!;

    public ItemType ItemType { get; set; } = ItemType.Book;

    // ── Book Fields ────────────────────────────────────────────────────────
    [MaxLength(300)]
    public string? Title { get; set; }

    [MaxLength(200)]
    public string? Author { get; set; }

    /// <summary>Auto-populated from Open Library API — no manual entry needed.</summary>
    [MaxLength(20)]
    public string? Isbn { get; set; }

    [MaxLength(200)]
    public string? Publisher { get; set; }

    [MaxLength(50)]
    public string? Edition { get; set; }

    public int? PublicationYear { get; set; }

    // ── Supply Fields ──────────────────────────────────────────────────────
    [MaxLength(300)]
    public string? SupplyDescription { get; set; }

    // ── Common Fields ──────────────────────────────────────────────────────
    public int Quantity { get; set; } = 1;
    public bool IsRequired { get; set; } = true;

    [MaxLength(500)]
    public string? Notes { get; set; }

    // ── Removal Request ────────────────────────────────────────────────────
    /// <summary>
    /// Set when a professor requests removal of an already-approved/verified item.
    /// The item stays in the DB until the material manager processes the removal.
    /// </summary>
    public DateTime? RemovalRequestedAt { get; set; }

    // ── Polymorphic display methods (virtual — can be overridden) ──────────

    /// <summary>
    /// Returns the display title for this item.
    /// Override in derived classes to customise how items present themselves.
    /// </summary>
    public virtual string GetDisplayTitle() => ItemType switch
    {
        ItemType.Book   => Title ?? Isbn ?? "(untitled book)",
        ItemType.Supply => SupplyDescription ?? "(unnamed supply)",
        _               => "(unknown item)"
    };

    /// <summary>
    /// Returns a one-line summary suitable for lists, emails, and notifications.
    /// Override in derived classes for richer descriptions.
    /// </summary>
    public virtual string GetDisplaySummary()
    {
        if (ItemType == ItemType.Book)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Title))  parts.Add(Title);
            if (!string.IsNullOrWhiteSpace(Author)) parts.Add($"by {Author}");
            if (!string.IsNullOrWhiteSpace(Isbn))   parts.Add($"(ISBN: {Isbn})");
            return parts.Count > 0 ? string.Join(" ", parts) : "(untitled book)";
        }
        return SupplyDescription ?? "(unnamed supply)";
    }

    /// <summary>
    /// Returns a label indicating whether this item is required or optional.
    /// </summary>
    public virtual string GetRequirementLabel() =>
        IsRequired ? "Required" : "Optional";
}
