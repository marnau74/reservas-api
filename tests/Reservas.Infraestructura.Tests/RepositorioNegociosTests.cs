using Reservas.Dominio.Locales;
using Reservas.Infraestructura.Persistencia;
using Reservas.Tests.Comunes;

using Shouldly;

namespace Reservas.Infraestructura.Tests;

public class RepositorioNegociosTests(ServidorPostgres servidor) : BaseDeDatosTest(servidor)
{
    [Fact]
    public async Task Un_negocio_se_encuentra_por_su_slug_con_zona_horaria_y_politicas()
    {
        await using var db = NuevoContexto();

        var negocio = await new RepositorioNegocios(db).ObtenerPorSlugAsync("bar-la-plaza", TestContext.Current.CancellationToken);

        negocio.ShouldNotBeNull();
        negocio.Id.ShouldBe(Negocio.Id);
        negocio.Nombre.ShouldBe("Bar La Plaza (demo)");
        negocio.Zona.Id.ShouldBe("Europe/Madrid");
        negocio.Politicas.AntelacionMinima.ShouldBe(TimeSpan.FromHours(1));
        negocio.Politicas.DiasMaximosAntelacion.ShouldBe(60);
        negocio.Politicas.MaxComensalesOnline.ShouldBe(10);
        negocio.Politicas.DuracionEstandar.ShouldBe(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public async Task Un_negocio_tambien_se_encuentra_por_su_identificador()
    {
        await using var db = NuevoContexto();

        var negocio = await new RepositorioNegocios(db).ObtenerAsync(Negocio.Id, TestContext.Current.CancellationToken);

        negocio.ShouldNotBeNull();
        negocio.Slug.ShouldBe("bar-la-plaza");
    }

    [Fact]
    public async Task Un_negocio_inexistente_devuelve_nulo()
    {
        await using var db = NuevoContexto();
        var repositorio = new RepositorioNegocios(db);

        (await repositorio.ObtenerPorSlugAsync("no-existe", TestContext.Current.CancellationToken)).ShouldBeNull();
        (await repositorio.ObtenerAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task La_configuracion_del_local_trae_solo_las_mesas_de_ese_negocio()
    {
        await using (var db = NuevoContexto())
        {
            await SembradorDemo.CrearNegocioAsync(db, "otro-bar", [(1, 8, true), (1, 8, true), (1, 8, true), (1, 8, true)]);
        }

        await using var consulta = NuevoContexto();
        var repositorio = new RepositorioNegocios(consulta);

        var mio = await repositorio.ObtenerConfiguracionAsync(Negocio.Id, TestContext.Current.CancellationToken);
        var otro = await repositorio.ObtenerConfiguracionAsync(
            (await repositorio.ObtenerPorSlugAsync("otro-bar", TestContext.Current.CancellationToken))!.Id,
            TestContext.Current.CancellationToken);

        mio.Mesas.Select(m => m.Id).ShouldBe([Mesa1.Id, Mesa2.Id, Mesa3.Id], ignoreOrder: true);
        otro.Mesas.Count.ShouldBe(4);
        otro.Mesas.ShouldAllBe(m => m.EsCombinable);
    }

    [Fact]
    public async Task La_configuracion_recupera_horarios_y_cierres_con_sus_horas_y_turnos()
    {
        await using (var db = NuevoContexto())
        {
            var horario = Horario.Crear(DayOfWeek.Saturday, Turno.Cena, new TimeOnly(20, 30), new TimeOnly(22, 45), 15).Valor;
            var cierreDia = new Cierre(new DateOnly(2026, 12, 25), null, "Navidad");
            var cierreTurno = new Cierre(new DateOnly(2026, 12, 24), Turno.Cena, "Nochebuena");
            db.Horarios.Add(horario);
            db.Cierres.AddRange(cierreDia, cierreTurno);
            AsignarNegocio(db, Negocio.Id, horario, cierreDia, cierreTurno);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var consulta = NuevoContexto();
        var configuracion = await new RepositorioNegocios(consulta).ObtenerConfiguracionAsync(Negocio.Id, TestContext.Current.CancellationToken);

        var extra = configuracion.Horarios.Single(h => h.IntervaloMinutos == 15);
        extra.Dia.ShouldBe(DayOfWeek.Saturday);
        extra.Turno.ShouldBe(Turno.Cena);
        extra.Inicio.ShouldBe(new TimeOnly(20, 30));
        extra.Fin.ShouldBe(new TimeOnly(22, 45));

        configuracion.Cierres.Count.ShouldBe(2);
        configuracion.Cierres.Single(c => c.Turno is null).Fecha.ShouldBe(new DateOnly(2026, 12, 25));
        configuracion.Cierres.Single(c => c.Turno == Turno.Cena).Motivo.ShouldBe("Nochebuena");
    }

    [Fact]
    public async Task Una_reserva_se_encuentra_por_el_codigo_de_su_enlace()
    {
        var reserva = NuevaReserva();
        await using (var db = NuevoContexto())
        {
            (await new RepositorioReservas(db).AgregarAsync(reserva, TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();
        }

        await using var consulta = NuevoContexto();
        var repositorio = new RepositorioReservas(consulta);

        var encontrada = await repositorio.ObtenerPorCodigoAsync(reserva.CodigoGestion, TestContext.Current.CancellationToken);

        encontrada.ShouldNotBeNull();
        encontrada.Id.ShouldBe(reserva.Id);
        (await repositorio.ObtenerPorCodigoAsync("codigo-que-no-existe", TestContext.Current.CancellationToken)).ShouldBeNull();
    }
}
