# ClubMTServer Project Documentation

## 1. Project Overview
ClubMTServer is a specialized management system for alumni MT (Membership Training) events. It provides a full-stack solution including an ASP.NET Core backend and an integrated frontend. The system is designed for "Closed Membership," where only pre-authorized members can register.

### Key Features:
- **Phone-Only Identification**: Simplified registration using only a full phone number.
- **Dynamic Site Management**: Admins can change titles, dates, capacity, and schedules via a visual builder.
- **Two-Layer Security**: JWT Bearer authentication for admins and hashed password (phone last 4 digits) for participants.
- **Infrastructure Protection**: IP-based Rate Limiting and strict Origin/Referer validation.
- **Mobile Responsive**: Fully optimized for mobile browsers and touch interactions.

---

## 2. Security Architecture
### Admin Security
- **Auth**: JWT (JSON Web Token) Bearer authentication.
- **Storage**: Admin credentials are seeded on startup from environment variables.
- **Gating**: All management APIs (DELETE, PUT) require a valid `Authorization: Bearer [token]` header.

### Participant Security
- **Identification**: Name + Full Phone Number.
- **Password**: Automatically set to the last 4 digits of the phone number.
- **Hashing**: All passwords (including participant last 4) are stored as BCrypt hashes.
- **Privacy**: Sensitive fields (phone numbers, hashes) are never returned in public list APIs.

---

## 3. Environment Variables (.env)
| Key | Description | Example |
| :--- | :--- | :--- |
| `ADMIN_KEY` | Default password for 'admin' user | `admin1234` |
| `ALLOWED_ORIGINS` | Comma-separated list of allowed domains | `http://localhost:8000,https://...` |
| `JWT_SECRET` | Secret key for signing JWT tokens | `your-very-long-secret-key` |
| `CONNECTION_STRINGS__DEFAULTCONNECTION` | SQLite connection string | `Data Source=data/clubmt.db` |

---

## 4. API Specification

### Authentication
#### [POST] `/api/Auth/login`
- **Description**: Admin login to receive a JWT token.
- **Request Body**: `{ "username": "admin", "password": "..." }`
- **Response**: `{ "token": "eyJhbG..." }`

### Site Settings
#### [GET] `/api/Settings`
- **Description**: Get public site settings (Title, D-Day, Schedule).
- **Auth**: None (Public)

#### [PUT] `/api/Settings`
- **Description**: Update site settings and capacity.
- **Auth**: JWT (Admin)
- **Request Body**: `SiteSettings` model (JSON)

### Participants (Registration)
#### [GET] `/api/Participants`
- **Description**: Get the full list of members and their registration status.
- **Auth**: None (Public - Returns name/generation/status only)

#### [GET] `/api/Participants/{id}`
- **Description**: Get full registration details for a specific member (for editing).
- **Auth**: Participant Password (via Header)
- **Header**: `X-Participant-Password: [last4digits]`

#### [POST] `/api/Participants`
- **Description**: Register an existing member for the MT.
- **Auth**: None (Identifies via `phoneNumber` in body)
- **Request Body**:
  ```json
  {
    "phoneNumber": "01012345678",
    "studentId": "2022...",
    "transportation": "Car",
    "isCarpoolAvailable": true,
    "carpoolSeats": 2,
    "oneLineExpectation": "Can't wait!"
  }
  ```

#### [PUT] `/api/Participants/{id}`
- **Description**: Update an existing registration.
- **Auth**: Individual Password check in body.
- **Request Body**: `ParticipantUpdateDto` (includes `password`)

#### [DELETE] `/api/Participants/{id}`
- **Description**: Remove/Reset a registration.
- **Auth**: JWT (Admin)

### Management
#### [GET] `/api/Management/status`
- **Description**: Get summarized counts and expectations for the dashboard.
- **Auth**: None (Public)
- **Returns**: `counts`, `expectations`, `cohortCounts`

---

## 5. Roadmap / TODO
- [ ] **[Security] Field-Level Encryption**: Encrypt `Name` and `PhoneNumber` inside the SQLite file using AES-256.
- [ ] **[Feature] CSV Bulk Import**: Add a tool to upload the initial alumni list via CSV.
- [ ] **[Ops] Auto-Backup**: Script to periodically back up `clubmt.db`.
