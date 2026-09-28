using System;
using System.IO;
using OpticentroZKTeco.Domain.Interfaces;

namespace OpticentroZKTeco.Infrastructure.Logging
{
    public class FileLogger : ILogger
    {
        private readonly string _rutaArchivo;

        public FileLogger(string rutaArchivo)
        {
            _rutaArchivo = rutaArchivo;
        }

        public void Info(string mensaje)
        {
            Escribir("INFO", mensaje);
        }

        public void Error(string mensaje)
        {
            Escribir("ERROR", mensaje);
        }

        private void Escribir(string nivel, string mensaje)
        {
            try
            {
                string directorio = Path.GetDirectoryName(_rutaArchivo);
                if (!string.IsNullOrEmpty(directorio) && !Directory.Exists(directorio))
                {
                    Directory.CreateDirectory(directorio);
                }

                string linea = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{nivel}] {mensaje}{Environment.NewLine}";
                File.AppendAllText(_rutaArchivo, linea);
            }
            catch
            {
                // Un fallo de logging nunca debe tumbar el servicio.
            }
        }
    }
}
