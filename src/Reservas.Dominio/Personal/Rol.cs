namespace Reservas.Dominio.Personal;

/// <summary>
/// Qué puede hacer una persona del negocio. Cada rol incluye todo lo del anterior: el propietario
/// puede lo que el encargado, y el encargado lo que el personal.
/// </summary>
public enum Rol
{
    /// <summary>Ve la agenda del día y atiende a los clientes: crea, sienta y cierra reservas.</summary>
    Personal = 1,

    /// <summary>Además, configura el local: salas, mesas, horarios y cierres.</summary>
    Encargado = 2,

    /// <summary>Además, gestiona quién trabaja en el negocio.</summary>
    Propietario = 3,
}

public static class RolExtensiones
{
    /// <summary>¿Este rol alcanza el nivel mínimo pedido?</summary>
    public static bool Cubre(this Rol rol, Rol minimo) => Enum.IsDefined(rol) && rol >= minimo;
}
