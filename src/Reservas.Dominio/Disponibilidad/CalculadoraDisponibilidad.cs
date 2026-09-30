using Reservas.Dominio.Comun;
using Reservas.Dominio.Gestion;

namespace Reservas.Dominio.Disponibilidad;

/// <summary>
/// Calcula a qué horas se puede reservar un día para un grupo. Es una función pura: recibe
/// todos los datos y la hora actual, no consulta nada, así que se prueba sin base de datos.
/// </summary>
public static class CalculadoraDisponibilidad
{
    /// <summary>
    /// Franjas libres de <paramref name="fecha"/> (día local del negocio), ordenadas por hora.
    /// Falla con un error de negocio si la petición incumple las políticas de reserva pública.
    /// </summary>
    public static Resultado<IReadOnlyList<Franja>> Calcular(
        DatosDisponibilidad datos,
        DateOnly fecha,
        int comensales,
        OrigenReserva origen,
        DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(datos);

        var politicas = datos.Negocio.Politicas;
        var zona = datos.Negocio.Zona;
        var publica = origen == OrigenReserva.Publica;

        if (comensales <= 0)
        {
            return Resultado.Fallo<IReadOnlyList<Franja>>(ErroresReserva.ComensalesInvalidos);
        }

        if (publica && comensales > politicas.MaxComensalesOnline)
        {
            return Resultado.Fallo<IReadOnlyList<Franja>>(ErroresReserva.DemasiadosComensalesOnline);
        }

        if (publica && fecha > zona.FechaLocal(ahora).AddDays(politicas.DiasMaximosAntelacion))
        {
            return Resultado.Fallo<IReadOnlyList<Franja>>(ErroresReserva.DemasiadoLejos);
        }

        // El público no puede reservar con menos antelación que la del negocio; el personal, sí.
        var primeraAdmitida = publica ? ahora + politicas.AntelacionMinima : DateTimeOffset.MinValue;
        var franjas = new List<Franja>();

        foreach (var horario in datos.Horarios.Where(h => h.Dia == fecha.DayOfWeek))
        {
            if (datos.Cierres.Any(c => c.Cubre(fecha, horario.Turno)))
            {
                continue;
            }

            foreach (var hora in horario.HorasOfrecidas())
            {
                // Una hora que no existe (cambio de hora de primavera) no se puede ofrecer.
                if (zona.AUtc(fecha, hora) is not { } inicio || inicio < primeraAdmitida)
                {
                    continue;
                }

                // La reserva ocupa tiempo real, no tiempo de reloj: dura lo mismo aunque
                // en medio se cambie la hora.
                var intervalo = new IntervaloTiempo(inicio, inicio + politicas.DuracionEstandar);
                var mesas = AsignadorMesas.Asignar(comensales, datos.Mesas, datos.Ocupaciones, intervalo);

                if (mesas.Count > 0)
                {
                    franjas.Add(new Franja(hora, horario.Turno, intervalo, mesas));
                }
            }
        }

        IReadOnlyList<Franja> ordenadas = [.. franjas.DistinctBy(f => f.Intervalo.Inicio).OrderBy(f => f.Intervalo.Inicio)];
        return Resultado.Exito(ordenadas);
    }
}
