using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolManagement.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AttendanceModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceRegisters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodNo = table.Column<int>(type: "integer", nullable: true),
                    TakenByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TakenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Present = table.Column<int>(type: "integer", nullable: false),
                    Absent = table.Column<int>(type: "integer", nullable: false),
                    Late = table.Column<int>(type: "integer", nullable: false),
                    OnLeave = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRegisters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRegisters_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceRegisters_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceRegisters_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceWarnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TermId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceWarnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceWarnings_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceWarnings_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceWarnings_Terms_TermId",
                        column: x => x.TermId,
                        principalTable: "Terms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameBn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DaysPerYear = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresDocument = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PeriodAttendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodNo = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Mark = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodAttendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PeriodAttendances_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PeriodAttendances_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PeriodAttendances_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PeriodAttendances_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffAttendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CheckInUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckOutUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    CheckInLatitude = table.Column<double>(type: "double precision", nullable: true),
                    CheckInLongitude = table.Column<double>(type: "double precision", nullable: true),
                    WithinGeofence = table.Column<bool>(type: "boolean", nullable: true),
                    LateMinutes = table.Column<int>(type: "integer", nullable: false),
                    EarlyLeaveMinutes = table.Column<int>(type: "integer", nullable: false),
                    OvertimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    StaffLeaveRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAttendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffAttendances_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffAttendances_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudentAttendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Mark = table.Column<int>(type: "integer", nullable: false),
                    ArrivalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    StudentLeaveRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlertSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentAttendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentAttendances_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentAttendances_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentAttendances_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentAttendances_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudentLeaveRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AttachmentPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AttachmentName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    AttachmentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentLeaveRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentLeaveRequests_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentLeaveRequests_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveEntitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    Days = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveEntitlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveEntitlements_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveEntitlements_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "LeaveTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveEntitlements_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffLeaveRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    HalfDay = table.Column<bool>(type: "boolean", nullable: false),
                    Days = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApprovalRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffLeaveRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffLeaveRequests_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffLeaveRequests_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffLeaveRequests_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "LeaveTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffLeaveRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRegisters_AcademicYearId",
                table: "AttendanceRegisters",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRegisters_CampusId",
                table: "AttendanceRegisters",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRegisters_Date",
                table: "AttendanceRegisters",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRegisters_SectionId_Date_PeriodNo",
                table: "AttendanceRegisters",
                columns: new[] { "SectionId", "Date", "PeriodNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceWarnings_CampusId",
                table: "AttendanceWarnings",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceWarnings_StudentId_TermId_Level",
                table: "AttendanceWarnings",
                columns: new[] { "StudentId", "TermId", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceWarnings_TermId",
                table: "AttendanceWarnings",
                column: "TermId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEntitlements_AcademicYearId",
                table: "LeaveEntitlements",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEntitlements_LeaveTypeId",
                table: "LeaveEntitlements",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEntitlements_UserId_LeaveTypeId_AcademicYearId",
                table: "LeaveEntitlements",
                columns: new[] { "UserId", "LeaveTypeId", "AcademicYearId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_Code",
                table: "LeaveTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodAttendances_AcademicYearId",
                table: "PeriodAttendances",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_PeriodAttendances_CampusId",
                table: "PeriodAttendances",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_PeriodAttendances_SectionId_Date_PeriodNo",
                table: "PeriodAttendances",
                columns: new[] { "SectionId", "Date", "PeriodNo" });

            migrationBuilder.CreateIndex(
                name: "IX_PeriodAttendances_StudentId_Date_PeriodNo",
                table: "PeriodAttendances",
                columns: new[] { "StudentId", "Date", "PeriodNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendances_CampusId",
                table: "StaffAttendances",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendances_Date",
                table: "StaffAttendances",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendances_UserId_Date",
                table: "StaffAttendances",
                columns: new[] { "UserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffLeaveRequests_AcademicYearId",
                table: "StaffLeaveRequests",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffLeaveRequests_CampusId",
                table: "StaffLeaveRequests",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffLeaveRequests_FromDate_ToDate",
                table: "StaffLeaveRequests",
                columns: new[] { "FromDate", "ToDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffLeaveRequests_LeaveTypeId",
                table: "StaffLeaveRequests",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffLeaveRequests_UserId_AcademicYearId_Status",
                table: "StaffLeaveRequests",
                columns: new[] { "UserId", "AcademicYearId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentAttendances_AcademicYearId",
                table: "StudentAttendances",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAttendances_CampusId",
                table: "StudentAttendances",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAttendances_Date_Mark_AlertSentAtUtc",
                table: "StudentAttendances",
                columns: new[] { "Date", "Mark", "AlertSentAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentAttendances_SectionId_Date",
                table: "StudentAttendances",
                columns: new[] { "SectionId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentAttendances_StudentId_Date",
                table: "StudentAttendances",
                columns: new[] { "StudentId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentLeaveRequests_CampusId",
                table: "StudentLeaveRequests",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentLeaveRequests_StudentId_Status",
                table: "StudentLeaveRequests",
                columns: new[] { "StudentId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceRegisters");

            migrationBuilder.DropTable(
                name: "AttendanceWarnings");

            migrationBuilder.DropTable(
                name: "LeaveEntitlements");

            migrationBuilder.DropTable(
                name: "PeriodAttendances");

            migrationBuilder.DropTable(
                name: "StaffAttendances");

            migrationBuilder.DropTable(
                name: "StaffLeaveRequests");

            migrationBuilder.DropTable(
                name: "StudentAttendances");

            migrationBuilder.DropTable(
                name: "StudentLeaveRequests");

            migrationBuilder.DropTable(
                name: "LeaveTypes");
        }
    }
}
