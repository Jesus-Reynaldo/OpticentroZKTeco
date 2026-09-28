using System.Configuration;
using OpticentroZKTeco.Application.UseCases;
using OpticentroZKTeco.Domain.Interfaces;
using OpticentroZKTeco.Infrastructure.Logging;
using OpticentroZKTeco.Infrastructure.Notificaciones;
using OpticentroZKTeco.Infrastructure.Persistence;
using OpticentroZKTeco.Infrastructure.Zkteco;

namespace OpticentroZKTeco.Service
{
    public class Dependencias
    {
        public SincronizarMarcacionesUseCase UseCase { get; set; }
        public ILogger Logger { get; set; }
    }

    public static class CompositionRoot
    {
        public static Dependencias CrearDependencias()
        {
            string ip = ConfigurationManager.AppSettings["ZktecoIp"];
            int puerto = int.Parse(ConfigurationManager.AppSettings["ZktecoPuerto"]);
            int machineNumber = ObtenerEnteroODefecto("ZktecoMachineNumber", 1);
            int? password = ObtenerEnteroOpcional("ZktecoPassword");
            string erpEndpointUrl = ConfigurationManager.AppSettings["ErpEndpointUrl"];
            string erpApiKey = ConfigurationManager.AppSettings["ErpApiKey"];
            string logFilePath = ConfigurationManager.AppSettings["LogFilePath"];
            string marcacionesFolderPath = ConfigurationManager.AppSettings["MarcacionesFolderPath"];
            string marcacionesMarcadorPath = ConfigurationManager.AppSettings["MarcacionesMarcadorPath"];
            int diasRecuperacion = ObtenerEnteroODefecto("DiasRecuperacion", SincronizarMarcacionesUseCase.DiasRecuperacionPorDefecto);

            ILogger logger = new FileLogger(logFilePath);
            IDispositivoAsistencia dispositivo = new ZktecoDeviceClient(ip, puerto, machineNumber, password);
            IMarcacionRepository repositorioErp = new MarcacionRepository(erpEndpointUrl, erpApiKey, logger);
            IMarcacionRepository repositorio = new MarcacionRepositoryConRespaldoLocal(repositorioErp, marcacionesFolderPath, logger);
            ISincronizacionMarcador marcador = new ArchivoSincronizacionMarcador(marcacionesMarcadorPath, logger);
            IAvisoVisual aviso = new AvisoVisualWts(logger);

            var useCase = new SincronizarMarcacionesUseCase(dispositivo, repositorio, logger, marcador, aviso, diasRecuperacion);

            return new Dependencias { UseCase = useCase, Logger = logger };
        }

        private static int ObtenerEnteroODefecto(string key, int valorPorDefecto)
        {
            string valor = ConfigurationManager.AppSettings[key];
            return string.IsNullOrEmpty(valor) ? valorPorDefecto : int.Parse(valor);
        }

        private static int? ObtenerEnteroOpcional(string key)
        {
            string valor = ConfigurationManager.AppSettings[key];
            return string.IsNullOrEmpty(valor) ? (int?)null : int.Parse(valor);
        }
    }
}
