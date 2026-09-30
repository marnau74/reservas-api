using NetArchTest.Rules;

using Shouldly;

using ResultadoRegla = NetArchTest.Rules.TestResult;

namespace Reservas.Arquitectura.Tests;

/// <summary>
/// Reglas de diseño más finas que la dirección de las dependencias entre capas. Cada una recoge
/// una decisión tomada (ver docs/adr) para que no se deshaga sin darse cuenta.
/// </summary>
public class ReglasDeDisenoTests
{
    private static readonly System.Reflection.Assembly Dominio = typeof(Reservas.Dominio.Comun.Resultado).Assembly;
    private static readonly System.Reflection.Assembly Aplicacion = typeof(Reservas.Aplicacion.ReferenciaEnsamblado).Assembly;
    private static readonly System.Reflection.Assembly Infraestructura = typeof(Reservas.Infraestructura.Persistencia.ReservasDbContext).Assembly;
    private static readonly System.Reflection.Assembly Api = typeof(Program).Assembly;

    private static string Explicar(ResultadoRegla resultado) =>
        $"Tipos que rompen la regla: {string.Join(", ", resultado.FailingTypeNames ?? [])}";

    [Fact]
    public void Los_endpoints_no_conocen_la_infraestructura_solo_los_casos_de_uso()
    {
        // Un endpoint que llamara al DbContext se saltaría las reglas de negocio y el aislamiento
        // entre negocios. Solo el arranque (Program) cablea la infraestructura.
        var resultado = Types.InAssembly(Api)
            .That().ResideInNamespaceStartingWith("Reservas.Api.Endpoints")
            .ShouldNot().HaveDependencyOnAny("Reservas.Infraestructura", "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        resultado.IsSuccessful.ShouldBeTrue(Explicar(resultado));
    }

    [Fact]
    public void La_api_solo_toca_entity_framework_en_el_arranque()
    {
        var resultado = Types.InAssembly(Api)
            .That().DoNotHaveName("Program")
            .And().DoNotResideInNamespace("Reservas.Api.Arranque")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        // Solo el arranque (Program y el espacio Arranque: migraciones y datos de demostración) habla con la
        // base de datos; el middleware de idempotencia y las tareas dependen de puertos de la aplicación.
        resultado.IsSuccessful.ShouldBeTrue(Explicar(resultado));
    }

    [Fact]
    public void Los_contratos_de_la_api_no_exponen_tipos_del_dominio()
    {
        // Lo que ve un cliente de la API es un contrato que se versiona; si fuera un objeto del dominio,
        // refactorizar el dominio rompería a los clientes. Solo los mapeos conocen ambos lados.
        var resultado = Types.InAssembly(Api)
            .That().ResideInNamespace("Reservas.Api.Contratos")
            .And().DoNotHaveNameStartingWith("Mapeo")
            .ShouldNot().HaveDependencyOn("Reservas.Dominio")
            .GetResult();

        resultado.IsSuccessful.ShouldBeTrue(Explicar(resultado));
    }

    [Fact]
    public void Los_casos_de_uso_de_la_aplicacion_son_clases_selladas_sin_estado_estatico()
    {
        var resultado = Types.InAssembly(Aplicacion)
            .That().AreClasses()
            .And().DoNotHaveName("ErroresAplicacion")
            .And().AreNotStatic()
            .And().AreNotAbstract()
            .Should().BeSealed()
            .GetResult();

        resultado.IsSuccessful.ShouldBeTrue(Explicar(resultado));
    }

    [Fact]
    public void Las_implementaciones_de_repositorios_viven_en_infraestructura_y_cumplen_un_puerto_de_la_aplicacion()
    {
        var puertos = Types.InAssembly(Aplicacion).That().AreInterfaces().And().HaveNameStartingWith("IRepositorio").GetTypes().ToList();
        puertos.Count.ShouldBeGreaterThan(4);

        var implementaciones = Types.InAssembly(Infraestructura)
            .That().HaveNameStartingWith("Repositorio")
            .GetTypes()
            .ToList();

        implementaciones.ShouldNotBeEmpty();
        foreach (var implementacion in implementaciones)
        {
            implementacion.GetInterfaces().ShouldContain(
                interfaz => puertos.Contains(interfaz),
                $"{implementacion.Name} debe implementar un puerto de la aplicación");
        }
    }

    [Fact]
    public void Ningun_tipo_del_dominio_tiene_propiedades_publicas_con_set()
    {
        // El estado del dominio solo cambia con sus métodos, que comprueban las reglas (ADR 0001).
        var conSet = Dominio.GetTypes()
            .Where(tipo => tipo is { IsClass: true, IsPublic: true } && !tipo.IsAbstract)
            .SelectMany(tipo => tipo.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(propiedad => propiedad.SetMethod is { IsPublic: true } && propiedad.SetMethod.ReturnParameter.GetRequiredCustomModifiers().All(m => m.Name != "IsExternalInit"))
                .Select(propiedad => $"{tipo.Name}.{propiedad.Name}"))
            .ToList();

        conSet.ShouldBeEmpty("estas propiedades del dominio se pueden cambiar desde fuera sin pasar por sus reglas");
    }

    [Fact]
    public void El_dominio_no_define_excepciones_los_errores_de_negocio_son_valores()
    {
        // ADR 0001: una reserva fuera de horario es un resultado esperado, no una excepción.
        Dominio.GetTypes().Where(tipo => typeof(Exception).IsAssignableFrom(tipo)).ShouldBeEmpty();
    }
}
