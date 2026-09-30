using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservas.Infraestructura.Persistencia.Migraciones
{
    /// <summary>
    /// Migración escrita a mano: EF Core no sabe expresar una restricción de exclusión.
    ///
    /// Impide que dos filas de <c>ocupaciones_mesa</c> activas tengan la misma mesa y periodos
    /// que se solapan. Es lo que hace imposible reservar dos veces la misma mesa a la misma
    /// hora, incluso con cientos de peticiones simultáneas: PostgreSQL serializa las
    /// escrituras que compiten por ella y rechaza todas menos una.
    /// </summary>
    public partial class RestriccionExclusionMesas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE ocupaciones_mesa
                    ADD CONSTRAINT ex_ocupaciones_mesa_sin_solapes
                    EXCLUDE USING gist (mesa_id WITH =, periodo WITH &&)
                    WHERE (activa);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE ocupaciones_mesa DROP CONSTRAINT ex_ocupaciones_mesa_sin_solapes;");
        }
    }
}
