using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Personal;

namespace Reservas.Aplicacion.Personal;

/// <summary>Cuánto duran las sesiones del personal.</summary>
public sealed record OpcionesSesion
{
    /// <summary>Un access token corto limita lo que puede hacer quien lo robe.</summary>
    public TimeSpan DuracionAcceso { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan DuracionRefresco { get; init; } = TimeSpan.FromDays(14);
}

/// <summary>Quién está haciendo la petición, según su sesión. El negocio sale de aquí, nunca de la URL.</summary>
public sealed record SesionUsuario(Guid UsuarioId, Guid NegocioId, Rol Rol);

/// <param name="Acceso">Token de corta duración con el que se llama a la API.</param>
/// <param name="TokenRefresco">El token de renovación en claro: solo existe aquí, en la base de datos queda su huella.</param>
/// <param name="RefrescoExpiraEn">Cuándo caduca el token de renovación.</param>
/// <param name="Usuario">Quién ha iniciado sesión.</param>
public sealed record SesionIniciada(TokenAcceso Acceso, string TokenRefresco, DateTimeOffset RefrescoExpiraEn, Usuario Usuario);
