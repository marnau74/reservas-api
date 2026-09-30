using Microsoft.EntityFrameworkCore;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Infraestructura.Persistencia;
using Reservas.Tests.Comunes;

using Shouldly;

namespace Reservas.Infraestructura.Tests;

public class AlmacenIdempotenciaTests(ServidorPostgres servidor) : BaseDeDatosTest(servidor)
{
    private const string Clave = "clave-de-prueba-0001";
    private const string Huella = "huella-1";

    private static readonly RespuestaGuardada Respuesta = new(201, "application/json", "{\"ok\":true}", "/api/v1/reservas/gestion/abc");

    private async Task<ResultadoAdquisicion> Adquirir(string clave = Clave, string huella = Huella, DateTimeOffset? ahora = null)
    {
        await using var db = NuevoContexto();
        return await new AlmacenIdempotencia(db).AdquirirAsync(clave, huella, ahora ?? Ahora, TestContext.Current.CancellationToken);
    }

    private async Task Completar(string clave = Clave, DateTimeOffset? ahora = null)
    {
        await using var db = NuevoContexto();
        await new AlmacenIdempotencia(db).CompletarAsync(clave, Respuesta, ahora ?? Ahora, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task La_primera_vez_que_se_ve_una_clave_se_adquiere()
    {
        (await Adquirir()).Estado.ShouldBe(EstadoAdquisicion.Adquirida);
    }

    [Fact]
    public async Task Mientras_se_procesa_la_misma_peticion_otra_igual_ve_que_esta_en_curso()
    {
        await Adquirir();

        (await Adquirir()).Estado.ShouldBe(EstadoAdquisicion.EnCurso);
    }

    [Fact]
    public async Task Una_peticion_ya_completada_devuelve_la_respuesta_guardada()
    {
        await Adquirir();
        await Completar();

        var repetida = await Adquirir();

        repetida.Estado.ShouldBe(EstadoAdquisicion.Repetida);
        repetida.Respuesta.ShouldBe(Respuesta);
    }

    [Fact]
    public async Task Reutilizar_la_clave_para_otra_peticion_se_detecta()
    {
        await Adquirir();
        await Completar();

        (await Adquirir(huella: "huella-distinta")).Estado.ShouldBe(EstadoAdquisicion.HuellaDistinta);
    }

    [Fact]
    public async Task Tambien_se_detecta_la_huella_distinta_mientras_la_peticion_esta_en_curso()
    {
        await Adquirir();

        (await Adquirir(huella: "huella-distinta")).Estado.ShouldBe(EstadoAdquisicion.HuellaDistinta);
    }

    [Fact]
    public async Task Liberar_una_clave_permite_reintentar_la_peticion()
    {
        await Adquirir();

        await using (var db = NuevoContexto())
        {
            await new AlmacenIdempotencia(db).LiberarAsync(Clave, TestContext.Current.CancellationToken);
        }

        (await Adquirir()).Estado.ShouldBe(EstadoAdquisicion.Adquirida);
    }

    [Fact]
    public async Task Liberar_no_borra_una_clave_ya_completada()
    {
        await Adquirir();
        await Completar();

        await using (var db = NuevoContexto())
        {
            await new AlmacenIdempotencia(db).LiberarAsync(Clave, TestContext.Current.CancellationToken);
        }

        (await Adquirir()).Estado.ShouldBe(EstadoAdquisicion.Repetida);
    }

    [Fact]
    public async Task Una_peticion_en_curso_que_lleva_demasiado_se_da_por_abandonada_y_se_puede_retomar()
    {
        await Adquirir();

        (await Adquirir(ahora: Ahora.AddMinutes(1))).Estado.ShouldBe(EstadoAdquisicion.EnCurso);
        (await Adquirir(ahora: Ahora.AddMinutes(3))).Estado.ShouldBe(EstadoAdquisicion.Adquirida);
    }

    [Fact]
    public async Task Al_retomar_una_peticion_abandonada_la_siguiente_ya_la_ve_en_curso()
    {
        await Adquirir();
        (await Adquirir(ahora: Ahora.AddMinutes(3))).Estado.ShouldBe(EstadoAdquisicion.Adquirida);

        (await Adquirir(ahora: Ahora.AddMinutes(3).AddSeconds(5))).Estado.ShouldBe(EstadoAdquisicion.EnCurso);
    }

    [Fact]
    public async Task Claves_distintas_no_se_afectan()
    {
        await Adquirir("clave-de-prueba-0001");
        await Completar("clave-de-prueba-0001");

        (await Adquirir("clave-de-prueba-0002")).Estado.ShouldBe(EstadoAdquisicion.Adquirida);
    }

    [Fact]
    public async Task Cuando_llegan_veinte_peticiones_con_la_misma_clave_a_la_vez_solo_una_se_adquiere()
    {
        var salida = new TaskCompletionSource();

        var peticiones = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var db = NuevoContexto();
            var almacen = new AlmacenIdempotencia(db);
            await salida.Task;
            return await almacen.AdquirirAsync(Clave, Huella, Ahora, TestContext.Current.CancellationToken);
        }).ToArray();

        salida.SetResult();
        var resultados = await Task.WhenAll(peticiones);

        resultados.Count(r => r.Estado == EstadoAdquisicion.Adquirida).ShouldBe(1);
        resultados.Count(r => r.Estado == EstadoAdquisicion.EnCurso).ShouldBe(19);

        await using var comprobacion = NuevoContexto();
        (await comprobacion.ClavesIdempotencia.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }
}
