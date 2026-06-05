# Facebook Page API Practice

Implementation chính của bài thực hành nằm trong `fb_api/`.

## Chạy hệ thống

```powershell
cd fb_api
docker compose up -d --build
```

Các service chính:

- `backend-api`: http://localhost:3000
- `webhook-service`: http://localhost:3001
- `core-service`: http://localhost:3002
- `retry-service`: http://localhost:3003
- Kafka UI: http://localhost:8090
- Prometheus: http://localhost:9090
- Alertmanager: http://localhost:9093

## Cấu hình local

Tạo file `fb_api/.env` và đặt các biến cần thiết:

```env
FACEBOOK_APP_SECRET=
FACEBOOK_VERIFY_TOKEN=
FACEBOOK_PAGE_ACCESS_TOKEN=
GEMINI_API_KEY=
ADMIN_API_KEY=dev-admin-key
```

Yêu cầu bài thực hành gốc nằm trong `Docs/Bai_tap_Thuc_hanh.md`.
