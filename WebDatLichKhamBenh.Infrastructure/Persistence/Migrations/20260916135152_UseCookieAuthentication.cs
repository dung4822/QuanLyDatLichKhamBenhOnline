using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebDatLichKhamBenh.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UseCookieAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phiên Bearer cũ không dùng được với cookie mới; yêu cầu đăng nhập lại.
            migrationBuilder.Sql("DELETE FROM \"UserSessions\";");
            migrationBuilder.DropIndex(
                name: "IX_UserSessions_TokenHash",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "UserSessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cookie không có TokenHash để khôi phục; thu hồi phiên trước khi tạo lại unique index.
            migrationBuilder.Sql("DELETE FROM \"UserSessions\";");
            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "UserSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_TokenHash",
                table: "UserSessions",
                column: "TokenHash",
                unique: true);
        }
    }
}
