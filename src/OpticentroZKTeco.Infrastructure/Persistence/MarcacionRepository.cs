using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Web.Script.Serialization;
using OpticentroZKTeco.Domain.Entities;
using OpticentroZKTeco.Domain.Interfaces;

namespace OpticentroZKTeco.Infrastructure.Persistence
{
    public class MarcacionRepository : IMarcacionRepository
    {
        private readonly string _endpointUrl;
        private readonly string _apiKey;
        private readonly ILogger _logger;
        private readonly TimeSpan _timeout;

        public MarcacionRepository(string endpointUrl, string apiKey, ILogger logger, TimeSpan? timeout = null)
        {
            _endpointUrl = endpointUrl;
            _apiKey = apiKey;
            _logger = logger;
            _timeout = timeout ?? TimeSpan.FromSeconds(30);
        }

        // El endpoint del ERP debe hacer upsert por (usuarioId, fecha, tipoMarca):
        // este lote puede reenviarse completo si la PC reinicia varias veces el mismo dia.
        public void GuardarLote(IReadOnlyList<Marcacion> marcaciones)
        {
            var payload = marcaciones.Select(m => new
            {
                usuarioId = m.UsuarioId,
                verificacion = m.Verificacion.ToString(),
                tipoMarca = m.TipoMarca.ToString(),
                fecha = m.Fecha.ToString("yyyy-MM-ddTHH:mm:ss")
            }).ToList();

            var serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(payload);

            using (var client = new HttpClient { Timeout = _timeout })
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            {
                if (!string.IsNullOrEmpty(_apiKey))
                {
                    client.DefaultRequestHeaders.Add("X-Api-Key", _apiKey);
                }

                HttpResponseMessage respuesta = client.PostAsync(_endpointUrl, content).GetAwaiter().GetResult();
                int statusCode = (int)respuesta.StatusCode;
                string responseBody = respuesta.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (respuesta.IsSuccessStatusCode)
                {
                    _logger?.Info($"ERP respondio {statusCode} al guardar el lote de {marcaciones.Count} marcaciones: {ResumirRespuesta(responseBody)}");
                }
                else
                {
                    _logger?.Error($"ERP respondio {statusCode} al guardar el lote en {_endpointUrl}. Respuesta: {responseBody}");
                }

                respuesta.EnsureSuccessStatusCode();
            }
        }

        // Resume los campos conocidos de la respuesta del ERP (total/procesadas/duplicadas/
        // descartadasPorIntervalo/errores); si la forma cambia o no es JSON valido, cae al body crudo.
        private static string ResumirRespuesta(string responseBody)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var datos = serializer.DeserializeObject(responseBody) as Dictionary<string, object>;
                if (datos == null)
                {
                    return responseBody;
                }

                string Valor(string clave) => datos.ContainsKey(clave) ? datos[clave]?.ToString() : "?";

                return $"total={Valor("total")}, procesadas={Valor("procesadas")}, " +
                       $"duplicadas={Valor("duplicadas")}, descartadasPorIntervalo={Valor("descartadasPorIntervalo")}, " +
                       $"errores={Valor("errores")}";
            }
            catch
            {
                return responseBody;
            }
        }
    }
}
