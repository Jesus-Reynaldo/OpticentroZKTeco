using System;
using System.Runtime.InteropServices;
using OpticentroZKTeco.Domain.Interfaces;

namespace OpticentroZKTeco.Infrastructure.Notificaciones
{
    // Muestra un MessageBox nativo en la sesion de escritorio activa desde un
    // Windows Service (que corre en la sesion 0, sin UI propia) via WTSSendMessage.
    // Si no hay nadie logueado en ese momento, simplemente no se muestra nada.
    public class AvisoVisualWts : IAvisoVisual
    {
        private const int MbIconInformation = 0x40;
        private const int TimeoutSegundos = 30;

        private readonly ILogger _logger;

        public AvisoVisualWts(ILogger logger)
        {
            _logger = logger;
        }

        public void Mostrar(string titulo, string mensaje)
        {
            try
            {
                int sessionId = WTSGetActiveConsoleSessionId();
                if (sessionId == -1)
                {
                    _logger?.Info("No hay sesion de escritorio activa; se omite el aviso visual.");
                    return;
                }

                WTSSendMessage(
                    IntPtr.Zero,
                    sessionId,
                    titulo,
                    titulo.Length,
                    mensaje,
                    mensaje.Length,
                    MbIconInformation,
                    TimeoutSegundos,
                    out int _,
                    false);
            }
            catch (Exception ex)
            {
                _logger?.Error($"No se pudo mostrar el aviso visual: {ex.Message}");
            }
        }

        [DllImport("kernel32.dll")]
        private static extern int WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool WTSSendMessage(
            IntPtr hServer,
            int sessionId,
            string pTitle,
            int titleLength,
            string pMessage,
            int messageLength,
            int style,
            int timeout,
            out int response,
            bool bWait);
    }
}
