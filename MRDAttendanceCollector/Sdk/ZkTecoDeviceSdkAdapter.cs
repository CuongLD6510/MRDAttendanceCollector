using System.Runtime.InteropServices;
using MRDAttendanceCollector.Models;

namespace MRDAttendanceCollector.Sdk;

public sealed class ZkTecoDeviceSdkAdapter : IDeviceSdkAdapter
{
    private readonly ILogger<ZkTecoDeviceSdkAdapter> _logger;

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
        return Task.Run(() => ReadLogsCore(device, fromInclusive, toInclusive, cancellationToken), cancellationToken);
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

        var zkem = new zkemkeeper.CZKEMClass();
        var machineNumber = device.MachineNumber <= 0 ? 1 : device.MachineNumber;
        var connected = false;

        try
        {
            connected = zkem.Connect_Net(device.IpAddress, device.PortNo);
            if (!connected)
            {
                var errorCode = 0;
                zkem.GetLastError(ref errorCode);
                throw new InvalidOperationException(
                    $"Không kết nối được máy {device.AttDeviceId} tại {device.IpAddress}:{device.PortNo}. ErrorCode={errorCode}");
            }

            _logger.LogInformation(
                "Đã kết nối máy ZKTeco {DeviceId} {Ip}:{Port}",
                device.AttDeviceId,
                device.IpAddress,
                device.PortNo);

            zkem.EnableDevice(machineNumber, false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var logs = ReadLogsInRange(zkem, machineNumber, fromInclusive, toInclusive, device.AttDeviceId);
                _logger.LogInformation(
                    "Máy {DeviceId} SDK trả về {Count} bản ghi thô",
                    device.AttDeviceId,
                    logs.Count);
                return logs;
            }
            finally
            {
                zkem.EnableDevice(machineNumber, true);
            }
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

            if (Marshal.IsComObject(zkem))
            {
                Marshal.ReleaseComObject(zkem);
            }
        }
    }

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
