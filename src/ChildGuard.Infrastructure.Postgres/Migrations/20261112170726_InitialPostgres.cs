using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ChildGuard.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "Admins",
                columns: table => new
                {
                    AdminId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PasswordResetToken = table.Column<string>(type: "text", nullable: true),
                    PasswordResetTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admins", x => x.AdminId);
                });

            migrationBuilder.CreateTable(
                name: "Drivers",
                columns: table => new
                {
                    DriverId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "citext", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    LicenseNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drivers", x => x.DriverId);
                });

            migrationBuilder.CreateTable(
                name: "FaceAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<int>(type: "integer", nullable: true),
                    Layer = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: true),
                    GuardianId = table.Column<int>(type: "integer", nullable: true),
                    VerificationId = table.Column<int>(type: "integer", nullable: true),
                    SessionId = table.Column<int>(type: "integer", nullable: true),
                    Detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Guardians",
                columns: table => new
                {
                    GuardianId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "citext", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "citext", maxLength: 30, nullable: true),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PasswordResetToken = table.Column<string>(type: "text", nullable: true),
                    PasswordResetTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordResetRequired = table.Column<bool>(type: "boolean", nullable: false),
                    FcmToken = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guardians", x => x.GuardianId);
                });

            migrationBuilder.CreateTable(
                name: "RfidDevices",
                columns: table => new
                {
                    RfidDeviceId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceLabel = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    ConnectionStatus = table.Column<int>(type: "integer", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApiKeyHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    ApiKeyIssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RfidDevices", x => x.RfidDeviceId);
                });

            migrationBuilder.CreateTable(
                name: "Routes",
                columns: table => new
                {
                    RouteId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RouteName = table.Column<string>(type: "citext", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routes", x => x.RouteId);
                });

            migrationBuilder.CreateTable(
                name: "Students",
                columns: table => new
                {
                    StudentId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "citext", maxLength: 150, nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Grade = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Students", x => x.StudentId);
                });

            migrationBuilder.CreateTable(
                name: "TemporaryPickupAuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AuthorizationId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActorRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<int>(type: "integer", nullable: true),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryPickupAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GuardianFaceData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuardianId = table.Column<int>(type: "integer", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianFaceData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuardianFaceData_Guardians_GuardianId",
                        column: x => x.GuardianId,
                        principalTable: "Guardians",
                        principalColumn: "GuardianId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GuardianFaceReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuardianId = table.Column<int>(type: "integer", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    QualityJson = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedById = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianFaceReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuardianFaceReferences_Guardians_GuardianId",
                        column: x => x.GuardianId,
                        principalTable: "Guardians",
                        principalColumn: "GuardianId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GuardianOtpChallenges",
                columns: table => new
                {
                    GuardianOtpChallengeId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChallengeToken = table.Column<Guid>(type: "uuid", nullable: false),
                    GuardianId = table.Column<int>(type: "integer", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    ResendCount = table.Column<int>(type: "integer", nullable: false),
                    LastSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsConsumed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianOtpChallenges", x => x.GuardianOtpChallengeId);
                    table.ForeignKey(
                        name: "FK_GuardianOtpChallenges_Guardians_GuardianId",
                        column: x => x.GuardianId,
                        principalTable: "Guardians",
                        principalColumn: "GuardianId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GuardianPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuardianId = table.Column<int>(type: "integer", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    ByteSize = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuardianPhotos_Guardians_GuardianId",
                        column: x => x.GuardianId,
                        principalTable: "Guardians",
                        principalColumn: "GuardianId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GuardianTrustedDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuardianId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianTrustedDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuardianTrustedDevices_Guardians_GuardianId",
                        column: x => x.GuardianId,
                        principalTable: "Guardians",
                        principalColumn: "GuardianId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Buses",
                columns: table => new
                {
                    BusId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusNumber = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    DriverId = table.Column<int>(type: "integer", nullable: true),
                    RouteId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Buses", x => x.BusId);
                    table.ForeignKey(
                        name: "FK_Buses_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "DriverId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Buses_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "RouteId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GuardianVerifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    GuardianId = table.Column<int>(type: "integer", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MatchScore = table.Column<double>(type: "double precision", nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    PickupApproved = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedByAdminId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CaptureSource = table.Column<int>(type: "integer", nullable: true),
                    QualityResult = table.Column<int>(type: "integer", nullable: true),
                    FaceDetectionResult = table.Column<int>(type: "integer", nullable: true),
                    IdentityResult = table.Column<int>(type: "integer", nullable: true),
                    AuthorisationResult = table.Column<int>(type: "integer", nullable: true),
                    LivenessResult = table.Column<int>(type: "integer", nullable: true),
                    LivenessScore = table.Column<double>(type: "double precision", nullable: true),
                    BestReferenceId = table.Column<int>(type: "integer", nullable: true),
                    SessionId = table.Column<int>(type: "integer", nullable: true),
                    LayerNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuardianVerifications_Admins_ApprovedByAdminId",
                        column: x => x.ApprovedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "AdminId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GuardianVerifications_Guardians_GuardianId",
                        column: x => x.GuardianId,
                        principalTable: "Guardians",
                        principalColumn: "GuardianId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GuardianVerifications_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuardianId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RelatedEntityType = table.Column<string>(type: "text", nullable: true),
                    RelatedEntityId = table.Column<int>(type: "integer", nullable: true),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FcmStatus = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationId);
                    table.ForeignKey(
                        name: "FK_Notifications_Guardians_GuardianId",
                        column: x => x.GuardianId,
                        principalTable: "Guardians",
                        principalColumn: "GuardianId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notifications_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RfidCards",
                columns: table => new
                {
                    RfidCardId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CardUid = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RfidCards", x => x.RfidCardId);
                    table.ForeignKey(
                        name: "FK_RfidCards_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StudentGuardians",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    GuardianId = table.Column<int>(type: "integer", nullable: false),
                    Relationship = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsAuthorizedForPickup = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentGuardians", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentGuardians_Guardians_GuardianId",
                        column: x => x.GuardianId,
                        principalTable: "Guardians",
                        principalColumn: "GuardianId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentGuardians_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    ByteSize = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentPhotos_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TemporaryPickupAuthorizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    RequestedByGuardianId = table.Column<int>(type: "integer", nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Relationship = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IdentityNote = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedByAdminId = table.Column<int>(type: "integer", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedByAdminId = table.Column<int>(type: "integer", nullable: true),
                    HandoverFaceScore = table.Column<double>(type: "double precision", nullable: true),
                    JourneyEventId = table.Column<int>(type: "integer", nullable: true),
                    QrNonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FaceEmbeddingJson = table.Column<string>(type: "text", nullable: false),
                    FaceModel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryPickupAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemporaryPickupAuthorizations_Admins_CompletedByAdminId",
                        column: x => x.CompletedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "AdminId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryPickupAuthorizations_Admins_DecidedByAdminId",
                        column: x => x.DecidedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "AdminId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryPickupAuthorizations_Guardians_RequestedByGuardian~",
                        column: x => x.RequestedByGuardianId,
                        principalTable: "Guardians",
                        principalColumn: "GuardianId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryPickupAuthorizations_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VerificationSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByAdminId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    IdentifiedGuardianId = table.Column<int>(type: "integer", nullable: true),
                    IdentifiedSource = table.Column<int>(type: "integer", nullable: true),
                    IdentifiedScore = table.Column<double>(type: "double precision", nullable: true),
                    ChallengeTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ChallengeSteps = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ChallengeIssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ChallengeExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ChallengeConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedLiveAttempts = table.Column<int>(type: "integer", nullable: false),
                    NeutralFrameHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    VerificationId = table.Column<int>(type: "integer", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationSessions_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudentBusAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    BusId = table.Column<int>(type: "integer", nullable: false),
                    RouteId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentBusAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentBusAssignments_Buses_BusId",
                        column: x => x.BusId,
                        principalTable: "Buses",
                        principalColumn: "BusId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentBusAssignments_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "RouteId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StudentBusAssignments_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRecords",
                columns: table => new
                {
                    AttendanceId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    RfidCardId = table.Column<int>(type: "integer", nullable: true),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRecords", x => x.AttendanceId);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_RfidCards_RfidCardId",
                        column: x => x.RfidCardId,
                        principalTable: "RfidCards",
                        principalColumn: "RfidCardId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JourneyEvents",
                columns: table => new
                {
                    JourneyEventId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    RfidCardId = table.Column<int>(type: "integer", nullable: true),
                    RfidDeviceId = table.Column<int>(type: "integer", nullable: true),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    ScanId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JourneyEvents", x => x.JourneyEventId);
                    table.ForeignKey(
                        name: "FK_JourneyEvents_RfidCards_RfidCardId",
                        column: x => x.RfidCardId,
                        principalTable: "RfidCards",
                        principalColumn: "RfidCardId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_JourneyEvents_RfidDevices_RfidDeviceId",
                        column: x => x.RfidDeviceId,
                        principalTable: "RfidDevices",
                        principalColumn: "RfidDeviceId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_JourneyEvents_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemporaryPickupPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TemporaryPickupAuthorizationId = table.Column<int>(type: "integer", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    ByteSize = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryPickupPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemporaryPickupPhotos_TemporaryPickupAuthorizations_Tempora~",
                        column: x => x.TemporaryPickupAuthorizationId,
                        principalTable: "TemporaryPickupAuthorizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Admins_Email",
                table: "Admins",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_RfidCardId",
                table: "AttendanceRecords",
                column: "RfidCardId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_StudentId_Date_EventType",
                table: "AttendanceRecords",
                columns: new[] { "StudentId", "Date", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_Buses_BusNumber",
                table: "Buses",
                column: "BusNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Buses_DriverId",
                table: "Buses",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_Buses_RouteId",
                table: "Buses",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_FaceAuditLogs_GuardianId",
                table: "FaceAuditLogs",
                column: "GuardianId");

            migrationBuilder.CreateIndex(
                name: "IX_FaceAuditLogs_OccurredAt",
                table: "FaceAuditLogs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_FaceAuditLogs_StudentId",
                table: "FaceAuditLogs",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFaceData_GuardianId",
                table: "GuardianFaceData",
                column: "GuardianId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianFaceReferences_GuardianId_IsActive",
                table: "GuardianFaceReferences",
                columns: new[] { "GuardianId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_GuardianOtpChallenges_ChallengeToken",
                table: "GuardianOtpChallenges",
                column: "ChallengeToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianOtpChallenges_GuardianId_IsConsumed",
                table: "GuardianOtpChallenges",
                columns: new[] { "GuardianId", "IsConsumed" });

            migrationBuilder.CreateIndex(
                name: "IX_GuardianPhotos_GuardianId",
                table: "GuardianPhotos",
                column: "GuardianId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Guardians_Email",
                table: "Guardians",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Guardians_Phone",
                table: "Guardians",
                column: "Phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianTrustedDevices_GuardianId_RevokedAt",
                table: "GuardianTrustedDevices",
                columns: new[] { "GuardianId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GuardianTrustedDevices_TokenHash",
                table: "GuardianTrustedDevices",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianVerifications_ApprovedByAdminId",
                table: "GuardianVerifications",
                column: "ApprovedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianVerifications_AttemptedAt",
                table: "GuardianVerifications",
                column: "AttemptedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianVerifications_GuardianId",
                table: "GuardianVerifications",
                column: "GuardianId");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianVerifications_StudentId_AttemptedAt",
                table: "GuardianVerifications",
                columns: new[] { "StudentId", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JourneyEvents_RfidCardId",
                table: "JourneyEvents",
                column: "RfidCardId");

            migrationBuilder.CreateIndex(
                name: "IX_JourneyEvents_RfidDeviceId_ScanId",
                table: "JourneyEvents",
                columns: new[] { "RfidDeviceId", "ScanId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JourneyEvents_StudentId_Timestamp",
                table: "JourneyEvents",
                columns: new[] { "StudentId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_JourneyEvents_Timestamp",
                table: "JourneyEvents",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_GuardianId_IsRead",
                table: "Notifications",
                columns: new[] { "GuardianId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_StudentId",
                table: "Notifications",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_RfidCards_CardUid",
                table: "RfidCards",
                column: "CardUid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RfidCards_StudentId",
                table: "RfidCards",
                column: "StudentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RfidDevices_ApiKeyHash",
                table: "RfidDevices",
                column: "ApiKeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RfidDevices_DeviceLabel",
                table: "RfidDevices",
                column: "DeviceLabel",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentBusAssignments_BusId",
                table: "StudentBusAssignments",
                column: "BusId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBusAssignments_RouteId",
                table: "StudentBusAssignments",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBusAssignments_StudentId_BusId",
                table: "StudentBusAssignments",
                columns: new[] { "StudentId", "BusId" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentGuardians_GuardianId",
                table: "StudentGuardians",
                column: "GuardianId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGuardians_StudentId_GuardianId",
                table: "StudentGuardians",
                columns: new[] { "StudentId", "GuardianId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentPhotos_StudentId",
                table: "StudentPhotos",
                column: "StudentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupAuditEntries_AuthorizationId_OccurredAt",
                table: "TemporaryPickupAuditEntries",
                columns: new[] { "AuthorizationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupAuditEntries_StudentId",
                table: "TemporaryPickupAuditEntries",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupAuthorizations_CompletedByAdminId",
                table: "TemporaryPickupAuthorizations",
                column: "CompletedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupAuthorizations_DecidedByAdminId",
                table: "TemporaryPickupAuthorizations",
                column: "DecidedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupAuthorizations_RequestedByGuardianId",
                table: "TemporaryPickupAuthorizations",
                column: "RequestedByGuardianId");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupAuthorizations_Status_ValidUntilUtc",
                table: "TemporaryPickupAuthorizations",
                columns: new[] { "Status", "ValidUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupAuthorizations_StudentId_ValidUntilUtc",
                table: "TemporaryPickupAuthorizations",
                columns: new[] { "StudentId", "ValidUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupPhotos_TemporaryPickupAuthorizationId",
                table: "TemporaryPickupPhotos",
                column: "TemporaryPickupAuthorizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationSessions_NeutralFrameHash",
                table: "VerificationSessions",
                column: "NeutralFrameHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationSessions_StudentId_State",
                table: "VerificationSessions",
                columns: new[] { "StudentId", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceRecords");

            migrationBuilder.DropTable(
                name: "FaceAuditLogs");

            migrationBuilder.DropTable(
                name: "GuardianFaceData");

            migrationBuilder.DropTable(
                name: "GuardianFaceReferences");

            migrationBuilder.DropTable(
                name: "GuardianOtpChallenges");

            migrationBuilder.DropTable(
                name: "GuardianPhotos");

            migrationBuilder.DropTable(
                name: "GuardianTrustedDevices");

            migrationBuilder.DropTable(
                name: "GuardianVerifications");

            migrationBuilder.DropTable(
                name: "JourneyEvents");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "StudentBusAssignments");

            migrationBuilder.DropTable(
                name: "StudentGuardians");

            migrationBuilder.DropTable(
                name: "StudentPhotos");

            migrationBuilder.DropTable(
                name: "TemporaryPickupAuditEntries");

            migrationBuilder.DropTable(
                name: "TemporaryPickupPhotos");

            migrationBuilder.DropTable(
                name: "VerificationSessions");

            migrationBuilder.DropTable(
                name: "RfidCards");

            migrationBuilder.DropTable(
                name: "RfidDevices");

            migrationBuilder.DropTable(
                name: "Buses");

            migrationBuilder.DropTable(
                name: "TemporaryPickupAuthorizations");

            migrationBuilder.DropTable(
                name: "Drivers");

            migrationBuilder.DropTable(
                name: "Routes");

            migrationBuilder.DropTable(
                name: "Admins");

            migrationBuilder.DropTable(
                name: "Guardians");

            migrationBuilder.DropTable(
                name: "Students");
        }
    }
}
