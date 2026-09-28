using System;
using System.Windows.Forms;
using OpticentroZKTeco.Infrastructure.Zkteco;

namespace OpticentroZKTeco.Setup.Actions
{
    internal static class ConexionTester
    {
        public static void ProbarConexionSegura(string ip, string puertoTexto)
        {
            try
            {
                if (!int.TryParse(puertoTexto, out int puerto))
                {
                    MessageBoxHelper.Mostrar(
                        "No se pudo interpretar el puerto para la prueba de conexión.",
                        MessageBoxIcon.Warning);
                    return;
                }

                var cliente = new ZktecoDeviceClient(ip, puerto);
                bool conectado = cliente.Conectar();

                if (conectado)
                {
                    MessageBoxHelper.Mostrar(
                        "Instalación completada. Conexión exitosa con el dispositivo " +
                        $"biométrico en {ip}:{puerto}.",
                        MessageBoxIcon.Information);
                    cliente.Desconectar();
                }
                else
                {
                    string ultimoError = cliente.ObtenerUltimoError();
                    MessageBoxHelper.Mostrar(
                        "Instalación completada. El servicio quedó instalado y se " +
                        "iniciará en el próximo arranque de Windows.\n\n" +
                        "Sin embargo, no se pudo conectar en este momento con el " +
                        $"dispositivo biométrico en {ip}:{puerto}.\n" +
                        $"Detalle: {ultimoError}\n\n" +
                        "Verifique que el dispositivo esté encendido y en la misma red, " +
                        "y que la IP/puerto sean correctos. Puede editar manualmente " +
                        "'OpticentroZKTeco.Service.exe.config' si es necesario.",
                        MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                // Blindaje total: Commit nunca debe relanzar ni revertir la instalación.
                MessageBoxHelper.Mostrar(
                    "Instalación completada. El servicio quedó instalado, pero ocurrió " +
                    $"un error inesperado al intentar probar la conexión: {ex.Message}",
                    MessageBoxIcon.Warning);
            }
        }
    }
}
