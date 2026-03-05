using System.Net;
using System.Net.Mail;

namespace BYUIVerbaCollect.Services;

/// <summary>
/// Sends automated notification emails to professors.
/// Configure SMTP in appsettings.json under "Email".
/// Falls back to console logging when SMTP is not set.
/// </summary>
public interface IEmailService
{
    Task SendAsync(string toAddress, string toName, string subject, string htmlBody);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration cfg, ILogger<EmailService> logger)
    {
        _cfg = cfg; _logger = logger;
    }

    public async Task SendAsync(string toAddress, string toName, string subject, string htmlBody)
    {
        var host = _cfg["Email:SmtpHost"];
        if (string.IsNullOrWhiteSpace(host))
        {
            // No SMTP configured — log the email body for development
            _logger.LogInformation(
                "[EMAIL SIMULATION] To: {To} <{Addr}>\nSubject: {Subject}\n\n{Body}",
                toName, toAddress, subject, StripHtml(htmlBody));
            return;
        }

        try
        {
            var port     = int.TryParse(_cfg["Email:SmtpPort"], out var p) ? p : 587;
            var user     = _cfg["Email:Username"] ?? "";
            var pass     = _cfg["Email:Password"] ?? "";
            var fromAddr = _cfg["Email:FromAddress"] ?? user;
            var fromName = _cfg["Email:FromName"]    ?? "BYUI University Store";

            using var msg = new MailMessage
            {
                From       = new MailAddress(fromAddr, fromName),
                Subject    = subject,
                Body       = htmlBody,
                IsBodyHtml = true
            };
            msg.To.Add(new MailAddress(toAddress, toName));

            using var client = new SmtpClient(host, port)
            {
                EnableSsl      = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Credentials    = new NetworkCredential(user, pass)
            };
            await client.SendMailAsync(msg);
            _logger.LogInformation("Email sent to {To} — {Subject}", toAddress, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", toAddress);
        }
    }

    private static string StripHtml(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "").Trim();
}

/// <summary>
/// Factory methods for the standard notification emails sent by the Material Manager workflow.
/// </summary>
public static class EmailTemplates
{
    // ── Email 1: Book price > $60 ─────────────────────────────────────────
    public static (string subject, string body) HighPriceAlert(
        string profName, string courseNumber, string bookTitle,
        string isbn, decimal price, string? alternativeSuggestion = null)
    {
        var alt = !string.IsNullOrWhiteSpace(alternativeSuggestion)
            ? $@"<p><strong>Suggested Alternative:</strong><br>{alternativeSuggestion}</p>"
            : "";

        var subject = $"[BYUI Bookstore] Affordability Review — {bookTitle} (${price:F2})";
        var body = $@"
<p>Dear {profName},</p>

<p>The BYUI University Store's Material Manager system has flagged the following
textbook as potentially unaffordable for students in <strong>{courseNumber}</strong>:</p>

<table style=""border-collapse:collapse;width:100%;max-width:520px;font-family:sans-serif;font-size:14px;"">
  <tr><td style=""padding:6px 12px;background:#f5f5f5;font-weight:bold;"">Title</td>
      <td style=""padding:6px 12px;"">{bookTitle}</td></tr>
  <tr><td style=""padding:6px 12px;background:#f5f5f5;font-weight:bold;"">ISBN</td>
      <td style=""padding:6px 12px;"">{isbn}</td></tr>
  <tr><td style=""padding:6px 12px;background:#f5f5f5;font-weight:bold;"">Listed Price</td>
      <td style=""padding:6px 12px;color:#c00;font-weight:bold;"">${price:F2}</td></tr>
  <tr><td style=""padding:6px 12px;background:#f5f5f5;font-weight:bold;"">Affordability Score</td>
      <td style=""padding:6px 12px;"">{Math.Max(0, (int)Math.Round(100 - (double)price * 1.25))} / 100
          &nbsp;(medium threshold: $40 = score 50)</td></tr>
</table>

{alt}

<p>Could you please confirm whether this textbook is still required, or let us know
if a lower-cost alternative would be acceptable? The bookstore can help identify
comparable titles.</p>

<p>Thank you,<br>
<em>BYUI University Store — Material Manager Automation</em></p>";

        return (subject, body);
    }

    // ── Email 2: Required → Optional status change ─────────────────────────
    public static (string subject, string body) RequiredStatusChangeAlert(
        string profName, string courseNumber, string bookTitle,
        string isbn, string previousSemester, bool wasRequired, bool isNowRequired)
    {
        var change = wasRequired && !isNowRequired
            ? "was <strong>Required</strong> last semester but is now listed as <strong>Optional</strong>"
            : "was <strong>Optional</strong> last semester but is now listed as <strong>Required</strong>";

        var subject = $"[BYUI Bookstore] Course Material Status Change — {bookTitle}";
        var body = $@"
<p>Dear {profName},</p>

<p>Our Material Manager system noticed that the following textbook for
<strong>{courseNumber}</strong> has a changed required/optional status compared to
<strong>{previousSemester}</strong>:</p>

<table style=""border-collapse:collapse;width:100%;max-width:520px;font-family:sans-serif;font-size:14px;"">
  <tr><td style=""padding:6px 12px;background:#f5f5f5;font-weight:bold;"">Title</td>
      <td style=""padding:6px 12px;"">{bookTitle}</td></tr>
  <tr><td style=""padding:6px 12px;background:#f5f5f5;font-weight:bold;"">ISBN</td>
      <td style=""padding:6px 12px;"">{isbn}</td></tr>
  <tr><td style=""padding:6px 12px;background:#f5f5f5;font-weight:bold;"">Status Change</td>
      <td style=""padding:6px 12px;"">This book {change} this semester.</td></tr>
</table>

<p>Could you please confirm this is intentional? If students no longer need to
purchase this book, the bookstore will update the course materials list accordingly.</p>

<p>Thank you,<br>
<em>BYUI University Store — Material Manager Automation</em></p>";

        return (subject, body);
    }
}
