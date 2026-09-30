using Reservas.Dominio.Comun;
using Reservas.Dominio.Correos;
using Reservas.Dominio.Gestion;

using Shouldly;

namespace Reservas.Dominio.Tests.Correos;

public class CorreoPendienteTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    private static CorreoPendiente Nuevo() =>
        CorreoPendiente.Crear(Guid.NewGuid(), Guid.NewGuid(), TipoCorreo.Solicitud, "ana@example.com", "Asunto", "Cuerpo", Ahora);

    [Fact]
    public void Un_correo_nuevo_esta_pendiente_y_toca_enviarlo_ya()
    {
        var correo = Nuevo();

        correo.EstaPendiente.ShouldBeTrue();
        correo.Intentos.ShouldBe(0);
        correo.ProximoIntentoEn.ShouldBe(Ahora);
        correo.EnviadoEn.ShouldBeNull();
    }

    [Theory]
    [InlineData("", "Asunto", "Cuerpo")]
    [InlineData("ana@example.com", " ", "Cuerpo")]
    [InlineData("ana@example.com", "Asunto", "")]
    public void Un_correo_sin_destinatario_asunto_o_cuerpo_no_se_crea(string destinatario, string asunto, string cuerpo)
    {
        Should.Throw<ArgumentException>(() =>
            CorreoPendiente.Crear(Guid.NewGuid(), null, TipoCorreo.Solicitud, destinatario, asunto, cuerpo, Ahora));
    }

    [Fact]
    public void Enviado_deja_de_estar_pendiente_y_borra_el_ultimo_error()
    {
        var correo = Nuevo();
        correo.RegistrarFallo("timeout", Ahora);

        correo.MarcarEnviado(Ahora.AddMinutes(2));

        correo.EstaPendiente.ShouldBeFalse();
        correo.EnviadoEn.ShouldBe(Ahora.AddMinutes(2));
        correo.UltimoError.ShouldBeNull();
    }

    [Fact]
    public void Cada_fallo_espera_mas_que_el_anterior()
    {
        var correo = Nuevo();
        var esperas = new List<TimeSpan>();

        for (var i = 0; i < CorreoPendiente.MaximoIntentos - 1; i++)
        {
            correo.RegistrarFallo("error", Ahora);
            esperas.Add(correo.ProximoIntentoEn - Ahora);
        }

        esperas.ShouldBe([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12)]);
        correo.EstaPendiente.ShouldBeTrue();
    }

    [Fact]
    public void Tras_el_ultimo_intento_fallido_el_correo_se_abandona()
    {
        var correo = Nuevo();

        for (var i = 0; i < CorreoPendiente.MaximoIntentos; i++)
        {
            correo.RegistrarFallo("error persistente", Ahora);
        }

        correo.Abandonado.ShouldBeTrue();
        correo.EstaPendiente.ShouldBeFalse();
        correo.Intentos.ShouldBe(CorreoPendiente.MaximoIntentos);
        correo.UltimoError.ShouldBe("error persistente");
    }

    [Fact]
    public void Un_error_muy_largo_se_recorta()
    {
        var correo = Nuevo();

        correo.RegistrarFallo(new string('x', 2000), Ahora);

        correo.UltimoError!.Length.ShouldBe(500);
    }

    [Fact]
    public void Reservar_un_correo_aplaza_su_siguiente_intento()
    {
        var correo = Nuevo();

        correo.Reservar(Ahora.AddMinutes(5));

        correo.ProximoIntentoEn.ShouldBe(Ahora.AddMinutes(5));
        correo.Intentos.ShouldBe(0, "reservarlo no cuenta como un intento");
    }
}

public class ReservaRecordatorioYSupresionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    private static Reserva Nueva(OrigenReserva origen = OrigenReserva.Personal) =>
        Reserva.Crear(
            Guid.NewGuid(),
            new IntervaloTiempo(Ahora.AddDays(3), Ahora.AddDays(3).AddMinutes(90)),
            2,
            DatosCliente.Crear("Ana Pérez", "ana@example.com", "600 000 000").Valor,
            [Guid.NewGuid()],
            origen,
            Ahora).Valor;

    [Fact]
    public void El_recordatorio_solo_se_programa_una_vez_y_solo_en_una_reserva_confirmada()
    {
        var pendiente = Nueva(OrigenReserva.Publica);
        pendiente.MarcarRecordatorioProgramado().Error.ShouldBe(ErroresReserva.TransicionInvalida);

        var confirmada = Nueva();
        confirmada.MarcarRecordatorioProgramado().EsExito.ShouldBeTrue();
        confirmada.RecordatorioProgramado.ShouldBeTrue();
        confirmada.MarcarRecordatorioProgramado().Error.ShouldBe(ErroresReserva.TransicionInvalida);
    }

    [Fact]
    public void Una_reserva_activa_no_se_puede_anonimizar()
    {
        foreach (var reserva in new[] { Nueva(OrigenReserva.Publica), Nueva() })
        {
            reserva.Anonimizar().Error.ShouldBe(ErroresReserva.ReservaActiva);
            reserva.Cliente.EstaAnonimizado.ShouldBeFalse();
        }

        var sentada = Nueva();
        sentada.Sentar();
        sentada.Anonimizar().EsFallo.ShouldBeTrue();
    }

    [Fact]
    public void Una_reserva_cancelada_o_terminada_se_anonimiza_y_conserva_todo_lo_demas()
    {
        var reserva = Nueva();
        var inicio = reserva.Intervalo;
        reserva.Cancelar();

        reserva.Anonimizar().EsExito.ShouldBeTrue();

        reserva.Cliente.EstaAnonimizado.ShouldBeTrue();
        reserva.Cliente.Nombre.ShouldNotContain("Ana");
        reserva.Cliente.Email.ShouldNotContain("ana");
        reserva.Cliente.Telefono.ShouldBeNull();
        reserva.Intervalo.ShouldBe(inicio);
        reserva.Comensales.ShouldBe(2);
        reserva.Estado.ShouldBe(EstadoReserva.Cancelada);
    }

    [Fact]
    public void Dos_reservas_anonimizadas_no_comparten_el_mismo_objeto_de_cliente()
    {
        var a = Nueva();
        var b = Nueva();
        a.Cancelar();
        b.Cancelar();

        a.Anonimizar();
        b.Anonimizar();

        // EF Core no admite que dos entidades propietarias compartan una misma instancia de un tipo «owned».
        ReferenceEquals(a.Cliente, b.Cliente).ShouldBeFalse();
    }
}
