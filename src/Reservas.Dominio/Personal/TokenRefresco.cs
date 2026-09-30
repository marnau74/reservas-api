namespace Reservas.Dominio.Personal;

/// <summary>
/// Permite renovar la sesión sin volver a escribir la contraseña. El access token dura poco
/// (minutos) para limitar el daño si alguien lo roba; este dura días y solo sirve para pedir uno
/// nuevo. Cada uso lo consume y entrega otro (rotación), así que uno robado se delata cuando el
/// dueño legítimo intenta usar el que ya se gastó.
/// </summary>
/// <remarks>Solo se guarda la huella del token, no el token: quien lea la base de datos no puede usarlos.</remarks>
public sealed class TokenRefresco
{
    // Constructor para que EF Core reconstruya el token desde la base de datos.
    private TokenRefresco() => HashToken = null!;

    private TokenRefresco(Guid usuarioId, string hashToken, DateTimeOffset creadoEn, DateTimeOffset expiraEn)
    {
        Id = Guid.NewGuid();
        UsuarioId = usuarioId;
        HashToken = hashToken;
        CreadoEn = creadoEn.ToUniversalTime();
        ExpiraEn = expiraEn.ToUniversalTime();
    }

    public Guid Id { get; }

    public Guid UsuarioId { get; }

    public string HashToken { get; }

    public DateTimeOffset CreadoEn { get; }

    public DateTimeOffset ExpiraEn { get; }

    public DateTimeOffset? RevocadoEn { get; private set; }

    public static TokenRefresco Crear(Guid usuarioId, string hashToken, DateTimeOffset ahora, TimeSpan duracion) =>
        new(usuarioId, hashToken, ahora, ahora + duracion);

    public bool EstaRevocado => RevocadoEn is not null;

    public bool EstaActivo(DateTimeOffset ahora) => !EstaRevocado && ahora < ExpiraEn;

    public void Revocar(DateTimeOffset ahora) => RevocadoEn ??= ahora.ToUniversalTime();
}
