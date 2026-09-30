using System.Net;

using Microsoft.EntityFrameworkCore;

using Shouldly;

namespace Reservas.Api.Tests;

/// <summary>Las peticiones simultáneas de verdad, a través de HTTP, contra PostgreSQL.</summary>
public class ConcurrenciaTests(ApiConBaseDeDatos api) : PruebaApi(api), IClassFixture<ApiConBaseDeDatos>
{
    [Fact]
    public async Task Veinte_peticiones_simultaneas_por_la_misma_hora_de_una_mesa_producen_una_sola_reserva()
    {
        var fecha = Api.SiguienteFecha();
        var marca = Guid.NewGuid().ToString("N");
        var salida = new TaskCompletionSource();

        var peticiones = Enumerable.Range(0, 20).Select(async indice =>
        {
            await salida.Task;
            return await ReservarAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha, "21:00", email: $"{indice}.{marca}@example.com");
        }).ToArray();

        salida.SetResult();
        var respuestas = await Task.WhenAll(peticiones);

        // Exactamente una gana; las otras diecinueve saben que esa hora ya no está disponible.
        respuestas.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        var perdedoras = respuestas.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();
        perdedoras.Count.ShouldBe(19);
        perdedoras.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict);

        foreach (var perdedora in perdedoras)
        {
            (await LeerProblemaAsync(perdedora)).Code.ShouldBe("reserva.franja_no_disponible");
        }

        await using var db = Api.NuevoContexto();
        (await db.Reservas.CountAsync(r => r.Cliente.Email.EndsWith($".{marca}@example.com"), Cancelacion)).ShouldBe(1);
    }

    [Fact]
    public async Task Dos_peticiones_simultaneas_por_la_misma_hora_caben_las_dos_si_hay_dos_mesas()
    {
        // Las dos calculan la misma mesa (la más ajustada) y una pierde esa carrera; al recalcular
        // debe encontrar la otra mesa en lugar de fallar. Se repite varias veces para que la
        // carrera ocurra de verdad en alguna ronda.
        for (var ronda = 0; ronda < 10; ronda++)
        {
            var fecha = Api.SiguienteFecha();
            var marca = Guid.NewGuid().ToString("N");
            var salida = new TaskCompletionSource();

            var peticiones = Enumerable.Range(0, 2).Select(async indice =>
            {
                await salida.Task;
                return await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, "21:00", email: $"{indice}.{marca}@example.com");
            }).ToArray();

            salida.SetResult();
            var respuestas = await Task.WhenAll(peticiones);

            respuestas.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created, $"ronda {ronda}");

            await using var db = Api.NuevoContexto();
            var mesas = (await db.Reservas.Where(r => r.Cliente.Email.EndsWith($".{marca}@example.com")).ToListAsync(Cancelacion))
                .Select(r => r.MesaIds.Single())
                .ToList();
            mesas.Distinct().Count().ShouldBe(2, $"ronda {ronda}: cada reserva debe tener su propia mesa");
        }
    }

    [Fact]
    public async Task Con_mas_peticiones_que_mesas_solo_ganan_tantas_como_mesas_hay()
    {
        var fecha = Api.SiguienteFecha();
        var marca = Guid.NewGuid().ToString("N");
        var salida = new TaskCompletionSource();

        var peticiones = Enumerable.Range(0, 12).Select(async indice =>
        {
            await salida.Task;
            return await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, "21:00", email: $"{indice}.{marca}@example.com");
        }).ToArray();

        salida.SetResult();
        var respuestas = await Task.WhenAll(peticiones);

        respuestas.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(2);
        respuestas.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(10);
    }
}
