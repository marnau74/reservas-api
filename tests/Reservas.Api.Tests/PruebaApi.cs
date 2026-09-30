using System.Net.Http.Json;
using System.Text.Json;

using Reservas.Api.Contratos;

namespace Reservas.Api.Tests;

/// <summary>Un error <c>application/problem+json</c> tal como lo recibe un cliente de la API.</summary>
public sealed record Problema(string? Type, string? Title, int? Status, string? Detail, string? Code, Dictionary<string, string[]>? Errors);

/// <summary>Herramientas para escribir tests que usan la API como lo haría un cliente.</summary>
public abstract class PruebaApi(ApiConBaseDeDatos api)
{
    protected ApiConBaseDeDatos Api { get; } = api;

    protected static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    protected static string NuevoEmail() => $"{Guid.NewGuid():N}@example.com";

    protected Task<HttpResponseMessage> ReservarAsync(
        string slug,
        DateOnly fecha,
        string hora = "21:00",
        int comensales = 2,
        string? clave = null,
        string? email = null,
        HttpClient? cliente = null)
    {
        var solicitud = new SolicitudReservaDto(
            Formatos.DeFecha(fecha), hora, comensales, new ClienteDto("Ana Pérez", email ?? NuevoEmail(), "600 000 000"));

        return EnviarReservaAsync(slug, JsonContent.Create(solicitud), clave ?? Guid.NewGuid().ToString("N"), cliente);
    }

    protected async Task<HttpResponseMessage> EnviarReservaAsync(string slug, HttpContent cuerpo, string? clave, HttpClient? cliente = null)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/negocios/{slug}/reservas") { Content = cuerpo };

        if (clave is not null)
        {
            peticion.Headers.Add("Idempotency-Key", clave);
        }

        var http = cliente ?? Api.CrearCliente();
        return await http.SendAsync(peticion, Cancelacion);
    }

    protected async Task<HttpResponseMessage> DisponibilidadAsync(string slug, DateOnly fecha, int comensales = 2) =>
        await Api.CrearCliente().GetAsync(
            new Uri($"/api/v1/negocios/{slug}/disponibilidad?fecha={Formatos.DeFecha(fecha)}&comensales={comensales}", UriKind.Relative),
            Cancelacion);

    protected async Task<string[]> HorasDisponiblesAsync(string slug, DateOnly fecha, int comensales = 2)
    {
        using var respuesta = await DisponibilidadAsync(slug, fecha, comensales);
        var disponibilidad = await respuesta.Content.ReadFromJsonAsync<DisponibilidadRespuesta>(Cancelacion);
        return [.. disponibilidad!.Franjas.Select(f => f.Hora)];
    }

    protected async Task<HttpResponseMessage> GestionarAsync(string codigo, string? accion = null, HttpMethod? metodo = null)
    {
        using var peticion = new HttpRequestMessage(
            metodo ?? (accion is null ? HttpMethod.Get : HttpMethod.Post),
            $"/api/v1/reservas/gestion/{codigo}{(accion is null ? string.Empty : "/" + accion)}");

        return await Api.CrearCliente().SendAsync(peticion, Cancelacion);
    }

    protected static async Task<ReservaRespuesta> LeerReservaAsync(HttpResponseMessage respuesta) =>
        (await respuesta.Content.ReadFromJsonAsync<ReservaRespuesta>(Cancelacion))!;

    protected static async Task<Problema> LeerProblemaAsync(HttpResponseMessage respuesta) =>
        (await respuesta.Content.ReadFromJsonAsync<Problema>(Cancelacion))!;

    protected static async Task<JsonElement> LeerJsonAsync(HttpResponseMessage respuesta) =>
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(Cancelacion));
}
