# School Management API

ASP.NET Core Web API for the school management system specified in
`School_Management_Software_Feature_List_and_Technical_Specification.pdf` (EMSL/LIS/SMS/2026-10),
using Lighthouse International School (Dhanmondi, Gulshan, Lalmatia) as the reference configuration.

**Release 1 scope (this code): spec sections 4.1 CORE, 4.2 ADM and 4.3 SIS.**

## Run it

Requirements: .NET 10 SDK and SQL Server (or PostgreSQL 16).

```powershell
cd "D:\Asikur\School Management"
dotnet run --project src/SchoolManagement.Api --launch-profile http
```

On first start the database is migrated and seeded (campuses, 2026-2027 year, programmes and classes,
sections, Appendix A grading schemes, roles, approval workflows and message templates).

| | |
|---|---|
| API reference (Scalar UI) | http://localhost:5107/scalar |
| OpenAPI document | http://localhost:5107/openapi/v1.json |
| Health | http://localhost:5107/health |
| Dev login | `admin` / `Admin@12345` (from `appsettings.Development.json`; change it) |

Sign in with `POST /api/auth/login`, then send `Authorization: Bearer <accessToken>`.
Sample calls are in `src/SchoolManagement.Api/SchoolManagement.Api.http`.

Tests: `dotnet test`.

### Configuration

| Key | Purpose |
|---|---|
| `ConnectionStrings:Default` | Database connection |
| `Database:Provider` | `SqlServer` (default) or `PostgreSql` (spec 5.3 recommendation) |
| `Jwt:SigningKey` | ≥ 32 bytes. Use user-secrets / environment variables outside development |
| `Seed:AdminPassword` | Creates the `admin` account on first run; leave empty in production and create users deliberately |
| `Storage:RootPath` | Document vault folder, outside any web root |
| `Notifications:Sms`, `Notifications:Smtp` | SMS gateway / SMTP. When empty, messages are logged instead of sent |
| `Backup:*` | Daily backup hour (Asia/Dhaka), folder, retention |
| `PublicSite:BaseUrl` | Used to build the offer accept/decline link in SMS |

Both providers have their own migrations (`Persistence/Migrations/SqlServer`, `.../PostgreSql`). To add one:

```powershell
dotnet ef migrations add <Name> --project src/SchoolManagement.Infrastructure --startup-project src/SchoolManagement.Api --context SqlServerAppDbContext --output-dir Persistence/Migrations/SqlServer
dotnet ef migrations add <Name> --project src/SchoolManagement.Infrastructure --startup-project src/SchoolManagement.Api --context PostgresAppDbContext  --output-dir Persistence/Migrations/PostgreSql
```

## Structure

Modular monolith (spec 5.1):

```
src/
  SchoolManagement.Domain          entities per module (Core, Identity, Audit, Approvals, Notifications, Admission, Students)
  SchoolManagement.Infrastructure  EF Core context, campus filters, audit trail, numbering, notification hub,
                                   file storage, approval engine, backup, background jobs, seeding
  SchoolManagement.Api             controllers and services per module (Modules/Identity, Core, Admission, Students)
tests/SchoolManagement.Tests
```

Cross-cutting rules enforced in `AppDbContext`:

- **Campus isolation (CORE-01)** – every campus-owned record is filtered to the caller's campuses, and writes to other campuses are refused.
- **Soft delete** with `IsDeleted` filters; created/updated audit columns in UTC.
- **Audit trail (CORE-05)** – before/after values for `[Audited]` entities, written in the same transaction; a background job seals rows into a SHA-256 hash chain and `GET /api/audit/verify` detects edits or deletions.
- **Locked academic years (CORE-02/13)** – records of a locked or archived year are read-only.
- Money and marks are `decimal(18,2)`; numbers (admission no., invoice no., …) come from configurable sequences.

## Feature coverage (Phase 1 items of sections 4.1–4.3)

| ID | Feature | Where |
|---|---|---|
| CORE-01 | Multi-campus tenancy | global query filters, campus-scoped role assignments |
| CORE-02 | Academic year & term management, roll-over wizard | `api/academic-years` (open/lock/archive/rollover) |
| CORE-03 | RBAC per module/action/campus | `api/users`, `api/roles`, `[HasPermission]` |
| CORE-04 | Approval workflows, delegation, escalation | `api/approvals`, escalation job |
| CORE-05 | Tamper-evident audit trail, search, export | `api/audit` |
| CORE-06 | Bilingual data, Bangla digits, lakh/crore, Hijri | `NameBn` fields, bn/en templates, `api/localization/today` |
| CORE-07 | Master data & settings, numbering formats | `api/structure`, `api/master`, `api/grading-schemes`, `api/settings` |
| CORE-08 | Notification hub (SMS, e-mail, push, in-app) with retry | `api/notifications`, dispatch job |
| CORE-09 | Global search | `api/search` |
| CORE-10 | Excel import with validation preview; Excel/CSV export | `api/students/import`, `api/students/export` |
| CORE-13 | Daily backups, restore verification, read-only archive | `api/backups`, daily job |
| ADM-01 | Enquiry capture (staff + website) | `api/admission/enquiries`, `api/public/admission/enquiries` |
| ADM-02 | Lead pipeline, tasks, conversion analytics | `.../enquiries/{id}/stage`, `/activities`, `/analytics`, `/my-tasks` |
| ADM-03 | Online application with draft-save & documents | `api/public/admission/applications/{token}` |
| ADM-04 | Test sessions, admit cards, scores, interviews | `api/admission/test-sessions` |
| ADM-05 | Rule-based merit list, waiting list, seat dashboard | `api/admission/selection` |
| ADM-06 | Bilingual offer letters, online accept/decline, expiry | `api/admission/offers`, offer-expiry job |
| ADM-07 | Admission invoice raised on acceptance | `api/admission/invoices` |
| ADM-08 | One-click enrolment to student record + parent login | `api/admission/applications/{id}/enrol` |
| SIS-01 | Student profile | `api/students` |
| SIS-02 | Guardians, family & sibling linking | `api/students/{id}/guardians`, `/siblings` |
| SIS-03 | Document vault (type-checked, scanned, outside web root) | `api/students/{id}/documents` |
| SIS-04 | Section allocation, roll rules, transfers with history | `api/students/allocation`, `/roll-numbers`, `/{id}/transfer` |
| SIS-05 | Promotion review & graduation | `api/students/promotion/*` |
| SIS-06 | Withdrawal with dues clearance & approval, TC register | `api/students/{id}/withdrawals`, `/transfer-certificates` |
| SIS-08 | ID cards with QR, batch print, paid reissue | `api/students/id-cards` |

### Section 4.4 ACA — academic structure, timetable & calendar

| ID | Feature | Where (UI / API) |
|---|---|---|
| ACA-01 | Programme & curriculum per class (subjects, periods/week, lab preference) | Academic setup → Curriculum · `api/academics/curriculum` |
| ACA-02 | Option groups (choose min–max) and each student's electives | Students → Subject choices · `api/academics/subject-choices` |
| ACA-03 | Teacher–subject–section allocation, teacher load (over/under-used), gaps | Timetable → Teacher allocation · `api/academics/allocations` |
| ACA-04 | Timetable builder: click-to-edit grid, clash checks by clock time across campuses, option blocks, auto-generate, school-wide check, print | Timetable → Timetable · `api/academics/timetable` |
| ACA-05 | Substitution: absent teacher's periods, ranked free teachers, SMS/in-app notice, cover report for payroll | Timetable → Substitutions · `api/academics/substitutions` |
| ACA-07 | Academic calendar (terms, holidays, events), website JSON and iCal feed | Timetable → Academic calendar · `api/academics/calendar`, `api/public/calendar/feed.ics` |
| ACA-08 | Daily routine and class-teacher view; printable section/teacher/room routines | Timetable → My day · `api/academics/timetable/my-day` |

Supporting set-up: Rooms and Bell schedule (Academic setup). Timetable rules live in Settings: `Timetable:WorkingDays`,
`Timetable:MaxTeacherPeriodsPerDay`, `Timetable:MaxConsecutivePeriods`, `Timetable:MaxTeacherPeriodsPerWeek`, `Timetable:MinTeacherPeriodsPerWeek`.
Not built yet: ACA-06 room/equipment booking and ACA-09 syllabus tracking (Phase 2); roll-over does not yet copy allocations and timetables to the new year.

Phase 2/3 items in these sections (CORE-11 2FA/SSO, CORE-12 privacy tooling, ADM-09/10, SIS-07/09/10) are not built yet.

## Integration points left for later releases

- **FEE (release 2):** online gateway (bKash/Nagad/SSLCommerz), fee plans on enrolment, fee carry-forward on roll-over, ledger posting. Admission invoices and the ID-card reissue fee are recorded now and are ready to be picked up.
- **LIB / TRN / INV:** each registers an `IClearanceCheck` for withdrawal; placeholders report "not installed" today.
- **EXM / ATT:** implement `IPromotionRule` to feed results and attendance thresholds into the promotion review.
- **DOC:** offer letters, admit cards, ID cards and TCs are rendered as printable HTML; the template designer and PDF output come with DOC-01.
- SMS and push need provider credentials; WhatsApp is a P2 channel and currently logs only.
