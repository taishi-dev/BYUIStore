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

    public ApiController(IsbnLookupService isbnSearch, IsbnDirectLookupService isbnDirect)
    {
        _isbnSearch = isbnSearch;
        _isbnDirect = isbnDirect;
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
}
