using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using OpticentroZKTeco.Domain.Entities;
using OpticentroZKTeco.Domain.Interfaces;

namespace OpticentroZKTeco.Infrastructure.Persistence
{
    // Marcador de idempotencia diaria: {yyyy-MM-dd}.json nombrado por la fecha de
    // EJECUCION. Carpeta separada de MarcacionesFolderPath (respaldo ERP, nombrado
    // por la fecha de los DATOS) para evitar colisiones entre ambos archivos.
    // Las ejecuciones manuales se registran como manual_{yyyy-MM-dd_HHmmss}.json para
    // no alterar YaSincronizadoHoy, pero sus fechas si cuentan como sincronizadas.
    public class ArchivoSincronizacionMarcador : ISincronizacionMarcador
    {
        private const string FormatoFecha = "yyyy-MM-dd";

        private readonly string _carpeta;
        private readonly ILogger _logger;

        public ArchivoSincronizacionMarcador(string carpeta, ILogger logger)
        {
            _carpeta = carpeta;
            _logger = logger;
        }

        public bool YaSincronizadoHoy()
        {
            try
            {
                return File.Exists(RutaDeHoy());
            }
            catch (Exception ex)
            {
                _logger?.Error($"No se pudo verificar el marcador de sincronizacion: {ex.Message}");
                return false;
            }
        }

        // Lee todos los marcadores: "fechasDatos" (formato actual) o "fechaDatos"
        // (marcadores anteriores, un solo dia). Un archivo ilegible se ignora.
        public IReadOnlyCollection<DateTime> ObtenerFechasSincronizadas()
        {
            var fechas = new HashSet<DateTime>();

            try
            {
                if (!Directory.Exists(_carpeta))
                {
                    return fechas;
                }

                var serializer = new JavaScriptSerializer();

                foreach (string ruta in Directory.GetFiles(_carpeta, "*.json"))
                {
                    try
                    {
                        var datos = serializer.DeserializeObject(File.ReadAllText(ruta)) as Dictionary<string, object>;
                        if (datos == null)
                        {
                            continue;
                        }

                        if (datos.TryGetValue("fechasDatos", out object lista) && lista is object[] valores)
                        {
                            foreach (object valor in valores)
                            {
                                AgregarFecha(fechas, valor);
                            }
                        }
                        else if (datos.TryGetValue("fechaDatos", out object unica))
                        {
                            AgregarFecha(fechas, unica);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error($"No se pudo leer el marcador {ruta}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"No se pudieron leer los marcadores de sincronizacion: {ex.Message}");
            }

            return fechas;
        }

        public void MarcarComoSincronizado(IReadOnlyList<DateTime> fechasDatos, IReadOnlyList<Marcacion> marcaciones, bool esManual)
        {
            string ruta = esManual
                ? Path.Combine(_carpeta, $"manual_{DateTime.Now:yyyy-MM-dd_HHmmss}.json")
                : RutaDeHoy();

            try
            {
                if (!Directory.Exists(_carpeta))
                {
                    Directory.CreateDirectory(_carpeta);
                }

                var payload = new
                {
                    fechaEjecucion = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    manual = esManual,
                    fechasDatos = fechasDatos.OrderBy(f => f).Select(f => f.ToString(FormatoFecha)).ToList(),
                    cantidadMarcaciones = marcaciones.Count,
                    usuariosIds = marcaciones.Select(m => m.UsuarioId).Distinct().ToList()
                };

                var serializer = new JavaScriptSerializer();
                File.WriteAllText(ruta, serializer.Serialize(payload));

                _logger?.Info($"Marcador de sincronizacion escrito en {ruta}");
            }
            catch (Exception ex)
            {
                _logger?.Error($"No se pudo escribir el marcador de sincronizacion: {ex.Message}");
            }
        }

        private static void AgregarFecha(HashSet<DateTime> fechas, object valor)
        {
            if (DateTime.TryParseExact(valor as string, FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fecha))
            {
                fechas.Add(fecha);
            }
        }

        private string RutaDeHoy() => Path.Combine(_carpeta, $"{DateTime.Today:yyyy-MM-dd}.json");
    }
}
