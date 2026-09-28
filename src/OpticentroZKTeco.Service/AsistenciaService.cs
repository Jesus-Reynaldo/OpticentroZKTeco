using System;
using System.Configuration;
using System.ServiceProcess;
using System.Timers;
using OpticentroZKTeco.Application.UseCases;
using OpticentroZKTeco.Domain.Interfaces;

namespace OpticentroZKTeco.Service
{
    public class AsistenciaService : ServiceBase
    {
        private static readonly TimeSpan HoraDisparoPorDefecto = new TimeSpan(10, 0, 0);

        private readonly object _lock = new object();

        private SincronizarMarcacionesUseCase _useCase;
        private ILogger _logger;
        private Timer _timer;
        private TimeSpan _horaDisparo;

        public AsistenciaService()
        {
            ServiceName = "OpticentroZKTeco";
        }

        protected override void OnStart(string[] args)
        {
            Dependencias dependencias = CompositionRoot.CrearDependencias();
            _useCase = dependencias.UseCase;
            _logger = dependencias.Logger;
            _horaDisparo = ObtenerHoraSincronizacion();

            ProgramarProximaEjecucion(ejecutarInmediatamenteSiYaPaso: true);
        }

        private TimeSpan ObtenerHoraSincronizacion()
        {
            string valor = ConfigurationManager.AppSettings["HoraSincronizacion"];
            if (TimeSpan.TryParse(valor, out TimeSpan hora))
            {
                return hora;
            }

            _logger?.Error($"HoraSincronizacion inválida o ausente en config ('{valor}'); usando valor por defecto {HoraDisparoPorDefecto}.");
            return HoraDisparoPorDefecto;
        }

        protected override void OnStop()
        {
            lock (_lock)
            {
                DetenerTimer();
            }
        }

        private void ProgramarProximaEjecucion(bool ejecutarInmediatamenteSiYaPaso)
        {
            DateTime ahora = DateTime.Now;
            DateTime disparoDeHoy = ahora.Date + _horaDisparo;

            if (ejecutarInmediatamenteSiYaPaso && ahora >= disparoDeHoy)
            {
                EjecutarYReprogramar();
                return;
            }

            DateTime proximoDisparo = ahora < disparoDeHoy ? disparoDeHoy : disparoDeHoy.AddDays(1);
            TimeSpan intervalo = proximoDisparo - ahora;

            lock (_lock)
            {
                DetenerTimer();
                _timer = new Timer(intervalo.TotalMilliseconds) { AutoReset = false };
                _timer.Elapsed += Timer_Elapsed;
                _timer.Start();
            }

            _logger?.Info($"Proxima sincronizacion programada para {proximoDisparo:yyyy-MM-dd HH:mm:ss}.");
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            EjecutarYReprogramar();
        }

        private void EjecutarYReprogramar()
        {
            try
            {
                _useCase.Ejecutar();
            }
            catch (Exception ex)
            {
                _logger?.Error($"Fallo inesperado al ejecutar la sincronizacion programada: {ex.Message}");
            }
            finally
            {
                ProgramarProximaEjecucion(ejecutarInmediatamenteSiYaPaso: false);
            }
        }

        private void DetenerTimer()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Elapsed -= Timer_Elapsed;
                _timer.Dispose();
                _timer = null;
            }
        }
    }
}
