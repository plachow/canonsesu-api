using CanonSeSu.Api.Data;
using CanonSeSu.Api.Data.Models;
using LinqToDB;
using LinqToDB.Async;

namespace CanonSeSu.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        // GET /api/info — returns current reporting period and deadline (public, no auth)
        app.MapGet("/api/info", async (AppDb db) =>
        {
            var row = await db.ServiceDeviceCounters
                .OrderByDescending(x => x.DatumAktualnihoHlaseni)
                .Select(x => new { x.DatumAktualnihoHlaseni, x.DeadlineDate })
                .FirstOrDefaultAsync();

            if (row is null)
                return Results.Ok(new { period = (DateOnly?)null, deadline = (DateOnly?)null, isPastDeadline = false });

            var isPastDeadline = row.DeadlineDate.HasValue && DateTime.UtcNow.Date > row.DeadlineDate.Value.Date;
            return Results.Ok(new { period = row.DatumAktualnihoHlaseni, deadline = row.DeadlineDate, isPastDeadline });
        })
        .WithName("GetInfo");

        // GET /api/user/{idcode} — returns all devices for this user in the current period
        app.MapGet("/api/user/{idcode}", async (AppDb db, string idcode) =>
        {
            var maxDate = await db.ServiceDeviceCounters
                .OrderByDescending(x => x.DatumAktualnihoHlaseni)
                .Select(x => x.DatumAktualnihoHlaseni)
                .FirstOrDefaultAsync();

            if (maxDate is null)
                return Results.NotFound("No active reporting period found.");

            var devices = await db.ServiceDeviceCounters
                .Where(x => x.IdCode == idcode && x.DatumAktualnihoHlaseni == maxDate)
                .ToListAsync();

            if (devices.Count == 0)
                return Results.NotFound("Invalid or expired link.");

            var deadline = devices.First().DeadlineDate;
            var isPastDeadline = deadline.HasValue && DateTime.UtcNow.Date > deadline.Value.Date;

            return Results.Ok(new
            {
                period = maxDate,
                deadline,
                isPastDeadline,
                alreadySubmitted = devices.Any(d => d.AktualniStavPocitadla.HasValue),
                devices = devices.Select(d => new
                {
                    d.RecordId,
                    d.TypStroje,
                    d.VyrobniCislo,
                    d.TypKonfigurace,
                    d.TypPocitadla,
                    d.NazevPocitadla,
                    d.PosledniStavPocitadla,
                    d.AktualniStavPocitadla,
                    d.Poznamka
                })
            });
        })
        .WithName("GetUserDevices")
        ;

        // POST /api/user/{idcode} — submit counter readings for all devices
        app.MapPost("/api/user/{idcode}", async (AppDb db, string idcode, List<CounterSubmission> submissions) =>
        {
            if (submissions is not { Count: > 0 })
                return Results.BadRequest("No submissions provided.");

            var maxDate = await db.ServiceDeviceCounters
                .OrderByDescending(x => x.DatumAktualnihoHlaseni)
                .Select(x => x.DatumAktualnihoHlaseni)
                .FirstOrDefaultAsync();

            if (maxDate is null)
                return Results.NotFound("No active reporting period found.");

            var devices = await db.ServiceDeviceCounters
                .Where(x => x.IdCode == idcode && x.DatumAktualnihoHlaseni == maxDate)
                .ToListAsync();

            if (devices.Count == 0)
                return Results.NotFound("Invalid or expired link.");

            var deadline = devices.First().DeadlineDate;
            if (deadline.HasValue && DateTime.UtcNow.Date > deadline.Value.Date)
                return Results.UnprocessableEntity($"Submission deadline was {deadline.Value:d. M. yyyy}. This period is closed.");

            var deviceIds = devices.Select(d => d.RecordId).ToHashSet();
            var invalidIds = submissions.Select(s => s.RecordId).Where(id => !deviceIds.Contains(id)).ToList();
            if (invalidIds.Count > 0)
                return Results.BadRequest($"RecordIds not belonging to this idcode: {string.Join(", ", invalidIds)}");

            var now = DateTime.UtcNow;
            foreach (var submission in submissions)
            {
                await db.ServiceDeviceCounters
                    .Where(x => x.RecordId == submission.RecordId && x.IdCode == idcode)
                    .Set(x => x.AktualniStavPocitadla, submission.AktualniStavPocitadla)
                    .Set(x => x.Poznamka, submission.Poznamka)
                    .Set(x => x.DatumCasNahlaseni, now)
                    .UpdateAsync();
            }

            return Results.Ok(new { message = "Počítadla byla úspěšně nahlášena. Děkujeme.", submittedAt = now });
        })
        .WithName("SubmitCounters")
        ;
    }
}

public record CounterSubmission(
    int RecordId,
    int? AktualniStavPocitadla,
    string? Poznamka
);
