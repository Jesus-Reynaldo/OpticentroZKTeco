using System;
using System.Globalization;
using System.ServiceProcess;

namespace OpticentroZKTeco.Service
{
    internal static class Program
    {
        private const string FormatoFecha = "yyyy-MM-dd";

        private static int Main(string[] args)
        {
            if (Environment.UserInteractive)
            {
                return EjecutarEnConsola(args);
            }

            ServiceBase.Run(new ServiceBase[] { new AsistenciaService() });
            return 0;
        }

        // Uso:
        //   OpticentroZKTeco.Service.exe                                       -> sincronizacion automatica
        //   OpticentroZKTeco.Service.exe --desde 2026-09-26                    -> del 26/09 hasta ayer
        //   OpticentroZKTeco.Service.exe --desde 2026-09-26 --hasta 2026-09-26 -> solo el 26/09
        private static int EjecutarEnConsola(string[] args)
        {
            string desdeTexto = ObtenerArgumento(args, "--desde");
            string hastaTexto = ObtenerArgumento(args, "--hasta");

            if (desdeTexto == null && hastaTexto == null)
            {
                Console.WriteLine("Ejecutando en modo consola (sin instalar el servicio)...");
                CompositionRoot.CrearDependencias().UseCase.Ejecutar();
                Console.WriteLine("Listo. Revise el log configurado en LogFilePath.");
                return 0;
            }

            if (!TryParseFecha(desdeTexto, out DateTime desde))
            {
                return MostrarUso($"Falta --desde o no tiene el formato {FormatoFecha}: '{desdeTexto}'.");
            }

            DateTime hasta = DateTime.Today.AddDays(-1);
            if (hastaTexto != null && !TryParseFecha(hastaTexto, out hasta))
            {
                return MostrarUso($"--hasta no tiene el formato {FormatoFecha}: '{hastaTexto}'.");
            }

            if (desde > hasta)
            {
                return MostrarUso($"--desde ({desde:yyyy-MM-dd}) no puede ser posterior a --hasta ({hasta:yyyy-MM-dd}).");
            }

            if (hasta > DateTime.Today)
            {
                return MostrarUso($"--hasta ({hasta:yyyy-MM-dd}) no puede ser una fecha futura.");
            }

            Console.WriteLine($"Sincronizacion manual del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}...");
            CompositionRoot.CrearDependencias().UseCase.EjecutarManual(desde, hasta);
            Console.WriteLine("Listo. Revise el log configurado en LogFilePath.");
            return 0;
        }

        private static string ObtenerArgumento(string[] args, string nombre)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], nombre, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static bool TryParseFecha(string texto, out DateTime fecha)
        {
            return DateTime.TryParseExact(texto, FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha);
        }

        private static int MostrarUso(string error)
        {
            Console.WriteLine(error);
            Console.WriteLine();
            Console.WriteLine("Uso:");
            Console.WriteLine("  OpticentroZKTeco.Service.exe                                        sincronizacion automatica");
            Console.WriteLine("  OpticentroZKTeco.Service.exe --desde AAAA-MM-DD                     desde esa fecha hasta ayer");
            Console.WriteLine("  OpticentroZKTeco.Service.exe --desde AAAA-MM-DD --hasta AAAA-MM-DD  rango especifico");
            return 1;
        }
    }
}
