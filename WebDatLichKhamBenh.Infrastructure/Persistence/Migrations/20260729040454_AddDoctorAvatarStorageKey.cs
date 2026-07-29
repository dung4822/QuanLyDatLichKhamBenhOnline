using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebDatLichKhamBenh.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorAvatarStorageKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarStorageKey",
                table: "Doctors",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarStorageKey",
                table: "Doctors");
        }
    }
}
