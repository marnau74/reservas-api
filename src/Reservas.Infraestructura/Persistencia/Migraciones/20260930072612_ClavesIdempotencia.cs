using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservas.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ClavesIdempotencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "claves_idempotencia",
                columns: table => new
                {
                    clave = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    huella_peticion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    completada = table.Column<bool>(type: "boolean", nullable: false),
                    estado_http = table.Column<int>(type: "integer", nullable: true),
                    tipo_contenido = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cuerpo = table.Column<string>(type: "text", nullable: true),
                    ubicacion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_claves_idempotencia", x => x.clave);
                });

            migrationBuilder.CreateIndex(
                name: "ix_claves_idempotencia_actualizada_en",
                table: "claves_idempotencia",
                column: "actualizada_en");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "claves_idempotencia");
        }
    }
}
