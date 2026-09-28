using System;
using System.Configuration.Install;
using System.Globalization;
using System.IO;
using System.Net;
using System.Xml;

namespace OpticentroZKTeco.Setup.Actions
{
    internal static class ConfigWriter
    {
        public static void EscribirConfig(string targetDir, string ip, string puertoTexto, string horaTexto)
        {
            // Trim: CustomActionData le agrega un espacio final a propósito para evitar
            // que el "\" final de [TARGETDIR] escape la comilla de cierre del argumento.
            targetDir = targetDir?.Trim();

            if (!IPAddress.TryParse(ip, out _))
            {
                MessageBoxHelper.MostrarError(
                    $"La dirección IP ingresada ('{ip}') no es válida.\n\n" +
                    "La instalación se cancelará. Vuelva a ejecutar el instalador " +
                    "e ingrese una dirección IP válida (ej. 192.168.1.201).");
                throw new InstallException($"IP inválida: '{ip}'.");
            }

            if (!int.TryParse(puertoTexto, out int puerto) || puerto <= 0 || puerto > 65535)
            {
                MessageBoxHelper.MostrarError(
                    $"El puerto ingresado ('{puertoTexto}') no es válido.\n\n" +
                    "La instalación se cancelará. Vuelva a ejecutar el instalador " +
                    "e ingrese un puerto numérico entre 1 y 65535 (ej. 4370).");
                throw new InstallException($"Puerto inválido: '{puertoTexto}'.");
            }

            if (!TimeSpan.TryParseExact(horaTexto, @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan hora))
            {
                MessageBoxHelper.MostrarError(
                    $"La hora de sincronización ingresada ('{horaTexto}') no es válida.\n\n" +
                    "La instalación se cancelará. Vuelva a ejecutar el instalador " +
                    "e ingrese una hora en formato HH:mm (ej. 10:00).");
                throw new InstallException($"Hora de sincronización inválida: '{horaTexto}'.");
            }

            string configPath = Path.Combine(targetDir, "OpticentroZKTeco.Service.exe.config");
            if (!File.Exists(configPath))
            {
                MessageBoxHelper.MostrarError(
                    $"No se encontró el archivo de configuración esperado:\n{configPath}");
                throw new InstallException($"No se encontró '{configPath}'.");
            }

            var doc = new XmlDocument();
            doc.Load(configPath);

            SetAppSetting(doc, "ZktecoIp", ip);
            SetAppSetting(doc, "ZktecoPuerto", puerto.ToString());
            SetAppSetting(doc, "HoraSincronizacion", hora.ToString(@"hh\:mm"));

            doc.Save(configPath);
        }

        private static void SetAppSetting(XmlDocument doc, string key, string value)
        {
            var node = doc.SelectSingleNode($"//appSettings/add[@key='{key}']") as XmlElement;
            if (node == null)
            {
                throw new InstallException($"No se encontró appSettings key='{key}' en el config.");
            }

            node.SetAttribute("value", value);
        }
    }
}
