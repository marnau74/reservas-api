using Reservas.Dominio.Comun;

namespace Reservas.Api.Idempotencia;

public static class ErroresIdempotencia
{
    public static readonly ErrorDominio ClaveRequerida =
        new("idempotencia.clave_requerida", "Falta la cabecera Idempotency-Key: es obligatoria para crear una reserva.");

    public static readonly ErrorDominio ClaveInvalida =
        new("idempotencia.clave_invalida", "Idempotency-Key debe tener entre 8 y 64 caracteres: letras, números, guiones y guiones bajos.");

    public static readonly ErrorDominio EnCurso =
        new("idempotencia.en_curso", "Otra petición con esa misma Idempotency-Key se está procesando. Vuelve a intentarlo en unos segundos.");

    public static readonly ErrorDominio ClaveReutilizada =
        new("idempotencia.clave_reutilizada", "Esa Idempotency-Key ya se usó con una petición distinta. Usa una clave nueva para cada operación.");
}
