using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolManagement.Infrastructure.Persistence.Migrations.SqlServer
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampusId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    YearGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TitleBn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ForStudents = table.Column<bool>(type: "bit", nullable: false),
                    ForParents = table.Column<bool>(type: "bit", nullable: false),
                    ForStaff = table.Column<bool>(type: "bit", nullable: false),
                    IsPinned = table.Column<bool>(type: "bit", nullable: false),
                    IsUrgent = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    AttachmentPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AttachmentName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    AttachmentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
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
