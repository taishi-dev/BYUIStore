namespace CampusAdoptions.Models.Verba;

public class VerbaCourse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public int SectionCount { get; set; }
    public bool IsCurrentSinceExported { get; set; }
    public bool IsPushed { get; set; }
    public bool NoTextRequired { get; set; }
    public List<Section> Sections { get; set; } = new();
}
