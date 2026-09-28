using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using OpticentroZKTeco.Domain.Entities;
using OpticentroZKTeco.Domain.Interfaces;

namespace OpticentroZKTeco.Infrastructure.Persistence
{
    // Decorator: antes de delegar el guardado real al repositorio interno (ERP),
    // escribe un respaldo local en JSON, un archivo por cada Fecha.Date distinta
    // presente en el lote. Un fallo al escribir el respaldo nunca bloquea el envio
    // al ERP, y viceversa: cada paso se protege por separado.
    public class MarcacionRepositoryConRespaldoLocal : IMarcacionRepository
    {
        private readonly IMarcacionRepository _interno;
        private readonly string _carpetaDestino;
        private readonly ILogger _logger;

        public MarcacionRepositoryConRespaldoLocal(IMarcacionRepository interno, string carpetaDestino, ILogger logger)
        {
            _interno = interno;
            _carpetaDestino = carpetaDestino;
            _logger = logger;
        }

        public void GuardarLote(IReadOnlyList<Marcacion> marcaciones)
        {
            GuardarRespaldoLocal(marcaciones);
            _interno.GuardarLote(marcaciones);
        }

        private void GuardarRespaldoLocal(IReadOnlyList<Marcacion> marcaciones)
        {
            try
            {
                if (!Directory.Exists(_carpetaDestino))
                {
                    Directory.CreateDirectory(_carpetaDestino);
                }

                var serializer = new JavaScriptSerializer();

                foreach (var grupoPorDia in marcaciones.GroupBy(m => m.Fecha.Date))
                {
                    var payload = grupoPorDia.Select(m => new
                    {
                        usuarioId = m.UsuarioId,
                        verificacion = m.Verificacion.ToString(),
                        tipoMarca = m.TipoMarca.ToString(),
                        fecha = m.Fecha.ToString("yyyy-MM-ddTHH:mm:ss")
                    }).ToList();

                    string nombreArchivo = grupoPorDia.Key.ToString("yyyy-MM-dd") + ".json";
                    string rutaArchivo = Path.Combine(_carpetaDestino, nombreArchivo);
                    string json = serializer.Serialize(payload);

                    File.WriteAllText(rutaArchivo, json);
                }

                _logger?.Info($"Respaldo local de {marcaciones.Count} marcaciones guardado en {_carpetaDestino}");
            }
            catch (Exception ex)
            {
                _logger?.Error($"No se pudo guardar el respaldo local de marcaciones: {ex.Message}");
            }
        }
    }
}
