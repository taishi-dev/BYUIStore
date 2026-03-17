using BYUIVerbaCollect.Models;

namespace BYUIVerbaCollect.ViewModels;

public class CourseMaterialsViewModel
{
    public string CourseNumber    { get; set; } = "";
    public string CourseName      { get; set; } = "";
    public string CurrentSemester { get; set; } = "Spring 2026";

    /// <summary>The current semester's request (SELECTED MATERIALS tab). Null if not yet submitted.</summary>
    public CourseRequest? CurrentRequest { get; set; }

    /// <summary>Previous approved requests for the same course (COPY ANOTHER ADOPTION tab).</summary>
    public List<CourseRequest> PriorAdoptions { get; set; } = new();

    /// <summary>Which tab is active: "selected" | "copy" | "add" | "messages"</summary>
    public string ActiveTab { get; set; } = "selected";
}
