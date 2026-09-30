using Reservas.Aplicacion.Disponibilidad;
using Reservas.Aplicacion.ReservasPublicas;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Negocios;

namespace Reservas.Api.Contratos;

/// <summary>Traducción de los objetos del dominio a los contratos públicos. Aquí se decide qué se enseña y qué no.</summary>
public static class Mapeo
{
    public static NegocioRespuesta ANegocio(Negocio negocio)
    {
        ArgumentNullException.ThrowIfNull(negocio);

        return new NegocioRespuesta(
            negocio.Slug,
            negocio.Nombre,
            negocio.Zona.Id,
            negocio.Politicas.MaxComensalesOnline,
            negocio.Politicas.DiasMaximosAntelacion);
    }

    /// <summary>De cada franja solo se enseña la hora: las mesas asignadas son un detalle interno del local.</summary>
    public static DisponibilidadRespuesta ADisponibilidad(DisponibilidadDia dia)
    {
        ArgumentNullException.ThrowIfNull(dia);

        return new DisponibilidadRespuesta(
            dia.Negocio.Slug,
            Formatos.DeFecha(dia.Fecha),
            dia.Comensales,
            [.. dia.Franjas.Select(f => new FranjaRespuesta(Formatos.DeHora(f.HoraLocal), Texto(f.Turno), f.Intervalo.Inicio))]);
    }

    public static ReservaRespuesta AReserva(ReservaConNegocio datos, bool mostrarCodigoGestion)
    {
        ArgumentNullException.ThrowIfNull(datos);

        var reserva = datos.Reserva;
        var local = datos.Negocio.Zona.ALocal(reserva.Intervalo.Inicio);

        return new ReservaRespuesta(
            mostrarCodigoGestion ? reserva.CodigoGestion : null,
            Texto(reserva.Estado),
            datos.Negocio.Slug,
            Formatos.DeFecha(DateOnly.FromDateTime(local)),
            Formatos.DeHora(TimeOnly.FromDateTime(local)),
            reserva.Intervalo.Inicio,
            reserva.Intervalo.Fin,
            reserva.Comensales,
            new ClienteRespuesta(reserva.Cliente.Nombre, reserva.Cliente.Email),
            reserva.Estado == EstadoReserva.Pendiente ? reserva.CaducaEn : null);
    }

    /// <summary>Nombre de un valor de enumeración en camelCase (<c>NoPresentada</c> → <c>noPresentada</c>).</summary>
    private static string Texto<TEnum>(TEnum valor)
        where TEnum : struct, Enum
    {
        var nombre = valor.ToString();
        return char.ToLowerInvariant(nombre[0]) + nombre[1..];
    }
}
