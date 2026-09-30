using FluentValidation;

using Reservas.Api.Contratos;

namespace Reservas.Api.Validacion;

/// <summary>
/// Valida la forma de la petición: que estén los campos y tengan el formato del contrato. Las
/// reglas de negocio (horario, mesas, políticas del negocio) las decide el dominio.
/// </summary>
public sealed class ValidadorSolicitudReserva : AbstractValidator<SolicitudReservaDto>
{
    public ValidadorSolicitudReserva()
    {
        // Con «Stop», si una regla de un campo falla no se evalúan las siguientes de ese campo
        // (un campo vacío da «es obligatorio», no también «formato incorrecto»).
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Fecha)
            .NotEmpty().WithMessage("La fecha es obligatoria.")
            .Must(f => Formatos.TryParseFecha(f, out _)).WithMessage("La fecha debe tener el formato AAAA-MM-DD.");

        RuleFor(x => x.Hora)
            .NotEmpty().WithMessage("La hora es obligatoria.")
            .Must(h => Formatos.TryParseHora(h, out _)).WithMessage("La hora debe tener el formato HH:mm.");

        RuleFor(x => x.Comensales)
            .NotNull().WithMessage("El número de comensales es obligatorio.")
            .GreaterThan(0).WithMessage("El número de comensales debe ser mayor que cero.");

        RuleFor(x => x.Cliente).NotNull().WithMessage("Los datos del cliente son obligatorios.");

        When(x => x.Cliente is not null, () =>
        {
            RuleFor(x => x.Cliente!.Nombre)
                .NotEmpty().WithMessage("El nombre es obligatorio.")
                .MaximumLength(100).WithMessage("El nombre no puede superar los 100 caracteres.");

            RuleFor(x => x.Cliente!.Email)
                .NotEmpty().WithMessage("El correo electrónico es obligatorio.")
                .MaximumLength(254).WithMessage("El correo no puede superar los 254 caracteres.")
                .EmailAddress().WithMessage("El correo electrónico no es válido.");

            RuleFor(x => x.Cliente!.Telefono)
                .MaximumLength(30).WithMessage("El teléfono no puede superar los 30 caracteres.");
        });
    }
}

public sealed class ValidadorConsultaDisponibilidad : AbstractValidator<ConsultaDisponibilidadDto>
{
    public ValidadorConsultaDisponibilidad()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Fecha)
            .NotEmpty().WithMessage("La fecha es obligatoria.")
            .Must(f => Formatos.TryParseFecha(f, out _)).WithMessage("La fecha debe tener el formato AAAA-MM-DD.");

        RuleFor(x => x.Comensales)
            .NotEmpty().WithMessage("El número de comensales es obligatorio.")
            .Must(c => Formatos.TryParseComensales(c, out _)).WithMessage("El número de comensales debe ser un entero mayor que cero.");
    }
}
