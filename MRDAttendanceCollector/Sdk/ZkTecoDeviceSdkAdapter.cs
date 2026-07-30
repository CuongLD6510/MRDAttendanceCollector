using System.Runtime.InteropServices;
using MRDAttendanceCollector.Models;

namespace MRDAttendanceCollector.Sdk;

public sealed class ZkTecoDeviceSdkAdapter : IDeviceSdkAdapter
{
    private readonly ILogger<ZkTecoDeviceSdkAdapter> _logger;
    private static int _nativePathReady;

    public ZkTecoDeviceSdkAdapter(ILogger<ZkTecoDeviceSdkAdapter> logger)
    {
        _logger = logger;
    }

    public string Vendor => "ZKTeco";

    public Task<IReadOnlyList<RawAttendanceLog>> ReadLogsAsync(
        AttendanceDevice device,
        DateTime fromInclusive,
        DateTime toInclusive,
        CancellationToken cancellationToken)
    {
        // ZKTeco COM (zkemkeeper) cần STA — không dùng thread-pool MTA.
        var tcs = new TaskCompletionSource<IReadOnlyList<RawAttendanceLog>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var logs = ReadLogsCore(device, fromInclusive, toInclusive, cancellationToken);
                tcs.TrySetResult(logs);
            }
            catch (OperationCanceledException ex)
            {
                tcs.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        thread.IsBackground = true;
#pragma warning disable CA1416
        thread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
        thread.Name = $"ZkTeco-STA-{device.AttDeviceId}";
        thread.Start();

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        return tcs.Task;
    }

    private IReadOnlyList<RawAttendanceLog> ReadLogsCore(
        AttendanceDevice device,
        DateTime fromInclusive,
        DateTime toInclusive,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ZKTeco SDK yêu cầu Windows (x86).");
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureNativeSdkPath();

        // Giống ZkDeviceService: 1 instance CZKEMClass / lần đọc, Connect_Net(ip, port), không password.
        var zkem = new zkemkeeper.CZKEMClass();
        var machineNumber = device.MachineNumber <= 0 ? 1 : device.MachineNumber;
        var ip = (device.IpAddress ?? string.Empty).Trim();
        var port = device.PortNo <= 0 ? 4370 : device.PortNo;
        var connected = false;

        try
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                throw new InvalidOperationException($"Máy {device.AttDeviceId} chưa có địa chỉ IP.");
            }

            connected = zkem.Connect_Net(ip, port);
            if (!connected)
            {
                var errorCode = 0;
                zkem.GetLastError(ref errorCode);
                throw new InvalidOperationException(
                    $"Không kết nối được máy {device.AttDeviceId} tại {ip}:{port}. ErrorCode={errorCode}");
            }

            _logger.LogInformation(
                "Đã kết nối máy ZKTeco {DeviceId} {Ip}:{Port}",
                device.AttDeviceId,
                ip,
                port);

            // Không khóa máy khi đọc log: nhiều NV — không được tạm chặn chấm vân tay / thẻ.
            // zkem.EnableDevice(machineNumber, false);
            cancellationToken.ThrowIfCancellationRequested();
            var logs = ReadLogsInRange(zkem, machineNumber, fromInclusive, toInclusive, device.AttDeviceId);
            _logger.LogInformation(
                "Máy {DeviceId} SDK trả về {Count} bản ghi thô",
                device.AttDeviceId,
                logs.Count);
            // zkem.EnableDevice(machineNumber, true);
            return logs;
        }
        finally
        {
            if (connected)
            {
                try { zkem.Disconnect(); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Lỗi khi ngắt kết nối máy {DeviceId}", device.AttDeviceId);
                }
            }

            // Không ReleaseComObject — POC Winform cũng chỉ Disconnect; RCW để GC thu.
        }
    }

    private static void EnsureNativeSdkPath()
    {
        if (Interlocked.Exchange(ref _nativePathReady, 1) == 1)
        {
            return;
        }

        var baseDir = AppContext.BaseDirectory;
        // Ưu tiên thư mục chạy app (dll đã copy cạnh exe). Fallback Libs\ nếu còn bản cũ.
        var candidates = new[]
        {
            baseDir,
            Path.Combine(baseDir, "Libs")
        };

        foreach (var dir in candidates)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            if (File.Exists(Path.Combine(dir, "tcpcomm.dll")) || File.Exists(Path.Combine(dir, "zkemkeeper.dll")))
            {
                SetDllDirectory(dir);
                var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                if (!path.Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Any(p => string.Equals(p, dir, StringComparison.OrdinalIgnoreCase)))
                {
                    Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + path);
                }

                return;
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    private static List<RawAttendanceLog> ReadLogsInRange(
        zkemkeeper.CZKEMClass device,
        int machineNumber,
        DateTime fromDate,
        DateTime toDate,
        int attDeviceId)
    {
        var timeFrom = fromDate.Date.ToString("yyyy-MM-dd") + " 00:00:00";
        var timeTo = toDate.Date.ToString("yyyy-MM-dd") + " 23:59:59";

        var logs = new List<RawAttendanceLog>();
        if (device.ReadTimeGLogData(machineNumber, timeFrom, timeTo))
        {
            CollectLogs(device, machineNumber, logs, fromDate, toDate, attDeviceId);
        }

        if (logs.Count == 0 && device.ReadGeneralLogData(machineNumber))
        {
            logs.Clear();
            CollectLogs(device, machineNumber, logs, fromDate, toDate, attDeviceId);
        }

        if (logs.Count == 0 && device.ReadAllGLogData(machineNumber))
        {
            logs.Clear();
            CollectLogs(device, machineNumber, logs, fromDate, toDate, attDeviceId);
        }

        return logs;
    }

    private static void CollectLogs(
        zkemkeeper.CZKEMClass device,
        int machineNumber,
        List<RawAttendanceLog> logs,
        DateTime fromDate,
        DateTime toDate,
        int attDeviceId)
    {
        string enrollNumber = string.Empty;
        int verifyMode = 0;
        int inOutMode = 0;
        int year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0;
        int workCode = 0;

        while (device.SSR_GetGeneralLogData(
            machineNumber,
            out enrollNumber,
            out verifyMode,
            out inOutMode,
            out year,
            out month,
            out day,
            out hour,
            out minute,
            out second,
            ref workCode))
        {
            var logTime = new DateTime(year, month, day, hour, minute, second);
            if (logTime < fromDate || logTime > toDate || string.IsNullOrWhiteSpace(enrollNumber))
            {
                continue;
            }

            logs.Add(new RawAttendanceLog
            {
                EnrollNumber = enrollNumber,
                LogTime = logTime,
                AttDeviceId = attDeviceId
            });
        }
    }
}
