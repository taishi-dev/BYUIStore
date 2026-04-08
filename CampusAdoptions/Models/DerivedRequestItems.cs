namespace CampusAdoptions.Models;

/// <summary>
/// Represents a textbook item within a course materials request.
/// Overrides base RequestItem display methods to include book-specific details
/// such as author, ISBN, publisher, and edition.
/// </summary>
public class BookRequestItem : RequestItem
{
    public BookRequestItem()
    {
        ItemType = ItemType.Book;
    }

    /// <summary>
    /// Returns the book title, falling back to ISBN if title is missing.
    /// </summary>
    public override string GetDisplayTitle() =>
        !string.IsNullOrWhiteSpace(Title)
            ? Title
            : !string.IsNullOrWhiteSpace(Isbn)
                ? $"ISBN: {Isbn}"
                : "(untitled book)";

    /// <summary>
    /// Returns a rich summary including title, author, ISBN, and edition.
    /// </summary>
    public override string GetDisplaySummary()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(Title))     parts.Add(Title);
        if (!string.IsNullOrWhiteSpace(Author))    parts.Add($"by {Author}");
        if (!string.IsNullOrWhiteSpace(Edition))   parts.Add($"({Edition} ed.)");
        if (!string.IsNullOrWhiteSpace(Publisher)) parts.Add($"[{Publisher}]");
        if (!string.IsNullOrWhiteSpace(Isbn))      parts.Add($"ISBN: {Isbn}");

        return parts.Count > 0 ? string.Join(" ", parts) : "(untitled book)";
    }

    /// <summary>
    /// Books show "Required Textbook" or "Optional Textbook" for clarity.
    /// </summary>
    public override string GetRequirementLabel() =>
        IsRequired ? "Required Textbook" : "Optional Textbook";
}

/// <summary>
/// Represents a supply/non-book item within a course materials request.
/// Overrides base RequestItem display methods to show supply-specific details.
/// </summary>
public class SupplyRequestItem : RequestItem
{
    public SupplyRequestItem()
    {
        ItemType = ItemType.Supply;
    }

    /// <summary>
    /// Returns the supply description as the display title.
    /// </summary>
    public override string GetDisplayTitle() =>
        !string.IsNullOrWhiteSpace(SupplyDescription)
            ? SupplyDescription
            : "(unnamed supply)";

    /// <summary>
    /// Supplies show their description and quantity.
    /// </summary>
    public override string GetDisplaySummary()
    {
        var desc = SupplyDescription ?? "(unnamed supply)";
        return Quantity > 1 ? $"{desc} (×{Quantity})" : desc;
    }

    /// <summary>
    /// Supplies show "Required Supply" or "Optional Supply".
    /// </summary>
    public override string GetRequirementLabel() =>
        IsRequired ? "Required Supply" : "Optional Supply";
}
