using NetArchTest.Rules;

using Shouldly;

using ResultadoRegla = NetArchTest.Rules.TestResult;

namespace Reservas.Arquitectura.Tests;

/// <summary>
/// Las dependencias entre capas van siempre hacia dentro:
/// Dominio ← Aplicación ← Infraestructura / Api. Si alguien añade una referencia que rompe
/// esto, falla la build de tests, no una revisión de código.
/// </summary>
public class DependenciasTests
{
    private const string Dominio = "Reservas.Dominio";
    private const string Aplicacion = "Reservas.Aplicacion";
    private const string Infraestructura = "Reservas.Infraestructura";
    private const string Api = "Reservas.Api";

    private static readonly System.Reflection.Assembly EnsambladoDominio = typeof(Reservas.Dominio.Comun.Resultado).Assembly;
    private static readonly System.Reflection.Assembly EnsambladoAplicacion = typeof(Reservas.Aplicacion.ReferenciaEnsamblado).Assembly;

    [Fact]
    public void El_dominio_no_depende_de_ninguna_otra_capa_ni_de_frameworks()
    {
        var resultado = Types.InAssembly(EnsambladoDominio)
            .ShouldNot()
            .HaveDependencyOnAny(Aplicacion, Infraestructura, Api, "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore")
            .GetResult();

        resultado.IsSuccessful.ShouldBeTrue(Explicar(resultado));
    }

    [Fact]
    public void La_aplicacion_no_depende_de_infraestructura_ni_de_la_api()
    {
        var resultado = Types.InAssembly(EnsambladoAplicacion)
            .ShouldNot()
            .HaveDependencyOnAny(Infraestructura, Api, "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore")
            .GetResult();

        resultado.IsSuccessful.ShouldBeTrue(Explicar(resultado));
    }

    private static string Explicar(ResultadoRegla resultado) =>
        $"Tipos que rompen la regla: {string.Join(", ", resultado.FailingTypeNames ?? [])}";
}
