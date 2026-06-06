# Project Security Guidelines & Roadmap

이 문서는 ClubMTServer 프로젝트의 보안 표준 및 향후 개선 과제를 관리합니다.

## 🛡️ Security TO-DO List

### 1. 개인정보 보호 (Data Privacy)
- [ ] **감사 로그 내 PII 마스킹 강화**
  - `AuditLogMiddleware.cs`의 `SensitiveKeys` 배열에 `name`, `studentId`, `phoneNumber`를 추가하여 로그 파일 및 DB에 개인정보가 평문으로 남지 않도록 조치.
- [ ] **공개 엔드포인트 데이터 최소화**
  - `api/Participants` (GET) 호출 시, 비인증 사용자에게 노출되는 필드를 최소화하고 마스킹 로직을 강화.

### 2. 접근 제어 강화 (Access Control)
- [ ] **커스텀 헤더 기반 클라이언트 검증**
  - 단순 `Referer` 체크는 스푸핑에 취약하므로, 클라이언트 앱과 서버 간의 사전에 약속된 커스텀 헤더(예: `X-ClubMT-Source`)를 사용하여 접근 제어 강화.
- [ ] **WAF 규칙 최적화**
  - 단순히 User-Agent를 차단하는 방식보다, 비정상적인 요청 패턴이나 속도 제한을 활용한 차단 규칙으로 고도화.

### 3. 가용성 및 성능 (Availability)
- [ ] **Rate Limiting 임계값 조정**
  - `Program.cs` 내 `cheer` 정책의 한도(현재 10초당 1000회)가 비정상적으로 높음. 실제 서비스 요구사항에 맞춰 하향 조정하여 DOS 공격 리스크 방지.

## 🏗️ Architectural Standards

- **PII Encryption:** 모든 개인정보(이름, 학번, 전화번호)는 DB 저장 시 `EncryptionService`를 통해 AES-256-CBC 방식으로 암호화되어야 하며, 검색을 위해 `NameHash` (HMAC-SHA256)를 병행 사용한다.
- **Single-Session Management:** `CurrentSessionId` 기반의 검증 로직을 유지하여 중복 로그인 및 세션 탈취를 방지한다.
- **Environment Isolation:** Swagger UI 및 OpenAPI 정의는 오직 `Development` 환경에서만 노출되어야 한다.
