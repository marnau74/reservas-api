using Reservas.Tests.Comunes;

// Un único contenedor de PostgreSQL para todos los tests de este ensamblado.
[assembly: AssemblyFixture(typeof(ServidorPostgres))]
