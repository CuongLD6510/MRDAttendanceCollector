using System.Text;
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
    private readonly ReprocessOptions _reprocess;
    private readonly ILogger<HttpAttendanceBackendClient> _logger;

    public HttpAttendanceBackendClient(
        IHttpClientFactory httpClientFactory,
        IOptions<BackendOptions> options,
        IOptions<SchedulerOptions> schedulerOptions,
        IOptions<ReprocessOptions> reprocessOptions,
        ILogger<HttpAttendanceBackendClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _scheduler = schedulerOptions.Value;
        _reprocess = reprocessOptions.Value;
        _logger = logger;
    }

    public async Task<CollectorDevicesSnapshot> GetActiveDevicesAsync(CancellationToken cancellationToken)
    {
        var response = await PostWithRetryAsync("api/AttendanceAPI/fnGetCollectorDevices", new { }, cancellationToken);
        EnsureSuccess(response, "fnGetCollectorDevices");

        var devices = new List<AttendanceDevice>();
        if (response.Data?.Devices != null)
        {
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
        }

        return new CollectorDevicesSnapshot
        {
            Devices = devices,
            InitialSyncFrom = response.Data?.INITIAL_SYNC_FROM
        };
    }

    public async Task<IReadOnlyList<BlackoutWindowOptions>> GetBlackoutWindowsAsync(CancellationToken cancellationToken)
    {
        var response = await PostWithRetryAsync("api/AttendanceAPI/fnGetCollectorBlackoutWindows", new { }, cancellationToken);
        EnsureSuccess(response, "fnGetCollectorBlackoutWindows");

        var list = new List<BlackoutWindowOptions>();
        if (response.Data?.BlackoutWindows is null)
        {
            return list;
        }

        foreach (var w in response.Data.BlackoutWindows)
        {
            list.Add(new BlackoutWindowOptions
            {
                Start = w.Start ?? string.Empty,
                End = w.End ?? string.Empty
            });
        }

        return list;
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

    public async Task<DrainReprocessResult> DrainReprocessQueueAsync(int maxItems, CancellationToken cancellationToken)
    {
        var payload = new
        {
            MaxItems = maxItems < 1 ? 1 : maxItems
        };
        var drainTimeout = TimeSpan.FromSeconds(Math.Max(60, _reprocess.TimeoutSeconds));
        var response = await PostWithRetryAsync(
            "api/AttendanceAPI/fnDrainAttReprocessQueue",
            payload,
            cancellationToken,
            drainTimeout);
        EnsureSuccess(response, "fnDrainAttReprocessQueue");
        return new DrainReprocessResult
        {
            Processed = response.Data?.Processed ?? 0,
            Failed = response.Data?.Failed ?? 0,
            Remaining = response.Data?.Remaining ?? 0
        };
    }

    private async Task<ApiEnvelope> PostWithRetryAsync(
        string relativeUrl,
        object payload,
        CancellationToken cancellationToken,
        TimeSpan? requestTimeout = null)
    {
        Exception? lastError = null;
        var attempts = Math.Max(1, _scheduler.RetryMaxAttempts);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var httpClient = _httpClientFactory.CreateClient(HttpClientName);
                if (requestTimeout.HasValue)
                {
                    httpClient.Timeout = requestTimeout.Value;
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl);
                var json = JsonSerializer.Serialize(payload, JsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                await content.LoadIntoBufferAsync();
                request.Content = content;
                if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);
                }

                using var response = await httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode} khi gọi {relativeUrl}: {body}");
                }

                return JsonSerializer.Deserialize<ApiEnvelope>(body, JsonOptions)
                    ?? throw new InvalidOperationException($"Phản hồi rỗng từ {relativeUrl}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient.Timeout — cho phép retry thay vì coi như host đang shutdown.
                lastError = ex;
                _logger.LogWarning(
                    ex,
                    "Gọi Backend {Url} lần {Attempt}/{Max} hết thời gian chờ ({Timeout}s)",
                    relativeUrl,
                    attempt,
                    attempts,
                    requestTimeout?.TotalSeconds ?? _options.TimeoutSeconds);
                if (attempt < attempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_scheduler.RetryDelaySeconds), cancellationToken);
                    continue;
                }

                throw new TimeoutException(
                    $"Gọi Backend {relativeUrl} hết thời gian chờ sau {attempts} lần thử.",
                    ex);
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(
                    ex,
                    "Gọi Backend {Url} lần {Attempt}/{Max} thất bại",
                    relativeUrl,
                    attempt,
                    attempts);
                if (attempt < attempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_scheduler.RetryDelaySeconds), cancellationToken);
                }
            }
        }

        throw new InvalidOperationException($"Gọi Backend {relativeUrl} thất bại sau {attempts} lần thử.", lastError);
    }

    private static void EnsureSuccess(ApiEnvelope response, string apiName)
    {
        if (!string.Equals(response.ErrCode, "1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{apiName} trả về ErrCode={response.ErrCode}, ErrMsg={response.ErrMsg}");
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
        public DateTime? INITIAL_SYNC_FROM { get; set; }
        public List<ApiBlackoutWindow>? BlackoutWindows { get; set; }
        public int Inserted { get; set; }
        public int Duplicate { get; set; }
        public int Processed { get; set; }
        public int Failed { get; set; }
        public int Remaining { get; set; }
    }

    private sealed class ApiBlackoutWindow
    {
        public string? Start { get; set; }
        public string? End { get; set; }
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
