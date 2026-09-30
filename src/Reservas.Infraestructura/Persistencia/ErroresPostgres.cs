using Npgsql;

namespace Reservas.Infraestructura.Persistencia;

internal static class ErroresPostgres
{
    /// <summary>
    /// EF Core envuelve los errores de la base de datos, y los que considera transitorios los
    /// envuelve una vez más: hay que recorrer la cadena hasta el error original de PostgreSQL.
    /// </summary>
    public static PostgresException? Buscar(Exception? excepcion)
    {
        while (excepcion is not null)
        {
            if (excepcion is PostgresException postgres)
            {
                return postgres;
            }

            excepcion = excepcion.InnerException;
        }

        return null;
    }

    public static bool Es(Exception excepcion, string sqlState) => Buscar(excepcion)?.SqlState == sqlState;
}
