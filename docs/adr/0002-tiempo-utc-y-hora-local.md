# 0002 · Instantes en UTC, horarios en hora local y una única conversión

- **Estado:** aceptada
- **Fecha:** 2026-09-30

## Contexto

Un negocio abre «las cenas a las 21:00». Eso es una hora local, no un instante: significa
las 19:00 UTC en verano y las 20:00 UTC en invierno. Una reserva, en cambio, es un momento
concreto e inequívoco. Mezclar las dos cosas produce errores sutiles, sobre todo en los dos
días del año en que cambia la hora:

- El **29 de marzo de 2026** los relojes pasan de las 02:00 a las 03:00: las 02:30 no existen.
- El **25 de octubre de 2026** pasan de las 03:00 a las 02:00: las 02:30 ocurren dos veces.

## Decisión

- Los **horarios** del negocio se guardan como hora local (`TimeOnly`) junto a su zona
  horaria (`Europe/Madrid`).
- Las **reservas** se guardan como instantes UTC (`IntervaloTiempo`, semiabierto: incluye el
  inicio y excluye el fin).
- Solo `ZonaHorariaNegocio` convierte entre ambos mundos, con reglas explícitas:
  una hora que no existe no se puede reservar (`null`) y una hora repetida se interpreta
  como su primera ocurrencia.
- La duración de una reserva es tiempo **real**, no de reloj: una reserva de dos horas que
  empieza a las 01:30 el día del salto termina a las 04:30 en el reloj, no a las 03:30.
- Ninguna clase del dominio lee la hora del sistema: reciben `ahora` como parámetro, así que
  todo se prueba fijando el instante.

## Alternativas descartadas

- **Guardar todo en hora local:** ambigua en el cambio de hora de otoño e imposible de
  comparar entre zonas horarias.
- **Guardar los horarios como UTC:** cambiarían de hora local dos veces al año y habría que
  recalcularlos.
- **`TimeProvider` inyectado en el dominio:** añade una dependencia donde un parámetro
  basta; se usará en la capa de aplicación, donde sí hay servicios.

## Consecuencias

- Los tests cubren los dos cambios de hora de 2026 con instantes concretos.
- Una zona horaria inexistente se rechaza al crear el negocio, no al calcular disponibilidad.
- El resto de capas trabajan siempre con UTC y delegan la conversión en este único punto.
