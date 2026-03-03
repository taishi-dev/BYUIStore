using BYUIVerbaCollect.Data;
using BYUIVerbaCollect.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Net;

namespace BYUIVerbaCollect.Services;

/// <summary>
/// Automation checklist for books used last semester:
///   ✔ Is it still available to buy? (Amazon + publisher link)
///   ✔ What does it cost? (Google Books saleInfo)
///   ✔ Is there a digital/eBook edition? (VitalSource search)
///   ✔ How many students are enrolled in the course?
///   ✔ Is the book required or optional?
/// </summary>
public class BookAvailabilityService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<BookAvailabilityService> _logger;

    public BookAvailabilityService(AppDbContext db, IHttpClientFactory httpFactory,
        ILogger<BookAvailabilityService> logger)
    {
        _db = db; _httpFactory = httpFactory; _logger = logger;
    }

    // ── Main entry: pull books from approved requests, check everything ────
    public async Task<List<BookAvailabilityItem>> GetReportAsync(string? semester = null)
    {
        // 1. Load all approved requests (optionally filtered by semester)
        var query = _db.CourseRequests
            .Include(r => r.Items)
            .Where(r => r.Status == RequestStatus.Approved);

        if (!string.IsNullOrEmpty(semester))
            query = query.Where(r => r.Semester == semester);

        var requests = await query.AsNoTracking().ToListAsync();

        // 2. Flatten to book items only (deduplicate by ISBN)
        var bookItems = requests
            .SelectMany(r => r.Items
                .Where(i => i.ItemType == ItemType.Book && !string.IsNullOrEmpty(i.Isbn))
                .Select(i => new { Request = r, Item = i }))
            .ToList();

        // Deduplicate — keep one row per ISBN per course
        var uniqueKeys = new HashSet<string>();
        var deduplicated = bookItems
            .Where(x => uniqueKeys.Add($"{x.Item.Isbn}|{x.Request.CourseNumber}"))
            .ToList();

        // 3. Get enrollment counts for all course numbers referenced
        var courseNumbers = deduplicated.Select(x => x.Request.CourseNumber).Distinct().ToList();
        var enrollmentCounts = await _db.Courses
            .Where(c => courseNumbers.Contains(c.CourseNumber))
            .Select(c => new { c.CourseNumber, Count = c.Enrollments.Count() })
            .ToListAsync();
        var enrollMap = enrollmentCounts.ToDictionary(e => e.CourseNumber, e => e.Count);

        // 4. For each book, check availability concurrently
        var http = _httpFactory.CreateClient("GoogleBooks");
        var tasks = deduplicated.Select(x => BuildItemAsync(x.Request, x.Item, enrollMap, http));
        var results = await Task.WhenAll(tasks);

        return results.OrderBy(r => r.CourseNumber).ThenBy(r => r.Title).ToList();
    }

    // ── Per-book availability check ────────────────────────────────────────
    private async Task<BookAvailabilityItem> BuildItemAsync(
        CourseRequest req, RequestItem item,
        Dictionary<string, int> enrollMap, HttpClient http)
    {
        var isbn = item.Isbn!.Replace("-", "").Trim();

        var result = new BookAvailabilityItem
        {
            Isbn         = isbn,
            Title        = item.Title        ?? "(unknown title)",
            Author       = item.Author       ?? "",
            Publisher    = item.Publisher    ?? "",
            Edition      = item.Edition      ?? "",
            IsRequired   = item.IsRequired,
            CourseNumber = req.CourseNumber,
            CourseName   = req.CourseName,
            Semester     = req.Semester,
            EnrollmentCount = enrollMap.TryGetValue(req.CourseNumber, out var cnt) ? cnt : 0,
            // Always-available deep-links (no API key needed)
            AmazonUrl      = $"https://www.amazon.com/s?k={Uri.EscapeDataString(isbn)}&i=stripbooks",
            VitalSourceUrl = $"https://www.vitalsource.com/search?term={Uri.EscapeDataString(isbn)}",
        };

        // Run price/digital checks in parallel
        await Task.WhenAll(
            CheckGoogleBooksAsync(isbn, result, http),
            CheckVitalSourceAsync(isbn, result, http)
        );

        return result;
    }

    // ── Google Books saleInfo ──────────────────────────────────────────────
    private async Task CheckGoogleBooksAsync(string isbn, BookAvailabilityItem item, HttpClient http)
    {
        try
        {
            var url  = $"https://www.googleapis.com/books/v1/volumes?q=isbn:{isbn}&maxResults=1";
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return;     // silent on 429

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
                return;

            var vol  = items[0];
            var info = vol.GetProperty("volumeInfo");
            var sale = vol.TryGetProperty("saleInfo", out var si) ? si : (JsonElement?)null;

            // Print availability
            if (sale.HasValue)
            {
                var saleability = Str(sale.Value, "saleability");  // "FOR_SALE", "FREE", "NOT_FOR_SALE"
                item.PrintAvailableOnGoogle = saleability is "FOR_SALE" or "FREE";

                if (sale.Value.TryGetProperty("listPrice", out var lp) &&
                    lp.TryGetProperty("amount", out var amt))
                    item.PrintListPrice = amt.GetDecimal();

                if (sale.Value.TryGetProperty("retailPrice", out var rp) &&
                    rp.TryGetProperty("amount", out var ra))
                    item.PrintRetailPrice = ra.GetDecimal();

                if (sale.Value.TryGetProperty("buyLink", out var bl))
                    item.GoogleBuyLink = bl.GetString();

                // eBook availability
                if (sale.Value.TryGetProperty("isEbook", out var eb) && eb.GetBoolean())
                {
                    item.EbookAvailableOnGoogle = true;
                    if (sale.Value.TryGetProperty("listPrice", out var elp) &&
                        elp.TryGetProperty("amount", out var ea))
                        item.EbookPrice = ea.GetDecimal();
                }
            }

            // Page count (useful for ordering)
            if (info.TryGetProperty("pageCount", out var pc)) item.PageCount = pc.GetInt32();

            // Cover thumbnail
            if (info.TryGetProperty("imageLinks", out var il) &&
                il.TryGetProperty("thumbnail", out var th))
                item.CoverThumbnailUrl = th.GetString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Books check failed for ISBN {Isbn}", isbn);
        }
    }

    // ── VitalSource digital availability ──────────────────────────────────
    private async Task CheckVitalSourceAsync(string isbn, BookAvailabilityItem item, HttpClient http)
    {
        try
        {
            var url = $"https://www.vitalsource.com/search?term={Uri.EscapeDataString(isbn)}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            req.Headers.Add("Accept", "text/html");

            var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return;

            var html  = await resp.Content.ReadAsStringAsync();
            // VitalSource returns 200 even for "not found" pages; check for a real product
            var hasProduct = html.Contains("vitalsource.com/products/", StringComparison.OrdinalIgnoreCase)
                          && !html.Contains("No results found", StringComparison.OrdinalIgnoreCase);

            item.DigitalAvailableOnVitalSource = hasProduct;

            // Try to extract VitalSource price from description
            var desc = ExtractOgContent(html, "og:description") ?? "";
            // VitalSource doesn't expose price in meta — note availability only
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VitalSource check failed for ISBN {Isbn}", isbn);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static string Str(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

    private static string? ExtractOgContent(string html, string property)
    {
        var pattern = $@"property=[""']{System.Text.RegularExpressions.Regex.Escape(property)}[""'][\s\S]*?content=[""']([\s\S]*?)[""']";
        var m = System.Text.RegularExpressions.Regex.Match(html, pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Replace("\n", " ").Trim();
    }

    // ── Available semester labels ─────────────────────────────────────────
    public async Task<List<string>> GetAvailableSemestersAsync() =>
        await _db.CourseRequests
            .Where(r => r.Status == RequestStatus.Approved)
            .Select(r => r.Semester)
            .Distinct()
            .OrderByDescending(s => s)
            .ToListAsync();
}

/// <summary>One book with all its availability data for the report.</summary>
public class BookAvailabilityItem
{
    // ── Book Identity ──────────────────────────────────────────────────────
    public string Isbn         { get; set; } = "";
    public string Title        { get; set; } = "";
    public string Author       { get; set; } = "";
    public string Publisher    { get; set; } = "";
    public string Edition      { get; set; } = "";

    // ── Course Info ────────────────────────────────────────────────────────
    public string CourseNumber    { get; set; } = "";
    public string CourseName      { get; set; } = "";
    public string Semester        { get; set; } = "";
    public bool   IsRequired      { get; set; }
    public int    EnrollmentCount { get; set; }

    // ── Print Availability (Google Books) ──────────────────────────────────
    public bool     PrintAvailableOnGoogle { get; set; }
    public decimal? PrintListPrice         { get; set; }
    public decimal? PrintRetailPrice       { get; set; }
    public string?  GoogleBuyLink          { get; set; }
    public string   AmazonUrl             { get; set; } = "";

    // ── Digital / eBook ────────────────────────────────────────────────────
    public bool     EbookAvailableOnGoogle       { get; set; }
    public decimal? EbookPrice                   { get; set; }
    public bool     DigitalAvailableOnVitalSource { get; set; }
    public string   VitalSourceUrl               { get; set; } = "";

    // ── Extra ──────────────────────────────────────────────────────────────
    public int?    PageCount         { get; set; }
    public string? CoverThumbnailUrl { get; set; }
}
