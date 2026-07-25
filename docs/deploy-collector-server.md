# Deploy MRDAttendanceCollector lên server (Windows Service)

Hướng dẫn **từng bước**: chỉnh config Production → publish → cài SDK → đăng ký Windows Service → kiểm tra trên máy chủ.

Tài liệu liên quan:

- [install-windows-service.md](install-windows-service.md) — lệnh `sc.exe` / PowerShell ngắn
- [getting-started.md](getting-started.md) — chạy thử console trên máy dev
- [api-contracts.md](api-contracts.md) — contract API Backend

---

## 0. Chuẩn bị trước khi deploy

| Hạng mục | Yêu cầu |
| -------- | ------- |
| OS server | Windows (64-bit OK; process collector chạy **x86 / WOW64**) |
| Quyền | Administrator khi đăng ký COM SDK và tạo Windows Service |
| Runtime | [.NET 8 Runtime (x86)](https://dotnet.microsoft.com/download/dotnet/8.0) — vì publish `--self-contained false` |
| SDK build (máy publish) | .NET 8 SDK nếu publish từ máy dev |
| Backend | `MRDMobileApplication` (AttendanceAPI) đã chạy, collector gọi được qua HTTP |
| Mạng | Server collector → Backend URL; server collector → IP:Port máy chấm công (thường **4370**) |
| Web HR | Đã có máy ACTIVE (IP / Port / MachineNumber đúng); `ENROLL_NUMBER` máy = `EMP_NUMBER` |

**Đường dẫn khuyến nghị trên server:**

```text
C:\Services\MRDAttendanceCollector\
```

---

## 1. Chỉnh config Production (trước hoặc sau publish)

File nguồn trong repo:

`MRDAttendanceCollector/appsettings.Production.json`

Khi service chạy với `DOTNET_ENVIRONMENT=Production`, host đọc lần lượt:

1. `appsettings.json`
2. `appsettings.Production.json` (ghi đè)

### 1.1. Giá trị Production hiện tại (tham chiếu)

| Section | Key | Giá trị khuyến nghị Production | Ghi chú |
| ------- | --- | ----------------------------- | ------- |
| `Scheduler` | `Cron` | `0 */15 * * * *` | Đọc máy **mỗi 15 phút** (Cron 6 field: giây phút giờ …) |
| `Scheduler` | `TimeZone` | `SE Asia Standard Time` | Giờ Việt Nam |
| `Scheduler` | `JobTimeoutSeconds` | `180` | Timeout 1 máy |
| `Scheduler` | `DefaultOverlapMinutes` | `30` | Đọc chồng để tránh miss biên |
| `Scheduler` | `InitialSyncFromDate` | (fallback) | Máy mới ưu tiên `INITIAL_SYNC_FROM` từ API = **đầu kỳ lương − 1 ngày, 23:59** |
| `Reprocess` | `Enabled` | `true` | Giữ bật drain bảng công |
| `Reprocess` | `IntervalSeconds` | `15` | **Giữ nguyên** theo cấu hình đã chốt |
| `Reprocess` | `MaxItemsPerDrain` | `15` | **Giữ nguyên** |
| `Reprocess` | `TimeoutSeconds` | `300` | **Giữ nguyên** |
| `Backend` | `BaseUrl` | URL API thật trên server | Bắt buộc đổi — ví dụ `http://192.168.1.10:54989/` |
| `Backend` | `ApiKey` | Theo cấu hình Backend (nếu có) | Header `X-Api-Key` |
| `Backend` | `TimeoutSeconds` | `90` | Timeout HTTP thường (drain dùng `Reprocess:TimeoutSeconds`) |

### 1.2. Mẫu chỉnh trên server (sau publish)

Mở file trong thư mục publish:

`C:\Services\MRDAttendanceCollector\appsettings.Production.json`

Ví dụ tối thiểu cần sửa cho môi trường thật:

```json
{
  "Scheduler": {
    "Cron": "0 */15 * * * *",
    "TimeZone": "SE Asia Standard Time",
    "JobTimeoutSeconds": 180,
    "RetryMaxAttempts": 3,
    "RetryDelaySeconds": 10,
    "DefaultOverlapMinutes": 30
  },
  "Reprocess": {
    "IntervalSeconds": 15,
    "MaxItemsPerDrain": 15,
    "TimeoutSeconds": 300,
    "Enabled": true
  },
  "Backend": {
    "BaseUrl": "http://YOUR_BACKEND_HOST:54989/",
    "ApiKey": "",
    "TimeoutSeconds": 90
  }
}
```

**Lưu ý:**

- `BaseUrl` nên kết thúc bằng `/`.
- Giờ tạm dừng đồng bộ (**Blackout**) cấu hình trên Web → **Cấu hình chung**, không đặt trong appsettings.
- Máy mới (chưa sync): Backend trả `INITIAL_SYNC_FROM` trong `fnGetCollectorDevices` — không cần chỉnh tay ngày đầu năm.

---

## 2. Publish từ máy có source

Mở PowerShell tại thư mục solution collector:

```powershell
cd D:\CuongLD\WorkSpace\projects\TimeLeave\MRDAttendanceCollector

# (Tuỳ chọn) kiểm tra build trước
dotnet build .\MRDAttendanceCollector.sln -c Release

# Publish win-x86 — bắt buộc x86 vì ZKTeco COM
dotnet publish .\MRDAttendanceCollector\MRDAttendanceCollector.csproj `
  -c Release `
  -r win-x86 `
  --self-contained false `
  -o C:\Services\MRDAttendanceCollector
```

Nếu publish sang thư mục tạm rồi copy lên server:

```powershell
dotnet publish .\MRDAttendanceCollector\MRDAttendanceCollector.csproj `
  -c Release `
  -r win-x86 `
  --self-contained false `
  -o D:\Publish\MRDAttendanceCollector
```

Sau đó copy toàn bộ nội dung `D:\Publish\MRDAttendanceCollector` lên server (ví dụ `C:\Services\MRDAttendanceCollector`).

### 2.1. Kiểm tra thư mục publish có đủ file

Trong thư mục output phải thấy gần đúng:

| File / nhóm | Mục đích |
| ----------- | -------- |
| `MRDAttendanceCollector.exe` | Process Windows Service |
| `MRDAttendanceCollector.dll` + deps / runtimeconfig | .NET Worker |
| `appsettings.json` | Config gốc |
| `appsettings.Production.json` | Config Production |
| `zkemkeeper.dll` + các DLL SDK (comms, tcpcomm, …) | ZKTeco native — **cạnh .exe**, không chỉ trong `Libs\` |
| `Interop.zkemkeeper.dll` | Interop COM |
| `Register_SDK_x86.bat`, `SDK-SETUP.txt` | Đăng ký COM trên server |

Nếu thiếu DLL SDK cạnh `.exe`, kết nối máy thường lỗi SDK (ví dụ ErrorCode `-201`).

---

## 3. Cài trên server — .NET Runtime & SDK COM

### 3.1. Cài .NET 8 Runtime (x86)

Vì publish **không** self-contained:

1. Tải **.NET 8 Desktop/ASP.NET / Runtime x86** phù hợp (ít nhất **.NET Runtime 8.x - x86**).
2. Cài trên server.
3. Kiểm tra (tuỳ phiên bản có thể chỉ thấy x64; quan trọng là đã cài gói **x86**):

```powershell
dotnet --list-runtimes
```

### 3.2. Đăng ký ZKTeco COM (bắt buộc, chạy 1 lần / mỗi máy chủ)

Trong thư mục publish (có `zkemkeeper.dll`):

```powershell
# PowerShell / CMD — Run as Administrator
cd C:\Services\MRDAttendanceCollector
%windir%\SysWOW64\regsvr32.exe zkemkeeper.dll
```

Hoặc click phải `Register_SDK_x86.bat` → **Run as administrator**.

Thành công sẽ có hộp thoại / thông báo đăng ký DLL thành công.

> Dùng `SysWOW64\regsvr32` (đăng ký COM 32-bit). Không dùng `System32\regsvr32` cho SDK x86.

---

## 4. Chỉnh config trên server & biến môi trường

1. Sửa `appsettings.Production.json` — đặc biệt **`Backend:BaseUrl`** trỏ đúng API.
2. (Khuyến nghị) Đặt biến môi trường cho service / máy:

```text
DOTNET_ENVIRONMENT=Production
```

Cách đặt cho Windows Service (sau khi đã tạo service — xem bước 5):

```powershell
# Gán biến môi trường hệ thống (cần Admin; khởi động lại service sau đó)
[System.Environment]::SetEnvironmentVariable(
  "DOTNET_ENVIRONMENT",
  "Production",
  "Machine")
```

Hoặc trong **System Properties → Environment Variables** thêm `DOTNET_ENVIRONMENT=Production`, rồi restart service.

3. Kiểm tra nhanh Backend từ server collector:

```powershell
# Thay URL cho đúng
Invoke-WebRequest -Uri "http://YOUR_BACKEND_HOST:54989/" -UseBasicParsing -TimeoutSec 10
```

(Hoặc gọi thử API collector nếu đã biết route; miễn là TCP/HTTP tới Backend OK.)

---

## 5. Đăng ký và chạy Windows Service

### 5.1. Tạo service lần đầu (`sc.exe`)

**CMD/PowerShell Administrator:**

```powershell
sc.exe create MRDAttendanceCollector `
  binPath= "C:\Services\MRDAttendanceCollector\MRDAttendanceCollector.exe" `
  start= auto `
  DisplayName= "MRD Attendance Collector"

sc.exe description MRDAttendanceCollector "Dong bo du lieu may cham cong ZKTeco theo lich Cron"

sc.exe start MRDAttendanceCollector
```

**Lưu ý cú pháp `sc.exe`:** sau `binPath=` và `start=` phải có **một dấu cách**.

### 5.2. Cách PowerShell (thay thế)

```powershell
New-Service -Name "MRDAttendanceCollector" `
  -BinaryPathName "C:\Services\MRDAttendanceCollector\MRDAttendanceCollector.exe" `
  -DisplayName "MRD Attendance Collector" `
  -StartupType Automatic `
  -Description "Dong bo du lieu may cham cong ZKTeco theo lich Cron"

Start-Service -Name "MRDAttendanceCollector"
```

### 5.3. Kiểm tra trạng thái

```powershell
Get-Service MRDAttendanceCollector
# Status phải là Running
```

---

## 6. Kiểm tra sau khi chạy

### 6.1. Event Viewer (log chính khi chạy service)

Khi chạy Windows Service **không** có console. Xem log tại:

**Event Viewer** → **Windows Logs** → **Application** → Source: **`MRDAttendanceCollector`**

Log kỳ vọng khi ổn:

- `Dịch vụ lịch Cron đã khởi động. Cron=0 */15 * * * * ...`
- `AttReprocessDrainHostedService đã khởi động...`
- Mỗi chu kỳ: `Bắt đầu chu kỳ đồng bộ` → `Máy ... khoảng đọc ...` → `THÀNH CÔNG` / lỗi rõ ràng
- Máy mới: mốc đọc theo `INITIAL_SYNC_FROM` (đầu kỳ − 1, 23:59), không phải đầu năm

### 6.2. Kiểm tra dữ liệu / Web

| Kiểm tra | Nơi xem |
| -------- | ------- |
| Job sync | Bảng `TBL_ATT_SYNC_JOB_LOG` / log Event Viewer |
| Raw log | `TBL_ATT_RAW_LOG` |
| Máy ACTIVE | Web cấu hình máy chấm công |
| Drain bảng công | Monitor hàng đợi reprocess / bảng công cập nhật |

### 6.3. Chạy thử console trên server (debug nhanh)

Nếu service lỗi khó đọc Event Log, tạm dừng service rồi chạy tay:

```powershell
Stop-Service MRDAttendanceCollector -ErrorAction SilentlyContinue

cd C:\Services\MRDAttendanceCollector
$env:DOTNET_ENVIRONMENT = "Production"
.\MRDAttendanceCollector.exe
```

Log ra cửa sổ console. Dừng bằng **Ctrl+C**. Xong thì `Start-Service` lại.

---

## 7. Cập nhật phiên bản (redeploy)

Khi có bản build mới:

```powershell
# 1. Dừng service
Stop-Service MRDAttendanceCollector

# 2. Publish / copy file mới vào C:\Services\MRDAttendanceCollector
#    Giữ lại appsettings.Production.json đã chỉnh BaseUrl trên server
#    (hoặc merge lại BaseUrl sau khi ghi đè)

# 3. (Nếu SDK DLL đổi) đăng ký lại COM nếu cần
# cd C:\Services\MRDAttendanceCollector
# %windir%\SysWOW64\regsvr32.exe zkemkeeper.dll

# 4. Start lại
Start-Service MRDAttendanceCollector
Get-Service MRDAttendanceCollector
```

**Mẹo:** Backup `appsettings.Production.json` trước khi copy đè thư mục publish.

---

## 8. Dừng / gỡ service

```powershell
sc.exe stop MRDAttendanceCollector
sc.exe delete MRDAttendanceCollector
```

Hoặc:

```powershell
Stop-Service MRDAttendanceCollector
# PowerShell 6+: Remove-Service MRDAttendanceCollector
# Windows PowerShell 5.1: dùng sc.exe delete
sc.exe delete MRDAttendanceCollector
```

Stop service sẽ hủy `CancellationToken` — Cron / job đang chạy dừng theo token (không treo vô hạn).

---

## 9. Checklist go-live (in và đánh dấu)

- [ ] Backend AttendanceAPI chạy; `Backend:BaseUrl` đúng từ server collector
- [ ] Đã cài .NET 8 Runtime **x86** trên server
- [ ] Publish `win-x86` Release; DLL SDK nằm **cạnh** `.exe`
- [ ] Đã `regsvr32` `zkemkeeper.dll` bằng `SysWOW64` (Admin)
- [ ] `appsettings.Production.json`: Cron `0 */15 * * * *`; drain giữ `15` / `15` / `300`
- [ ] `DOTNET_ENVIRONMENT=Production`
- [ ] Windows Service `MRDAttendanceCollector` = **Running**, Startup = Automatic
- [ ] Web: máy ACTIVE, IP/Port đúng; mạng TCP 4370 OK
- [ ] Event Log có log Cron / sync thành công
- [ ] Có bản ghi mới trong raw log / sync job; drain chạy bình thường

---

## 10. Xử lý sự cố thường gặp

| Triệu chứng | Hướng xử lý |
| ----------- | ----------- |
| Service không start / exit ngay | Chạy console (mục 6.3); thiếu Runtime x86 hoặc thiếu DLL |
| Không kết nối máy / ErrorCode `-201` | Đăng ký lại COM; đảm bảo DLL SDK cạnh `.exe` (không chỉ trong thư mục con) |
| Không gọi được Backend | Kiểm tra `BaseUrl`, firewall, API Key |
| Sync nhưng không có log mới | Máy chưa ACTIVE; IP sai; blackout trên Web; ngoài cửa sổ đọc |
| Máy mới đọc quá nhiều lịch sử | Cần Backend mới có `INITIAL_SYNC_FROM`; deploy đủ Backend + Collector |
| Drain không chạy | `Reprocess:Enabled=true`; xem log `AttReprocessDrainHostedService` |
| Cron không khớp giờ VN | `TimeZone` = `SE Asia Standard Time` |

---

## Tóm tắt lệnh nhanh (copy-paste)

```powershell
# --- Máy build ---
cd D:\CuongLD\WorkSpace\projects\TimeLeave\MRDAttendanceCollector
dotnet publish .\MRDAttendanceCollector\MRDAttendanceCollector.csproj `
  -c Release -r win-x86 --self-contained false `
  -o C:\Services\MRDAttendanceCollector

# --- Server (Admin): đăng ký COM ---
cd C:\Services\MRDAttendanceCollector
%windir%\SysWOW64\regsvr32.exe zkemkeeper.dll

# --- Server (Admin): tạo + start service ---
sc.exe create MRDAttendanceCollector `
  binPath= "C:\Services\MRDAttendanceCollector\MRDAttendanceCollector.exe" `
  start= auto `
  DisplayName= "MRD Attendance Collector"
sc.exe start MRDAttendanceCollector
Get-Service MRDAttendanceCollector
```

Sau đó chỉnh `Backend:BaseUrl` trong `appsettings.Production.json` và đảm bảo `DOTNET_ENVIRONMENT=Production`.
