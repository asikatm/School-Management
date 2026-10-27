using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolManagement.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class NoticeBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampusId = table.Column<Guid>(type: "uuid", nullable: true),
                    YearGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TitleBn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    ForStudents = table.Column<bool>(type: "boolean", nullable: false),
                    ForParents = table.Column<bool>(type: "boolean", nullable: false),
                    ForStaff = table.Column<bool>(type: "boolean", nullable: false),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                    IsUrgent = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    AttachmentPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AttachmentName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    AttachmentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_Notices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notices_Campuses_CampusId",
                        column: x => x.CampusId,
                        principalTable: "Campuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notices_YearGroups_YearGroupId",
                        column: x => x.YearGroupId,
                        principalTable: "YearGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notices_CampusId",
                table: "Notices",
                column: "CampusId");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_PublishedAtUtc",
                table: "Notices",
                column: "PublishedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_YearGroupId",
                table: "Notices",
                column: "YearGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notices");
        }
    }
}
