using Microsoft.EntityFrameworkCore;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Locales;
using Reservas.Dominio.Personal;
using Reservas.Infraestructura.Persistencia;
using Reservas.Tests.Comunes;

using Shouldly;

namespace Reservas.Infraestructura.Tests;

/// <summary>El negocio de la petición, fijado por el test.</summary>
internal sealed class ContextoNegocioFijo(Guid? negocioId) : IContextoNegocio
{
    public Guid? NegocioId { get; } = negocioId;
}

/// <summary>El filtro global por negocio del contexto de EF Core, comprobado contra PostgreSQL de verdad.</summary>
public class FiltroPorNegocioTests(ServidorPostgres servidor) : BaseDeDatosTest(servidor)
{
    private NegocioDemo _otro = null!;

    private ReservasDbContext ComoNegocio(Guid? negocioId) => ServidorPostgres.CrearContexto(CadenaConexion, new ContextoNegocioFijo(negocioId));

    private async Task PrepararOtroNegocioAsync()
    {
        await using var db = NuevoContexto();
        _otro = await SembradorDemo.CrearNegocioAsync(db, "otro-bar", [(1, 2, false)]);
        await SembradorDemo.CrearUsuarioAsync(db, Negocio.Id, "ana@example.com", "Ana", Rol.Personal, "Contrasena-1234");
        await SembradorDemo.CrearUsuarioAsync(db, _otro.Negocio.Id, "berta@example.com", "Berta", Rol.Personal, "Contrasena-1234");

        await using var reservas = NuevoContexto();
        var reservaA = NuevaReserva(mesas: Mesa1);
        var reservaB = Dominio.Gestion.Reserva.Crear(_otro.Negocio.Id, Cena, 2, Cliente(2), [_otro.Mesas[0].Id], Dominio.Gestion.OrigenReserva.Personal, Ahora).Valor;
        (await new RepositorioReservas(reservas).AgregarAsync(reservaA, [], TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();
        (await new RepositorioReservas(reservas).AgregarAsync(reservaB, [], TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();
    }

    [Fact]
    public async Task Con_un_negocio_en_el_contexto_todas_las_consultas_ven_solo_sus_datos()
    {
        await PrepararOtroNegocioAsync();
        var ct = TestContext.Current.CancellationToken;

        await using var db = ComoNegocio(_otro.Negocio.Id);

        (await db.Mesas.CountAsync(ct)).ShouldBe(1);
        (await db.Salas.CountAsync(ct)).ShouldBe(1);
        (await db.Horarios.CountAsync(ct)).ShouldBe(14);
        (await db.Reservas.CountAsync(ct)).ShouldBe(1);
        (await db.OcupacionesMesa.CountAsync(ct)).ShouldBe(1);
        (await db.Usuarios.SingleAsync(ct)).Email.ShouldBe("berta@example.com");

        // Los negocios son públicos: se siguen encontrando todos por su slug.
        (await db.Negocios.CountAsync(ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Sin_negocio_en_el_contexto_no_se_filtra_nada_como_en_las_peticiones_publicas()
    {
        await PrepararOtroNegocioAsync();
        var ct = TestContext.Current.CancellationToken;

        await using var db = ComoNegocio(null);

        (await db.Mesas.CountAsync(ct)).ShouldBe(4);
        (await db.Reservas.CountAsync(ct)).ShouldBe(2);
        (await db.Usuarios.CountAsync(ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Buscar_por_identificador_algo_de_otro_negocio_no_lo_encuentra()
    {
        await PrepararOtroNegocioAsync();
        var ct = TestContext.Current.CancellationToken;

        Guid idDeAna;
        await using (var sinFiltro = NuevoContexto())
        {
            idDeAna = (await sinFiltro.Usuarios.SingleAsync(u => u.Email == "ana@example.com", ct)).Id;
        }

        await using var db = ComoNegocio(_otro.Negocio.Id);

        (await db.Mesas.FirstOrDefaultAsync(m => m.Id == Mesa1.Id, ct)).ShouldBeNull();
        (await db.Salas.AnyAsync(s => s.Id == Sala.Id, ct)).ShouldBeFalse();
        (await new RepositorioUsuarios(db).ObtenerAsync(idDeAna, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Ni_siquiera_un_borrado_masivo_alcanza_datos_de_otro_negocio()
    {
        await PrepararOtroNegocioAsync();
        var ct = TestContext.Current.CancellationToken;

        await using (var db = ComoNegocio(_otro.Negocio.Id))
        {
            (await db.Mesas.Where(m => m.Id == Mesa2.Id).ExecuteDeleteAsync(ct)).ShouldBe(0);
            (await db.Salas.Where(s => s.Id == Sala.Id).ExecuteDeleteAsync(ct)).ShouldBe(0);
        }

        await using var comprobacion = NuevoContexto();
        (await comprobacion.Mesas.AnyAsync(m => m.Id == Mesa2.Id, ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task El_repositorio_del_local_trata_los_identificadores_de_otro_negocio_como_inexistentes()
    {
        await PrepararOtroNegocioAsync();
        var ct = TestContext.Current.CancellationToken;

        await using var db = NuevoContexto();
        var repositorio = new RepositorioLocal(db);

        // Sin contexto (no hay filtro global): solo el parámetro explícito de negocio protege.
        (await repositorio.EliminarMesaAsync(_otro.Negocio.Id, Mesa2.Id, ct)).Error.Codigo.ShouldBe("mesa.no_encontrada");
        (await repositorio.EliminarSalaAsync(_otro.Negocio.Id, Sala.Id, ct)).Error.Codigo.ShouldBe("sala.no_encontrada");
        (await repositorio.EliminarHorarioAsync(_otro.Negocio.Id, Guid.NewGuid(), ct)).Error.Codigo.ShouldBe("horario.no_encontrado");
        (await repositorio.EliminarCierreAsync(_otro.Negocio.Id, Guid.NewGuid(), ct)).Error.Codigo.ShouldBe("cierre.no_encontrado");
        (await repositorio.ExisteSalaAsync(_otro.Negocio.Id, Sala.Id, ct)).ShouldBeFalse();
        (await repositorio.ListarMesasAsync(_otro.Negocio.Id, ct)).Select(m => m.Id).ShouldBe([_otro.Mesas[0].Id]);
    }

    [Fact]
    public async Task La_busqueda_por_correo_del_inicio_de_sesion_ignora_el_filtro()
    {
        await PrepararOtroNegocioAsync();

        await using var db = ComoNegocio(_otro.Negocio.Id);

        (await new RepositorioUsuarios(db).ObtenerPorEmailAsync("ana@example.com", TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Las_reservas_de_otro_negocio_no_salen_en_la_agenda()
    {
        await PrepararOtroNegocioAsync();

        await using var db = ComoNegocio(Negocio.Id);
        var agenda = await new RepositorioReservas(db).ListarPorInicioAsync(_otro.Negocio.Id, Cena, TestContext.Current.CancellationToken);

        // Aunque se pida el negocio ajeno por parámetro, el filtro del contexto manda.
        agenda.ShouldBeEmpty();
    }
}

public class RepositoriosPersonalTests(ServidorPostgres servidor) : BaseDeDatosTest(servidor)
{
    private async Task<Usuario> NuevoUsuarioAsync(string email = "ana@example.com")
    {
        await using var db = NuevoContexto();
        return await SembradorDemo.CrearUsuarioAsync(db, Negocio.Id, email, "Ana", Rol.Encargado, "Contrasena-1234", Ahora);
    }

    [Fact]
    public async Task Un_usuario_se_guarda_y_se_recupera_con_todos_sus_datos()
    {
        var creado = await NuevoUsuarioAsync();

        await using var db = NuevoContexto();
        var leido = await new RepositorioUsuarios(db).ObtenerPorEmailAsync("ana@example.com", TestContext.Current.CancellationToken);

        leido.ShouldNotBeNull();
        leido.Id.ShouldBe(creado.Id);
        leido.NegocioId.ShouldBe(Negocio.Id);
        leido.Nombre.ShouldBe("Ana");
        leido.Rol.ShouldBe(Rol.Encargado);
        leido.Activo.ShouldBeTrue();
        leido.CreadoEn.ShouldBe(Ahora);
        leido.HashContrasena.ShouldBe(creado.HashContrasena);
    }

    [Fact]
    public async Task El_estado_de_bloqueo_se_guarda()
    {
        await NuevoUsuarioAsync();

        await using (var db = NuevoContexto())
        {
            var repositorio = new RepositorioUsuarios(db);
            var usuario = (await repositorio.ObtenerPorEmailAsync("ana@example.com", TestContext.Current.CancellationToken))!;
            for (var i = 0; i < Usuario.MaximoIntentosFallidos; i++)
            {
                usuario.RegistrarIntento(Ahora);
            }

            await repositorio.GuardarAsync(TestContext.Current.CancellationToken);
        }

        await using var lectura = NuevoContexto();
        var guardado = (await new RepositorioUsuarios(lectura).ObtenerPorEmailAsync("ana@example.com", TestContext.Current.CancellationToken))!;
        guardado.EstaBloqueado(Ahora).ShouldBeTrue();
        guardado.BloqueadoHasta.ShouldBe(Ahora + Usuario.DuracionBloqueo);
    }

    [Fact]
    public async Task Dos_altas_simultaneas_con_el_mismo_correo_solo_crean_un_usuario()
    {
        var salida = new TaskCompletionSource();

        var altas = Enumerable.Range(0, 10).Select(async i =>
        {
            await using var db = NuevoContexto();
            var usuario = Usuario.Crear(Negocio.Id, "mismo@example.com", $"Persona {i}", Rol.Personal, "hash", Ahora).Valor;
            await salida.Task;
            return await new RepositorioUsuarios(db).AgregarAsync(usuario, TestContext.Current.CancellationToken);
        }).ToArray();

        salida.SetResult();
        var resultados = await Task.WhenAll(altas);

        resultados.Count(r => r.EsExito).ShouldBe(1);
        resultados.Where(r => r.EsFallo).ShouldAllBe(r => r.Error.Codigo == "usuario.email_en_uso");
    }

    [Fact]
    public async Task Un_token_de_renovacion_solo_se_puede_consumir_una_vez_aunque_lleguen_muchas_peticiones_a_la_vez()
    {
        var usuario = await NuevoUsuarioAsync();
        var token = TokenRefresco.Crear(usuario.Id, new string('a', 64), Ahora, TimeSpan.FromDays(14));

        await using (var db = NuevoContexto())
        {
            var repositorio = new RepositorioTokensRefresco(db);
            await repositorio.AgregarAsync(token, TestContext.Current.CancellationToken);
            await repositorio.GuardarAsync(TestContext.Current.CancellationToken);
        }

        var salida = new TaskCompletionSource();
        var intentos = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var db = NuevoContexto();
            await salida.Task;
            return await new RepositorioTokensRefresco(db).ConsumirAsync(token.Id, Ahora, TestContext.Current.CancellationToken);
        }).ToArray();

        salida.SetResult();

        (await Task.WhenAll(intentos)).Count(consumido => consumido).ShouldBe(1);

        await using var lectura = NuevoContexto();
        (await new RepositorioTokensRefresco(lectura).ObtenerPorHashAsync(new string('a', 64), TestContext.Current.CancellationToken))!
            .RevocadoEn.ShouldBe(Ahora);
    }

    [Fact]
    public async Task Revocar_todos_los_tokens_de_un_usuario_no_toca_los_de_otros()
    {
        var ana = await NuevoUsuarioAsync();
        var berta = await NuevoUsuarioAsync("berta@example.com");
        var tokenAna = TokenRefresco.Crear(ana.Id, new string('a', 64), Ahora, TimeSpan.FromDays(14));
        var tokenBerta = TokenRefresco.Crear(berta.Id, new string('b', 64), Ahora, TimeSpan.FromDays(14));

        await using (var db = NuevoContexto())
        {
            var repositorio = new RepositorioTokensRefresco(db);
            await repositorio.AgregarAsync(tokenAna, TestContext.Current.CancellationToken);
            await repositorio.AgregarAsync(tokenBerta, TestContext.Current.CancellationToken);
            await repositorio.GuardarAsync(TestContext.Current.CancellationToken);
            await repositorio.RevocarTodosAsync(ana.Id, Ahora, TestContext.Current.CancellationToken);
        }

        await using var lectura = NuevoContexto();
        var repo = new RepositorioTokensRefresco(lectura);
        (await repo.ObtenerPorHashAsync(new string('a', 64), TestContext.Current.CancellationToken))!.EstaRevocado.ShouldBeTrue();
        (await repo.ObtenerPorHashAsync(new string('b', 64), TestContext.Current.CancellationToken))!.EstaRevocado.ShouldBeFalse();
    }

    [Fact]
    public async Task No_se_puede_borrar_una_sala_con_mesas_ni_una_mesa_con_reservas()
    {
        await using (var db = NuevoContexto())
        {
            (await new RepositorioReservas(db).AgregarAsync(NuevaReserva(mesas: Mesa1), [], TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();
        }

        await using var consulta = NuevoContexto();
        var repositorio = new RepositorioLocal(consulta);

        (await repositorio.EliminarSalaAsync(Negocio.Id, Sala.Id, TestContext.Current.CancellationToken)).Error.Codigo.ShouldBe("sala.con_mesas");
        (await repositorio.EliminarMesaAsync(Negocio.Id, Mesa1.Id, TestContext.Current.CancellationToken)).Error.Codigo.ShouldBe("mesa.con_reservas");
        (await repositorio.EliminarMesaAsync(Negocio.Id, Mesa2.Id, TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();
    }

    [Fact]
    public async Task Los_horarios_y_cierres_se_guardan_con_su_identificador_y_se_borran_por_el()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NuevoContexto();
        var repositorio = new RepositorioLocal(db);

        var idHorario = await repositorio.AgregarHorarioAsync(Negocio.Id, Horario.Crear(DayOfWeek.Sunday, Turno.Comida, new TimeOnly(13, 0), new TimeOnly(15, 0), 30).Valor, ct);
        var idCierre = await repositorio.AgregarCierreAsync(Negocio.Id, new Cierre(new DateOnly(2026, 12, 25), null, "Navidad"), ct);

        (await repositorio.ListarHorariosAsync(Negocio.Id, ct)).Single().Id.ShouldBe(idHorario);
        (await repositorio.ListarCierresAsync(Negocio.Id, ct)).Single().Id.ShouldBe(idCierre);

        (await repositorio.EliminarHorarioAsync(Negocio.Id, idHorario, ct)).EsExito.ShouldBeTrue();
        (await repositorio.EliminarCierreAsync(Negocio.Id, idCierre, ct)).EsExito.ShouldBeTrue();
        (await repositorio.ListarHorariosAsync(Negocio.Id, ct)).ShouldBeEmpty();
        (await repositorio.ListarCierresAsync(Negocio.Id, ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task El_sembrador_crea_las_cuentas_de_demostracion_una_sola_vez()
    {
        await using (var db = NuevoContexto())
        {
            await SembradorDemo.SembrarSiHaceFaltaAsync(db);
        }

        await using (var db = NuevoContexto())
        {
            await SembradorDemo.SembrarSiHaceFaltaAsync(db);
        }

        await using var lectura = NuevoContexto();
        var usuarios = await lectura.Usuarios.OrderBy(u => u.Email).ToListAsync(TestContext.Current.CancellationToken);
        usuarios.Select(u => u.Email).ShouldBe(["encargado@demo.example", "personal@demo.example", "propietario@demo.example"]);
        usuarios.Select(u => u.Rol).ShouldBe([Rol.Encargado, Rol.Personal, Rol.Propietario], ignoreOrder: true);
    }
}

public class HasherContrasenasTests
{
    private readonly HasherContrasenas _hasher = new();

    [Fact]
    public void Una_contrasena_verifica_con_su_huella_y_no_con_otra()
    {
        var hash = _hasher.Hashear("Contrasena-1234");

        _hasher.Verificar(hash, "Contrasena-1234").ShouldBeTrue();
        _hasher.Verificar(hash, "contrasena-1234").ShouldBeFalse();
        _hasher.Verificar(hash, string.Empty).ShouldBeFalse();
    }

    [Fact]
    public void Cada_huella_lleva_su_propia_sal_y_no_contiene_la_contrasena()
    {
        var uno = _hasher.Hashear("Contrasena-1234");
        var otro = _hasher.Hashear("Contrasena-1234");

        uno.ShouldNotBe(otro);
        uno.ShouldNotContain("Contrasena-1234");
    }

    [Fact]
    public void La_huella_falsa_es_estable_valida_y_no_se_corresponde_con_ninguna_contrasena_habitual()
    {
        _hasher.HashFalso.ShouldBe(_hasher.HashFalso);
        _hasher.Verificar(_hasher.HashFalso, "Contrasena-1234").ShouldBeFalse();
    }
}
