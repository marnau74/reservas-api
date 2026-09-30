using System.Globalization;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Aplicacion.Agenda;
using Reservas.Aplicacion.ReservasPublicas;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Locales;
using Reservas.Dominio.Negocios;
using Reservas.Dominio.Personal;

namespace Reservas.Api.Contratos;

/// <summary>Traducción a los contratos de la parte privada, que enseñan más que los públicos (teléfono, mesas, identificadores).</summary>
public static class MapeoGestion
{
    private static readonly string[] NombresDia = ["domingo", "lunes", "martes", "miercoles", "jueves", "viernes", "sabado"];

    public static UsuarioRespuesta AUsuario(Usuario usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        return new UsuarioRespuesta(usuario.Id, usuario.Email, usuario.Nombre, Minuscula(usuario.Rol), usuario.Activo);
    }

    public static ReservaGestionRespuesta AReserva(Reserva reserva, Negocio negocio)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        ArgumentNullException.ThrowIfNull(negocio);

        var local = negocio.Zona.ALocal(reserva.Intervalo.Inicio);

        return new ReservaGestionRespuesta(
            reserva.Id,
            Camel(reserva.Estado),
            Formatos.DeFecha(DateOnly.FromDateTime(local)),
            Formatos.DeHora(TimeOnly.FromDateTime(local)),
            reserva.Intervalo.Inicio,
            reserva.Intervalo.Fin,
            reserva.Comensales,
            new ClienteGestionRespuesta(reserva.Cliente.Nombre, reserva.Cliente.Email, reserva.Cliente.Telefono),
            reserva.MesaIds);
    }

    public static ReservaGestionRespuesta AReserva(ReservaConNegocio datos) => AReserva(datos.Reserva, datos.Negocio);

    public static AgendaRespuesta AAgenda(AgendaDia agenda)
    {
        ArgumentNullException.ThrowIfNull(agenda);

        return new AgendaRespuesta(Formatos.DeFecha(agenda.Fecha), [.. agenda.Reservas.Select(r => AReserva(r, agenda.Negocio))]);
    }

    public static SalaRespuesta ASala(Sala sala) => new(sala.Id, sala.Nombre);

    public static MesaRespuesta AMesa(Mesa mesa) =>
        new(mesa.Id, mesa.SalaId, mesa.Nombre, mesa.CapacidadMinima, mesa.CapacidadMaxima, mesa.EsCombinable);

    public static HorarioRespuesta AHorario(Registrado<Horario> registro) => new(
        registro.Id,
        NombreDia(registro.Valor.Dia),
        Camel(registro.Valor.Turno),
        Formatos.DeHora(registro.Valor.Inicio),
        Formatos.DeHora(registro.Valor.Fin),
        registro.Valor.IntervaloMinutos);

    public static CierreRespuesta ACierre(Registrado<Cierre> registro) => new(
        registro.Id,
        Formatos.DeFecha(registro.Valor.Fecha),
        registro.Valor.Turno is { } turno ? Camel(turno) : null,
        registro.Valor.Motivo);

    public static string NombreDia(DayOfWeek dia) => NombresDia[(int)dia];

    public static bool TryParseDia(string? texto, out DayOfWeek dia)
    {
        var indice = Array.IndexOf(NombresDia, texto?.Trim().ToLowerInvariant());
        dia = indice < 0 ? default : (DayOfWeek)indice;
        return indice >= 0;
    }

    public static bool TryParseTurno(string? texto, out Turno turno) =>
        Enum.TryParse(texto?.Trim(), ignoreCase: true, out turno) && Enum.IsDefined(turno);

    public static bool TryParseRol(string? texto, out Rol rol) =>
        Enum.TryParse(texto?.Trim(), ignoreCase: true, out rol) && Enum.IsDefined(rol);

    private static string Minuscula(Rol rol) => rol.ToString().ToLower(CultureInfo.InvariantCulture);

    private static string Camel<TEnum>(TEnum valor)
        where TEnum : struct, Enum
    {
        var nombre = valor.ToString();
        return char.ToLowerInvariant(nombre[0]) + nombre[1..];
    }
}
