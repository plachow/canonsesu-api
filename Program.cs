using System.Threading.RateLimiting;
using CanonSeSu.Api.Data;
using CanonSeSu.Api.Endpoints;
using CanonSeSu.Api.Middleware;
using Microsoft.AspNetCore.RateLimiting;

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapDevicesEndpoints();

app.Run();
