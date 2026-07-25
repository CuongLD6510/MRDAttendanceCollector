# API Contracts — Attendance Collector

Backend (`MRDMobileApplication` / `AttendanceAPI`) implement các endpoint dưới đây. Collector luôn gọi theo contract này qua `HttpAttendanceBackendClient` (`Backend:BaseUrl`).

Quy ước response:

- `ErrCode = "1"` → thành công
- `ErrCode != "1"` → lỗi
- Giữ nguyên tên property (không camelCase)

**Client (.NET):** gửi body bằng `StringContent` + `LoadIntoBufferAsync()` (có `Content-Length`). Không dùng `JsonContent` — Web API 2 không bind `JObject` khi request `Transfer-Encoding: chunked`.

---

## 1. `POST api/AttendanceAPI/fnGetCollectorDevices` _(đã implement)_

Lấy danh sách máy `STATUS_ID = ACTIVE` kèm mốc sync.

### Request (mẫu)

```json
{}
```

### Response (mẫu)

```json
{
  "ErrCode": "1",
  "ErrMsg": "success",
  "ErrBack": "",
  "Data": {
    "Devices": [
      {
        "ATT_DEVICE_ID": 1,
        "DEVICE_NAME": "Gate A",
        "IP_ADDRESS": "192.168.1.101",
        "PORT_NO": 4370,
        "MACHINE_NUMBER": 1,
        "LAST_PROCESSED_LOG_TIME": "2026-07-16T18:30:00",
        "DEVICE_VENDOR": "ZKTeco"
      },
      {
        "ATT_DEVICE_ID": 2,
        "DEVICE_NAME": "Gate B",
        "IP_ADDRESS": "192.168.1.102",
        "PORT_NO": 4370,
        "MACHINE_NUMBER": 1,
        "LAST_PROCESSED_LOG_TIME": null,
        "DEVICE_VENDOR": "ZKTeco"
      }
    ],
    "INITIAL_SYNC_FROM": "2026-07-20T23:59:00"
  }
}
```

| Field                     | Ghi chú                                                                       |
| ------------------------- | ----------------------------------------------------------------------------- |
| `LAST_PROCESSED_LOG_TIME` | `null` nếu chưa sync lần nào → Collector dùng `Data.INITIAL_SYNC_FROM`        |
| `INITIAL_SYNC_FROM`       | (Ngày đầu kỳ lương hiện tại − 1) lúc 23:59. Ví dụ kỳ tháng 8 (21/7→20/8) → `2026-07-20T23:59:00`. Fallback Collector: `Scheduler:InitialSyncFromDate` |
| `DEVICE_VENDOR`           | Mặc định `ZKTeco`; dùng để chọn SDK adapter                                   |

---

## 1b. `POST api/AttendanceAPI/fnGetCollectorBlackoutWindows` _(đã implement)_

Lấy danh sách khoảng giờ tạm dừng đồng bộ máy (HR cấu hình trên GeneralConfig / key `BlackoutWindows` trong `TBL_ATT_SCORING_CONFIG`).

### Request (mẫu)

```json
{}
```

### Response (mẫu)

```json
{
  "ErrCode": "1",
  "ErrMsg": "success",
  "ErrBack": "",
  "Data": {
    "BlackoutWindows": [
      { "Start": "11:30", "End": "12:30" },
      { "Start": "22:00", "End": "23:00" }
    ]
  }
}
```

| Field             | Ghi chú                                                                              |
| ----------------- | ------------------------------------------------------------------------------------ |
| `BlackoutWindows` | `[]` = không tạm dừng; Collector gọi API này mỗi chu kỳ Cron (không đọc appsettings) |

---

## 2. `POST api/AttendanceAPI/fnPostCollectorRawLogs`

Đẩy batch log thô. Server **dedup** theo PK `(ENROLL_NUMBER, WORK_DATE, LOG_TIME)`.

### Request (mẫu)

```json
{
  "ATT_DEVICE_ID": 1,
  "Logs": [
    {
      "ENROLL_NUMBER": "1001",
      "LOG_TIME": "2026-07-17T07:05:12",
      "WORK_DATE": "2026-07-17"
    },
    {
      "ENROLL_NUMBER": "1002",
      "LOG_TIME": "2026-07-17T05:45:00",
      "WORK_DATE": "2026-07-16"
    }
  ]
}
```

`WORK_DATE`: nếu `LOG_TIME` &lt; 06:00 → ngày lịch − 1 (Collector đã tính sẵn).

### Response (mẫu)

```json
{
  "ErrCode": "1",
  "ErrMsg": "success",
  "ErrBack": "",
  "Data": {
    "Inserted": 1,
    "Duplicate": 1
  }
}
```

---

## 3. `POST api/AttendanceAPI/fnPostCollectorSyncResult`

Cập nhật mốc sync trên `TBL_ATT_DEVICE` và ghi `TBL_ATT_SYNC_JOB_LOG`.

### Request (mẫu)

```json
{
  "ATT_DEVICE_ID": 1,
  "JOB_START_TIME": "2026-07-17T10:00:00",
  "JOB_END_TIME": "2026-07-17T10:00:45",
  "READ_FROM_TIME": "2026-07-17T09:00:00",
  "LAST_PROCESSED_LOG_TIME": "2026-07-17T09:58:10",
  "RECORDS_READ": 12,
  "RECORDS_INSERTED": 10,
  "RECORDS_DUPLICATE": 2,
  "RETRY_COUNT": 0,
  "JOB_STATUS": "SUCCESS",
  "ERROR_MESSAGE": null
}
```

`JOB_STATUS`: `SUCCESS` | `FAILED` | `TIMEOUT`.

### Response (mẫu)

```json
{
  "ErrCode": "1",
  "ErrMsg": "success",
  "ErrBack": "",
  "Data": null
}
```

---

## 4. `POST api/AttendanceAPI/fnDrainAttReprocessQueue`

Collector (hosted drain) gọi định kỳ để backend claim job `TBL_ATT_REPROCESS_QUEUE` → tính bảng công từng NV theo lát ≤ 31 ngày với `PERSIST=true` → upsert `TBL_ATT_DAILY_RESULT`. Job thiếu mã NV → `FAILED` (không fan-out toàn công ty).

### Request (mẫu)

```json
{
  "MaxItems": 15
}
```

### Response (mẫu)

```json
{
  "ErrCode": "1",
  "ErrMsg": "success",
  "ErrBack": "",
  "Data": {
    "Processed": 1,
    "Failed": 0,
    "Remaining": 2
  }
}
```

---

## Auth (tuỳ chọn)

Collector gửi header `X-Api-Key` nếu `Backend:ApiKey` khác rỗng. Backend validate khi `Web.config` key `AttendanceCollectorApiKey` khác rỗng.

