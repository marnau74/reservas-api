using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservas.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Personal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hash_contrasena = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    intentos_fallidos = table.Column<int>(type: "integer", nullable: false),
                    bloqueado_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuarios_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tokens_refresco",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revocado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tokens_refresco", x => x.id);
                    table.ForeignKey(
                        name: "fk_tokens_refresco_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tokens_refresco_expira_en",
                table: "tokens_refresco",
                column: "expira_en");

            migrationBuilder.CreateIndex(
                name: "ix_tokens_refresco_hash_token",
                table: "tokens_refresco",
                column: "hash_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tokens_refresco_usuario_id",
                table: "tokens_refresco",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                table: "usuarios",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_negocio_id",
                table: "usuarios",
                column: "negocio_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tokens_refresco");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
