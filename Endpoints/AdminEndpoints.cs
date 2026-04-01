using System.Text.Json;
using CanonSeSu.Api.Data;
using CanonSeSu.Api.Data.Models;
using CanonSeSu.Api.Jobs;
using CanonSeSu.Api.Services;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using Quartz;

namespace CanonSeSu.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        app.MapPost("/api/devices/bulk", async (AppDb db, HttpRequest httpRequest) =>
        {
            using var reader = new StreamReader(httpRequest.Body);
            var rawBody = await reader.ReadToEndAsync();

            // Strip literal CR/LF characters that SQL Server FOR JSON sometimes injects
            // inside string values when output exceeds ~2033 chars per row
            var sanitized = rawBody.Replace("\r\n", "").Replace("\r", "").Replace("\n", "");

            var requests = JsonSerializer.Deserialize<List<BulkInsertDeviceRequest>>(sanitized,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (requests is not { Count: > 0 })
                return Results.BadRequest("No devices provided.");

            // Expand records where email field contains multiple addresses (comma- or semicolon-separated)
            var expanded = requests.SelectMany(r =>
            {
                var emails = (r.Email ?? "")
                    .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim())
                    .Where(e => e.Contains('@'))
                    .ToList();
                return emails.Count > 0
                    ? emails.Select(email => r with { Email = email })
                    : (IEnumerable<BulkInsertDeviceRequest>)[r];
            }).ToList();

            var codePerEmail = expanded
                .Select(r => r.Email)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(email => email!, _ => Guid.NewGuid().ToString("N"), StringComparer.OrdinalIgnoreCase);

            var rows = expanded.Select(r => new ServiceDeviceCounter
            {
                IdCode                 = codePerEmail[r.Email!],
                Email                  = r.Email,
                TypKonfigurace         = r.TypKonfigurace,
                TypStroje              = r.TypStroje,
                VyrobniCislo           = r.VyrobniCislo,
                TypPocitadla           = r.TypPocitadla,
                NazevPocitadla         = r.NazevPocitadla,
                KonfiguraceId          = r.KonfiguraceId,
                PocitadloId            = r.PocitadloId,
                DatumPoslednihoHlaseni = r.DatumPoslednihoHlaseni,
                PosledniStavPocitadla  = r.PosledniStavPocitadla,
                DatumAktualnihoHlaseni = r.DatumAktualnihoHlaseni,
                DeadlineDate           = r.DeadlineDate,
            }).ToList();

            await db.BulkCopyAsync(rows);

            var result = codePerEmail.Select(kv => new { Email = kv.Key, IdCode = kv.Value });
            return Results.Ok(result);
        })
        .WithName("BulkInsertDevices")
        ;

        app.MapPost("/api/admin/emails/trigger", async (ISchedulerFactory schedulerFactory) =>
        {
            var scheduler = await schedulerFactory.GetScheduler();
            await scheduler.TriggerJob(CounterRequestEmailJob.Key);
            return Results.Accepted(value: new { message = "Email job triggered. Check application logs for progress." });
        })
        .WithName("TriggerEmailJob")
        ;

        // POST /api/admin/emails/resend/{email} — resend email to a single recipient from the current period
        app.MapPost("/api/admin/emails/resend/{email}", async (AppDb db, EmailService emailService, string email) =>
        {
            var maxDate = await db.ServiceDeviceCounters
                .OrderByDescending(x => x.DatumAktualnihoHlaseni)
                .Select(x => x.DatumAktualnihoHlaseni)
                .FirstOrDefaultAsync();

            if (maxDate is null)
                return Results.NotFound("No active reporting period found.");

            var devices = await db.ServiceDeviceCounters
                .Where(x => x.Email == email && x.DatumAktualnihoHlaseni == maxDate)
                .ToListAsync();

            if (devices.Count == 0)
                return Results.NotFound($"No devices found for '{email}' in the current period.");

            var group = devices.GroupBy(x => x.Email!, StringComparer.OrdinalIgnoreCase);
            await emailService.SendCounterRequestEmailsAsync(group);

            return Results.Ok(new { message = $"Email resent to {email}.", deviceCount = devices.Count });
        })
        .WithName("ResendEmail")
        ;

        // GET /api/admin/status — submission status for the current period
        app.MapGet("/api/admin/status", async (AppDb db) =>
        {
            var maxDate = await db.ServiceDeviceCounters
                .OrderByDescending(x => x.DatumAktualnihoHlaseni)
                .Select(x => x.DatumAktualnihoHlaseni)
                .FirstOrDefaultAsync();

            if (maxDate is null)
                return Results.NotFound("No active reporting period found.");

            var devices = await db.ServiceDeviceCounters
                .Where(x => x.DatumAktualnihoHlaseni == maxDate)
                .ToListAsync();

            var totalDevices      = devices.Count;
            var submitted         = devices.Count(d => d.AktualniStavPocitadla.HasValue);
            var pending           = totalDevices - submitted;
            var totalRecipients   = devices.Select(d => d.Email).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var submittedEmails   = devices
                .Where(d => d.AktualniStavPocitadla.HasValue)
                .Select(d => d.Email)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            var deadline = devices.First().DeadlineDate;

            return Results.Ok(new
            {
                period             = maxDate,
                deadline,
                isPastDeadline     = deadline.HasValue && DateTime.UtcNow.Date > deadline.Value.Date,
                totalDevices,
                submitted,
                pending,
                totalRecipients,
                submittedRecipients = submittedEmails,
                pendingRecipients   = totalRecipients - submittedEmails,
                submissionRate      = totalDevices > 0 ? Math.Round((double)submitted / totalDevices * 100, 1) : 0
            });
        })
        .WithName("GetSubmissionStatus")
        ;
    }
}

public record BulkInsertDeviceRequest(
    string? Email,
    string? TypKonfigurace,
    string? TypStroje,
    string? VyrobniCislo,
    string? TypPocitadla,
    string? NazevPocitadla,
    string? KonfiguraceId,
    string? PocitadloId,
    DateTime? DatumPoslednihoHlaseni,
    int? PosledniStavPocitadla,
    DateTime? DatumAktualnihoHlaseni,
    DateTime? DeadlineDate
);
