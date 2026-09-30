# Decisiones de arquitectura

Cada decisión relevante queda registrada en un ADR corto: contexto, decisión, alternativas
descartadas y consecuencias. Un ADR no se edita cuando cambia la decisión: se escribe uno
nuevo que lo sustituye.

| Nº | Decisión | Estado |
|---|---|---|
| [0001](0001-resultado-en-lugar-de-excepciones.md) | Errores de negocio con `Resultado`, sin excepciones | Aceptada |
| [0002](0002-tiempo-utc-y-hora-local.md) | Instantes en UTC, horarios en hora local y una única conversión | Aceptada |
| [0003](0003-no-solapar-reservas-en-postgresql.md) | No solapar reservas: restricción de exclusión y cola por mesa | Aceptada |
| [0004](0004-idempotencia-de-las-peticiones.md) | Idempotencia de las peticiones con `Idempotency-Key` | Aceptada |
| [0005](0005-sesion-del-personal-y-aislamiento-por-negocio.md) | Sesión del personal y aislamiento entre negocios | Aceptada |
