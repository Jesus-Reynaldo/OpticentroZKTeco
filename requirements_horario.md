# requirements.md — OpticentroZKTeco: cambio de disparo a horario fijo (10:00 AM)

Este documento es un **addendum** al `requirements.md` ya aplicado. No repite
lo que no cambia (arquitectura por capas, `ZktecoDeviceClient`,
`ErpHttpMarcacionRepository`, contrato del endpoint) — solo describe el nuevo
comportamiento de disparo de `AsistenciaService`, que reemplaza al "corre una
vez al iniciar el servicio".

## 1. Cambio de comportamiento

- **Antes**: la sincronización corría una sola vez, en `OnStart`, apenas
  arrancaba el servicio (que a su vez arrancaba al prender la PC).
- **Ahora**: la sincronización debe dispararse **todos los días a las 10:00
  AM, hora del sistema operativo**, sin importar a qué hora se prendió la PC
  o se inició el servicio. En ese momento debe enviar la información del
  **día anterior** (el filtro `Fecha.Date == DateTime.Today.AddDays(-1)` que
  ya está en `SincronizarMarcacionesUseCase` se mantiene igual).
- El servicio ahora debe **quedar residente**, esperando la hora exacta, en
  vez de ejecutar y terminar su tarea inmediatamente en `OnStart`.

## 2. Casos a cubrir por el disparador

1. **La PC ya estaba prendida antes de las 10:00 AM** (el servicio arrancó,
   por ejemplo, a las 8:00 AM): debe esperar hasta las 10:00 AM de ese mismo
   día y disparar en ese momento.
2. **La PC se prende después de las 10:00 AM** (ej. el servicio arranca a
   las 11:30 AM): la sincronización de ese día **ya no se puede ejecutar a
   las 10:00 exactas** porque ya pasó. Comportamiento esperado: ejecutar
   inmediatamente al detectar que la hora de hoy ya pasó las 10:00 y aún no
   se sincronizó hoy, en vez de esperar hasta mañana. (Ver sección 5 para el
   control de "ya sincronizado hoy".)
3. **El servicio sigue corriendo más de un día** (la PC queda prendida
   varios días seguidos sin reiniciarse): debe volver a dispararse cada
   10:00 AM, todos los días, sin necesidad de reiniciar el servicio.
4. **La PC se apaga antes de las 10:00 AM y se prende recién al día
   siguiente**: se pierde la sincronización de ese día (riesgo ya conocido y
   aceptado — mismo criterio que en el requirements original, sección 14).

## 3. Diseño técnico

`AsistenciaService` vuelve a usar un `System.Timers.Timer`, pero ya no con un
intervalo fijo tipo "cada 5 minutos" — ahora se recalcula dinámicamente el
tiempo exacto hasta la próxima ocurrencia de las 10:00 AM, y el timer es de
un solo disparo (`AutoReset = false`) que se reprograma a sí mismo después de
cada ejecución.

```csharp
using System;
using System.Timers;
using System.ServiceProcess;
using OpticentroZKTeco.Application.UseCases;
using OpticentroZKTeco.Domain.Interfaces;

namespace OpticentroZKTeco.Service
{
    public partial class AsistenciaService : ServiceBase
    {
        private static readonly TimeSpan HoraDisparo = new TimeSpan(10, 0, 0); // 10:00 AM

        private Timer _timer;
        private SincronizarMarcacionesUseCase _useCase;
        private ILogger _logger;

        public AsistenciaService()
        {
            InitializeComponent();
            ServiceName = "OpticentroZKTeco";
        }

        protected override void OnStart(string[] args)
        {
            var deps = CompositionRoot.CrearDependencias();
            _useCase = deps.UseCase;
            _logger = deps.Logger;

            ProgramarProximaEjecucion(ejecutarInmediatamenteSiYaPaso: true);
        }

        protected override void OnStop()
        {
            _timer?.Stop();
            _timer?.Dispose();
        }

        private void ProgramarProximaEjecucion(bool ejecutarInmediatamenteSiYaPaso)
        {
            var ahora = DateTime.Now;
            var disparoDeHoy = ahora.Date + HoraDisparo;

            if (ejecutarInmediatamenteSiYaPaso && ahora >= disparoDeHoy)
            {
                // Ya pasaron las 10:00 de hoy y el servicio recién arranca:
                // corre ya mismo en vez de esperar hasta mañana.
                EjecutarYReprogramar();
                return;
            }

            var proximoDisparo = ahora >= disparoDeHoy
                ? disparoDeHoy.AddDays(1)  // ya pasó hoy -> mañana a las 10:00
                : disparoDeHoy;             // todavía no llega hoy -> hoy a las 10:00

            var intervalo = proximoDisparo - ahora;

            _timer = new Timer(intervalo.TotalMilliseconds);
            _timer.AutoReset = false;
            _timer.Elapsed += (s, e) => EjecutarYReprogramar();
            _timer.Start();

            _logger.Info($"Próxima sincronización programada para {proximoDisparo:yyyy-MM-dd HH:mm}.");
        }

        private void EjecutarYReprogramar()
        {
            try
            {
                _useCase.Ejecutar();
            }
            catch (Exception ex)
            {
                _logger.Error($"Fallo no controlado durante la sincronización: {ex.Message}");
            }
            finally
            {
                // siempre se reprograma para la próxima ocurrencia (mañana a las 10:00),
                // sin ejecutar inmediato esta vez, ya que la de hoy recién se hizo.
                ProgramarProximaEjecucion(ejecutarInmediatamenteSiYaPaso: false);
            }
        }
    }
}
```

Notas de diseño:

- `Timer` de .NET puede desviarse unos milisegundos/segundos en esperas
  largas (horas), pero para este caso de uso (precisión de minutos, no de
  milisegundos) es más que suficiente.
- El `try/catch` alrededor de `_useCase.Ejecutar()` es una capa extra de
  seguridad: aunque el caso de uso ya maneja sus propios errores
  internamente (sección 14 del requirements original), una excepción no
  prevista aquí no debe matar el timer ni dejar de reprogramar el siguiente
  disparo.

## 4. Cambio en `CompositionRoot`

Como el timer ahora vive en `AsistenciaService` y necesita también el
`ILogger` (para loguear cuándo quedó programado el próximo disparo), conviene
que `CompositionRoot` devuelva un pequeño contenedor con ambas dependencias,
en vez de solo el caso de uso:

```csharp
public class Dependencias
{
    public SincronizarMarcacionesUseCase UseCase { get; set; }
    public ILogger Logger { get; set; }
}

public static Dependencias CrearDependencias()
{
    // arma ILogger, IDispositivoAsistencia, IMarcacionRepository, UseCase
    // igual que antes, y devuelve ambos en el contenedor
}
```

## 5. Control de "ya sincronizado hoy" — marcador por archivo

En vez de depender solo del índice único de Mongo (que evita duplicados en
el ERP, pero no evita que el servicio intente conectarse al K30 innecesariamente),
se agrega un **marcador local en disco**: después de cada sincronización
exitosa, se escribe un archivo `{yyyy-MM-dd}.json` (fecha de **hoy**, el día
en que corrió el proceso) en:

```
C:\ProgramData\OpticentroZKTeco\marcaciones\
```

Ejemplo: si el proceso corre el 15/09/2026 y sincroniza los registros del
14/09/2026, el archivo que se crea es `2026-09-15.json` (nombrado por la
fecha de ejecución, no por la fecha de los datos).

Contenido del archivo (para auditoría/debug, no solo un marcador vacío):
```json
{
  "fechaEjecucion": "2026-09-15T10:00:04",
  "fechaDatos": "2026-09-14",
  "cantidadMarcaciones": 3,
  "usuariosIds": ["1023", "1045"]
}
```

### 5.1 Nueva interfaz en Domain

```csharp
namespace OpticentroZKTeco.Domain.Interfaces
{
    public interface ISincronizacionMarcador
    {
        bool YaSincronizadoHoy();
        void MarcarComoSincronizado(DateTime fechaDatos, IReadOnlyList<Marcacion> marcaciones);
    }
}
```

### 5.2 Implementación en Infrastructure

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using OpticentroZKTeco.Domain.Entities;
using OpticentroZKTeco.Domain.Interfaces;

namespace OpticentroZKTeco.Infrastructure.Persistence
{
    public class ArchivoSincronizacionMarcador : ISincronizacionMarcador
    {
        private readonly string _carpeta;

        public ArchivoSincronizacionMarcador(string carpeta)
        {
            _carpeta = carpeta;
        }

        private string RutaDeHoy() =>
            Path.Combine(_carpeta, $"{DateTime.Today:yyyy-MM-dd}.json");

        public bool YaSincronizadoHoy() => File.Exists(RutaDeHoy());

        public void MarcarComoSincronizado(DateTime fechaDatos, IReadOnlyList<Marcacion> marcaciones)
        {
            Directory.CreateDirectory(_carpeta);

            var contenido = new
            {
                fechaEjecucion = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                fechaDatos = fechaDatos.ToString("yyyy-MM-dd"),
                cantidadMarcaciones = marcaciones.Count,
                usuariosIds = marcaciones.Select(m => m.UsuarioId).Distinct().ToList()
            };

            File.WriteAllText(RutaDeHoy(), JsonConvert.SerializeObject(contenido, Formatting.Indented));
        }
    }
}
```

La carpeta (`C:\ProgramData\OpticentroZKTeco\marcaciones\`) va en `App.config`
como `MarcacionesMarcadorPath`. Se usa `ProgramData` (no la carpeta del
`.exe`) porque es la ubicación estándar de Windows para datos persistentes
de un servicio, y sobrevive a reinstalaciones/actualizaciones del ejecutable.

### 5.3 Cambio en `SincronizarMarcacionesUseCase`

El caso de uso ahora recibe también `ISincronizacionMarcador` por
constructor, y lo consulta **antes** de siquiera conectar al K30 — así, si
ya se sincronizó hoy, ni se molesta en conectarse al dispositivo:

```csharp
public void Ejecutar()
{
    if (_marcador.YaSincronizadoHoy())
    {
        _logger.Info("Ya se sincronizó hoy, no se vuelve a intentar.");
        return;
    }

    if (!_dispositivo.Conectar())
    {
        _logger.Error("No se pudo conectar al dispositivo de asistencia.");
        return;
    }

    try
    {
        var todas = _dispositivo.DescargarMarcaciones();
        var ayer = DateTime.Today.AddDays(-1);
        var marcacionesDeAyer = todas.Where(m => m.Fecha.Date == ayer).ToList();

        _logger.Info($"Se descargaron {todas.Count} registros totales, {marcacionesDeAyer.Count} corresponden a ayer.");

        if (marcacionesDeAyer.Count > 0)
        {
            _repositorio.GuardarLote(marcacionesDeAyer);
            _marcador.MarcarComoSincronizado(ayer, marcacionesDeAyer);
        }
        else
        {
            // No hubo marcaciones de ayer (ej. feriado): igual se marca el día
            // como procesado, para no reintentar en cada arranque del mismo día.
            _marcador.MarcarComoSincronizado(ayer, marcacionesDeAyer);
        }
    }
    finally
    {
        _dispositivo.Desconectar();
    }
}
```

### 5.4 Simplificación de `AsistenciaService`

Con el marcador centralizando la idempotencia dentro del caso de uso, ya no
hace falta que `AsistenciaService` distinga "ya se ejecutó hoy" — simplemente
programa el disparo de las 10:00, y si por cualquier motivo se re-dispara
(reinicio del servicio, doble arranque el mismo día), el propio caso de uso
detecta el marcador y no hace nada. Esto también resuelve el **caso 2 y 4**
de la sección 2: al arrancar, si ya pasaron las 10:00 y no hay marcador de
hoy, `ejecutarInmediatamenteSiYaPaso` dispara `Ejecutar()`, que revisa el
marcador (no existe → corre normalmente). Si el marcador sí existe (porque
ya se ejecutó antes hoy), `Ejecutar()` simplemente no hace nada — sin
necesidad de que `AsistenciaService` sepa nada sobre archivos ni fechas.

## 6. Configuración adicional en App.config

```xml
<add key="MarcacionesFolderPath" value="C:\ProgramData\OpticentroZKTeco\marcaciones" />
```

## 7. Criterios de aceptación

- [ ] Si el servicio arranca antes de las 10:00 AM y no hay marcador de hoy,
  no ejecuta nada hasta esa hora exacta (con margen de segundos, no de
  minutos).
- [ ] Si el servicio arranca después de las 10:00 AM y no hay marcador de
  `{yyyy-MM-dd}.json` para la fecha de hoy en
  `C:\ProgramData\OpticentroZKTeco\marcaciones\`, ejecuta la sincronización
  inmediatamente al arrancar.
- [ ] Si ya existe el marcador de hoy, el servicio no vuelve a conectarse al
  K30 ni al ERP aunque se reinicie varias veces el mismo día después de las
  10:00.
- [ ] Con el servicio corriendo varios días sin reiniciarse, se dispara una
  vez por día, a las 10:00 AM, y crea un archivo marcador nuevo cada día.
- [ ] El archivo marcador se crea también cuando no hubo marcaciones ese día
  (para no reintentar en vano en cada arranque).
- [ ] El log (`asistencia.log`) registra explícitamente la fecha/hora del
  próximo disparo programado cada vez que se reprograma.
- [ ] Un error dentro de `_useCase.Ejecutar()` no impide que se programe el
  disparo del día siguiente.
- [ ] El filtro de "día anterior" en `SincronizarMarcacionesUseCase` no
  cambia — sigue igual que en el requirements original.