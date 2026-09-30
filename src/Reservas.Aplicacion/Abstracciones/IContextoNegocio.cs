namespace Reservas.Aplicacion.Abstracciones;

/// <summary>
/// El negocio en cuyo nombre se está atendiendo la petición actual. Lo decide el host a partir de
/// la sesión del personal (nunca de un dato que envíe el cliente) y la persistencia lo usa para
/// que las consultas solo vean datos de ese negocio. En las peticiones sin sesión, como las del
/// público, es <c>null</c> y no se filtra nada: allí el negocio se indica en cada consulta.
/// </summary>
public interface IContextoNegocio
{
    Guid? NegocioId { get; }
}
