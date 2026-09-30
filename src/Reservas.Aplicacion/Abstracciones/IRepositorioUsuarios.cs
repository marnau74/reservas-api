using Reservas.Dominio.Comun;
using Reservas.Dominio.Personal;

namespace Reservas.Aplicacion.Abstracciones;

/// <summary>Personas que trabajan en los negocios. Cada instancia es una unidad de trabajo.</summary>
public interface IRepositorioUsuarios
{
    /// <summary>Busca por correo entre todos los negocios (el inicio de sesión ocurre antes de saber en cuál).</summary>
    Task<Usuario?> ObtenerPorEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>Busca por identificador. Con sesión de personal, solo encuentra usuarios de su propio negocio.</summary>
    Task<Usuario?> ObtenerAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Usuario>> ListarAsync(Guid negocioId, CancellationToken cancellationToken);

    /// <summary>Guarda un usuario nuevo, o devuelve <c>usuario.email_en_uso</c> si ese correo ya existe.</summary>
    Task<Resultado> AgregarAsync(Usuario usuario, CancellationToken cancellationToken);

    /// <summary>Guarda los cambios de los usuarios obtenidos con este repositorio.</summary>
    Task GuardarAsync(CancellationToken cancellationToken);
}
