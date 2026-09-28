using System.Windows.Forms;

namespace OpticentroZKTeco.Setup.Actions
{
    internal static class MessageBoxHelper
    {
        public static void Mostrar(string mensaje, MessageBoxIcon icono)
        {
            MessageBox.Show(
                mensaje,
                "Opticentro ZKTeco - Instalador",
                MessageBoxButtons.OK,
                icono,
                MessageBoxDefaultButton.Button1,
                MessageBoxOptions.DefaultDesktopOnly);
        }

        public static void MostrarError(string mensaje)
        {
            Mostrar(mensaje, MessageBoxIcon.Error);
        }
    }
}
