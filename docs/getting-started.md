# Hướng dẫn người mới — MRDAttendanceCollector

Tài liệu này dành cho người **lần đầu** làm việc với Windows Service / Worker Service .NET. Đọc theo thứ tự: khái niệm → cách chạy thử → xem log → file cần quan tâm → luồng hoạt động.

---

## 1. Windows Service là gì? (trong dự án này)

| Khái niệm | Ý nghĩa đơn giản |
| --------- | ---------------- |
| **Console app** | Chạy khi bạn mở terminal, tắt khi đóng cửa sổ |
| **Windows Service** | Chạy nền trên Windows, có thể **tự start khi máy boot**, không cần đăng nhập UI |
| **Worker Service (.NET)** | Mẫu dự án .NET dùng để viết service dài hạn: có `Host`, DI, Logging, cấu hình `appsettings.json` |

`MRDAttendanceCollector` là **Worker Service .NET 8** đã bật `AddWindowsService()`:

- **Khi debug / chạy thử:** chạy như console → log hiện ngay trên terminal (Visual Studio F5 hoặc `dotnet run`).
- **Khi cài lên máy chủ:** đăng ký bằng `sc.exe` / PowerShell → chạy nền, log vào **Event Viewer**.

Collector **luôn** gọi Backend HTTP + đọc máy ZKTeco thật. Cần Backend chạy và máy ACTIVE trên Web trước khi test.

---

## 2. Dự án này làm gì?

Mỗi lần đến lịch **Cron**:

1. Kiểm tra có nằm trong **Blackout** (giờ cấm sync) không → nếu có thì **bỏ qua**.
2. Gọi Backend lấy danh sách máy chấm công đang ACTIVE (`fnGetCollectorDevices`).
3. Với **từng máy một** (tuần tự): kết nối SDK ZKTeco → đọc log → chuẩn hóa `WORK_DATE` → `fnPostCollectorRawLogs` → `fnPostCollectorSyncResult` → máy tiếp theo.
4. Hosted drain (song song với Cron): định kỳ gọi `fnDrainAttReprocessQueue` để backend materialize bảng công (Collector **không** scoring).

Collector **chỉ thu thập dữ liệu thô** (+ trigger drain); không tính bảng công trong process này.

```text
[Cron tick]
    │
    ├─ Trong Blackout? ──Yes──► Bỏ qua lần này
    │
    No
    ▼
[Lấy danh sách máy] ──► [Đọc tuần tự từng máy] ──► [Đẩy API] ──► [Ghi kết quả sync]
```

---

## 3. Chạy thử lần đầu (khuyến nghị: console / Visual Studio F5)

### 3.1. Điều kiện

- Cài [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (hoặc SDK mới hơn, project dùng `net8.0`).
- Backend `MRDMobileApplication` lắng nghe `Backend:BaseUrl` (mặc định `http://localhost:54989/`).
- Đăng ký COM SDK x86 (`Libs/Register_SDK_x86.bat` Run as Administrator).
- Máy chấm công ACTIVE trên Web; TCP tới IP:Port (thường 4370).
- Mở PowerShell / Terminal tại thư mục repo (hoặc mở solution trong Visual Studio).

### 3.2. Build

```powershell
cd D:\CuongLD\WorkSpace\projects\TimeLeave\MRDAttendanceCollector

dotnet build .\MRDAttendanceCollector.sln -c Debug
```

Thấy `Build succeeded` là ổn.

### 3.3. Chạy console (dễ xem log nhất)

```powershell
dotnet run --project .\MRDAttendanceCollector\MRDAttendanceCollector.csproj
```

Hoặc mở solution trong Visual Studio / Cursor → chọn profile `MRDAttendanceCollector` → **F5**.

`Program.cs` đã ép `Console.OutputEncoding = UTF-8` để log tiếng Việt có dấu hiển thị đúng. Nếu vẫn thấy `?` trên CMD cũ:

```powershell
chcp 65001
dotnet run --project .\MRDAttendanceCollector\MRDAttendanceCollector.csproj
```

Font terminal nên hỗ trợ Unicode (Cascadia Mono / Consolas).

**Môi trường Development** (`launchSettings.json` đặt `DOTNET_ENVIRONMENT=Development`) sẽ đọc thêm `appsettings.Development.json` (blackout gần như tắt để dễ test ban ngày).

### 3.4. Dừng

Trong cửa sổ console: nhấn **Ctrl+C**. Đây cũng là cách kiểm tra **graceful shutdown** (service nhận tín hiệu dừng, hủy token, không treo vô hạn).

---

## 4. Xem log như thế nào?

### A. Khi chạy console (`dotnet run` / F5) — dùng khi học / debug

Log in thẳng ra terminal, ví dụ:

```text
info: ...CronSyncHostedService[0]
      Dịch vụ lịch Cron đã khởi động. Cron=0 */1 * * * * ...
info: ...AttendanceSyncService[0]
      Bắt đầu chu kỳ đồng bộ
info: ...ZkTecoDeviceSdkAdapter[0]
      Đã kết nối máy ZKTeco ...
info: ...AttendanceSyncService[0]
      Máy 1 THÀNH CÔNG Đọc=2 Thêm mới=2 Trùng=0
warn: ...CronSyncHostedService[0]
      Bỏ qua đồng bộ vì đang trong khoảng Blackout. Giờ local=11:33:00
```

| Mức log | Ý nghĩa |
| ------- | ------- |
| `info` / Information | Bình thường: bắt đầu chu kỳ, sync thành công |
| `warn` / Warning | Cảnh báo: đang blackout, retry API/máy |
| `fail` / Error | Lỗi: hết retry, timeout, exception |

Chỉnh độ chi tiết trong `appsettings.json` → `Logging:LogLevel`.

Khi chạy Windows Service, Event Viewer dùng Unicode — tiếng Việt thường hiển thị đúng mà không cần `chcp`.

### B. Khi đã cài Windows Service — dùng trên máy server

Service **không mở cửa sổ console**. Log ghi vào **Windows Event Log**:

1. Mở **Event Viewer** (`eventvwr.msc`).
2. **Windows Logs** → **Application**.
3. Lọc theo Source: **`MRDAttendanceCollector`**.

Hoặc PowerShell:

```powershell
Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'MRDAttendanceCollector' } -MaxEvents 30 |
  Format-List TimeCreated, LevelDisplayName, Message
```

Nếu chưa thấy source: chạy service ít nhất một lần (hoặc tạo source Event Log khi chạy lần đầu với quyền phù hợp).

### C. Kiểm tra service đang chạy

```powershell
Get-Service MRDAttendanceCollector
# Status = Running / Stopped
```

Chi tiết cài / gỡ service: [install-windows-service.md](install-windows-service.md).

---

## 5. File / thư mục cần quan tâm (ưu tiên đọc)

Đọc theo thứ tự này sẽ nắm nhanh 80% hệ thống:

| Ưu tiên | File / thư mục | Vì sao quan trọng |
| ------- | -------------- | ----------------- |
| 1 | `MRDAttendanceCollector/appsettings.json` | Cron, Blackout, Backend BaseUrl |
| 1 | `MRDAttendanceCollector/appsettings.Development.json` | Cấu hình khi debug (blackout test) |
| 2 | `MRDAttendanceCollector/Program.cs` | Điểm vào: đăng ký DI, Windows Service, Logging |
| 3 | `Scheduling/CronSyncHostedService.cs` | Vòng lặp: chờ Cron → check blackout → gọi sync |
| 4 | `Models/AttendanceSyncService.cs` | Nghiệp vụ 1 chu kỳ: lấy máy → đọc → đẩy → cập nhật mốc |
| 5 | `Models/BlackoutService.cs` | Logic khoảng giờ không sync |
| 6 | `Backend/HttpAttendanceBackendClient.cs` | Gọi Backend AttendanceAPI |
| 7 | `Sdk/ZkTecoDeviceSdkAdapter.cs` | Đọc máy ZKTeco thật (COM SDK) |
| 8 | `Configuration/Options.cs` | Class map từ `appsettings` |
| 9 | `docs/api-contracts.md` | Contract API Collector + drain |
| — | `Libs/` | DLL ZKTeco; đăng ký COM trước khi chạy |
| — | `appsettings.Production.json` | Cấu hình khi cài Windows Service |

Các file entity (`AttendanceDevice`, `RawAttendanceLog`, …) trong `Models/` là kiểu dữ liệu; đọc khi cần hiểu payload.

### Cấu trúc gọn

```text
MRDAttendanceCollector/
├── Program.cs                 ← vào đây trước
├── appsettings*.json          ← chỉnh cấu hình ở đây
├── Configuration/             ← Options (class cấu hình)
├── Scheduling/                ← Cron hosted service
├── Models/                    ← Nghiệp vụ sync + entity (giống MRDMobileApplication)
├── Backend/                   ← HTTP API client
├── Sdk/                       ← ZKTeco adapter
└── Libs/                      ← SDK nhà sản xuất
```

---

## 6. Luồng code (map sang file)

```text
Program.cs
  └─ đăng ký CronSyncHostedService (BackgroundService)

CronSyncHostedService
  ├─ chờ đến mốc Cron (thư viện Cronos)
  ├─ BlackoutService.IsInBlackout? → skip
  └─ AttendanceSyncService.RunCycleAsync()

AttendanceSyncService
  ├─ IAttendanceBackendClient.GetActiveDevicesAsync()
  │     └─ HttpAttendanceBackendClient
  ├─ với từng máy (tuần tự — xong máy A mới tới máy B):
  │     ├─ IDeviceSdkAdapter.ReadLogsAsync()  (ZKTeco chạy thread STA)
  │     │     └─ ZkTecoDeviceSdkAdapter
  │     ├─ chuẩn hóa WORK_DATE (WorkDateHelper — quy tắc 06:00)
  │     ├─ PostRawLogsAsync
  │     └─ PostSyncResultAsync
  └─ retry / timeout theo Scheduler options

AttReprocessDrainHostedService (cùng process)
  └─ định kỳ DrainReprocessQueueAsync → backend materialize TBL_ATT_DAILY_RESULT
```

---

## 7. Cấu hình hay chỉnh khi học

### Backend

```json
"Backend": {
  "BaseUrl": "http://localhost:54989/",
  "ApiKey": "",
  "TimeoutSeconds": 60
}
```

### Cron quá chậm khi test?

Mặc định **mỗi phút**: `0 */1 * * * *`  
(Cron 6 field: **giây phút giờ ngày tháng thứ**.)  
Khi debug có thể tạm đổi sang `0/15 * * * * *` (mỗi 15 giây).

### Đang bị “Bỏ qua đồng bộ vì đang trong khoảng Blackout”?

Giờ máy nằm trong `BlackoutWindows` (vd. 11:30–12:30). Khi debug, `appsettings.Development.json` đã ghi đè blackout sang khoảng 03:00–03:01.  
**Lưu ý:** mảng config gộp theo index — không chỉ đặt `[]` để xóa blackout từ file gốc.

### Checklist trước khi chạy thật

1. API Backend đang chạy — xem [api-contracts.md](api-contracts.md).
2. `Backend:BaseUrl` đúng (mặc định `http://localhost:54989/`).
3. Đăng ký COM SDK x86 (`Libs/SDK-SETUP.txt` / `Register_SDK_x86.bat` **Run as Administrator**).
4. Máy reachable TCP (thường port **4370**); cấu hình ACTIVE trên Web.
5. Mã trên máy (`ENROLL_NUMBER`) = `EMP_NUMBER` trong `TBL_EMPLOYEE`.

---

## 8. Checklist “tôi đã hiểu chưa?”

- [ ] Chạy được `dotnet run` / F5 và thấy log `Dịch vụ lịch Cron đã khởi động`
- [ ] Thấy ít nhất một chu kỳ `Bắt đầu chu kỳ đồng bộ` → kết nối máy / `THÀNH CÔNG`
- [ ] Biết Ctrl+C dừng service
- [ ] Biết chỉnh Cron / Blackout / `Backend:BaseUrl` trong `appsettings*.json`
- [ ] Biết file nào chứa lịch (`CronSyncHostedService`) và file nào làm nghiệp vụ (`AttendanceSyncService`)
- [ ] (Tuỳ chọn) Biết mở Event Viewer khi cài Windows Service

---

## 9. Tài liệu liên quan

| Tài liệu | Nội dung |
| -------- | -------- |
| [README.md](../README.md) | Tổng quan repo |
| [install-windows-service.md](install-windows-service.md) | Publish + `sc.exe` / PowerShell cài service |
| [api-contracts.md](api-contracts.md) | JSON request/response API Collector |

Nếu lỗi build / SDK COM / không thấy Event Log: ghi lại **lệnh đã chạy**, **nội dung log**, và **môi trường** (Debug console hay Windows Service).
