using BYUIVerbaCollect.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BYUIVerbaCollect.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class ApiController : ControllerBase
{
    private readonly IsbnLookupService _isbnSearch;
    private readonly IsbnDirectLookupService _isbnDirect;
    private readonly BookAvailabilityService _availService;

    public ApiController(IsbnLookupService isbnSearch, IsbnDirectLookupService isbnDirect,
        BookAvailabilityService availService)
    {
        _isbnSearch   = isbnSearch;
        _isbnDirect   = isbnDirect;
        _availService = availService;
    }

    /// <summary>
    /// GET /api/isbn-search?title=...&amp;author=...
    /// Title/author keyword search — returns up to 10 results from Open Library.
    /// </summary>
    [HttpGet("isbn-search")]
    public async Task<IActionResult> IsbnSearch([FromQuery] string title, [FromQuery] string? author = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { error = "Please provide a book title." });

        var results = await _isbnSearch.SearchAsync(title, author);
        return Ok(results);
    }

    /// <summary>
    /// GET /api/isbn-direct?isbn=...
    /// ISBN-first lookup:
    ///   1. Checks the local catalog (previously approved books) — instant, no network.
    ///   2. Falls back to Google Books API, then Open Library.
    /// Returns all book metadata so faculty never need to type it manually.
    /// </summary>
    [HttpGet("isbn-direct")]
    public async Task<IActionResult> IsbnDirect([FromQuery] string isbn)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            return BadRequest(new { error = "Please provide an ISBN." });

        var result = await _isbnDirect.LookupAsync(isbn);

        if (result is null)
            return NotFound(new { error = $"No book found for ISBN '{isbn}'. Please verify the number." });

        return Ok(result);
    }

    /// <summary>
    /// GET /api/check-availability?isbn=...
    /// Checks Google Books (price/eBook) and VitalSource (digital match) for one ISBN.
    /// Used by the "ADD MATERIALS" tab on the Details page.
    /// </summary>
    [HttpGet("check-availability")]
    public async Task<IActionResult> CheckAvailability([FromQuery] string isbn)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            return BadRequest(new { error = "ISBN required." });

        var result = await _availService.CheckSingleIsbnAsync(isbn.Trim());
        return Ok(new
        {
            digitalVitalSource = result.DigitalAvailableOnVitalSource,
            digitalGoogle      = result.EbookAvailableOnGoogle,
            googlePrice        = result.EbookPrice,
            printPrice         = result.PrintRetailPrice ?? result.PrintListPrice,
            amazonUrl          = result.AmazonUrl,
            vitalsourceUrl     = result.VitalSourceUrl,
            googleBuyLink      = result.GoogleBuyLink,
            coverThumbnail     = result.CoverThumbnailUrl
        });
    }

    /// <summary>
    /// GET /api/book-checklist?isbn=...&amp;courseNumber=...&amp;isRequired=...&amp;requestId=...
    /// Runs the full 4-point automated review checklist for one book:
    ///   1. Still available to buy (Amazon + Google)
    ///   2. Price (flagged if &gt; $100 → suggest contacting professor)
    ///   3. Digital availability (VitalSource first, then Google Books)
    ///   4. Required/Optional change detection vs previous semester
    /// Called automatically when the Verify or Approve page loads.
    /// </summary>
    [HttpGet("book-checklist")]
    public async Task<IActionResult> BookChecklist(
        [FromQuery] string isbn,
        [FromQuery] string? courseNumber = null,
        [FromQuery] bool isRequired = true,
        [FromQuery] int? requestId = null)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            return BadRequest(new { error = "ISBN required." });

        var r = await _availService.CheckBookChecklistAsync(
            isbn.Trim(), courseNumber, isRequired, requestId);

        return Ok(new
        {
            isbn                = r.Isbn,
            printAvailable      = r.PrintAvailable,
            printPrice          = r.PrintPrice,
            priceFlagged        = r.PriceFlagged,
            digitalOnVitalSource = r.DigitalOnVitalSource,
            digitalOnGoogle     = r.DigitalOnGoogle,
            vitalSourceUrl      = r.VitalSourceUrl,
            amazonUrl           = r.AmazonUrl,
            googleBuyLink       = r.GoogleBuyLink,
            coverThumbnail      = r.CoverThumbnail,
            requiredChanged     = r.RequiredChanged,
            previousIsRequired  = r.PreviousIsRequired,
            previousSemester    = r.PreviousSemester
        });
    }
}
