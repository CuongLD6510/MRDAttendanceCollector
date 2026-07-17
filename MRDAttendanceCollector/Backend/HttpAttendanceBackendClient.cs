using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MRDAttendanceCollector.Configuration;
using MRDAttendanceCollector.Models;

namespace MRDAttendanceCollector.Backend;

public sealed class HttpAttendanceBackendClient : IAttendanceBackendClient
{
    public const string HttpClientName = "AttendanceBackend";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BackendOptions _options;
    private readonly SchedulerOptions _scheduler;
    private readonly ILogger<HttpAttendanceBackendClient> _logger;

    public HttpAttendanceBackendClient(
        IHttpClientFactory httpClientFactory,
        IOptions<BackendOptions> options,
        IOptions<SchedulerOptions> schedulerOptions,
        ILogger<HttpAttendanceBackendClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _scheduler = schedulerOptions.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AttendanceDevice>> GetActiveDevicesAsync(CancellationToken cancellationToken)
    {
        var response = await PostWithRetryAsync("api/AttendanceAPI/fnGetCollectorDevices", new { }, cancellationToken);
        EnsureSuccess(response, "fnGetCollectorDevices");

        var devices = new List<AttendanceDevice>();
        if (response.Data?.Devices is null)
        {
            return devices;
        }

        foreach (var d in response.Data.Devices)
        {
            devices.Add(new AttendanceDevice
            {
                AttDeviceId = d.ATT_DEVICE_ID,
                DeviceName = d.DEVICE_NAME ?? string.Empty,
                IpAddress = d.IP_ADDRESS ?? string.Empty,
                PortNo = d.PORT_NO <= 0 ? 4370 : d.PORT_NO,
                MachineNumber = d.MACHINE_NUMBER <= 0 ? 1 : d.MACHINE_NUMBER,
                LastProcessedLogTime = d.LAST_PROCESSED_LOG_TIME,
                DeviceVendor = string.IsNullOrWhiteSpace(d.DEVICE_VENDOR) ? "ZKTeco" : d.DEVICE_VENDOR
            });
        }

        return devices;
    }

    public async Task<PostRawLogsResult> PostRawLogsAsync(
        int attDeviceId,
        IReadOnlyList<RawAttendanceLog> logs,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            ATT_DEVICE_ID = attDeviceId,
            Logs = logs.Select(l => new
            {
                ENROLL_NUMBER = l.EnrollNumber,
                LOG_TIME = l.LogTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                WORK_DATE = l.WorkDate.ToString("yyyy-MM-dd")
            }).ToList()
        };

        var response = await PostWithRetryAsync("api/AttendanceAPI/fnPostCollectorRawLogs", payload, cancellationToken);
        EnsureSuccess(response, "fnPostCollectorRawLogs");

        return new PostRawLogsResult
        {
            Inserted = response.Data?.Inserted ?? 0,
            Duplicate = response.Data?.Duplicate ?? 0
        };
    }

    public async Task PostSyncResultAsync(SyncJobResult result, CancellationToken cancellationToken)
    {
        var payload = new
        {
            ATT_DEVICE_ID = result.AttDeviceId,
            JOB_START_TIME = result.JobStartTime.ToString("yyyy-MM-ddTHH:mm:ss"),
            JOB_END_TIME = result.JobEndTime.ToString("yyyy-MM-ddTHH:mm:ss"),
            READ_FROM_TIME = result.ReadFromTime?.ToString("yyyy-MM-ddTHH:mm:ss"),
            LAST_PROCESSED_LOG_TIME = result.LastProcessedLogTime?.ToString("yyyy-MM-ddTHH:mm:ss"),
            RECORDS_READ = result.RecordsRead,
            RECORDS_INSERTED = result.RecordsInserted,
            RECORDS_DUPLICATE = result.RecordsDuplicate,
            RETRY_COUNT = result.RetryCount,
            JOB_STATUS = result.JobStatus,
            ERROR_MESSAGE = result.ErrorMessage
        };

        var response = await PostWithRetryAsync("api/AttendanceAPI/fnPostCollectorSyncResult", payload, cancellationToken);
        EnsureSuccess(response, "fnPostCollectorSyncResult");
    }

    private async Task<ApiEnvelope> PostWithRetryAsync(string relativeUrl, object payload, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        var attempts = Math.Max(1, _scheduler.RetryMaxAttempts);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var httpClient = _httpClientFactory.CreateClient(HttpClientName);
                using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl);
                request.Content = JsonContent.Create(payload, options: JsonOptions);
                if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);
                }

                using var response = await httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode} calling {relativeUrl}: {body}");
                }

                return JsonSerializer.Deserialize<ApiEnvelope>(body, JsonOptions)
                    ?? throw new InvalidOperationException($"Empty response from {relativeUrl}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Backend call {Url} attempt {Attempt}/{Max} failed", relativeUrl, attempt, attempts);
                if (attempt < attempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_scheduler.RetryDelaySeconds), cancellationToken);
                }
            }
        }

        throw new InvalidOperationException($"Backend call {relativeUrl} failed after {attempts} attempts.", lastError);
    }

    private static void EnsureSuccess(ApiEnvelope response, string apiName)
    {
        if (!string.Equals(response.ErrCode, "1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{apiName} returned ErrCode={response.ErrCode}, ErrMsg={response.ErrMsg}");
        }
    }

    private sealed class ApiEnvelope
    {
        public string? ErrCode { get; set; }
        public string? ErrMsg { get; set; }
        public string? ErrBack { get; set; }
        public ApiData? Data { get; set; }
    }

    private sealed class ApiData
    {
        public List<ApiDevice>? Devices { get; set; }
        public int Inserted { get; set; }
        public int Duplicate { get; set; }
    }

    private sealed class ApiDevice
    {
        public int ATT_DEVICE_ID { get; set; }
        public string? DEVICE_NAME { get; set; }
        public string? IP_ADDRESS { get; set; }
        public int PORT_NO { get; set; }
        public int MACHINE_NUMBER { get; set; }
        public DateTime? LAST_PROCESSED_LOG_TIME { get; set; }
        public string? DEVICE_VENDOR { get; set; }
    }
}
