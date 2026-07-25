using System.Text;
using Microsoft.Extensions.Options;
using MRDAttendanceCollector.Backend;
using MRDAttendanceCollector.Configuration;
using MRDAttendanceCollector.Models;
using MRDAttendanceCollector.Scheduling;
using MRDAttendanceCollector.Sdk;

TrySetConsoleUtf8();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "MRDAttendanceCollector";
});

builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection(SchedulerOptions.SectionName));
builder.Services.Configure<BackendOptions>(builder.Configuration.GetSection(BackendOptions.SectionName));
builder.Services.Configure<ReprocessOptions>(builder.Configuration.GetSection(ReprocessOptions.SectionName));

builder.Services.AddSingleton<IBlackoutService, BlackoutService>();
builder.Services.AddSingleton<AttendanceSyncService>();
builder.Services.AddSingleton<IAttendanceBackendClient, HttpAttendanceBackendClient>();
builder.Services.AddSingleton<IDeviceSdkAdapter, ZkTecoDeviceSdkAdapter>();

builder.Services.AddHttpClient(HttpAttendanceBackendClient.HttpClientName, (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<BackendOptions>>().Value;
    var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "http://localhost/" : options.BaseUrl;
    if (!baseUrl.EndsWith('/'))
    {
        baseUrl += "/";
    }

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
});

builder.Services.AddHostedService<CronSyncHostedService>();
builder.Services.AddHostedService<AttReprocessDrainHostedService>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
#pragma warning disable CA1416
builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "MRDAttendanceCollector";
});
#pragma warning restore CA1416

var host = builder.Build();
host.Run();

static void TrySetConsoleUtf8()
{
    try
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
    }
    catch (IOException)
    {
        // Không có console (Windows Service) — bỏ qua.
    }
}
