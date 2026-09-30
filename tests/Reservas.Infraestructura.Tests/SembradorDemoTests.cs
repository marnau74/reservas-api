using Microsoft.EntityFrameworkCore;

using Reservas.Infraestructura.Persistencia;
using Reservas.Tests.Comunes;

using Shouldly;

namespace Reservas.Infraestructura.Tests;

public class SembradorDemoTests(ServidorPostgres servidor)
{
    private async Task<ReservasDbContext> BaseDeDatosVaciaAsync() =>
        ServidorPostgres.CrearContexto(await servidor.CrearBaseDeDatosAsync());

    [Fact]
    public async Task Crea_un_negocio_completo_con_mesas_y_horarios_de_toda_la_semana()
    {
        await using var db = await BaseDeDatosVaciaAsync();

        var demo = await SembradorDemo.CrearNegocioAsync(db);

        demo.Negocio.Slug.ShouldBe("bar-la-plaza");
        demo.Mesas.Count.ShouldBe(2);
        (await db.Horarios.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(14); // 7 días x comida y cena
        (await db.Salas.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task Sembrar_si_hace_falta_se_puede_repetir_en_cada_arranque_sin_duplicar_nada()
    {
        await using var db = await BaseDeDatosVaciaAsync();

        await SembradorDemo.SembrarSiHaceFaltaAsync(db);
        await SembradorDemo.SembrarSiHaceFaltaAsync(db);
        await SembradorDemo.SembrarSiHaceFaltaAsync(db);

        (await db.Negocios.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await db.Mesas.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task Se_pueden_crear_varios_negocios_en_el_mismo_contexto()
    {
        // Regresión: las políticas por defecto eran un único objeto compartido y EF Core no admite
        // que dos negocios tengan el mismo objeto de tipo «propiedad».
        await using var db = await BaseDeDatosVaciaAsync();

        await SembradorDemo.CrearNegocioAsync(db, "uno");
        await SembradorDemo.CrearNegocioAsync(db, "dos");

        (await db.Negocios.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }
}
