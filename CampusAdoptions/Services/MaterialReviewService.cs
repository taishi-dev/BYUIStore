using CampusAdoptions.Data;
using CampusAdoptions.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CampusAdoptions.Services;

public class MaterialReviewService
{
    private readonly AppDbContext _db;
    private readonly BookAvailabilityService _availability;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<MaterialReviewService> _logger;

    public MaterialReviewService(
        AppDbContext db,
        BookAvailabilityService availability,
        IHttpClientFactory httpFactory,
        ILogger<MaterialReviewService> logger)
    {
        _db = db;
        _availability = availability;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>
    /// Runs all review checks for a single book item in parallel.
    /// Persists suggestions to the database and returns them.
    /// If suggestions already exist for this item, returns the persisted ones.
    /// </summary>
    public async Task<List<MaterialSuggestion>> ReviewItemAsync(
        RequestItem item, string courseNumber, int requestId)
    {
        if (item.ItemType != ItemType.Book || string.IsNullOrWhiteSpace(item.Isbn))
            return [];

        // Check if suggestions already exist (revisit scenario)
        var existing = await _db.MaterialSuggestions
            .Where(s => s.RequestItemId == item.Id)
            .ToListAsync();
        if (existing.Count > 0)
            return existing;

        var isbn = item.Isbn.Replace("-", "").Trim();

        // Get availability data (uses 24h cache)
        BookAvailabilityItem? availData = null;
        try
        {
            availData = await _availability.CheckSingleIsbnAsync(isbn);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Availability check failed for ISBN {Isbn}", isbn);
        }

        var originalPrice = availData?.PrintRetailPrice ?? availData?.PrintListPrice;

        // Run all three analysis tasks in parallel
        var cheaperTask = FindCheaperAlternativesAsync(item, courseNumber, originalPrice);
        var newerTask = FindNewerEditionsAsync(item);
        var digitalTask = CheckDigitalAvailabilityAsync(item, availData);

        await Task.WhenAll(cheaperTask, newerTask, digitalTask);

        var suggestions = new List<MaterialSuggestion>();
        suggestions.AddRange(await cheaperTask);
        suggestions.AddRange(await newerTask);
        suggestions.AddRange(await digitalTask);

        if (suggestions.Count > 0)
        {
            _db.MaterialSuggestions.AddRange(suggestions);
            await _db.SaveChangesAsync();
        }

        return suggestions;
    }

    /// <summary>
    /// Loads previously persisted suggestions for all items in a request.
    /// </summary>
    public async Task<List<MaterialSuggestion>> GetSuggestionsForRequestAsync(int requestId)
    {
        return await _db.MaterialSuggestions
            .Include(s => s.RequestItem)
            .Where(s => s.RequestItem.CourseRequestId == requestId)
            .OrderBy(s => s.RequestItemId)
            .ThenBy(s => s.Type)
            .ToListAsync();
    }

    /// <summary>
    /// Marks a suggestion as dismissed by the professor.
    /// </summary>
    public async Task DismissSuggestionAsync(int suggestionId)
    {
        var suggestion = await _db.MaterialSuggestions.FindAsync(suggestionId);
        if (suggestion is not null)
        {
            suggestion.DismissedByProfessor = true;
            await _db.SaveChangesAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CHEAPER ALTERNATIVES
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<List<MaterialSuggestion>> FindCheaperAlternativesAsync(
        RequestItem item, string courseNumber, decimal? originalPrice)
    {
        var suggestions = new List<MaterialSuggestion>();
        if (!originalPrice.HasValue || originalPrice.Value <= 0) return suggestions;

        var isbn = item.Isbn!.Replace("-", "").Trim();
        var normalizedTitle = NormalizeTitle(item.Title ?? "");

        // Step 1: Search local CourseBookAssignment catalog for same title, different ISBN
        if (!string.IsNullOrWhiteSpace(normalizedTitle))
        {
            var candidates = await _db.CourseBookAssignments
                .Where(b => b.Isbn != isbn && b.Title != null)
                .ToListAsync();

            var matches = candidates
                .Where(b => NormalizeTitle(b.Title ?? "").Contains(normalizedTitle)
                          || normalizedTitle.Contains(NormalizeTitle(b.Title ?? "")))
                .DistinctBy(b => b.Isbn)
                .Take(5)
                .ToList();

            foreach (var match in matches)
            {
                try
                {
                    var matchAvail = await _availability.CheckSingleIsbnAsync(match.Isbn);
                    var matchPrice = matchAvail.PrintRetailPrice ?? matchAvail.PrintListPrice;

                    if (matchPrice.HasValue && matchPrice.Value > 0 &&
                        IsMeaningfullyCheaper(originalPrice.Value, matchPrice.Value))
                    {
                        suggestions.Add(new MaterialSuggestion
                        {
                            RequestItemId = item.Id,
                            Type = SuggestionType.CheaperAlternative,
                            SuggestedIsbn = match.Isbn,
                            SuggestedTitle = match.Title,
                            SuggestedAuthor = match.Author,
                            SuggestedEdition = match.Edition,
                            SuggestedPublicationYear = match.PublicationYear,
                            SuggestedPrice = matchPrice.Value,
                            OriginalPrice = originalPrice.Value,
                            PriceSavings = originalPrice.Value - matchPrice.Value,
                            SourceUrl = matchAvail.AmazonUrl,
                            Source = "Local Catalog",
                            ReasonText = $"Save ${originalPrice.Value - matchPrice.Value:F2} — " +
                                         $"previously used at BYU-I ({match.Edition ?? "same"} edition)"
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Price check failed for catalog ISBN {Isbn}", match.Isbn);
                }
            }
        }

        // Step 2: Search Google Books by title for cheaper editions
        if (!string.IsNullOrWhiteSpace(item.Title))
        {
            try
            {
                var http = _httpFactory.CreateClient("GoogleBooks");
                var query = Uri.EscapeDataString(item.Title);
                if (!string.IsNullOrWhiteSpace(item.Author))
                    query += "+" + Uri.EscapeDataString(item.Author);

                var url = $"https://www.googleapis.com/books/v1/volumes?q=intitle:{query}&maxResults=5";
                var resp = await http.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("items", out var items))
                    {
                        foreach (var vol in items.EnumerateArray())
                        {
                            var info = vol.GetProperty("volumeInfo");
                            var volIsbn = ExtractIsbn13(info);
                            if (string.IsNullOrEmpty(volIsbn) || volIsbn == isbn) continue;

                            // Already suggested from local catalog?
                            if (suggestions.Any(s => s.SuggestedIsbn == volIsbn)) continue;

                            if (!vol.TryGetProperty("saleInfo", out var sale)) continue;
                            if (!sale.TryGetProperty("retailPrice", out var rp)) continue;
                            if (!rp.TryGetProperty("amount", out var amt)) continue;

                            var googlePrice = amt.GetDecimal();
                            if (googlePrice > 0 &&
                                IsMeaningfullyCheaper(originalPrice.Value, googlePrice))
                            {
                                var volTitle = info.TryGetProperty("title", out var t) ? t.GetString() : null;
                                var volAuthor = info.TryGetProperty("authors", out var a)
                                    ? string.Join(", ", a.EnumerateArray().Select(x => x.GetString()))
                                    : null;
                                var buyLink = sale.TryGetProperty("buyLink", out var bl) ? bl.GetString() : null;

                                suggestions.Add(new MaterialSuggestion
                                {
                                    RequestItemId = item.Id,
                                    Type = SuggestionType.CheaperAlternative,
                                    SuggestedIsbn = volIsbn,
                                    SuggestedTitle = volTitle,
                                    SuggestedAuthor = volAuthor,
                                    SuggestedPrice = googlePrice,
                                    OriginalPrice = originalPrice.Value,
                                    PriceSavings = originalPrice.Value - googlePrice,
                                    SourceUrl = buyLink,
                                    Source = "Google Books",
                                    ReasonText = $"Save ${originalPrice.Value - googlePrice:F2} — " +
                                                 $"alternative edition available on Google Books"
                                });
                                break; // One Google suggestion is enough
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Google Books cheaper search failed for '{Title}'", item.Title);
            }
        }

        return suggestions;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NEWER EDITIONS
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<List<MaterialSuggestion>> FindNewerEditionsAsync(RequestItem item)
    {
        var suggestions = new List<MaterialSuggestion>();
        if (string.IsNullOrWhiteSpace(item.Title)) return suggestions;

        var isbn = item.Isbn!.Replace("-", "").Trim();
        var normalizedTitle = NormalizeTitle(item.Title);
        var currentYear = item.PublicationYear ?? 0;

        // Step 1: Search local CourseBookAssignment catalog
        var candidates = await _db.CourseBookAssignments
            .Where(b => b.Isbn != isbn &&
                        b.PublicationYear != null &&
                        b.PublicationYear > currentYear &&
                        b.Title != null)
            .OrderByDescending(b => b.PublicationYear)
            .ToListAsync();

        var localMatch = candidates
            .FirstOrDefault(b => TitlesMatch(normalizedTitle, NormalizeTitle(b.Title ?? "")));

        if (localMatch is not null)
        {
            suggestions.Add(new MaterialSuggestion
            {
                RequestItemId = item.Id,
                Type = SuggestionType.NewerEdition,
                SuggestedIsbn = localMatch.Isbn,
                SuggestedTitle = localMatch.Title,
                SuggestedAuthor = localMatch.Author,
                SuggestedEdition = localMatch.Edition,
                SuggestedPublicationYear = localMatch.PublicationYear,
                Source = "Local Catalog",
                ReasonText = $"A {localMatch.PublicationYear} edition exists — " +
                             $"your selected edition is from {(currentYear > 0 ? currentYear.ToString() : "unknown year")}"
            });
            return suggestions; // Local match is sufficient
        }

        // Step 2: Search Google Books by title + author
        if (currentYear > 0)
        {
            try
            {
                var http = _httpFactory.CreateClient("GoogleBooks");
                var query = Uri.EscapeDataString(item.Title);
                if (!string.IsNullOrWhiteSpace(item.Author))
                    query += "+inauthor:" + Uri.EscapeDataString(item.Author);

                var url = $"https://www.googleapis.com/books/v1/volumes?q=intitle:{query}&maxResults=5&orderBy=newest";
                var resp = await http.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("items", out var items))
                    {
                        foreach (var vol in items.EnumerateArray())
                        {
                            var info = vol.GetProperty("volumeInfo");
                            var volIsbn = ExtractIsbn13(info);
                            if (string.IsNullOrEmpty(volIsbn) || volIsbn == isbn) continue;

                            var volTitle = info.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                            if (!TitlesMatch(normalizedTitle, NormalizeTitle(volTitle))) continue;

                            var pubDate = info.TryGetProperty("publishedDate", out var pd) ? pd.GetString() : null;
                            var pubYear = ParseYear(pubDate);

                            if (pubYear.HasValue && pubYear.Value > currentYear)
                            {
                                var volAuthor = info.TryGetProperty("authors", out var a)
                                    ? string.Join(", ", a.EnumerateArray().Select(x => x.GetString()))
                                    : null;
                                var buyLink = vol.TryGetProperty("saleInfo", out var si)
                                    && si.TryGetProperty("buyLink", out var bl)
                                    ? bl.GetString() : null;

                                suggestions.Add(new MaterialSuggestion
                                {
                                    RequestItemId = item.Id,
                                    Type = SuggestionType.NewerEdition,
                                    SuggestedIsbn = volIsbn,
                                    SuggestedTitle = volTitle,
                                    SuggestedAuthor = volAuthor,
                                    SuggestedPublicationYear = pubYear,
                                    SourceUrl = buyLink,
                                    Source = "Google Books",
                                    ReasonText = $"A {pubYear} edition exists — " +
                                                 $"your selected edition is from {currentYear}"
                                });
                                break; // One newer edition suggestion is enough
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Google Books newer edition search failed for '{Title}'", item.Title);
            }
        }

        return suggestions;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DIGITAL AVAILABILITY
    // ═══════════════════════════════════════════════════════════════════════

    private Task<List<MaterialSuggestion>> CheckDigitalAvailabilityAsync(
        RequestItem item, BookAvailabilityItem? availData)
    {
        var suggestions = new List<MaterialSuggestion>();
        if (availData is null) return Task.FromResult(suggestions);

        var printPrice = availData.PrintRetailPrice ?? availData.PrintListPrice;

        if (availData.DigitalAvailableOnVitalSource)
        {
            var digitalPrice = availData.VitalSourcePrice;
            var savings = (printPrice.HasValue && digitalPrice.HasValue)
                ? printPrice.Value - digitalPrice.Value
                : (decimal?)null;

            var daysLabel = availData.VitalSourcePriceDays switch
            {
                120 => "120-day rental",
                180 => "180-day rental",
                0 => "Lifetime access",
                _ => "digital edition"
            };

            suggestions.Add(new MaterialSuggestion
            {
                RequestItemId = item.Id,
                Type = SuggestionType.DigitalAvailable,
                SuggestedIsbn = item.Isbn,
                SuggestedTitle = item.Title,
                SuggestedAuthor = item.Author,
                SuggestedPrice = digitalPrice,
                OriginalPrice = printPrice,
                PriceSavings = savings > 0 ? savings : null,
                SourceUrl = availData.VitalSourceUrl,
                Source = "VitalSource",
                ReasonText = digitalPrice.HasValue
                    ? $"Digital {daysLabel} at ${digitalPrice:F2} on VitalSource" +
                      (savings > 0 ? $" — saves ${savings:F2} vs print" : "")
                    : $"Digital {daysLabel} available on VitalSource"
            });
        }
        else if (availData.EbookAvailableOnGoogle)
        {
            var ebookPrice = availData.EbookPrice;
            var savings = (printPrice.HasValue && ebookPrice.HasValue)
                ? printPrice.Value - ebookPrice.Value
                : (decimal?)null;

            suggestions.Add(new MaterialSuggestion
            {
                RequestItemId = item.Id,
                Type = SuggestionType.DigitalAvailable,
                SuggestedIsbn = item.Isbn,
                SuggestedTitle = item.Title,
                SuggestedAuthor = item.Author,
                SuggestedPrice = ebookPrice,
                OriginalPrice = printPrice,
                PriceSavings = savings > 0 ? savings : null,
                SourceUrl = availData.GoogleBuyLink,
                Source = "Google Books",
                ReasonText = ebookPrice.HasValue
                    ? $"eBook at ${ebookPrice:F2} on Google Books" +
                      (savings > 0 ? $" — saves ${savings:F2} vs print" : "")
                    : "eBook available on Google Books"
            });
        }

        return Task.FromResult(suggestions);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    private static bool IsMeaningfullyCheaper(decimal original, decimal suggested)
    {
        var savings = original - suggested;
        return savings >= 10m || (savings / original) >= 0.15m;
    }

    private static string NormalizeTitle(string title)
    {
        // Strip edition indicators and normalize for comparison
        var normalized = Regex.Replace(title, @"\s*:.*$", ""); // Remove subtitle
        normalized = Regex.Replace(normalized, @"\s*\d+(st|nd|rd|th)\s+edition\s*$", "",
            RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s*\(.*?\)\s*", " "); // Remove parentheticals
        return normalized.Trim().ToLowerInvariant();
    }

    private static bool TitlesMatch(string normalizedA, string normalizedB)
    {
        if (string.IsNullOrWhiteSpace(normalizedA) || string.IsNullOrWhiteSpace(normalizedB))
            return false;
        return normalizedA == normalizedB
            || normalizedA.Contains(normalizedB)
            || normalizedB.Contains(normalizedA);
    }

    private static string? ExtractIsbn13(JsonElement volumeInfo)
    {
        if (!volumeInfo.TryGetProperty("industryIdentifiers", out var ids)) return null;
        foreach (var id in ids.EnumerateArray())
        {
            if (id.TryGetProperty("type", out var type) && type.GetString() == "ISBN_13" &&
                id.TryGetProperty("identifier", out var val))
                return val.GetString();
        }
        // Fallback to ISBN_10
        foreach (var id in ids.EnumerateArray())
        {
            if (id.TryGetProperty("type", out var type) && type.GetString() == "ISBN_10" &&
                id.TryGetProperty("identifier", out var val))
                return val.GetString();
        }
        return null;
    }

    private static int? ParseYear(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;
        var match = Regex.Match(dateStr, @"(\d{4})");
        return match.Success && int.TryParse(match.Groups[1].Value, out var year) ? year : null;
    }
}
