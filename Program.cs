using System.Threading.RateLimiting;
using CanonSeSu.Api.Data;
using CanonSeSu.Api.Endpoints;
using CanonSeSu.Api.Jobs;
using CanonSeSu.Api.Middleware;
using CanonSeSu.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("fixed", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = 60;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0;
    });
});

builder.Services.AddScoped<AppDb>(_ =>
    new AppDb(builder.Configuration.GetConnectionString("DefaultConnection")!));

builder.Services.AddScoped<EmailService>();

builder.Services.AddQuartz(q =>
{
    q.AddJob<CounterRequestEmailJob>(CounterRequestEmailJob.Key, c => c.StoreDurably());
    q.AddTrigger(t => t
        .ForJob(CounterRequestEmailJob.Key)
        .WithIdentity("CounterRequestEmailTrigger")
        .WithCronSchedule(
            "0 0 2 28 * ?",
            x => x.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"))));
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapHealthChecks("/health");
app.MapDevicesEndpoints();
app.MapAdminEndpoints();
app.MapUserEndpoints();

app.Run();
