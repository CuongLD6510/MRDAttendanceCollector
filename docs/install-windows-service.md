# Cài đặt Windows Service — MRDAttendanceCollector

> **Deploy đầy đủ trên server** (config → publish → SDK → service → checklist): xem **[deploy-collector-server.md](deploy-collector-server.md)**.
>
> Người mới: nên chạy thử dạng **console** và đọc [getting-started.md](getting-started.md) trước khi cài service lên máy chủ.

Chạy trên máy Windows (x86 / WOW64) có quyền Administrator.

## 1. Đăng ký ZKTeco SDK (COM)

```powershell
cd D:\CuongLD\WorkSpace\projects\TimeLeave\MRDAttendanceCollector\MRDAttendanceCollector\Libs
# Chạy CMD/PowerShell với quyền Administrator:
%windir%\SysWOW64\regsvr32.exe zkemkeeper.dll
```

Hoặc click phải `Register_SDK_x86.bat` → Run as administrator.

Chi tiết: `Libs/SDK-SETUP.txt`.

## 2. Publish

```powershell
cd D:\CuongLD\WorkSpace\projects\TimeLeave\MRDAttendanceCollector

dotnet publish .\MRDAttendanceCollector\MRDAttendanceCollector.csproj `
  -c Release `
  -r win-x86 `
  --self-contained false `
  -o C:\Services\MRDAttendanceCollector
```

Chỉnh cấu hình trong thư mục publish:

- Copy / merge từ `appsettings.Production.json`: `Backend:BaseUrl` đúng host API (mặc định local `http://localhost:54989/`).
- Cron, `Reprocess:Enabled`, optional `ApiKey`. Giờ tạm dừng đồng bộ cấu hình trên Web (Cấu hình chung).
- Đặt biến môi trường service (khuyến nghị): `DOTNET_ENVIRONMENT=Production`.

## 3. Tạo service (`sc.exe`)

```powershell
sc.exe create MRDAttendanceCollector `
  binPath= "C:\Services\MRDAttendanceCollector\MRDAttendanceCollector.exe" `
  start= auto `
  DisplayName= "MRD Attendance Collector"

sc.exe description MRDAttendanceCollector "Dong bo du lieu may cham cong ZKTeco theo lich Cron"
sc.exe start MRDAttendanceCollector
```

Lưu ý: sau `binPath=` và `start=` phải có **một dấu cách**.

## 4. PowerShell (thay thế)

```powershell
New-Service -Name "MRDAttendanceCollector" `
  -BinaryPathName "C:\Services\MRDAttendanceCollector\MRDAttendanceCollector.exe" `
  -DisplayName "MRD Attendance Collector" `
  -StartupType Automatic `
  -Description "Dong bo du lieu may cham cong ZKTeco theo lich Cron"

Start-Service -Name "MRDAttendanceCollector"
```

## 5. Kiểm tra

```powershell
Get-Service MRDAttendanceCollector
# Log console không có khi chạy service — xem Event Viewer:
# Windows Logs → Application → Source: MRDAttendanceCollector
```

Khi service start, log đầu tiên dạng:

```text
Môi trường=Production. Đang đọc appsettings.json + appsettings.Production.json (nếu có). Cron=0 */15 * * * * Backend=http://...
```

- `Môi trường=Production` → đang dùng **`appsettings.Production.json`**
- `Môi trường=Development` → đang dùng **`appsettings.Development.json`**

## 6. Dừng / gỡ

```powershell
sc.exe stop MRDAttendanceCollector
sc.exe delete MRDAttendanceCollector
```

Hoặc:

```powershell
Stop-Service MRDAttendanceCollector
Remove-Service MRDAttendanceCollector   # PowerShell 6+
# Windows PowerShell 5.1: sc.exe delete ...
```

## 7. Graceful shutdown

Khi Stop service, host hủy `CancellationToken`; Cron loop và job sync đang chạy sẽ dừng theo token (không treo vô hạn).
