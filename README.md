# Facebook Page API Practice

Hệ thống API phân tán dùng để quản lý Facebook Page, nhận webhook từ Facebook, xử lý bình luận theo thời gian thực, phân tích nội dung bằng AI và tự động tạo lệnh phản hồi qua Facebook Graph API.
## Tổng quan

Dự án triển khai một hệ thống microservice cho bài thực hành Lập trình API. Luồng xử lý chính:

1. Facebook gửi webhook khi có bình luận mới trên Page.
2. `webhook-service` xác thực chữ ký HMAC, parse payload và chuẩn hóa event.
3. Event được publish vào Kafka topic `raw_events`.
4. `core-service` consume event, phân tích spam/intent/sentiment bằng rule engine và Gemini API.
5. `core-service` tạo lệnh xử lý và publish vào topic `reply_commands`.
6. `backend-api` consume lệnh, kiểm tra idempotency trong PostgreSQL và gọi Facebook Graph API.
7. Nếu gửi thất bại, message được đưa vào `send_failed` để `retry-service` retry.
8. Nếu retry vượt ngưỡng, message được đưa vào `dead_letter` để theo dõi và xử lý thủ công.

## Công nghệ sử dụng

- .NET 8
- ASP.NET Core Web API
- Apache Kafka
- PostgreSQL 16
- Docker Compose
- Facebook Graph API
- Gemini API
- Prometheus
- Alertmanager
- xUnit, Moq, FluentAssertions

## Kiến trúc hệ thống

```text
Facebook Page
    |
    | Webhook HTTP POST
    v
webhook-service :3001
    |
    | publish raw_events
    v
Kafka
    |
    | consume raw_events
    v
core-service :3002
    |
    | publish reply_commands
    v
Kafka
    |
    | consume reply_commands / send_retry
    v
backend-api :3000 -----> PostgreSQL
    |
    | call Facebook Graph API
    v
Facebook Page

Khi lỗi:
backend-api -> send_failed -> retry-service :3003 -> send_retry hoặc dead_letter
```

Các service chính:

| Service | Port | Vai trò |
| --- | --- | --- |
| `backend-api` | `3000` | Expose API quản trị, gọi Facebook Graph API, xử lý idempotency và trạng thái command. |
| `webhook-service` | `3001` | Nhận webhook Facebook, verify token/chữ ký, chuẩn hóa event và publish Kafka. |
| `core-service` | `3002` | Consume event, phân tích AI/rule, phát sinh lệnh tự động. |
| `retry-service` | `3003` | Retry các lệnh gửi thất bại và đưa vào dead-letter khi quá số lần thử. |
| `postgres` | `5432` | Lưu idempotency key, trạng thái command và thông tin comment đã xử lý. |
| `kafka-ui` | `8090` | Giao diện quan sát topic/message Kafka. |
| `prometheus` | `9090` | Thu thập metric. |
| `alertmanager` | `9093` | Quản lý cảnh báo. |

## Cấu trúc thư mục

```text
.
+-- Docs/
|   +-- Bai_tap_Thuc_hanh.md
+-- fb_api/
|   +-- alertmanager/
|   +-- infra/
|   |   +-- postgres/init/001_schema.sql
|   +-- prometheus/
|   +-- services/
|   |   +-- backend-api/
|   |   +-- core-service/
|   |   +-- retry-service/
|   |   +-- webhook-service/
|   +-- shared/
|   |   +-- contracts/
|   +-- tests/
|   +-- tools/
|   +-- docker-compose.yml
|   +-- FbApi.IntegrationTests.sln
+-- README.md
```

## Yêu cầu cài đặt

- Docker Desktop
- Docker Compose
- .NET SDK 8.0 nếu cần chạy test bằng `dotnet test`
- Facebook App/Page token hợp lệ nếu muốn gọi thật Facebook Graph API
- Gemini API key nếu muốn dùng phân tích AI thật

## Cấu hình môi trường

Tạo file `fb_api/.env`:

```env
FACEBOOK_APP_SECRET=
FACEBOOK_VERIFY_TOKEN=
FACEBOOK_PAGE_ACCESS_TOKEN=
GEMINI_API_KEY=
GEMINI_MODEL=gemini-2.0-flash
ADMIN_API_KEY=dev-admin-key
```

Ý nghĩa các biến:

| Biến | Mô tả |
| --- | --- |
| `FACEBOOK_APP_SECRET` | App secret dùng để kiểm tra chữ ký webhook `X-Hub-Signature-256`. |
| `FACEBOOK_VERIFY_TOKEN` | Token dùng khi Facebook verify webhook bằng `GET /webhook`. |
| `FACEBOOK_PAGE_ACCESS_TOKEN` | Page access token để gọi Facebook Graph API. |
| `GEMINI_API_KEY` | API key dùng cho AI analysis trong `core-service`. |
| `GEMINI_MODEL` | Model Gemini sử dụng, mặc định là `gemini-2.0-flash`. |
| `ADMIN_API_KEY` | API key cho các endpoint quản trị cần header `X-Admin-Api-Key`. |

## Chạy hệ thống

Từ thư mục gốc repository:

```powershell
cd fb_api
docker compose up -d --build
```

Kiểm tra container:

```powershell
docker compose ps
```

Xem log một service:

```powershell
docker compose logs -f backend-api
docker compose logs -f webhook-service
docker compose logs -f core-service
docker compose logs -f retry-service
```

## Endpoint chính

### Backend API

Base URL: `http://localhost:3000`

| Method | Endpoint | Mô tả |
| --- | --- | --- |
| `GET` | `/health` | Liveness check. |
| `GET` | `/health/ready` | Readiness check, kiểm tra PostgreSQL. |
| `GET` | `/posts?pageId={pageId}` | Lấy danh sách bài viết của Page. Nếu không truyền `pageId`, dùng `me`. |
| `POST` | `/posts` | Tạo bài viết mới trên Page. Cần header `X-Admin-Api-Key`. |
| `POST` | `/post` | Alias của `POST /posts`. Cần header `X-Admin-Api-Key`. |
| `GET` | `/comments?postId={postId}` | Lấy comment theo post ID. |
| `GET` | `/comments/{postId}` | Lấy comment theo post ID. |

Ví dụ tạo bài viết:

```powershell
curl -X POST http://localhost:3000/posts `
  -H "Content-Type: application/json" `
  -H "X-Admin-Api-Key: dev-admin-key" `
  -d "{\"pageId\":\"PAGE_ID\",\"message\":\"Hello from API\"}"
```

### Webhook Service

Base URL: `http://localhost:3001`

| Method | Endpoint | Mô tả |
| --- | --- | --- |
| `GET` | `/webhook` | Endpoint để Facebook verify webhook. |
| `POST` | `/webhook` | Nhận webhook event từ Facebook, yêu cầu chữ ký HMAC hợp lệ. |
| `GET` | `/health` | Liveness check. |
| `GET` | `/health/ready` | Readiness check, kiểm tra Kafka. |

Ví dụ verify webhook:

```powershell
curl "http://localhost:3001/webhook?hub.mode=subscribe&hub.verify_token=VERIFY_TOKEN&hub.challenge=123456"
```

### Core Service

Base URL: `http://localhost:3002`

| Method | Endpoint | Mô tả |
| --- | --- | --- |
| `GET` | `/health` | Liveness check. |
| `GET` | `/health/ready` | Readiness check, kiểm tra Kafka. |

### Retry Service

Base URL: `http://localhost:3003`

| Method | Endpoint | Mô tả |
| --- | --- | --- |
| `GET` | `/health` | Liveness check. |
| `GET` | `/health/ready` | Readiness check, kiểm tra Kafka. |

## Kafka topic

Các topic được tạo tự động bởi service `kafka-init`:

| Topic | Producer | Consumer | Mô tả |
| --- | --- | --- | --- |
| `raw_events` | `webhook-service` | `core-service` | Event đã chuẩn hóa từ webhook Facebook. |
| `reply_commands` | `core-service` | `backend-api` | Lệnh tự động như reply, hide comment, manual review. |
| `send_failed` | `backend-api` | `retry-service` | Lệnh gọi Facebook thất bại. |
| `send_retry` | `retry-service` | `backend-api` | Lệnh cần gửi lại sau khi tính retry delay. |
| `dead_letter` | `retry-service` | Không có consumer mặc định | Message lỗi sau khi retry quá ngưỡng. |

## Cơ sở dữ liệu

PostgreSQL được khởi tạo bằng script `fb_api/infra/postgres/init/001_schema.sql`.

Các bảng chính:

| Bảng | Mô tả |
| --- | --- |
| `idempotency_keys` | Lưu key đã xử lý để tránh gửi trùng command. |
| `command_status` | Lưu trạng thái gửi command, lỗi, response Facebook và số lần retry. |
| `comments` | Lưu thông tin comment đã xử lý, intent, sentiment và action đã thực hiện. |

Thông tin kết nối mặc định trong Docker:

```text
Host: localhost
Port: 5432
Database: fb_api
User: fb_user
Password: fb_password
```

## Kiểm thử

Chạy test bằng .NET SDK:

```powershell
cd fb_api
dotnet test FbApi.IntegrationTests.sln
```

Nhóm test hiện có:

- `HmacValidationServiceTests`
- `NormalizedEventMapperTests`
- `SpamDetectorServiceTests`
- `RuleEngineServiceTests`
- `RetryPolicyServiceTests`
- `IdempotencyRepositoryTests`
- `EventStatusTrackerTests`
- `BackendApiTests`
- `CoreServicePipelineTests`

## Giám sát

Các công cụ giám sát sau được chạy cùng Docker Compose:

| Công cụ | URL | Mô tả |
| --- | --- | --- |
| Kafka UI | `http://localhost:8090` | Xem Kafka cluster, topic, consumer group và message. |
| Prometheus | `http://localhost:9090` | Theo dõi metric và alert rule. |
| Alertmanager | `http://localhost:9093` | Theo dõi trạng thái cảnh báo. |

Prometheus có rule theo dõi `dead_letter`. Khi topic này có message mới, hệ thống có thể phát cảnh báo qua Alertmanager theo cấu hình trong `fb_api/alertmanager/alertmanager.yml`.

## Dừng hệ thống

Dừng container:

```powershell
cd fb_api
docker compose down
```

Dừng và xóa volume dữ liệu:

```powershell
docker compose down -v
```

Lệnh `down -v` sẽ xóa dữ liệu Kafka, PostgreSQL và Prometheus trong các Docker volume của dự án.
