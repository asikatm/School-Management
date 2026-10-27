# School Management API

ASP.NET Core Web API for the school management system specified in
`School_Management_Software_Feature_List_and_Technical_Specification.pdf` (EMSL/LIS/SMS/2026-10),
using Lighthouse International School (Dhanmondi, Gulshan, Lalmatia) as the reference configuration.

**Scope so far: spec sections 4.1 CORE, 4.2 ADM, 4.3 SIS, 4.4 ACA, 4.5 ATT, 4.6 LRN and 4.7 EXM (phase 1 items).**

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

### Section 4.5 ATT — attendance & leave

| ID | Feature | Where (UI / API) |
|---|---|---|
| ATT-01 | Daily register per section (P / A / Late with arrival time / Leave), period-wise registers for senior programmes, own-class rule and 48 h edit window, "not taken yet" list | Attendance → Take attendance, Today's overview · `api/attendance/register`, `/overview` |
| ATT-02 | Monthly register grid with totals and % | Attendance → Monthly register · `api/attendance/monthly` |
| ATT-03 | Absence / late SMS to guardians after the morning cut-off (once per mark; none for approved leave) | background job `attendance-alerts` |
| ATT-04 | Student leave from the parent portal or the office, with attachment; class teacher approves, school days are marked Leave | Attendance → Student leave, Parent portal → My children · `api/attendance/leave`, `api/portal/*` |
| ATT-05 | Term attendance thresholds (warning / exam minimum), one SMS per level per term, printable warning letter | Attendance → Attendance thresholds · `api/attendance/thresholds` · job `attendance-warnings` |
| ATT-06 | Staff check-in/out: geofenced mobile punch, biometric/RFID device feed, late / early leaving / overtime, corrections, monthly summary for payroll | Attendance → My attendance & leave, Staff attendance · `api/staff-attendance/*` |
| ATT-07 | Leave types and yearly entitlements (per-person overrides), balances, requests through the CORE-04 approval chain (Head of Section → Principal after 24 h); approved leave shows on the staff register and removes the teacher from the cover list | Attendance → My attendance & leave, Staff leave · `api/staff-leave/*` |

Rules live in Settings (organisation-wide, or per campus): `Attendance:AlertCutoff`, `Attendance:SendAlerts`, `Attendance:EditWindowHours`,
`Attendance:WarningPercent`, `Attendance:MinimumPercent`, `Attendance:PeriodWiseProgrammes`, `StaffAttendance:WorkStart` / `WorkEnd`,
`StaffAttendance:LateGraceMinutes`, `StaffAttendance:OvertimeAfterMinutes` and `StaffAttendance:Geofence` (`latitude,longitude,radius metres` — set it per campus).
Late counts as attended; leave counts as a day missed. Staff absence is derived (a school day with no punch and no leave).

### Section 4.6 LRN — teaching, learning & e-classroom

| ID | Feature | Where (UI / API) |
|---|---|---|
| LRN-02 | Homework with due time and attachment; online hand-in (file/photo/text) by the student or guardian; late rule (refuse, or % penalty); marks, feedback, "redo"; in-app notice when set and a "due tomorrow" reminder to those who have not handed in | Teaching & learning → Homework · `api/learning/homework` · job `homework-reminders` |
| LRN-03 | Class diary (topics, homework, reminders, uniform/kit) and the parents' daily digest with homework set and due next | Teaching & learning → Class diary · `api/learning/diary` |
| LRN-04 | Learning resources (PDF, Word, PowerPoint, Excel, images, or links e.g. videos) by subject/class/section, staff-only or family audience, download tracking | Teaching & learning → Learning resources · `api/learning/resources` |
| LRN-08 | Gradebook: weighted categories (school default or per class), assessed work and homework columns, term %, submission and head-of-department moderation (± adjustment, approve/lock, return, re-open) | Teaching & learning → Gradebook, Gradebook moderation · `api/learning/gradebook` |

Teachers work on their own classes (class teacher, or allocated to the subject); `learning.manage` covers any class. Not built yet: LRN-01 lesson plans, LRN-05 online classes, LRN-06 question bank, LRN-09 target tracking (phase 2) and LRN-07 online tests (phase 3).

### Section 4.7 EXM — examination, assessment & reports

| ID | Feature | Where (UI / API) |
|---|---|---|
| EXM-01 | Exams (class test, mid-term, final, mock, board practice) per year/term/campus and classes; date sheet; clash check for students' elective combinations; publish to portal and website (guardians get an SMS) | Examinations → Exams & results → Date sheet · `api/exams`, `api/public/exams/date-sheets` |
| EXM-02 | Seating plan by room (sections interleaved), invigilators per exam day (auto, skipping staff on leave), seat lookup, admit cards with photo, QR, own papers and an attendance-eligibility warning | … → Seating & admit cards · `api/exams/{id}/seating`, `/admit-cards` |
| EXM-03 | Mark sheets per paper and section: components with weights (written, practical, oral, CA), AB/EX codes, Excel template and upload (dry run first), CA filled from the approved gradebook, double-entry verification by a second person, approval lock and controlled re-open with reason | Examinations → My mark sheets, … → Mark sheets · `api/exams/papers/{paper}/sections/{section}/*` |
| EXM-04 | Grade from the class's grading scheme (programme default or per-exam override): 9–1, A*–E, GPA 5.00 (a failed subject gives GPA 0.00, configurable), primary descriptors | `Grading`, Academic setup → Grading schemes |
| EXM-05 | Report cards: bilingual, per-programme columns, component marks, grade/GP/descriptor, section highest, attendance, conduct, co-curricular, teacher and principal remarks; batch print; HMAC-signed with a QR that opens the public verification page | … → Results & report cards, Remarks · `api/exams/{id}/report-cards`, `/public/verify/{code}` |
| EXM-06 | Finalise (frozen, signed results), section/class position and percentile (`Exams:ShowRank`), publication now or at an embargo time, withdrawal; multi-year transcript with subject history and cumulative GPA | … → Results, Examinations → Transcripts · `api/exams/{id}/results/*`, `api/exams/transcripts/{student}` · job `result-publication` |
| EXM-10 | Pass %, grade distribution, subject/section/teacher/campus comparison, section × subject heat map, top performers, failed/at-risk list, comparison with another exam (e.g. last year's) | … → Analysis · `api/exams/{id}/analysis` |

Teachers enter marks only for subjects they are allocated to (spec 5.6 "own subjects"); `exams.moderate` verifies, approves and re-opens; `exams.manage` (Exam Officer role) runs schedules, seating, finalising and publication.
Topic-level ("weak topic") analysis needs per-question marks from the question bank (LRN-06). Not built yet: EXM-07/08/09 Edexcel entries, predicted grades, external results (phase 2) and EXM-11/12 (phase 3).

Settings: `Homework:ReminderTime`, `Homework:LatePenaltyPercent`, `Exams:DoubleEntry`, `Exams:ShowRank`, `Exams:GpaZeroOnFail`, `Exams:MaxFailedForPromotion`, `ReportCard:PrincipalName`.
Report-card signatures use `Documents:SigningKey` (falls back to `Jwt:SigningKey`); keep it secret and stable, or old cards stop verifying.

### Portal (parents and students)

**Student portal** (Portal → Student portal, `/student`; a student account lands here after signing in): profile header, today's attendance,
term attendance %, homework to hand in, next exam and latest result, then tabs for today's classes, notices, the weekly class routine (own
subjects only), exam routine with seat and admit card, the monthly attendance calendar, homework with hand-in, and results with report cards
and transcript. Guardians can open it for each child too.

Portal → Homework, exams & results also shows the class diary and learning resources. All portal data comes from `api/portal/students/{id}/*`
(`overview`, `notices`, `timetable`, `attendance`, `homework`, `diary`, `resources`, `exams`, `results`, `transcript`, `photo`).
A guardian sees their children; a student sees themself. Student logins are created from the student profile ("Student portal login", user name = admission number).

**Notice board** (Administration → Notice board, `api/notices`): notices for students, parents and/or staff by campus and class, with pin,
expiry and an attachment. Students see student notices and guardians parent notices in the portal; staff see staff notices. Publishing
tells the audience in-app; urgent notices also go to primary guardians by SMS. Posting needs `notices.manage` (Vice-Principal, Head of
Section, Exam Officer, Principal).

Phase 2/3 items in these sections (CORE-11 2FA/SSO, CORE-12 privacy tooling, ADM-09/10, SIS-07/09/10) are not built yet.

## Integration points left for later releases

- **FEE (release 2):** online gateway (bKash/Nagad/SSLCommerz), fee plans on enrolment, fee carry-forward on roll-over, ledger posting. Admission invoices and the ID-card reissue fee are recorded now and are ready to be picked up.
- **LIB / TRN / INV:** each registers an `IClearanceCheck` for withdrawal; placeholders report "not installed" today.
- **EXM → SIS:** `ExamResultPromotionRule` suggests conditional/repeat from the year's final exam in the promotion review. Attendance thresholds flag exam eligibility on admit cards; an attendance `IPromotionRule` can follow the same pattern.
- **Devices:** biometric / RFID readers post punches to `POST api/staff-attendance/device` (user name + UTC time); a student gate feed can follow the same pattern.
- **DOC:** offer letters, admit cards, ID cards and TCs are rendered as printable HTML; the template designer and PDF output come with DOC-01.
- SMS and push need provider credentials; WhatsApp is a P2 channel and currently logs only.
