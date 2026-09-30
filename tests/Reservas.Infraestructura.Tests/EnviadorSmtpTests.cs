using System.Net.Http.Json;
using System.Text.Json;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Infraestructura.Correo;

using Shouldly;

namespace Reservas.Infraestructura.Tests;

/// <summary>
/// El envío por SMTP de verdad, contra Mailpit (el mismo servidor de pruebas que usa el entorno
/// local): se envía un correo y se lee de su bandeja, como haría una persona.
/// </summary>
public sealed class EnviadorSmtpTests : IAsyncLifetime
{
    private const int PuertoSmtp = 1025;
    private const int PuertoWeb = 8025;

    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:latest")
        .WithPortBinding(PuertoSmtp, assignRandomHostPort: true)
        .WithPortBinding(PuertoWeb, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(PuertoWeb).ForPath("/api/v1/info")))
        .Build();

    private readonly HttpClient _http = new();

    public async ValueTask InitializeAsync()
    {
        await _mailpit.StartAsync();
        _http.BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(PuertoWeb)}");
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _mailpit.DisposeAsync();
    }

    private EnviadorSmtp Enviador() => new(new OpcionesSmtp
    {
        Servidor = _mailpit.Hostname,
        Puerto = _mailpit.GetMappedPublicPort(PuertoSmtp),
        Remitente = "reservas@bar.example",
        NombreRemitente = "Bar La Plaza",
    });

    [Fact]
    public async Task Un_correo_enviado_llega_a_la_bandeja_con_asunto_texto_y_acentos()
    {
        var ct = TestContext.Current.CancellationToken;
        var mensaje = new MensajeCorreo(
            "ana@example.com",
            "Confirma tu reserva en Bar La Plaza",
            "Hola Ana Pérez:\n\nTu mesa del sábado a las 21:00 para 2 personas.\nhttps://bar.example/reservas/abc\n");

        await Enviador().EnviarAsync(mensaje, ct);

        var lista = await _http.GetFromJsonAsync<JsonElement>("/api/v1/messages", ct);
        lista.GetProperty("total").GetInt32().ShouldBe(1);

        var resumen = lista.GetProperty("messages")[0];
        resumen.GetProperty("Subject").GetString().ShouldBe("Confirma tu reserva en Bar La Plaza");
        resumen.GetProperty("To")[0].GetProperty("Address").GetString().ShouldBe("ana@example.com");
        resumen.GetProperty("From").GetProperty("Address").GetString().ShouldBe("reservas@bar.example");
        resumen.GetProperty("From").GetProperty("Name").GetString().ShouldBe("Bar La Plaza");

        var completo = await _http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{resumen.GetProperty("ID").GetString()}", ct);
        var texto = completo.GetProperty("Text").GetString()!.ReplaceLineEndings("\n");
        texto.ShouldContain("Hola Ana Pérez:");
        texto.ShouldContain("sábado a las 21:00");
        texto.ShouldContain("https://bar.example/reservas/abc");
    }

    [Fact]
    public async Task Sin_servidor_al_otro_lado_el_envio_falla_para_que_el_proceso_lo_reintente()
    {
        var enviador = new EnviadorSmtp(new OpcionesSmtp { Servidor = "127.0.0.1", Puerto = 1 });

        await Should.ThrowAsync<Exception>(() =>
            enviador.EnviarAsync(new MensajeCorreo("ana@example.com", "Asunto", "Cuerpo"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Una_direccion_de_destino_invalida_falla_en_lugar_de_perderse()
    {
        await Should.ThrowAsync<Exception>(() =>
            Enviador().EnviarAsync(new MensajeCorreo("esto no es un correo", "Asunto", "Cuerpo"), TestContext.Current.CancellationToken));
    }
}
