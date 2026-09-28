using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;

namespace OpticentroZKTeco.Setup.Actions
{
    [RunInstaller(true)]
    public class SetupActionsInstaller : Installer
    {
        public override void Install(IDictionary savedState)
        {
            base.Install(savedState);

            string action = Context.Parameters["opaction"];
            switch (action)
            {
                case "config":
                    ConfigWriter.EscribirConfig(
                        Context.Parameters["targetdir"],
                        Context.Parameters["ip"],
                        Context.Parameters["puerto"],
                        Context.Parameters["hora"]);
                    break;

                default:
                    throw new InstallException(
                        $"Custom Action 'Install' invocada con /action desconocido: '{action}'.");
            }
        }

        public override void Commit(IDictionary savedState)
        {
            base.Commit(savedState);

            string action = Context.Parameters["opaction"];
            if (action == "testconnection")
            {
                // Nunca debe relanzar: un fallo aquí NO debe revertir la instalación.
                ConexionTester.ProbarConexionSegura(
                    Context.Parameters["ip"],
                    Context.Parameters["puerto"]);
            }
        }

        public override void Rollback(IDictionary savedState)
        {
            base.Rollback(savedState);
        }

        public override void Uninstall(IDictionary savedState)
        {
            base.Uninstall(savedState);
        }
    }
}
