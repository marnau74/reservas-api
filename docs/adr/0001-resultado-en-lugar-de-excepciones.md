# 0001 · Errores de negocio con `Resultado`, sin excepciones

- **Estado:** aceptada
- **Fecha:** 2026-09-30

## Contexto

Las reglas de una reserva se incumplen constantemente en el uso normal: intentar confirmar
una reserva que ya caducó, pedir mesa para once personas cuando el máximo online es diez,
reservar una hora que no existe el día que cambia el reloj. No son fallos del programa,
son resultados esperados que la API tiene que explicar al cliente con un código estable.

## Decisión

Las operaciones del dominio que pueden incumplir una regla devuelven un `Resultado`
(o `Resultado<T>`) con un `ErrorDominio` de código estable (`reserva.caducada`,
`negocio.slug_invalido`…), en lugar de lanzar excepciones. Los errores de cada concepto
viven junto a él (`ErroresReserva`, `ErroresNegocio`, `ErroresLocal`).

Las excepciones se reservan para los errores de programación: pasar `null`, crear un
intervalo con el fin anterior al inicio, pedir el valor de un `Resultado` fallido.

## Alternativas descartadas

- **Excepciones de dominio:** cada regla incumplida sería un `throw` que hay que capturar
  en la capa de aplicación, con un coste de rendimiento y de legibilidad, y el flujo normal
  quedaría mezclado con el excepcional.
- **Devolver `bool` o `null`:** no dice por qué falló, y la API necesita el motivo.
- **Una librería de resultados (`ErrorOr`, `FluentResults`):** para un tipo de veinte líneas
  no compensa una dependencia; el proyecto ya evita las de licencia comercial.

## Consecuencias

- La API traduce cada `ErrorDominio` a un `ProblemDetails` con su código, sin lógica
  condicional dispersa.
- Los tests comprueban códigos de error concretos, que forman parte del contrato de la API.
- Hay que acordarse de comprobar el resultado: `Resultado` no obliga a hacerlo. Los tests
  de la máquina de estados cubren todas las transiciones para que ninguna se ignore.
