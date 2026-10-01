using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservas.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class CopiasDeLasRespuestasPorReserva : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reserva_id",
                table: "claves_idempotencia",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_claves_idempotencia_reserva_id",
                table: "claves_idempotencia",
                column: "reserva_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_claves_idempotencia_reserva_id",
                table: "claves_idempotencia");

            migrationBuilder.DropColumn(
                name: "reserva_id",
                table: "claves_idempotencia");
        }
    }
}
