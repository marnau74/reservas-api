using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Reservas.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "negocios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    zona = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    politicas_antelacion_minima = table.Column<TimeSpan>(type: "interval", nullable: false),
                    politicas_dias_maximos_antelacion = table.Column<int>(type: "integer", nullable: false),
                    politicas_max_comensales_online = table.Column<int>(type: "integer", nullable: false),
                    politicas_duracion_estandar = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_negocios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cierres",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    turno = table.Column<short>(type: "smallint", nullable: true),
                    motivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cierres", x => x.id);
                    table.ForeignKey(
                        name: "fk_cierres_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "horarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dia = table.Column<short>(type: "smallint", nullable: false),
                    turno = table.Column<short>(type: "smallint", nullable: false),
                    inicio = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    fin = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    intervalo_minutos = table.Column<int>(type: "integer", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_horarios", x => x.id);
                    table.CheckConstraint("ck_horarios_fin", "fin >= inicio");
                    table.CheckConstraint("ck_horarios_intervalo", "intervalo_minutos BETWEEN 5 AND 120");
                    table.ForeignKey(
                        name: "fk_horarios_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reservas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo = table.Column<NpgsqlRange<DateTime>>(type: "tstzrange", nullable: false),
                    comensales = table.Column<int>(type: "integer", nullable: false),
                    cliente_nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cliente_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    cliente_telefono = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    codigo_gestion = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    mesa_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservas", x => x.id);
                    table.ForeignKey(
                        name: "fk_reservas_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "salas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salas", x => x.id);
                    table.ForeignKey(
                        name: "fk_salas_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mesas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sala_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    capacidad_minima = table.Column<int>(type: "integer", nullable: false),
                    capacidad_maxima = table.Column<int>(type: "integer", nullable: false),
                    es_combinable = table.Column<bool>(type: "boolean", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mesas", x => x.id);
                    table.CheckConstraint("ck_mesas_capacidad", "capacidad_minima >= 1 AND capacidad_minima <= capacidad_maxima AND capacidad_maxima <= 30");
                    table.ForeignKey(
                        name: "fk_mesas_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mesas_salas_sala_id",
                        column: x => x.sala_id,
                        principalTable: "salas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ocupaciones_mesa",
                columns: table => new
                {
                    reserva_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mesa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo = table.Column<NpgsqlRange<DateTime>>(type: "tstzrange", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ocupaciones_mesa", x => new { x.reserva_id, x.mesa_id });
                    table.ForeignKey(
                        name: "fk_ocupaciones_mesa_mesas_mesa_id",
                        column: x => x.mesa_id,
                        principalTable: "mesas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ocupaciones_mesa_reservas_reserva_id",
                        column: x => x.reserva_id,
                        principalTable: "reservas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cierres_negocio_id_fecha",
                table: "cierres",
                columns: new[] { "negocio_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_horarios_negocio_id",
                table: "horarios",
                column: "negocio_id");

            migrationBuilder.CreateIndex(
                name: "ix_mesas_negocio_id",
                table: "mesas",
                column: "negocio_id");

            migrationBuilder.CreateIndex(
                name: "ix_mesas_sala_id",
                table: "mesas",
                column: "sala_id");

            migrationBuilder.CreateIndex(
                name: "ix_negocios_slug",
                table: "negocios",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ocupaciones_mesa_mesa_id",
                table: "ocupaciones_mesa",
                column: "mesa_id");

            migrationBuilder.CreateIndex(
                name: "ix_ocupaciones_mesa_negocio_id",
                table: "ocupaciones_mesa",
                column: "negocio_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservas_codigo_gestion",
                table: "reservas",
                column: "codigo_gestion",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reservas_negocio_id",
                table: "reservas",
                column: "negocio_id");

            migrationBuilder.CreateIndex(
                name: "ix_salas_negocio_id",
                table: "salas",
                column: "negocio_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cierres");

            migrationBuilder.DropTable(
                name: "horarios");

            migrationBuilder.DropTable(
                name: "ocupaciones_mesa");

            migrationBuilder.DropTable(
                name: "mesas");

            migrationBuilder.DropTable(
                name: "reservas");

            migrationBuilder.DropTable(
                name: "salas");

            migrationBuilder.DropTable(
                name: "negocios");
        }
    }
}
