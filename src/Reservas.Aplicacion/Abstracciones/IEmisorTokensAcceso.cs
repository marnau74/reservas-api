using Reservas.Dominio.Personal;

namespace Reservas.Aplicacion.Abstracciones;

public sealed record TokenAcceso(string Token, DateTimeOffset ExpiraEn);

/// <summary>Crea el token de corta duración con el que el personal llama a la API.</summary>
public interface IEmisorTokensAcceso
{
    TokenAcceso Emitir(Usuario usuario, DateTimeOffset ahora);
}
