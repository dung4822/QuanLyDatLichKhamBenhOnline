using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WebDatLichKhamBenh.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatSessionsAndActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatSessions",
                columns: table => new
                {
                    ChatSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessions", x => x.ChatSessionId);
                    table.ForeignKey(
                        name: "FK_ChatSessions_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "PatientId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatTurns",
                columns: table => new
                {
                    ChatTurnId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChatSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AssistantReply = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatTurns", x => x.ChatTurnId);
                    table.ForeignKey(
                        name: "FK_ChatTurns_ChatSessions_ChatSessionId",
                        column: x => x.ChatSessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "ChatSessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatActions",
                columns: table => new
                {
                    ChatActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatTurnId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    PreviewJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatActions", x => x.ChatActionId);
                    table.CheckConstraint("CK_ChatActions_Status", "\"Status\" IN (0, 1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_ChatActions_Type", "\"Type\" IN (0, 1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_ChatActions_ChatTurns_ChatTurnId",
                        column: x => x.ChatTurnId,
                        principalTable: "ChatTurns",
                        principalColumn: "ChatTurnId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatActions_ChatTurnId",
                table: "ChatActions",
                column: "ChatTurnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_PatientId_UpdatedAt",
                table: "ChatSessions",
                columns: new[] { "PatientId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatTurns_ChatSessionId_ClientMessageId",
                table: "ChatTurns",
                columns: new[] { "ChatSessionId", "ClientMessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatActions");

            migrationBuilder.DropTable(
                name: "ChatTurns");

            migrationBuilder.DropTable(
                name: "ChatSessions");
        }
    }
}
