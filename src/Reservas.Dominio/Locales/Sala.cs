using Reservas.Dominio.Comun;

namespace Reservas.Dominio.Locales;

/// <summary>Una sala del local (interior, terraza, salón privado) que agrupa mesas.</summary>
public sealed class Sala
{
    private Sala(Guid id, string nombre)
    {
        Id = id;
        Nombre = nombre;
    }

    public Guid Id { get; }

    public string Nombre { get; }

    public static Resultado<Sala> Crear(string nombre) =>
        string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > 100
            ? Resultado.Fallo<Sala>(ErroresLocal.SalaInvalida)
            : Resultado.Exito(new Sala(Guid.NewGuid(), nombre.Trim()));
}
