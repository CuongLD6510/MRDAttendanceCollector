using System.Text;
using Microsoft.Extensions.Options;
using MRDAttendanceCollector.Backend;
using MRDAttendanceCollector.Configuration;
using MRDAttendanceCollector.Models;
using MRDAttendanceCollector.Scheduling;
using MRDAttendanceCollector.Sdk;

// Console Windows mặc định dùng code page OEM → tiếng Việt bị lỗi font (?).
// Ép UTF-8 khi chạy dạng console/debug để log hiển thị đúng dấu.
TrySetConsoleUtf8();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "MRDAttendanceCollector";
});

builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection(SchedulerOptions.SectionName));
builder.Services.Configure<BackendOptions>(builder.Configuration.GetSection(BackendOptions.SectionName));

builder.Services.AddSingleton<IBlackoutService, BlackoutService>();
builder.Services.AddSingleton<AttendanceSyncService>();
builder.Services.AddSingleton<MockAttendanceBackendClient>();
builder.Services.AddSingleton<HttpAttendanceBackendClient>();
builder.Services.AddSingleton<MockDeviceSdkAdapter>();
builder.Services.AddSingleton<ZkTecoDeviceSdkAdapter>();

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

builder.Services.AddSingleton<IAttendanceBackendClient>(sp =>
{
    var backend = sp.GetRequiredService<IOptions<BackendOptions>>().Value;
    return backend.UseMock
        ? sp.GetRequiredService<MockAttendanceBackendClient>()
        : sp.GetRequiredService<HttpAttendanceBackendClient>();
});

builder.Services.AddSingleton<IDeviceSdkAdapter>(sp =>
{
    var backend = sp.GetRequiredService<IOptions<BackendOptions>>().Value;
    return backend.UseMock
        ? sp.GetRequiredService<MockDeviceSdkAdapter>()
        : sp.GetRequiredService<ZkTecoDeviceSdkAdapter>();
});

builder.Services.AddHostedService<CronSyncHostedService>();

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
