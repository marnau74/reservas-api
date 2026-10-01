using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

using Reservas.Api.Seguridad;

using Shouldly;

namespace Reservas.Api.Tests;

/// <summary>De dónde sale el negocio que filtra todas las consultas: sin filtro en la parte pública, y nunca sin filtro en la privada.</summary>
public class ContextoNegocioTests
{
    private static Guid? NegocioDe(string ruta, ClaimsPrincipal? usuario = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = ruta;

        if (usuario is not null)
        {
            http.User = usuario;
        }

        return new ContextoNegocioHttp(new HttpContextAccessor { HttpContext = http }).NegocioId;
    }

    private static ClaimsPrincipal Sesion(Guid negocio) => new(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim(NombresClaim.Negocio, negocio.ToString()),
            new Claim("role", "encargado"),
        ],
        authenticationType: "prueba"));

    [Fact]
    public void En_la_parte_publica_no_hay_negocio_de_sesion_aunque_venga_un_token()
    {
        NegocioDe("/api/v1/negocios/bar-la-plaza", Sesion(Guid.NewGuid())).ShouldBeNull();
    }

    [Fact]
    public void En_la_parte_privada_el_negocio_es_el_de_la_sesion()
    {
        var negocio = Guid.NewGuid();

        NegocioDe("/api/v1/gestion/agenda", Sesion(negocio)).ShouldBe(negocio);
    }

    [Fact]
    public void En_la_parte_privada_sin_sesion_valida_no_se_ve_ningun_negocio_en_vez_de_todos()
    {
        // Si una ruta privada llegara a ejecutarse sin sesión (un error de configuración), el filtro debe quedarse
        // cerrado: un negocio que no existe, no «sin filtro».
        NegocioDe("/api/v1/gestion/agenda").ShouldBe(ContextoNegocioHttp.SinNegocio);
        NegocioDe("/api/v1/gestion/agenda", new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "encargado")], "prueba")))
            .ShouldBe(ContextoNegocioHttp.SinNegocio, "una sesión sin negocio tampoco abre el filtro");
    }
}
