namespace Reservas.Aplicacion.Abstracciones;

public interface IHasherContrasenas
{
    /// <summary>Una huella de una contraseña que no es de nadie, para tardar lo mismo al probar un correo que no existe.</summary>
    string HashFalso { get; }

    string Hashear(string contrasena);

    bool Verificar(string hash, string contrasena);
}
