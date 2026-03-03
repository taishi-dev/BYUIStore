using System.Text.Json;
using System.Web;

namespace BYUIVerbaCollect.Services;

/// <summary>
/// Queries the Open Library Search API to auto-populate ISBN, publisher,
/// and edition from a book title/author — so faculty never type ISBN manually.
/// </summary>
public class IsbnLookupService
{
    private readonly HttpClient _http;
    private readonly ILogger<IsbnLookupService> _logger;

    public IsbnLookupService(HttpClient http, ILogger<IsbnLookupService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<BookSearchResult>> SearchAsync(string title, string? author = null)
    {
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(title))
            queryParts.Add($"title={Uri.EscapeDataString(title)}");
        if (!string.IsNullOrWhiteSpace(author))
            queryParts.Add($"author={Uri.EscapeDataString(author)}");

        var url = $"https://openlibrary.org/search.json?{string.Join("&", queryParts)}&limit=10&fields=title,author_name,isbn,publisher,edition_count,first_publish_year";

        try
        {
            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var docs = doc.RootElement.GetProperty("docs");

            var results = new List<BookSearchResult>();
            foreach (var item in docs.EnumerateArray())
            {
                // Prefer ISBN-13 over ISBN-10
                string bestIsbn = "";
                if (item.TryGetProperty("isbn", out var isbnArr))
                {
                    var isbns = isbnArr.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                    bestIsbn = isbns.FirstOrDefault(s => s.Length == 13)
                            ?? isbns.FirstOrDefault(s => s.Length == 10)
                            ?? isbns.FirstOrDefault()
                            ?? "";
                }

                string publisher = "";
                if (item.TryGetProperty("publisher", out var pubArr))
                    publisher = pubArr.EnumerateArray().FirstOrDefault().GetString() ?? "";

                string authorStr = "";
                if (item.TryGetProperty("author_name", out var authArr))
                    authorStr = string.Join(", ", authArr.EnumerateArray().Select(a => a.GetString() ?? ""));

                int? year = null;
                if (item.TryGetProperty("first_publish_year", out var yearEl) && yearEl.ValueKind == JsonValueKind.Number)
                    year = yearEl.GetInt32();

                string editionStr = "";
                if (item.TryGetProperty("edition_count", out var edEl) && edEl.ValueKind == JsonValueKind.Number)
                    editionStr = edEl.GetInt32().ToString();

                results.Add(new BookSearchResult
                {
                    Title     = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    Author    = authorStr,
                    Isbn      = bestIsbn,
                    Publisher = publisher,
                    Edition   = editionStr,
                    Year      = year
                });
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ISBN lookup failed for title='{Title}'", title);
            return new List<BookSearchResult>();
        }
    }
}

public class BookSearchResult
{
    public string Title     { get; set; } = string.Empty;
    public string Author    { get; set; } = string.Empty;
    public string Isbn      { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Edition   { get; set; } = string.Empty;
    public int?   Year      { get; set; }
}
