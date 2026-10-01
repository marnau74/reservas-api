using System.Security.Claims;
using System.Text;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Aplicacion.Personal;
using Reservas.Dominio.Personal;

namespace Reservas.Api.Seguridad;

/// <summary>Ajustes de los tokens de acceso, en la sección <c>Jwt</c> de la configuración.</summary>
public sealed class OpcionesJwt
{
    public const string Seccion = "Jwt";

    /// <summary>Clave secreta con la que se firman los tokens: al menos 32 caracteres. Obligatoria fuera de desarrollo.</summary>
    public string? Clave { get; set; }

    public string Emisor { get; set; } = "reservas-api";

    public string Audiencia { get; set; } = "reservas-personal";

    public int MinutosAcceso { get; set; } = 15;

    public int DiasRefresco { get; set; } = 14;
}

public static class Politicas
{
    public const string Personal = "personal";
    public const string Encargado = "encargado";
    public const string Propietario = "propietario";

    /// <summary>Prefijo de las rutas de la parte privada: solo en ellas la sesión decide el negocio.</summary>
    public const string RutaGestion = "/api/v1/gestion";
}

public static class NombresClaim
{
    public const string Negocio = "negocio_id";
}

/// <summary>Firma los tokens de acceso del personal (JWT con HMAC-SHA256).</summary>
public sealed class EmisorJwt(OpcionesJwt opciones) : IEmisorTokensAcceso
{
    private readonly JsonWebTokenHandler _manejador = new();
    private readonly SigningCredentials _credenciales = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opciones.Clave!)),
        SecurityAlgorithms.HmacSha256);

    public TokenAcceso Emitir(Usuario usuario, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        var expira = ahora.AddMinutes(opciones.MinutosAcceso);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = opciones.Emisor,
            Audience = opciones.Audiencia,
            IssuedAt = ahora.UtcDateTime,
            NotBefore = ahora.UtcDateTime,
            Expires = expira.UtcDateTime,
            SigningCredentials = _credenciales,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = usuario.Id.ToString(),
                [NombresClaim.Negocio] = usuario.NegocioId.ToString(),
                ["role"] = Minuscula(usuario.Rol),
                ["name"] = usuario.Nombre,
            },
        };

        return new TokenAcceso(_manejador.CreateToken(descriptor), expira);
    }

    public static string Minuscula(Rol rol) => rol.ToString().ToLowerInvariant();
}

/// <summary>
/// El negocio de la petición, sacado de la sesión. Solo cuenta en las rutas de <c>/api/v1/gestion</c>:
/// en las públicas se ignora aunque venga un token, porque allí el negocio lo indica la URL.
/// </summary>
/// <remarks>
/// En la parte privada falla cerrado: sin una sesión válida devuelve un negocio que no existe, y el filtro global no
/// encuentra nada. Devolver «ningún negocio» quitaría el filtro y dejaría ver los datos de todos si alguna ruta privada
/// llegara a ejecutarse sin sesión por un error de configuración.
/// </remarks>
public sealed class ContextoNegocioHttp(IHttpContextAccessor accesor) : IContextoNegocio
{
    /// <summary>El negocio de una petición privada sin sesión válida: no existe, así que no se ve ningún dato.</summary>
    public static readonly Guid SinNegocio = Guid.Empty;

    public Guid? NegocioId
    {
        get
        {
            var http = accesor.HttpContext;

            if (http is null || !http.Request.Path.StartsWithSegments(Politicas.RutaGestion, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return SesionActual.Leer(http.User)?.NegocioId ?? SinNegocio;
        }
    }
}

public static class SesionActual
{
    /// <summary>La sesión del usuario autenticado, o <c>null</c> si no la hay o le faltan datos.</summary>
    public static SesionUsuario? Leer(ClaimsPrincipal usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        if (usuario.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var id = usuario.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var negocio = usuario.FindFirstValue(NombresClaim.Negocio);
        var rol = usuario.FindFirstValue("role");

        return Guid.TryParse(id, out var usuarioId)
            && Guid.TryParse(negocio, out var negocioId)
            && Enum.TryParse<Rol>(rol, ignoreCase: true, out var rolValido)
            && Enum.IsDefined(rolValido)
            ? new SesionUsuario(usuarioId, negocioId, rolValido)
            : null;
    }
}

public static class ServiciosSeguridad
{
    private const int LongitudMinimaClave = 32;

    // Clave solo para desarrollo local y tests, escrita aquí a la vista de todos: por eso fuera de
    // desarrollo no se acepta y hay que configurar una propia.
    private const string ClaveDesarrollo = "clave-solo-para-desarrollo-local-no-usar-en-produccion";

    public static IServiceCollection AddSeguridad(this IServiceCollection servicios, IConfiguration configuracion, bool esDesarrollo)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(configuracion);

        var opciones = configuracion.GetSection(OpcionesJwt.Seccion).Get<OpcionesJwt>() ?? new OpcionesJwt();

        if (string.IsNullOrWhiteSpace(opciones.Clave) && esDesarrollo)
        {
            opciones.Clave = ClaveDesarrollo;
        }

        if (opciones.Clave is not { Length: >= LongitudMinimaClave })
        {
            throw new InvalidOperationException(
                $"Falta configurar «{OpcionesJwt.Seccion}:Clave»: una cadena secreta de al menos {LongitudMinimaClave} caracteres con la que firmar los tokens.");
        }

        servicios.AddSingleton(opciones);
        servicios.AddSingleton<IEmisorTokensAcceso, EmisorJwt>();
        servicios.AddSingleton(new OpcionesSesion
        {
            DuracionAcceso = TimeSpan.FromMinutes(opciones.MinutosAcceso),
            DuracionRefresco = TimeSpan.FromDays(opciones.DiasRefresco),
        });

        servicios.AddHttpContextAccessor();
        servicios.AddScoped<IContextoNegocio, ContextoNegocioHttp>();

        servicios.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        servicios.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TimeProvider>((jwt, reloj) =>
            {
                // Los nombres de las claims son los del token, sin traducirlos a los de .NET.
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = opciones.Emisor,
                    ValidateAudience = true,
                    ValidAudience = opciones.Audiencia,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opciones.Clave)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = "name",
                    RoleClaimType = "role",

                    // La caducidad se comprueba con el reloj de la aplicación, no con el del sistema:
                    // así los tests pueden avanzar el tiempo.
                    LifetimeValidator = (antesDe, expira, _, _) =>
                    {
                        var ahora = reloj.GetUtcNow();
                        return (antesDe is null || antesDe <= ahora) && expira is not null && ahora < expira;
                    },
                };
            });

        servicios.AddAuthorizationBuilder()
            .AddPolicy(Politicas.Personal, p => p.RequireRole("personal", "encargado", "propietario"))
            .AddPolicy(Politicas.Encargado, p => p.RequireRole("encargado", "propietario"))
            .AddPolicy(Politicas.Propietario, p => p.RequireRole("propietario"));

        return servicios;
    }
}
