# 0003 · No solapar reservas: restricción de exclusión y cola por mesa

- **Estado:** aceptada
- **Fecha:** 2026-09-30

## Contexto

Dos personas no pueden reservar la misma mesa a la misma hora, aunque pulsen «reservar» en el
mismo instante. La solución ingenua es comprobar antes de guardar:

```
si (mesa libre a esa hora) entonces guardar
```

Es incorrecta: entre la comprobación y el guardado, otra petición puede hacer lo mismo, y
las dos ven la mesa libre (*condición de carrera*). Con poco tráfico casi nunca pasa; con
mucho, pasa.

## Decisión

Dos mecanismos, con papeles distintos:

**1. La garantía: una restricción de exclusión de PostgreSQL.** Cada mesa de cada reserva es
una fila de `ocupaciones_mesa (mesa_id, periodo tstzrange, activa)` y la tabla tiene:

```sql
EXCLUDE USING gist (mesa_id WITH =, periodo WITH &&) WHERE (activa)
```

«No puede haber dos filas activas con la misma mesa y periodos que se solapan». PostgreSQL lo
comprueba al escribir, dentro de la transacción, así que es cierto con cualquier número de
peticiones simultáneas y aunque alguien escriba en la base de datos saltándose la aplicación
(hay un test que lo demuestra con un `INSERT` directo). Las reservas canceladas o terminadas
tienen `activa = false`: dejan de bloquear sin perder el histórico.

**2. Una cola por mesa para evitar la pelea.** Antes de insertar, el repositorio bloquea las
filas de las mesas implicadas (`SELECT … FOR NO KEY UPDATE`, siempre en orden de identificador).

### Por qué hace falta la cola

Con solo la restricción, veinte peticiones simultáneas para la misma mesa funcionaban, pero
no bien: el test fallaba con `40P01: deadlock detected`. Cada transacción inserta su fila y
solo entonces la restricción descubre que otra ya insertó la suya; entonces esperan unas a
otras. PostgreSQL detecta el interbloqueo tras `deadlock_timeout` (un segundo por defecto),
aborta una transacción y el resto sigue, pero los interbloqueos se encadenan.

Reintentar la transacción abortada no lo arregla: con veinte competidoras se repite y el
reintento no llegaba a converger en cinco intentos. La causa no era la mala suerte sino que
todas insertan a la vez.

Con la cola, las peticiones que compiten por una mesa entran de una en una. Cuando le toca a
la segunda, la primera ya ha confirmado y el conflicto se detecta de inmediato, sin esperas.
Bloquear en orden de identificador evita interbloqueos entre reservas que abarcan varias
mesas. `NO KEY UPDATE` (y no `UPDATE`) serializa a los competidores sin bloquear las
comprobaciones de clave foránea de otras escrituras sobre esas mesas.

La suite de tests de infraestructura pasó de unos 35 segundos a unos 19 con este cambio.

## Alternativas descartadas

- **Comprobar y luego guardar:** condición de carrera, como se explica arriba.
- **Nivel de aislamiento `SERIALIZABLE`:** también evitaría el solapamiento, pero obliga a
  reintentar transacciones abortadas en toda la aplicación y a diseñar para ello cada
  operación. La restricción es local a una tabla y declarativa.
- **Un bloqueo de aplicación (semáforo o bloqueo distribuido):** no funciona con varias
  instancias de la API y añade una pieza más que puede fallar.
- **Reintentar los interbloqueos:** oculta el problema en lugar de evitarlo, y añade latencia
  de segundos.
- **Solo la cola, sin restricción:** la cola es una optimización de la espera; sin la
  restricción, cualquier camino que no la use (un script, otro servicio, un error) podría
  duplicar una mesa.

## Consecuencias

- Cualquier conflicto llega a la aplicación como una violación de exclusión (`23P01`), que el
  repositorio traduce a un error de negocio, `reserva.mesa_ocupada`, y la API a un 409.
- El modelo de EF Core no sabe expresar una restricción de exclusión: se crea en una migración
  escrita a mano (`RestriccionExclusionMesas`) y hay un test que comprueba que existe.
- Las peticiones que compiten por una misma mesa se serializan. Es lo deseado: la contención
  es real (solo una gana) y el coste es la duración de una transacción corta.
- Los tests corren contra un PostgreSQL 17 real en Docker (Testcontainers): con una base de
  datos en memoria o SQLite no existiría esta restricción y los tests no probarían nada.
