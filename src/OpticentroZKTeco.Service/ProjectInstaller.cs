using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace OpticentroZKTeco.Service
{
    [RunInstaller(true)]
    public class ProjectInstaller : Installer
    {
        public ProjectInstaller()
        {
            var processInstaller = new ServiceProcessInstaller
            {
                Account = ServiceAccount.LocalSystem
            };

            var serviceInstaller = new ServiceInstaller
            {
                ServiceName = "OpticentroZKTeco",
                DisplayName = "Opticentro ZKTeco - Sincronizacion de Asistencia",
                StartType = ServiceStartMode.Automatic
            };

            Installers.Add(processInstaller);
            Installers.Add(serviceInstaller);
        }
    }
}
