using Microsoft.EntityFrameworkCore;

using Npgsql;

using Reservas.Tests.Comunes;

using Shouldly;

namespace Reservas.Infraestructura.Tests;

public class MigracionesTests(ServidorPostgres servidor) : BaseDeDatosTest(servidor)
{
    [Fact]
    public async Task Las_migraciones_se_aplican_a_una_base_de_datos_vacia_y_reflejan_el_modelo()
    {
        await using var db = NuevoContexto();

        // InitializeAsync ya ha aplicado las migraciones: no debe quedar ninguna pendiente ni
        // cambios del modelo sin migración (alguien cambió el modelo y olvidó generarla).
        (await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task Existe_la_restriccion_de_exclusion_de_ocupaciones()
    {
        var definicion = await ConsultarEscalarAsync(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ex_ocupaciones_mesa_sin_solapes' AND contype = 'x'");

        definicion.ShouldNotBeNull();
        definicion.ShouldContain("EXCLUDE USING gist");
        definicion.ShouldContain("mesa_id WITH =");
        definicion.ShouldContain("periodo WITH &&");
        definicion.ShouldContain("WHERE (activa)");
    }

    [Fact]
    public async Task Las_tablas_y_columnas_usan_nombres_en_snake_case()
    {
        var tablas = await ConsultarListaAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory' ORDER BY 1");

        tablas.ShouldBe(["cierres", "claves_idempotencia", "horarios", "mesas", "negocios", "ocupaciones_mesa", "reservas", "salas", "tokens_refresco", "usuarios"]);
    }

    [Fact]
    public async Task Las_columnas_de_tiempo_usan_tipos_de_postgres_con_zona_horaria()
    {
        var tipos = await ConsultarListaAsync(
            "SELECT column_name || ':' || data_type FROM information_schema.columns " +
            "WHERE table_name IN ('reservas', 'ocupaciones_mesa') AND column_name IN ('periodo', 'creada_en', 'mesa_ids') ORDER BY 1");

        tipos.ShouldContain("creada_en:timestamp with time zone");
        tipos.ShouldContain("mesa_ids:ARRAY");
        tipos.ShouldContain("periodo:tstzrange");
    }

    [Fact]
    public async Task Una_mesa_con_capacidades_incoherentes_la_rechaza_la_base_de_datos()
    {
        await using var conexion = new NpgsqlConnection(CadenaConexion);
        await conexion.OpenAsync(TestContext.Current.CancellationToken);
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO mesas (id, sala_id, nombre, capacidad_minima, capacidad_maxima, es_combinable, negocio_id) " +
            "VALUES (gen_random_uuid(), @sala, 'Rota', 5, 2, false, @negocio)",
            conexion);
        insertar.Parameters.AddWithValue("sala", Sala.Id);
        insertar.Parameters.AddWithValue("negocio", Negocio.Id);

        var error = await Should.ThrowAsync<PostgresException>(() => insertar.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        error.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    private async Task<string?> ConsultarEscalarAsync(string sql)
    {
        await using var conexion = new NpgsqlConnection(CadenaConexion);
        await conexion.OpenAsync(TestContext.Current.CancellationToken);
        await using var comando = new NpgsqlCommand(sql, conexion);
        return (string?)await comando.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<string>> ConsultarListaAsync(string sql)
    {
        await using var conexion = new NpgsqlConnection(CadenaConexion);
        await conexion.OpenAsync(TestContext.Current.CancellationToken);
        await using var comando = new NpgsqlCommand(sql, conexion);
        await using var lector = await comando.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var filas = new List<string>();
        while (await lector.ReadAsync(TestContext.Current.CancellationToken))
        {
            filas.Add(lector.GetString(0));
        }

        return filas;
    }
}
