using FluentValidation;

using Reservas.Api.Contratos;

namespace Reservas.Api.Validacion;

// Validan la forma de las peticiones de la parte privada. Las reglas de negocio (capacidades,
// fortaleza de la contraseña, roles permitidos) las decide la aplicación con códigos propios.

public sealed class ValidadorLogin : AbstractValidator<LoginDto>
{
    public ValidadorLogin()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo electrónico es obligatorio.")
            .MaximumLength(254).WithMessage("El correo no puede superar los 254 caracteres.");

        // Un límite de longitud evita que alguien fuerce al servidor a calcular la huella de un texto enorme.
        RuleFor(x => x.Contrasena)
            .NotEmpty().WithMessage("La contraseña es obligatoria.")
            .MaximumLength(128).WithMessage("La contraseña no puede superar los 128 caracteres.");
    }
}

public sealed class ValidadorRefresco : AbstractValidator<RefrescoDto>
{
    public ValidadorRefresco()
    {
        RuleFor(x => x.TokenRefresco)
            .NotEmpty().WithMessage("El token de renovación es obligatorio.")
            .MaximumLength(100).WithMessage("El token de renovación no es válido.");
    }
}

public sealed class ValidadorCrearUsuario : AbstractValidator<CrearUsuarioDto>
{
    public ValidadorCrearUsuario()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo electrónico es obligatorio.")
            .MaximumLength(254).WithMessage("El correo no puede superar los 254 caracteres.")
            .EmailAddress().WithMessage("El correo electrónico no es válido.");

        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre es obligatorio.")
            .MaximumLength(100).WithMessage("El nombre no puede superar los 100 caracteres.");

        RuleFor(x => x.Rol)
            .NotEmpty().WithMessage("El rol es obligatorio.")
            .Must(r => MapeoGestion.TryParseRol(r, out _)).WithMessage("El rol debe ser «personal» o «encargado».");

        RuleFor(x => x.Contrasena)
            .NotEmpty().WithMessage("La contraseña es obligatoria.")
            .MaximumLength(128).WithMessage("La contraseña no puede superar los 128 caracteres.");
    }
}

public sealed class ValidadorConsultaAgenda : AbstractValidator<ConsultaAgendaDto>
{
    public ValidadorConsultaAgenda()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Fecha)
            .NotEmpty().WithMessage("La fecha es obligatoria.")
            .Must(f => Formatos.TryParseFecha(f, out _)).WithMessage("La fecha debe tener el formato AAAA-MM-DD.");
    }
}

public sealed class ValidadorCrearSala : AbstractValidator<CrearSalaDto>
{
    public ValidadorCrearSala()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre es obligatorio.")
            .MaximumLength(100).WithMessage("El nombre no puede superar los 100 caracteres.");
    }
}

public sealed class ValidadorCrearMesa : AbstractValidator<CrearMesaDto>
{
    public ValidadorCrearMesa()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.SalaId).NotNull().WithMessage("La sala es obligatoria.");

        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre es obligatorio.")
            .MaximumLength(100).WithMessage("El nombre no puede superar los 100 caracteres.");

        RuleFor(x => x.CapacidadMinima).NotNull().WithMessage("La capacidad mínima es obligatoria.");
        RuleFor(x => x.CapacidadMaxima).NotNull().WithMessage("La capacidad máxima es obligatoria.");
        RuleFor(x => x.EsCombinable).NotNull().WithMessage("Hay que indicar si la mesa es combinable.");
    }
}

public sealed class ValidadorCrearHorario : AbstractValidator<CrearHorarioDto>
{
    public ValidadorCrearHorario()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Dia)
            .NotEmpty().WithMessage("El día es obligatorio.")
            .Must(d => MapeoGestion.TryParseDia(d, out _)).WithMessage("El día debe ser lunes, martes, miercoles, jueves, viernes, sabado o domingo.");

        RuleFor(x => x.Turno)
            .NotEmpty().WithMessage("El turno es obligatorio.")
            .Must(t => MapeoGestion.TryParseTurno(t, out _)).WithMessage("El turno debe ser «comida» o «cena».");

        RuleFor(x => x.Inicio)
            .NotEmpty().WithMessage("La hora de inicio es obligatoria.")
            .Must(h => Formatos.TryParseHora(h, out _)).WithMessage("La hora de inicio debe tener el formato HH:mm.");

        RuleFor(x => x.Fin)
            .NotEmpty().WithMessage("La hora de fin es obligatoria.")
            .Must(h => Formatos.TryParseHora(h, out _)).WithMessage("La hora de fin debe tener el formato HH:mm.");

        RuleFor(x => x.IntervaloMinutos).NotNull().WithMessage("El intervalo en minutos es obligatorio.");
    }
}

public sealed class ValidadorCrearCierre : AbstractValidator<CrearCierreDto>
{
    public ValidadorCrearCierre()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Fecha)
            .NotEmpty().WithMessage("La fecha es obligatoria.")
            .Must(f => Formatos.TryParseFecha(f, out _)).WithMessage("La fecha debe tener el formato AAAA-MM-DD.");

        RuleFor(x => x.Turno)
            .Must(t => MapeoGestion.TryParseTurno(t, out _)).WithMessage("El turno debe ser «comida» o «cena», o dejarse vacío para cerrar todo el día.")
            .When(x => !string.IsNullOrWhiteSpace(x.Turno));

        RuleFor(x => x.Motivo)
            .NotEmpty().WithMessage("El motivo es obligatorio.")
            .MaximumLength(200).WithMessage("El motivo no puede superar los 200 caracteres.");
    }
}
