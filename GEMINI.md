# Project: ClubMTServer

## Foundational Mandates
- All sensitive configurations must be managed via `.env` files.
- The system must fail-fast if critical environment variables are missing.
- Administrator actions must be protected by JWT Bearer authentication.
- Participant self-editing must be protected by individual password hashing (BCrypt).

## Roadmap / TODOs
- [x] **[Security] 개인정보 필드 암호화 (Field-Level Encryption)**: 
  - `Participant` 모델의 `Name`, `PhoneNumber`, `StudentId` 등 개인정보 필드를 DB 저장 시 AES-256 등으로 암호화 처리.
  - 컨테이너 침해 및 DB 파일 탈취 시에도 데이터 복독이 불가능하도록 보안 강화.
  - 암호화 키는 환경 변수(`ENCRYPTION_KEY`)를 통해 주입.
- [x] **[Feature] CSV 동문 명단 임포트 (CSV Bulk Import)**:
  - 기존 동문 명단(이름, 기수, 핸드폰 뒤 4자리)을 CSV 파일로 업로드하여 DB에 일괄 등록하는 기능.
  - 업로드된 명단에 있는 사람만 MT 신청이 가능하도록 제한.
