using CanonSeSu.Api.Data;
using CanonSeSu.Api.Services;
using LinqToDB;
using LinqToDB.Async;
using Quartz;

namespace CanonSeSu.Api.Jobs;

[DisallowConcurrentExecution]
public class CounterRequestEmailJob(
    AppDb db,
    EmailService emailService,
    ILogger<CounterRequestEmailJob> logger) : IJob
{
    public static readonly JobKey Key = new(nameof(CounterRequestEmailJob));

    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Counter request email job started at {Time}", DateTimeOffset.Now);

        try
        {
            var maxDate = await db.ServiceDeviceCounters
                .OrderByDescending(x => x.DatumAktualnihoHlaseni)
                .Select(x => x.DatumAktualnihoHlaseni)
                .FirstOrDefaultAsync(context.CancellationToken);

            if (maxDate is null)
            {
                logger.LogWarning("No devices found in service_device_counters. Skipping email send.");
                return;
            }

            var devices = await db.ServiceDeviceCounters
                .Where(x => x.DatumAktualnihoHlaseni == maxDate && x.Email != null && x.IdCode != null)
                .ToListAsync(context.CancellationToken);

            if (devices.Count == 0)
            {
                logger.LogWarning("No devices with email/idcode found for period {Period}. Skipping.", maxDate);
                return;
            }

            var grouped = devices
                .GroupBy(x => x.Email!, StringComparer.OrdinalIgnoreCase)
                .ToList();

            logger.LogInformation(
                "Sending counter request emails for period {Period}: {DeviceCount} devices, {RecipientCount} recipients",
                maxDate, devices.Count, grouped.Count);

            await emailService.SendCounterRequestEmailsAsync(grouped, context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Counter request email job failed");
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
