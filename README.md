# MRDAttendanceCollector

Windows Service (.NET 8 Worker) thu thập log chấm công từ máy ZKTeco theo lịch Cron, đẩy về Backend qua HTTP API.

**Người mới bắt đầu?** Đọc trước: **[docs/getting-started.md](docs/getting-started.md)** — khái niệm Windows Service, file cần quan tâm, chạy thử và xem log.

Tham chiếu nghiệp vụ:

- [Blueprint Attendance Management](../ZktecoWinformTest/docs/attendance-management/blueprint.md)
- [DB Diagram](../ZktecoWinformTest/docs/db_diagrams/db_diagram_attendance_management.md)

## Phạm vi

- Windows Service + Cron + Blackout Time
- ZKTeco SDK adapter (x86 / COM `zkemkeeper`)
- HTTP API client theo [docs/api-contracts.md](docs/api-contracts.md)
- **Mock mode** (`Backend:UseMock = true`) để chạy không cần Backend / máy thật
- Backend API implement sau

## Cấu trúc (mono project)

```text
MRDAttendanceCollector/
  MRDAttendanceCollector.sln
  MRDAttendanceCollector/
    Program.cs
    appsettings.json
    Models/           # Entities + sync/blackout logic + interfaces
    Configuration/    # Options (Scheduler, Backend, Blackout, Mock)
    Backend/          # HttpClient + Mock API client
    Sdk/              # ZKTeco + Mock device adapter
    Scheduling/       # Cron hosted service
    Libs/             # ZKTeco COM / native DLLs
  docs/
    getting-started.md      # Hướng dẫn người mới
    api-contracts.md
    install-windows-service.md
```

## Yêu cầu

- .NET 8 SDK
- Windows x86 (PlatformTarget = x86) cho ZKTeco COM SDK
- Đăng ký COM: `MRDAttendanceCollector/Libs/SDK-SETUP.txt`

## Build & chạy

Chi tiết + xem log: [docs/getting-started.md](docs/getting-started.md).

```powershell
cd D:\CuongLD\WorkSpace\projects\TimeLeave\MRDAttendanceCollector
dotnet build .\MRDAttendanceCollector.sln -c Debug
dotnet run --project .\MRDAttendanceCollector\MRDAttendanceCollector.csproj
```

## Cài Windows Service

Xem [docs/install-windows-service.md](docs/install-windows-service.md).

## Lưu ý cấu hình mảng (`BlackoutWindows`, `MockDevices`)

`appsettings.json` và `appsettings.Development.json` gộp theo index. Để tắt blackout khi debug, ghi đè đủ các phần tử (xem `appsettings.Development.json`), không chỉ đặt `[]`.

## Bật gọi Backend thật

1. Implement 3 API theo [docs/api-contracts.md](docs/api-contracts.md).
2. Đặt `Backend:UseMock = false` và `Backend:BaseUrl`.
3. Đăng ký ZKTeco SDK x86.
