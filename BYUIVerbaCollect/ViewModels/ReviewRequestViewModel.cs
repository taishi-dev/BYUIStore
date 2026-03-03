using BYUIVerbaCollect.Models;

namespace BYUIVerbaCollect.ViewModels;

public class ReviewRequestViewModel
{
    public CourseRequest Request { get; set; } = null!;

    /// <summary>"verify" | "approve"</summary>
    public string Action { get; set; } = "verify";

    public string? RejectionNote { get; set; }
}
