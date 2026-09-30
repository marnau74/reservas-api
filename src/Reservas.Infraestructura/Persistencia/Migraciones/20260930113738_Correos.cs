using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservas.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Correos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "recordatorio_programado",
                table: "reservas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "correos_pendientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reserva_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    destinatario = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    asunto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cuerpo = table.Column<string>(type: "text", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    proximo_intento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    enviado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    abandonado = table.Column<bool>(type: "boolean", nullable: false),
                    ultimo_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_correos_pendientes", x => x.id);
                    table.ForeignKey(
                        name: "fk_correos_pendientes_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_correos_pendientes_reservas_reserva_id",
                        column: x => x.reserva_id,
                        principalTable: "reservas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_correos_pendientes_negocio_id",
                table: "correos_pendientes",
                column: "negocio_id");

            migrationBuilder.CreateIndex(
                name: "ix_correos_pendientes_proximo_intento_en",
                table: "correos_pendientes",
                column: "proximo_intento_en",
                filter: "enviado_en IS NULL AND abandonado = false");

            migrationBuilder.CreateIndex(
                name: "ix_correos_pendientes_reserva_id",
                table: "correos_pendientes",
                column: "reserva_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "correos_pendientes");

            migrationBuilder.DropColumn(
                name: "recordatorio_programado",
                table: "reservas");
        }
    }
}
