using System;
using System.Collections.Generic;
using System.Linq;
using OpticentroZKTeco.Domain.Entities;
using OpticentroZKTeco.Domain.Interfaces;

namespace OpticentroZKTeco.Application.UseCases
{
    public class SincronizarMarcacionesUseCase
    {
        public const int DiasRecuperacionPorDefecto = 7;

        private readonly IDispositivoAsistencia _dispositivo;
        private readonly IMarcacionRepository _repositorio;
        private readonly ILogger _logger;
        private readonly ISincronizacionMarcador _marcador;
        private readonly IAvisoVisual _aviso;
        private readonly int _diasRecuperacion;

        public SincronizarMarcacionesUseCase(
            IDispositivoAsistencia dispositivo,
            IMarcacionRepository repositorio,
            ILogger logger,
            ISincronizacionMarcador marcador,
            IAvisoVisual aviso,
            int diasRecuperacion = DiasRecuperacionPorDefecto)
        {
            _dispositivo = dispositivo;
            _repositorio = repositorio;
            _logger = logger;
            _marcador = marcador;
            _aviso = aviso;
            _diasRecuperacion = Math.Max(1, diasRecuperacion);
        }

        // Automatico: envia ayer y cualquier dia de los ultimos _diasRecuperacion que no
        // tenga marcador (PC apagada, sin red, ERP caido...). Las marcaciones siguen en el
        // reloj y el ERP hace upsert, asi que reenviar un dia es inofensivo.
        public void Ejecutar()
        {
            try
            {
                if (_marcador.YaSincronizadoHoy())
                {
                    _logger.Info("Ya se sincronizo hoy, no se vuelve a intentar.");
                    return;
                }

                DateTime ayer = DateTime.Today.AddDays(-1);
                var yaSincronizadas = new HashSet<DateTime>(_marcador.ObtenerFechasSincronizadas());

                List<DateTime> pendientes = Enumerable.Range(0, _diasRecuperacion)
                    .Select(i => ayer.AddDays(-i))
                    .Where(f => !yaSincronizadas.Contains(f))
                    .OrderBy(f => f)
                    .ToList();

                if (pendientes.Count == 0)
                {
                    _logger.Info("No hay dias pendientes de sincronizar.");
                    _marcador.MarcarComoSincronizado(pendientes, new List<Marcacion>(), esManual: false);
                    return;
                }

                Sincronizar(pendientes, esManual: false);
            }
            catch (Exception ex)
            {
                _logger.Error($"Fallo inesperado durante la sincronizacion: {ex.Message}");
            }
        }

        // Manual: reenvia el rango indicado sin importar si ya se sincronizo hoy.
        public void EjecutarManual(DateTime desde, DateTime hasta)
        {
            try
            {
                List<DateTime> fechas = Enumerable.Range(0, (hasta.Date - desde.Date).Days + 1)
                    .Select(i => desde.Date.AddDays(i))
                    .ToList();

                _logger.Info($"Sincronizacion manual solicitada del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}.");
                Sincronizar(fechas, esManual: true);
            }
            catch (Exception ex)
            {
                _logger.Error($"Fallo inesperado durante la sincronizacion manual: {ex.Message}");
            }
        }

        private void Sincronizar(IReadOnlyList<DateTime> fechas, bool esManual)
        {
            bool conectado = _dispositivo.Conectar();
            if (!conectado)
            {
                _logger.Error("No se pudo conectar al dispositivo de asistencia.");
                return;
            }

            try
            {
                IReadOnlyList<Marcacion> todas = _dispositivo.DescargarMarcaciones();

                var fechasBuscadas = new HashSet<DateTime>(fechas);
                List<Marcacion> seleccionadas = todas.Where(m => fechasBuscadas.Contains(m.Fecha.Date)).ToList();
                string descripcion = DescribirFechas(fechas);

                _logger.Info($"{todas.Count} registros totales, {seleccionadas.Count} {descripcion}");

                if (seleccionadas.Count > 0)
                {
                    _repositorio.GuardarLote(seleccionadas);
                    _aviso.Mostrar(
                        "OpticentroZKTeco - Sincronizacion",
                        $"Se sincronizaron {seleccionadas.Count} marcaciones {descripcion} con el ERP.");
                }

                _marcador.MarcarComoSincronizado(fechas, seleccionadas, esManual);
            }
            finally
            {
                _dispositivo.Desconectar();
            }
        }

        private static string DescribirFechas(IReadOnlyList<DateTime> fechas)
        {
            return fechas.Count == 1
                ? $"del dia {fechas[0]:dd/MM/yyyy}"
                : $"de los dias {string.Join(", ", fechas.Select(f => f.ToString("dd/MM/yyyy")))}";
        }
    }
}
