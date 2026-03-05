using BYUIVerbaCollect.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Net;

namespace BYUIVerbaCollect.Services;

/// <summary>
/// ISBN-first lookup with 4-tier fallback:
///   1. Local DB catalog (previously approved books) – instant, no network.
///   2. Open Library ISBN API            – free, no key, good for academic titles.
///   3. Google Books API                 – fast when not rate-limited.
///   4. VitalSource catalog page scrape  – handles digital-only ISBNs.
/// If all sources fail, returns null so the user can fill in manually.
/// </summary>
public class IsbnDirectLookupService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<IsbnDirectLookupService> _logger;

    public IsbnDirectLookupService(
        AppDbContext db,
        IHttpClientFactory httpFactory,
        ILogger<IsbnDirectLookupService> logger)
    {
        _db          = db;
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    public async Task<IsbnLookupResult?> LookupAsync(string isbn)
    {
        isbn = isbn.Trim().Replace("-", "").Replace(" ", "");
        if (string.IsNullOrEmpty(isbn)) return null;

        // ── 1. Local DB cache ─────────────────────────────────────────────
        var cached = await _db.CourseBookAssignments
            .AsNoTracking()
            .Where(b => b.Isbn == isbn)
            .FirstOrDefaultAsync();

        if (cached != null)
        {
            _logger.LogInformation("ISBN {Isbn} found in local catalog.", isbn);
            return new IsbnLookupResult
            {
                Isbn = cached.Isbn, Title = cached.Title ?? "", Author = cached.Author ?? "",
                Publisher = cached.Publisher ?? "", Edition = cached.Edition ?? "",
                Year = cached.PublicationYear, FromLocalCache = true
            };
        }

        var http = _httpFactory.CreateClient("GoogleBooks");

        // ── 2. Open Library (best for academic/textbooks, no rate limits) ─
        var olResult = await TryOpenLibraryAsync(isbn, http);
        if (olResult != null && !string.IsNullOrEmpty(olResult.Title))
            return olResult;

        // ── 3. Google Books ──────────────────────────────────────────────
        var gbResult = await TryGoogleBooksAsync(isbn, http);
        if (gbResult != null && !string.IsNullOrEmpty(gbResult.Title))
            return gbResult;

        // ── 4. VitalSource catalog page ──────────────────────────────────
        var vsResult = await TryVitalSourceAsync(isbn, http);
        if (vsResult != null && !string.IsNullOrEmpty(vsResult.Title))
            return vsResult;

        // ── Nothing found – return null so user can fill manually ─────────
        _logger.LogWarning("ISBN {Isbn} not found in any external source.", isbn);
        return null;
    }

    // ── Open Library ─────────────────────────────────────────────────────
    private async Task<IsbnLookupResult?> TryOpenLibraryAsync(string isbn, HttpClient http)
    {
        try
        {
            var url  = $"https://openlibrary.org/api/books?bibkeys=ISBN:{isbn}&format=json&jscmd=data";
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json) || json == "{}") return null;

            using var doc = JsonDocument.Parse(json);
            var key = $"ISBN:{isbn}";
            if (!doc.RootElement.TryGetProperty(key, out var book)) return null;

            var title = Str(book, "title");

            var author = "";
            if (book.TryGetProperty("authors", out var authors))
                author = string.Join(", ", authors.EnumerateArray()
                    .Select(a => a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""));

            var publisher = "";
            if (book.TryGetProperty("publishers", out var pubs) && pubs.GetArrayLength() > 0)
                publisher = pubs[0].TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "";

            int? year = null;
            if (book.TryGetProperty("publish_date", out var pd))
            {
                var m = System.Text.RegularExpressions.Regex.Match(pd.GetString() ?? "", @"\d{4}");
                if (m.Success) year = int.Parse(m.Value);
            }

            // Open Library cover image
            var cover = "";
            if (book.TryGetProperty("cover", out var cov))
            {
                if (cov.TryGetProperty("large", out var lg)) cover = lg.GetString() ?? "";
                else if (cov.TryGetProperty("medium", out var md)) cover = md.GetString() ?? "";
            }

            return new IsbnLookupResult
            {
                Isbn = isbn, Title = title, Author = author,
                Publisher = publisher, Year = year,
                CoverThumbnail = cover, Source = "Open Library"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open Library lookup failed for ISBN {Isbn}", isbn);
            return null;
        }
    }

    // ── Google Books ─────────────────────────────────────────────────────
    private async Task<IsbnLookupResult?> TryGoogleBooksAsync(string isbn, HttpClient http)
    {
        try
        {
            var url  = $"https://www.googleapis.com/books/v1/volumes?q=isbn:{isbn}&maxResults=1";
            var resp = await http.GetAsync(url);

            if (resp.StatusCode == HttpStatusCode.TooManyRequests || !resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google Books returned {Status} for ISBN {Isbn}", resp.StatusCode, isbn);
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
                return null;

            var info = items[0].GetProperty("volumeInfo");

            var title  = Str(info, "title");
            var author = "";
            if (info.TryGetProperty("authors", out var authors))
                author = string.Join(", ", authors.EnumerateArray().Select(a => a.GetString() ?? ""));

            var publisher = Str(info, "publisher");
            int? year = null;
            if (info.TryGetProperty("publishedDate", out var pd))
            {
                var ds = pd.GetString() ?? "";
                if (ds.Length >= 4 && int.TryParse(ds[..4], out int y)) year = y;
            }

            // Prefer ISBN-13
            var bestIsbn = isbn;
            if (info.TryGetProperty("industryIdentifiers", out var ids))
                foreach (var id in ids.EnumerateArray())
                    if (id.TryGetProperty("type", out var tp) && tp.GetString() == "ISBN_13")
                    { bestIsbn = id.GetProperty("identifier").GetString() ?? isbn; break; }

            // Cover thumbnail
            var cover = "";
            if (info.TryGetProperty("imageLinks", out var imgLinks))
            {
                if (imgLinks.TryGetProperty("thumbnail", out var th))       cover = th.GetString() ?? "";
                else if (imgLinks.TryGetProperty("smallThumbnail", out var st)) cover = st.GetString() ?? "";
            }

            return new IsbnLookupResult
            {
                Isbn = bestIsbn, Title = title, Author = author,
                Publisher = publisher, Year = year,
                CoverThumbnail = cover, Source = "Google Books"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Books lookup failed for ISBN {Isbn}", isbn);
            return null;
        }
    }

    // ── VitalSource two-step scrape ───────────────────────────────────────
    // Step 1: GET /search?term={isbn}  → find first /products/... href
    // Step 2: GET /products/...        → extract OG tags + JSON-LD for full metadata
    private async Task<IsbnLookupResult?> TryVitalSourceAsync(string isbn, HttpClient http)
    {
        try
        {
            // ── Step 1: search page → product URL ─────────────────────────
            var searchHtml = await FetchHtmlAsync(
                $"https://www.vitalsource.com/search?term={Uri.EscapeDataString(isbn)}", http);
            if (searchHtml == null) return null;

            var productPath = System.Text.RegularExpressions.Regex.Match(
                searchHtml,
                @"href=""(/products/[^""?#]+)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Groups[1].Value;

            if (string.IsNullOrEmpty(productPath))
            {
                _logger.LogWarning("VitalSource: no product link found for ISBN {Isbn}", isbn);
                return null;
            }

            // ── Step 2: fetch product detail page ─────────────────────────
            var productHtml = await FetchHtmlAsync(
                "https://www.vitalsource.com" + productPath, http);
            if (productHtml == null) return null;

            // Title from og:title
            var title = ExtractOgContent(productHtml, "og:title") ?? "";
            title = System.Text.RegularExpressions.Regex.Replace(title,
                @"\s*\|[^|]*\|\s*VitalSource.*$", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(title)) return null;

            // Description: "Title is written by Author and published by Publisher."
            var desc = ExtractOgContent(productHtml, "og:description") ?? "";

            var author = "";
            var authorM = System.Text.RegularExpressions.Regex.Match(desc,
                @"written by\s+(.+?)\s+(?:and\s+published by|\.)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (authorM.Success) author = authorM.Groups[1].Value.Trim();

            var publisher = "";
            var pubM = System.Text.RegularExpressions.Regex.Match(desc,
                @"published by\s+([^\.]+)\.",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (pubM.Success) publisher = pubM.Groups[1].Value.Trim();

            var edition = "";
            var edM = System.Text.RegularExpressions.Regex.Match(desc,
                @"(\d+(?:st|nd|rd|th)\s+edition)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (edM.Success) edition = edM.Groups[1].Value.Trim();

            // Year — JSON-LD first, then copyright patterns
            int? year = null;
            var ym = System.Text.RegularExpressions.Regex.Match(productHtml,
                @"""datePublished""\s*:\s*""(\d{4})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (ym.Success) year = int.Parse(ym.Groups[1].Value);

            if (!year.HasValue)
            {
                ym = System.Text.RegularExpressions.Regex.Match(productHtml,
                    @"""copyrightYear""\s*:\s*(\d{4})",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (ym.Success) year = int.Parse(ym.Groups[1].Value);
            }
            if (!year.HasValue)
            {
                ym = System.Text.RegularExpressions.Regex.Match(productHtml,
                    @"(?:©|&copy;|Copyright)\s*(19\d{2}|20[0-3]\d)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (ym.Success) year = int.Parse(ym.Groups[1].Value);
            }
            if (!year.HasValue)
            {
                ym = System.Text.RegularExpressions.Regex.Match(desc,
                    @"\b(19\d{2}|20[0-3]\d)\b");
                if (ym.Success) year = int.Parse(ym.Groups[1].Value);
            }

            // Cover from og:image
            var cover = ExtractOgContent(productHtml, "og:image") ?? "";

            _logger.LogInformation(
                "VitalSource found ISBN {Isbn}: {Title} by {Author} ({Year})",
                isbn, title, author, year);

            return new IsbnLookupResult
            {
                Isbn                 = isbn,
                Title                = title,
                Author               = author,
                Publisher            = string.IsNullOrEmpty(publisher) ? "VitalSource" : publisher,
                Edition              = edition,
                Year                 = year,
                CoverThumbnail       = cover,
                DigitalOnVitalSource = true,   // found on VitalSource = digital confirmed
                Source               = "VitalSource"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VitalSource lookup failed for ISBN {Isbn}", isbn);
            return null;
        }
    }

    // ── Shared HTML fetcher ───────────────────────────────────────────────
    private static async Task<string?> FetchHtmlAsync(string url, HttpClient http)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        req.Headers.Add("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
        req.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        var resp = await http.SendAsync(req);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync() : null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static string? ExtractOgContent(string html, string property)
    {
        var pattern = $@"property=[""']{System.Text.RegularExpressions.Regex.Escape(property)}[""'][\s\S]*?content=[""']([\s\S]*?)[""']";
        var m = System.Text.RegularExpressions.Regex.Match(html, pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            pattern = $@"content=[""']([\s\S]*?)[""'][\s\S]*?property=[""']{System.Text.RegularExpressions.Regex.Escape(property)}[""']";
            m = System.Text.RegularExpressions.Regex.Match(html, pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        if (!m.Success) return null;
        var val = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
        return val.Replace("\n", " ").Replace("\r", "").Trim();
    }

    private static string Str(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

    private static string? ExtractH1Clean(string html)
    {
        var m = System.Text.RegularExpressions.Regex.Match(html,
            @"<h1[^>]*>\s*([\s\S]*?)\s*</h1>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var raw = m.Groups[1].Value;
        raw = System.Text.RegularExpressions.Regex.Replace(raw, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(raw.Trim());
    }
}

public class IsbnLookupResult
{
    public string  Isbn                 { get; set; } = string.Empty;
    public string  Title                { get; set; } = string.Empty;
    public string  Author               { get; set; } = string.Empty;
    public string  Publisher            { get; set; } = string.Empty;
    public string  Edition              { get; set; } = string.Empty;
    public int?    Year                 { get; set; }
    /// <summary>Book cover thumbnail URL (Google Books / VitalSource og:image).</summary>
    public string  CoverThumbnail       { get; set; } = string.Empty;
    /// <summary>True when VitalSource product page was found → digital eBook confirmed.</summary>
    public bool    DigitalOnVitalSource { get; set; }
    /// <summary>True = data came from the local database (previously approved book).</summary>
    public bool    FromLocalCache       { get; set; }
    /// <summary>"Open Library", "Google Books", "VitalSource", or "Local Catalog"</summary>
    public string  Source               { get; set; } = string.Empty;
}
