# MRDAttendanceCollector

Windows Service (.NET 8 Worker) thu thập log chấm công từ máy ZKTeco theo lịch Cron, đẩy về Backend qua HTTP API. Cùng process còn trigger **drain** hàng đợi tính lại bảng công (Phase 2) qua API backend.

**Người mới bắt đầu?** Đọc trước: **[docs/getting-started.md](docs/getting-started.md)**.

Tham chiếu nghiệp vụ:

- [Blueprint Attendance Management](../ZktecoWinformTest/docs/attendance-management/blueprint.md)
- [DB Diagram](../ZktecoWinformTest/docs/db_diagrams/db_diagram_attendance_management.md)

## Phạm vi

- Windows Service + Cron + Blackout Time
- Đọc **tuần tự từng máy** (SDK → `fnPostCollectorRawLogs` → `fnPostCollectorSyncResult`)
- ZKTeco SDK adapter (x86 / COM `zkemkeeper`, thread **STA**)
- HTTP API client theo [docs/api-contracts.md](docs/api-contracts.md) — Backend `AttendanceAPI`
- Hosted drain `fnDrainAttReprocessQueue` (config `Reprocess`)
- Luôn chạy **thật**: HTTP backend + máy ZKTeco (không có mock)

## Cấu trúc

```text
MRDAttendanceCollector/
  MRDAttendanceCollector.sln
  MRDAttendanceCollector/
    Program.cs
    appsettings.json / appsettings.Development.json / appsettings.Production.json
    Models/
    Configuration/
    Backend/
    Sdk/
    Scheduling/       # Cron sync + Reprocess drain
    Libs/             # ZKTeco COM / native DLLs
  docs/
```

## Yêu cầu

- .NET 8 SDK
- Windows x86 (`PlatformTarget` / `win-x86`) cho ZKTeco COM
- Đăng ký COM: `MRDAttendanceCollector/Libs/SDK-SETUP.txt`
- Backend `MRDMobileApplication` chạy được (`Backend:BaseUrl`, mặc định `http://localhost:54989/`)
- Máy ACTIVE trên Web `AttendanceDeviceConfig`

## Build & chạy (debug console — Visual Studio / F5)

Không cần cài Windows Service để test. F5 hoặc `dotnet run` chạy như console, log ra terminal:

```powershell
cd D:\CuongLD\WorkSpace\projects\TimeLeave\MRDAttendanceCollector
dotnet build .\MRDAttendanceCollector.sln -c Debug
dotnet run --project .\MRDAttendanceCollector\MRDAttendanceCollector.csproj
```

Hoặc mở solution → chọn profile `MRDAttendanceCollector` → **F5**.

`launchSettings.json` đặt `DOTNET_ENVIRONMENT=Development` (blackout gần tắt để dễ test ban ngày).

## Go-live checklist

1. Đăng ký SDK x86 (`Libs/Register_SDK_x86.bat` Run as Administrator).
2. Publish `win-x86` Release → cài Windows Service ([install-windows-service.md](docs/install-windows-service.md)).
3. Trong thư mục publish: chỉnh `Backend:BaseUrl` trỏ API thật; optional `ApiKey`.
4. Web: tạo máy ACTIVE (IP/Port/MachineNumber đúng).
5. `ENROLL_NUMBER` trên máy = `TBL_EMPLOYEE.EMP_NUMBER`.
6. Kiểm tra Event Log / `TBL_ATT_SYNC_JOB_LOG` / `TBL_ATT_RAW_LOG` → mở màn Bảng công.

## Cài Windows Service

Xem [install-windows-service.md](docs/install-windows-service.md).

## Lưu ý cấu hình mảng (`BlackoutWindows`)

`appsettings.json` và `appsettings.Development.json` gộp theo index. Để tắt blackout khi debug, ghi đè đủ các phần tử, không chỉ đặt `[]`.
