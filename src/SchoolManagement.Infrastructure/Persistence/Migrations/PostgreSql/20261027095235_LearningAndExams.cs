using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolManagement.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class LearningAndExams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SetOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DueAtLocal = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MaxMarks = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    OnlineSubmission = table.Column<bool>(type: "boolean", nullable: false),
                    AllowLate = table.Column<bool>(type: "boolean", nullable: false),
                    LatePenaltyPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    AttachmentPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AttachmentName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    AttachmentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReminderSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_Assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assignments_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Assignments_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Assignments_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Assignments_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClassDiaryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Topics = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Homework = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Reminders = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    KitNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_ClassDiaryEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassDiaryEntries_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassDiaryEntries_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassDiaryEntries_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassDiaryEntries_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Exams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    TermId = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameBn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SchedulePublished = table.Column<bool>(type: "boolean", nullable: false),
                    SchedulePublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultStatus = table.Column<int>(type: "integer", nullable: false),
                    FinalisedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_Exams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exams_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Exams_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Exams_Terms_TermId",
                        column: x => x.TermId,
                        principalTable: "Terms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GradebookCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    YearGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WeightPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_GradebookCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradebookCategories_YearGroups_YearGroupId",
                        column: x => x.YearGroupId,
                        principalTable: "YearGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GradebookModerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TermId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Adjustment = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModeratedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModeratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradebookModerations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradebookModerations_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradebookModerations_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradebookModerations_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradebookModerations_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradebookModerations_Terms_TermId",
                        column: x => x.TermId,
                        principalTable: "Terms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LearningResources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    YearGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Audience = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    FileType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    Downloads = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_LearningResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningResources_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LearningResources_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LearningResources_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LearningResources_YearGroups_YearGroupId",
                        column: x => x.YearGroupId,
                        principalTable: "YearGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssignmentSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    FileType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsLate = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Marks = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    FinalMarks = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Feedback = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    GradedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GradedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentSubmissions_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssignmentSubmissions_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssignmentSubmissions_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamClasses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    YearGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    GradingSchemeId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamClasses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamClasses_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamClasses_GradingSchemes_GradingSchemeId",
                        column: x => x.GradingSchemeId,
                        principalTable: "GradingSchemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamClasses_YearGroups_YearGroupId",
                        column: x => x.YearGroupId,
                        principalTable: "YearGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamInvigilators",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamInvigilators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamInvigilators_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamInvigilators_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamInvigilators_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamInvigilators_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamPapers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    YearGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamPapers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamPapers_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamPapers_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamPapers_YearGroups_YearGroupId",
                        column: x => x.YearGroupId,
                        principalTable: "YearGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    YearGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Percent = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    Grade = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Gpa = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: true),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    FailedSubjects = table.Column<int>(type: "integer", nullable: false),
                    SectionRank = table.Column<int>(type: "integer", nullable: true),
                    ClassRank = table.Column<int>(type: "integer", nullable: true),
                    Percentile = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    AttendancePercent = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    SubjectsJson = table.Column<string>(type: "text", nullable: false),
                    TeacherRemark = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PrincipalRemark = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Conduct = table.Column<int>(type: "integer", nullable: true),
                    CoCurricular = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    VerificationCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Signature = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamResults_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamResults_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamResults_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamResults_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamResults_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamSeats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeatNo = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSeats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamSeats_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamSeats_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamSeats_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamSeats_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportCardRemarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherRemark = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PrincipalRemark = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Conduct = table.Column<int>(type: "integer", nullable: true),
                    CoCurricular = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportCardRemarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportCardRemarks_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportCardRemarks_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportCardRemarks_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GradebookItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TermId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    MaxMarks = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_GradebookItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradebookItems_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradebookItems_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradebookItems_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradebookItems_GradebookCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "GradebookCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradebookItems_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradebookItems_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradebookItems_Terms_TermId",
                        column: x => x.TermId,
                        principalTable: "Terms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResourceDownloads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceDownloads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceDownloads_LearningResources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "LearningResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamPaperComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaxMarks = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WeightPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    FromGradebook = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamPaperComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamPaperComponents_ExamPapers_ExamPaperId",
                        column: x => x.ExamPaperId,
                        principalTable: "ExamPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarkSheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EnteredByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReopenCount = table.Column<int>(type: "integer", nullable: false),
                    ReopenReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarkSheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarkSheets_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarkSheets_ExamPapers_ExamPaperId",
                        column: x => x.ExamPaperId,
                        principalTable: "ExamPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarkSheets_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GradebookMarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Marks = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Excused = table.Column<bool>(type: "boolean", nullable: false),
                    Comment = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradebookMarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradebookMarks_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradebookMarks_GradebookItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "GradebookItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradebookMarks_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamMarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Marks = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Code = table.Column<int>(type: "integer", nullable: false),
                    SecondMarks = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SecondCode = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamMarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamMarks_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamMarks_ExamPaperComponents_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "ExamPaperComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamMarks_ExamPapers_ExamPaperId",
                        column: x => x.ExamPaperId,
                        principalTable: "ExamPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamMarks_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_AcademicYearId",
                table: "Assignments",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_CampusId",
                table: "Assignments",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_SectionId_DueAtLocal",
                table: "Assignments",
                columns: new[] { "SectionId", "DueAtLocal" });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_SubjectId",
                table: "Assignments",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentSubmissions_AssignmentId_StudentId",
                table: "AssignmentSubmissions",
                columns: new[] { "AssignmentId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentSubmissions_CampusId",
                table: "AssignmentSubmissions",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentSubmissions_StudentId",
                table: "AssignmentSubmissions",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassDiaryEntries_AcademicYearId",
                table: "ClassDiaryEntries",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassDiaryEntries_CampusId",
                table: "ClassDiaryEntries",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassDiaryEntries_SectionId_Date",
                table: "ClassDiaryEntries",
                columns: new[] { "SectionId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassDiaryEntries_SubjectId",
                table: "ClassDiaryEntries",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamClasses_ExamId_YearGroupId",
                table: "ExamClasses",
                columns: new[] { "ExamId", "YearGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamClasses_GradingSchemeId",
                table: "ExamClasses",
                column: "GradingSchemeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamClasses_YearGroupId",
                table: "ExamClasses",
                column: "YearGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamInvigilators_CampusId",
                table: "ExamInvigilators",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamInvigilators_ExamId_Date_RoomId_UserId",
                table: "ExamInvigilators",
                columns: new[] { "ExamId", "Date", "RoomId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamInvigilators_RoomId",
                table: "ExamInvigilators",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamInvigilators_UserId",
                table: "ExamInvigilators",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamMarks_CampusId",
                table: "ExamMarks",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamMarks_ComponentId_StudentId",
                table: "ExamMarks",
                columns: new[] { "ComponentId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamMarks_ExamPaperId_StudentId",
                table: "ExamMarks",
                columns: new[] { "ExamPaperId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamMarks_StudentId",
                table: "ExamMarks",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamPaperComponents_ExamPaperId",
                table: "ExamPaperComponents",
                column: "ExamPaperId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamPapers_ExamId_Date",
                table: "ExamPapers",
                columns: new[] { "ExamId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamPapers_ExamId_YearGroupId_SubjectId",
                table: "ExamPapers",
                columns: new[] { "ExamId", "YearGroupId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamPapers_SubjectId",
                table: "ExamPapers",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamPapers_YearGroupId",
                table: "ExamPapers",
                column: "YearGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_AcademicYearId",
                table: "ExamResults",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_CampusId",
                table: "ExamResults",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_ExamId_StudentId",
                table: "ExamResults",
                columns: new[] { "ExamId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_SectionId",
                table: "ExamResults",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_StudentId",
                table: "ExamResults",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_VerificationCode",
                table: "ExamResults",
                column: "VerificationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exams_AcademicYearId_Code",
                table: "Exams",
                columns: new[] { "AcademicYearId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exams_CampusId",
                table: "Exams",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_ResultStatus_PublishAtUtc",
                table: "Exams",
                columns: new[] { "ResultStatus", "PublishAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Exams_TermId",
                table: "Exams",
                column: "TermId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSeats_CampusId",
                table: "ExamSeats",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSeats_ExamId_RoomId_SeatNo",
                table: "ExamSeats",
                columns: new[] { "ExamId", "RoomId", "SeatNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamSeats_ExamId_StudentId",
                table: "ExamSeats",
                columns: new[] { "ExamId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamSeats_RoomId",
                table: "ExamSeats",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSeats_StudentId",
                table: "ExamSeats",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_GradebookCategories_YearGroupId",
                table: "GradebookCategories",
                column: "YearGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_GradebookItems_AcademicYearId",
                table: "GradebookItems",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_GradebookItems_AssignmentId",
                table: "GradebookItems",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_GradebookItems_CampusId",
                table: "GradebookItems",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_GradebookItems_CategoryId",
                table: "GradebookItems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GradebookItems_SectionId_SubjectId_TermId",
                table: "GradebookItems",
                columns: new[] { "SectionId", "SubjectId", "TermId" });

            migrationBuilder.CreateIndex(
                name: "IX_GradebookItems_SubjectId",
                table: "GradebookItems",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_GradebookItems_TermId",
                table: "GradebookItems",
                column: "TermId");

            migrationBuilder.CreateIndex(
                name: "IX_GradebookMarks_CampusId",
                table: "GradebookMarks",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_GradebookMarks_ItemId_StudentId",
                table: "GradebookMarks",
                columns: new[] { "ItemId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GradebookMarks_StudentId",
                table: "GradebookMarks",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_GradebookModerations_AcademicYearId",
                table: "GradebookModerations",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_GradebookModerations_CampusId",
                table: "GradebookModerations",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_GradebookModerations_SectionId_SubjectId_TermId",
                table: "GradebookModerations",
                columns: new[] { "SectionId", "SubjectId", "TermId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GradebookModerations_SubjectId",
                table: "GradebookModerations",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_GradebookModerations_TermId",
                table: "GradebookModerations",
                column: "TermId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningResources_CampusId",
                table: "LearningResources",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningResources_SectionId",
                table: "LearningResources",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningResources_SubjectId_YearGroupId",
                table: "LearningResources",
                columns: new[] { "SubjectId", "YearGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningResources_YearGroupId",
                table: "LearningResources",
                column: "YearGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_MarkSheets_CampusId",
                table: "MarkSheets",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_MarkSheets_ExamPaperId_SectionId",
                table: "MarkSheets",
                columns: new[] { "ExamPaperId", "SectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarkSheets_SectionId",
                table: "MarkSheets",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportCardRemarks_CampusId",
                table: "ReportCardRemarks",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportCardRemarks_ExamId_StudentId",
                table: "ReportCardRemarks",
                columns: new[] { "ExamId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportCardRemarks_StudentId",
                table: "ReportCardRemarks",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceDownloads_ResourceId_AtUtc",
                table: "ResourceDownloads",
                columns: new[] { "ResourceId", "AtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssignmentSubmissions");

            migrationBuilder.DropTable(
                name: "ClassDiaryEntries");

            migrationBuilder.DropTable(
                name: "ExamClasses");

            migrationBuilder.DropTable(
                name: "ExamInvigilators");

            migrationBuilder.DropTable(
                name: "ExamMarks");

            migrationBuilder.DropTable(
                name: "ExamResults");

            migrationBuilder.DropTable(
                name: "ExamSeats");

            migrationBuilder.DropTable(
                name: "GradebookMarks");

            migrationBuilder.DropTable(
                name: "GradebookModerations");

            migrationBuilder.DropTable(
                name: "MarkSheets");

            migrationBuilder.DropTable(
                name: "ReportCardRemarks");

            migrationBuilder.DropTable(
                name: "ResourceDownloads");

            migrationBuilder.DropTable(
                name: "ExamPaperComponents");

            migrationBuilder.DropTable(
                name: "GradebookItems");

            migrationBuilder.DropTable(
                name: "LearningResources");

            migrationBuilder.DropTable(
                name: "ExamPapers");

            migrationBuilder.DropTable(
                name: "Assignments");

            migrationBuilder.DropTable(
                name: "GradebookCategories");

            migrationBuilder.DropTable(
                name: "Exams");
        }
    }
}
