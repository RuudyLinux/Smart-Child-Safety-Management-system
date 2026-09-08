# ChildGuard – Master Development Task

## Task Assignment
Build the complete **ChildGuard – Smart Child Safety System** as a real, working academic project. Act as a **Senior Full-Stack Software Architect and Developer**. Treat this document as the source of truth.

## Project
ChildGuard is a school-based system for student identification, attendance, transportation journey events, guardian management, and secure guardian pickup verification.

Actors:
- Admin
- Guardian
- Student

**Student has no login, account, or mobile application. Student interaction is through RFID only.**

## Technology Stack
- Admin Web: ASP.NET Core MVC, C#, HTML/CSS/JavaScript
- Backend: ASP.NET Core Web API, C#, REST APIs, Entity Framework Core
- Database: MySQL, EF Core migrations
- Guardian App: Flutter/Dart
- RFID: Generic RFID reader and RFID card identifier
- Face Verification: Python, OpenCV, DeepFace, suitable model such as ArcFace
- Notifications: Firebase Cloud Messaging

Do not hard-code the system around one RFID reader model.

## Explicitly Excluded
Do NOT implement:
- GPS tracking
- Live bus tracking
- Google Maps
- Real-time bus location
- Student mobile application
- Student login/account
- SOS/emergency features
- Continuous geographical location tracking

Journey tracking means recording events such as:
`Bus Boarding → School Arrival → School Departure → Bus Drop-off`

## Architecture
```text
                    ChildGuard
                        |
        ┌───────────────┴────────────────┐
        |                                |
   Admin Web                         Guardian App
   ASP.NET MVC                         Flutter
        |                                |
        └───────────────┬────────────────┘
                        |
                 ASP.NET Core API
                        |
       ┌────────────────┼─────────────────┐
       |                |                 |
     MySQL             RFID          Face Service
    Database          Reader           Python
                                         |
                                    OpenCV/DeepFace
                        |
                  Notification Service
                         |
                  Firebase FCM
```

## Functional Modules

### Authentication
Admin/Guardian login, registration where required, forgot/reset password, logout, authentication and authorization.

### Student Management
Add, edit, view, manage status, associate Guardian, associate transportation, assign RFID.

### RFID Management
View cards, assign RFID, view assignments, change card status, identify student from scan.

RFID flow:
`RFID Card → Reader → Student Identification → State/Event Validation → Record Event → Notification`

Repeated scans must not create duplicate attendance/journey records or duplicate notifications. Use current state and valid next events rather than a fixed time lock.

### Transportation
Manage buses, routes, drivers, and student-to-bus/route assignments. No GPS or map functionality.

### Attendance
Record valid events with Student, RFID, Event, Date, Time, Status. Admin can access attendance records and reports.

### Journey
Record journey events and maintain history. Guardian can view journey status/history. No maps or geographical coordinates.

### Guardian Management
Manage Guardian information, student association, and account status.

### Face Registration
Guardian can register their face:
`Guardian → Camera Capture → Face Processing → Secure Face Data → Registration Complete`

Prefer secure face representations/embeddings rather than unnecessary raw image storage.

### Guardian Pickup Verification
`Student Identified → Authorized Guardian → Face Capture → Face Verification → Match? → Approve/Reject → Record → Notification`

Only an authorized Guardian with successful verification can complete pickup.

### Notifications
Notify Guardians about relevant attendance, journey, and pickup verification events. Maintain notification history.

### Reports
Admin reports:
- Attendance
- Journey
- Guardian verification

### Guardian Access
Guardian can access linked student information, attendance, journey history, notifications, and pickup verification.

### Profile
Admin and Guardian can manage profile information and password.

## Admin Pages
```text
Admin
├── Login
├── Forgot Password
├── Reset Password
├── Dashboard
├── Students
│   ├── Student List
│   ├── Add Student
│   ├── Edit Student
│   └── Student Details
├── RFID
│   ├── RFID List
│   ├── Assign RFID
│   └── RFID Details
├── Guardians
│   ├── Guardian List
│   └── Guardian Details
├── Transportation
│   ├── Buses
│   ├── Routes
│   ├── Drivers
│   └── Student Assignments
├── Attendance
├── Journey
│   └── Student Journey Details
├── Guardian Verification
├── Reports
│   ├── Attendance Reports
│   ├── Journey Reports
│   └── Verification Reports
├── Profile
├── Settings
└── Logout
```

Dashboard should show useful summaries such as Total Students, Active RFID Cards, Total Guardians, Active Buses, Today's Attendance, Today's Journey Events, Verification Status, and Recent Activity.

## Guardian Flutter Pages
```text
Guardian App
├── Splash
├── Login
├── Registration
├── Forgot Password
├── Reset Password
├── Dashboard
├── Student Profile
├── Attendance
├── Journey
├── Notifications
├── Face Registration
├── Pickup Verification
├── Guardian Profile
├── Change Password
└── Logout
```

Recommended bottom navigation:
`Home | Attendance | Journey | Notifications`

Journey uses a timeline, not a map.

## Database Entities
```text
Admins
Guardians
Students
RFIDCards
Buses
Routes
Drivers
StudentBusAssignments
Attendance
JourneyEvents
GuardianFaceData
GuardianVerifications
Notifications
```

Use proper primary keys, foreign keys, relationships, indexes, constraints, timestamps, and status fields. Avoid unnecessary duplication.

## Backend API Modules
```text
Auth
Students
Guardians
RFID
Transportation
Attendance
Journey
Verification
Notifications
Reports
```

Use DTOs, validation, services, exception handling, logging, authentication, authorization, and role-based access. Keep business logic out of controllers where practical.

## Face Recognition Service
Create a separate Python service:
```text
face-service/
├── app/
│   ├── main.py
│   ├── routes/
│   ├── services/
│   └── models/
├── requirements.txt
└── README.md
```

Required operations:
- Register Face
- Verify Face

ASP.NET Core communicates with this service. Do not expose the face service directly to the public internet.

## Security
Implement secure password hashing, authentication, authorization, role-based access, input/API validation, secure database access, error handling, logging, and protection of biometric/sensitive data.

Never hard-code passwords, API keys, database credentials, or Firebase credentials. Use configuration/environment variables.

## UI/UX
Modern, professional, clean, simple, responsive, and easy to understand.

Suggested theme:
- Deep blue primary
- Light background
- White cards
- Dark navy text
- Green success
- Orange warning
- Red error

Avoid excessive gradients, cartoon UI, too many colors, unnecessary animations, cluttered dashboards, and GPS maps.


## Single RFID Scanner Presentation Scenario

ChildGuard must support a **single physical RFID scanner** for development and presentation demonstrations. The same scanner can be configured for different logical checkpoints without changing the core RFID architecture.

Supported logical checkpoints:
- School Arrival
- School Departure
- Bus Boarding
- Bus Drop-off

The checkpoint is selected before scanning. The backend must validate the selected checkpoint against the student's current state and the valid next attendance/journey event.

Example:

`Select Checkpoint → School Arrival → Scan RFID → Identify Student → Validate Event → Record School Arrival → Notify Guardian`

### Single-Scanner Rules

- The system must work with only one physical RFID scanner.
- Do not create separate hard-coded scanner logic for each checkpoint.
- The scanner should support a configurable logical checkpoint.
- The selected checkpoint determines the event being recorded.
- Repeated scans must not create duplicate or invalid events.
- Event validation must use the student's current state and valid next event.
- The architecture should remain extensible so additional RFID scanners can be added later.

## Parent/Guardian Student Drop-off

ChildGuard must support a scenario where a parent/authorized Guardian personally brings the student to school.

Flow:

`Select School Arrival → Scan Student RFID → Identify Student → Record School Arrival → Notify Guardian`

No additional RFID scanner is required.

The system should clearly display the identified student and recorded event in the presentation/demo interface.

## Parent/Guardian Pickup Verification

ChildGuard must support secure student pickup by an authorized Guardian.

Flow:

`Student Identification → Authorized Guardian Identification → Guardian Face Capture → Face Verification → Approve/Reject Pickup → Record Verification → Send Notification`

The system must verify that the Guardian is authorized for the identified student. Pickup can be approved only after successful Guardian face verification.

Record:
- Student
- Guardian
- Date
- Time
- Verification result
- Pickup result

## Student Drop-off & Pickup Verification Demo Page

Create a focused Admin/demo page named:

**Student Drop-off & Pickup Verification**

This page demonstrates the single-RFID-scanner workflow during project presentation and development testing.

### RFID Scanner Section

Display:
- Scanner identifier/status, e.g. `RFID-001`
- Connected/disconnected status
- Logical checkpoint selector
- Scan RFID Card action

### Student Section

After scanning, display:
- Student name
- Masked RFID identifier
- Identification result
- Selected checkpoint/event
- Event recording result

Example:

```text
STUDENT

Student: Rahul Patel
RFID: ****A72F

✓ Student Identified
✓ School Arrival Recorded
```

### Guardian Pickup Verification Section

For pickup, display:
- Guardian name
- Authorization status
- Face verification section
- Camera/capture status
- Face verification result
- Approve Pickup action
- Reject Pickup action

Example:

```text
GUARDIAN PICKUP VERIFICATION

Guardian: Priya Patel
Status: Authorized Guardian ✓

Face Verification

[ Camera ]

✓ FACE VERIFIED

[ APPROVE PICKUP ]   [ REJECT ]
```

The page may also show the current/recent event after completion.

### Demo Boundary

This page is a **presentation/development interface**. It must use the real application architecture and APIs when integrated. It must not replace the ASP.NET Core backend, MySQL database, RFID integration, or Python face-verification service.

A temporary mock/simulation may be used only when physical RFID hardware or a camera is unavailable for development testing. The real integration must remain ready for the final demo.


## Development Phases
1. Architecture and database
2. Authentication and authorization
3. Admin student, Guardian, and RFID management
4. Transportation management
5. Attendance and journey
6. Guardian Flutter application
7. Face registration and verification service
8. Pickup verification integration
9. Notifications
10. Reports and dashboard
11. Testing, security, validation, and bug fixing
12. Final integration and deployment preparation

For every phase:
`Plan → Implement → Run/Test → Fix → Verify → Continue`

## Development Rules
1. Treat this document as the source of truth.
2. Do not add features outside scope.
3. No GPS/live bus tracking.
4. No Student login or Student mobile app.
5. No SOS/emergency functionality.
6. RFID identifies the student.
7. Face recognition verifies the authorized Guardian during pickup.
8. Journey tracking means events, not geographical tracking.
9. Keep RFID reader count configurable.
10. Prevent duplicate RFID events through state/event validation.
11. Keep Admin and Guardian permissions separate.
12. Keep database relationships clean.
13. Never hard-code secrets.
14. Write reusable, maintainable code.
15. Do not replace real functionality with fake/mock functionality unless hardware is unavailable for testing.
16. If hardware is unavailable, provide a clean development abstraction/mock while keeping real integration ready.
17. Do not silently change the architecture or stack.
18. Ask before making a major requirement change.

## First Assignment
Before implementing the application, provide:
1. Complete project architecture
2. Complete folder structure
3. Database ER design
4. Database table structure
5. API structure and major endpoints
6. ASP.NET Core architecture
7. Flutter architecture
8. Python face-service architecture
9. Communication flow between components
10. Complete development plan
11. Single-RFID-scanner presentation/demo page design
12. Student drop-off and Guardian pickup verification flow

**Do not generate the entire application at once. Start with the architecture and design, including the single-scanner demo scenario. After approval, implement phase by phase and test each phase before continuing.**

19. The same physical RFID scanner must support the configured logical checkpoints: School Arrival, School Departure, Bus Boarding, and Bus Drop-off.
20. Parent/Guardian student drop-off must be supported using the same RFID scanner and the School Arrival checkpoint.
21. Guardian pickup must include authorized-Guardian checking and successful face verification before pickup approval.
22. The Student Drop-off & Pickup Verification page is a demo/development UI and must remain integrated with the real backend, database, RFID, and face-verification architecture.

## Final Expected Flows

RFID:
`RFID → Student Identification → Attendance/Journey Event → Database → Guardian Notification`

Pickup:
`Student Pickup → Authorized Guardian → Face Verification → Verification Result → Pickup Approval/Reject → Record → Notification`

The final system must be practical, secure, maintainable, demonstrable, and suitable for academic presentation, testing, documentation, and viva.
