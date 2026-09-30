using System.Net;

using Reservas.Api.Contratos;
using Reservas.Dominio.Gestion;

using Shouldly;

namespace Reservas.Api.Tests;

/// <summary>
/// El día de la reserva: hay que avanzar el reloj hasta esa hora, y eso no le sienta bien a los
/// demás tests (que comparten fechas dentro de un plazo). Por eso tiene su propia API.
/// </summary>
public class NoPresentadaTests(ApiConPersonal api) : PruebaPersonal(api), IClassFixture<ApiConPersonal>
{
    [Fact]
    public async Task Pasado_el_margen_de_cortesia_el_personal_puede_dar_al_grupo_por_no_presentado()
    {
        var fecha = Api.SiguienteFecha();
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalB);
        var reserva = await ApuntarReservaAsync(personal, fecha, "21:00");
        var inicio = reserva.Inicio;
        (await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha)).ShouldNotContain("21:00");

        // A las 21:14 todavía es pronto.
        Api.Reloj.SetUtcNow(inicio + Reserva.MargenNoPresentada - TimeSpan.FromMinutes(1));
        var antes = await ClienteDeAsync(ApiConPersonal.PersonalB);
        using var pronto = await PostAsync(antes, $"/api/v1/gestion/reservas/{reserva.Id}/no-presentada");
        pronto.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeerProblemaAsync(pronto)).Code.ShouldBe("reserva.aun_no_es_hora");

        // A las 21:15 ya se puede.
        Api.Reloj.SetUtcNow(inicio + Reserva.MargenNoPresentada);
        var despues = await ClienteDeAsync(ApiConPersonal.PersonalB);
        using var respuesta = await PostAsync(despues, $"/api/v1/gestion/reservas/{reserva.Id}/no-presentada");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LeerAsync<ReservaGestionRespuesta>(respuesta)).Estado.ShouldBe("noPresentada");
    }
}
