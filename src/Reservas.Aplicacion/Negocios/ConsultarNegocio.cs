using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Negocios;

namespace Reservas.Aplicacion.Negocios;

/// <summary>Datos públicos de un negocio.</summary>
public sealed class ConsultarNegocio(IRepositorioNegocios negocios)
{
    public async Task<Resultado<Negocio>> EjecutarAsync(string slug, CancellationToken cancellationToken)
    {
        var negocio = await negocios.ObtenerPorSlugAsync(slug, cancellationToken);

        return negocio is null
            ? Resultado.Fallo<Negocio>(ErroresAplicacion.NegocioNoEncontrado)
            : Resultado.Exito(negocio);
    }
}
