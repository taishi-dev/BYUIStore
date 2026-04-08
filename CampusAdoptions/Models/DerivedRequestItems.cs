namespace CampusAdoptions.Models;

/// <summary>
/// Book-specific request item — overrides display methods with book metadata
/// (title, author, edition, publisher, ISBN).
/// </summary>
public class BookRequestItem : RequestItem
{
    public BookRequestItem()
    {
        ItemType = ItemType.Book;
    }

    public override string GetDisplayTitle() =>
        !string.IsNullOrWhiteSpace(Title) ? Title : "Untitled Book";

    public override string GetDisplaySummary()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Author))    parts.Add(Author);
        if (!string.IsNullOrWhiteSpace(Edition))    parts.Add($"Ed. {Edition}");
        if (!string.IsNullOrWhiteSpace(Publisher))  parts.Add(Publisher);
        if (!string.IsNullOrWhiteSpace(Isbn))       parts.Add($"ISBN {Isbn}");
        return string.Join(" · ", parts);
    }

    public override string GetRequirementLabel() =>
        IsRequired ? "Required Textbook" : "Optional Textbook";
}

/// <summary>
/// Supply-specific request item — overrides display methods with supply details
/// (description, quantity).
/// </summary>
public class SupplyRequestItem : RequestItem
{
    public SupplyRequestItem()
    {
        ItemType = ItemType.Supply;
    }

    public override string GetDisplayTitle() =>
        !string.IsNullOrWhiteSpace(SupplyDescription) ? SupplyDescription : "Untitled Supply";

    public override string GetDisplaySummary() =>
        Quantity > 1 ? $"Quantity: {Quantity}" : "Quantity: 1";

    public override string GetRequirementLabel() =>
        IsRequired ? "Required Supply" : "Optional Supply";
}
