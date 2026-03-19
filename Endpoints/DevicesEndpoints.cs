using CanonSeSu.Api.Data;
using LinqToDB.Async;

namespace CanonSeSu.Api.Endpoints;

public static class DevicesEndpoints
{
    public static void MapDevicesEndpoints(this WebApplication app)
    {
        app.MapGet("/api/devices/current", async (AppDb db, DateTime? startDate, DateTime? endDate) =>
        {
            var query = db.ServiceDeviceCounters.AsQueryable();

            if (startDate.HasValue || endDate.HasValue)
            {
                if (startDate.HasValue)
                    query = query.Where(x => x.DatumAktualnihoHlaseni!.Value.Date >= startDate.Value.Date);
                if (endDate.HasValue)
                    query = query.Where(x => x.DatumAktualnihoHlaseni!.Value.Date <= endDate.Value.Date);
            }
            else
            {
                query = query.Where(x => x.DatumAktualnihoHlaseni == db.ServiceDeviceCounters
                    .Max(y => y.DatumAktualnihoHlaseni));
            }

            var devices = await query.ToListAsync();
            return Results.Ok(devices);
        })
        .WithName("GetCurrentDevices")
        .RequireRateLimiting("fixed");
    }
}
