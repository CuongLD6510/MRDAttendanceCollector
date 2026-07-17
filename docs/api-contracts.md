# API Contracts — Attendance Collector

Backend (`MRDMobileApplication` / `AttendanceAPI`) **chưa implement** các endpoint dưới đây. Collector gọi theo contract này khi `Backend:UseMock = false`.

Quy ước response:

- `ErrCode = "1"` → thành công
- `ErrCode != "1"` → lỗi
- Giữ nguyên tên property (không camelCase)

---

## 1. `POST api/AttendanceAPI/fnGetCollectorDevices`

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
    ]
  }
}
```

| Field | Ghi chú |
| ----- | ------- |
| `LAST_PROCESSED_LOG_TIME` | `null` nếu chưa sync lần nào → Collector dùng `Scheduler:InitialSyncFromDate` |
| `DEVICE_VENDOR` | Mặc định `ZKTeco`; dùng để chọn SDK adapter |

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

## Auth (tuỳ chọn)

Collector gửi header `X-Api-Key` nếu `Backend:ApiKey` khác rỗng. Backend có thể bỏ qua hoặc validate sau.
