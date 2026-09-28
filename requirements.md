# requirements.md — OpticentroZKTeco

## 1. Resumen del proyecto

Servicio de Windows en C# que se conecta a un reloj biométrico **ZKTeco K30**
mediante el SDK oficial **zkemkeeper.dll** (referencia COM: *ZKEMKeeper 6.0
control*), descarga periódicamente las marcaciones de asistencia y las
persiste en la base de datos del ERP de Opticentro.

## 2. Stack técnico (no negociable)

- Lenguaje: **C#**
- Framework: **.NET Framework 4.8** (no .NET Core / .NET 5+)
- Plataforma de compilación: **x86** en todos los proyectos de la solución
  (obligatorio por la dependencia COM de 32 bits)
- Tipo de proyecto ejecutable: **Servicio de Windows** (`System.ServiceProcess`),
  no worker service moderno
- SDK del dispositivo: **zkemkeeper.dll**, referencia COM *"ZKEMKeeper 6.0
  control"* (NO usar *"ZKSoftware ZKfinger Engine"* — es un SDK distinto para
  lectores de huella USB, no aplica a este proyecto)
- Namespace de interoperabilidad generado: `zkemkeeper` (clase principal `CZKEM`)

## 3. Arquitectura obligatoria (Clean Architecture por capas)

La solución debe tener 4 proyectos separados, con dependencias apuntando
siempre hacia adentro:

```
OpticentroZKTeco.Domain          (class library, sin dependencias externas)
OpticentroZKTeco.Application     (class library, depende solo de Domain)
OpticentroZKTeco.Infrastructure  (class library, depende de Domain + Application)
OpticentroZKTeco.Service         (Windows Service, el único ejecutable)
```

Reglas estrictas:

- **`Domain`**: entidades (`Marcacion`) e interfaces (`IDispositivoAsistencia`,
  `IMarcacionRepository`, `ILogger`). No puede referenciar `zkemkeeper`, ADO.NET,
  ni ningún paquete externo.
- **`Application`**: casos de uso (`SincronizarMarcacionesUseCase`). No conoce
  el SDK del K30 ni el motor de base de datos concreto — solo trabaja contra
  las interfaces de `Domain`.
- **`Infrastructure`**: aquí y solo aquí puede aparecer `using zkemkeeper;`.
  Contiene `ZktecoDeviceClient` (implementa `IDispositivoAsistencia`),
  `MarcacionRepository` (implementa `IMarcacionRepository`), y `FileLogger`
  (implementa `ILogger`).
- **`Service`**: capa más delgada posible. `Program.cs` (entry point),
  `AsistenciaService.cs` (hereda de `ServiceBase`), `CompositionRoot.cs`
  (arma las dependencias manualmente, sin contenedor DI), `ProjectInstaller.cs`,
  `App.config`.

**No se permite** que `Application` o `Domain` referencien `Infrastructure`.
**No se permite** lógica de negocio dentro de `AsistenciaService.cs` — esa
clase solo debe arrancar el timer y delegar en el caso de uso.

## 4. Modelo de dominio

```csharp
public enum ModoVerificacion { Password = 0, Huella = 1, Tarjeta = 2 }

public enum ModoEntradaSalida
{
    Entrada = 0, Salida = 1, BreakOut = 2, BreakIn = 3, OtIn = 4, OtOut = 5
}

public class Marcacion
{
    public string UsuarioId { get; }
    public ModoVerificacion Verificacion { get; }
    public ModoEntradaSalida TipoMarca { get; }
    public DateTime Fecha { get; }
    // constructor con los 4 parámetros, propiedades de solo lectura
}
```

Los valores `int` crudos que devuelve el SDK (`verifyMode`, `inOutMode`) se
convierten a estos `enum` dentro de `Infrastructure`, nunca se exponen como
`int` fuera de esa capa.

## 5. Interfaces de Domain

```csharp
public interface IDispositivoAsistencia
{
    bool Conectar();
    IReadOnlyList<Marcacion> DescargarMarcaciones();
    void Desconectar();
}

public interface IMarcacionRepository
{
    void GuardarLote(IReadOnlyList<Marcacion> marcaciones);
}

public interface ILogger
{
    void Info(string mensaje);
    void Error(string mensaje);
}
```

## 6. Comportamiento del caso de uso `SincronizarMarcacionesUseCase`

1. Llama a `IDispositivoAsistencia.Conectar()`.
2. Si falla la conexión: loguear error con `ILogger.Error` y salir sin excepción
   (no hay reintento — ver sección 14 sobre timing de red al arrancar Windows).
3. Si conecta: llamar a `DescargarMarcaciones()`, que trae **todo** el buffer
   del dispositivo (el SDK no permite filtrar por fecha en el propio K30).
4. **Filtrar en memoria, dentro de este caso de uso**, solo las marcaciones
   cuyo `Fecha.Date` sea igual a `DateTime.Today.AddDays(-1)` (el día anterior
   a la fecha de ejecución).
5. Loguear con `ILogger.Info` tanto el total descargado como la cantidad que
   quedó tras el filtro de "ayer" (ej. "12 registros totales, 3 de ayer").
6. Si hay al menos una marcación filtrada, llamar a
   `IMarcacionRepository.GuardarLote(...)` solo con esas.
7. En un bloque `finally`, siempre llamar a `IDispositivoAsistencia.Desconectar()`,
   incluso si `DescargarMarcaciones()` lanza una excepción.
8. Esta clase debe ser testeable con mocks de las 3 dependencias — no debe
   instanciar nada concreto internamente (inyección por constructor).
9. **No lleva timer ni reintentos propios**: se ejecuta exactamente una vez
   por invocación de `Ejecutar()`. Quién decide cuándo se invoca es la capa
   `Service` (ver sección 10).

## 7. Implementación de `ZktecoDeviceClient` (Infrastructure)

- Constructor recibe: `ip` (string), `puerto` (int), `machineNumber` (int,
  default 1), `password` (int?, opcional — solo si el dispositivo tiene
  contraseña de comunicación configurada).
- `Conectar()`: si hay `password`, llamar primero a `SetCommPassword`. Luego
  `Connect_Net(ip, puerto)`. Guardar el resultado en un flag interno.
- `DescargarMarcaciones()`:
  - Si no está conectado, devolver lista vacía (no lanzar excepción).
  - Llamar `EnableDevice(machineNumber, false)` antes de leer, y
    `EnableDevice(machineNumber, true)` después, dentro de un `try/finally`.
  - Usar `ReadGeneralLogData(machineNumber)` seguido de un `while` con
    `SSR_GetGeneralLogData(...)` (la versión moderna con `EnrollNumber` como
    `string`, NO la versión legacy `GetGeneralLogData` con `int`).
  - Mapear cada registro a una entidad `Marcacion` del dominio.
- `Desconectar()`: llamar `Disconnect()` solo si el flag de conectado es `true`.
- Exponer también un método `ObtenerUltimoError()` que llame a
  `GetLastError(ref errorCode)` y devuelva el código como string, para
  usarlo al loguear fallos de conexión.

## 8. Implementación de `MarcacionRepository` (Infrastructure)

- Se implementa como `ErpHttpMarcacionRepository`, que envía el lote por HTTP
  al ERP (Symfony) en vez de tocar MongoDB directo — el servicio de Windows
  nunca abre una conexión a la base de datos, solo llama al endpoint.
- Constructor recibe `endpointUrl` (string) y `apiKey` (string), ambos desde
  `App.config` (`ErpEndpointUrl`, `ErpApiKey`).
- `GuardarLote` arma un JSON `{ "marcaciones": [{ usuarioId, fecha, tipo }] }`
  (uno por cada `Marcacion`, `fecha` en formato `yyyy-MM-ddTHH:mm:ss`, `tipo`
  como el nombre del enum `TipoMarca` — ej. "Entrada", "Salida") y hace un
  `POST` con header `X-Api-Key`.
- Debe verificar el código de respuesta HTTP y, si falla, propagar el error
  para que `SincronizarMarcacionesUseCase` lo capture y lo loguee (no debe
  tragarse el error silenciosamente).
- **Contrato completo del endpoint (lado Symfony) documentado por separado**
  en `erp-endpoint-marcaciones.md` — incluye el controlador PHP, el documento
  Mongo con índice único `{user, fecha}` para idempotencia, y la resolución
  de `usuarioId` (código de enrolamiento del K30) a `ObjectId` del empleado
  vía un campo `codigoAsistencia` en la colección `User`.
- **[PENDIENTE DE CONFIRMAR]**: si la colección `User` de Opticentro ya tiene
  un campo con el código de enrolamiento del reloj. Si no existe, hay que
  agregarlo y poblarlo antes de que el endpoint funcione.

## 9. `FileLogger` (Infrastructure)

- Recibe la ruta del archivo de log por constructor.
- `Info` y `Error` escriben una línea con timestamp (`yyyy-MM-dd HH:mm:ss`),
  nivel, y mensaje, usando `File.AppendAllText`.
- Debe crear el directorio del log si no existe (`Directory.CreateDirectory`).
- Cualquier excepción al escribir el log debe capturarse silenciosamente —
  un fallo de logging nunca debe tumbar el servicio.

## 10. `AsistenciaService` (Service)

- Hereda de `ServiceBase`. `ServiceName = "OpticentroZKTeco"`.
- **Sin timer.** `OnStart` arma las dependencias vía
  `CompositionRoot.CrearUseCase()` y llama a `_useCase.Ejecutar()` una única
  vez, sincrónicamente, durante el arranque del servicio.
- `OnStop`: no hay timer que detener; el método puede quedar vacío o solo
  loguear que el servicio se detuvo.
- No debe contener lógica de conexión al K30 ni de persistencia — todo
  delegado al caso de uso.
- **Modelo de uso esperado**: la máquina donde corre este servicio se apaga
  y se prende todos los días. Cada arranque de Windows dispara `OnStart`,
  que sincroniza los registros de "ayer" una sola vez. El servicio no queda
  haciendo polling en segundo plano — simplemente corre su única tarea y
  permanece inactivo hasta el próximo arranque.
- `StartType` del servicio debe ser **`Automatic`**, para que se dispare solo
  al iniciar Windows, sin intervención manual.

## 11. `CompositionRoot` (Service)

- Único lugar de la solución donde se instancian las clases concretas de
  `Infrastructure` y se inyectan en `SincronizarMarcacionesUseCase`.
- Lee configuración desde `ConfigurationManager.AppSettings`: `ZktecoIp`,
  `ZktecoPuerto`, `ConnectionString`, `IntervaloMinutos`.

## 12. App.config

Debe incluir como mínimo:
```xml
<appSettings>
  <add key="ZktecoIp" value="" />
  <add key="ZktecoPuerto" value="4370" />
  <add key="ErpEndpointUrl" value="" />
  <add key="ErpApiKey" value="" />
</appSettings>
```
(No hay `IntervaloMinutos`: el servicio ya no usa timer, corre una vez por
arranque — ver sección 10. Tampoco hay `ConnectionString` directa a Mongo: la
persistencia pasa por el endpoint HTTP del ERP — ver sección 8.)

## 13. Instalador del servicio

- `ProjectInstaller.cs` con `ServiceProcessInstaller` (Account: definir si
  `LocalSystem` o una cuenta de servicio dedicada) y `ServiceInstaller`
  (`ServiceName = OpticentroZKTeco`, `StartType = Automatic`).

## 14. Manejo de errores esperado

- Fallo de conexión al K30 (IP inalcanzable, firewall, timeout): loguear y
  terminar sin excepción. **No hay reintento dentro de la misma ejecución** —
  la próxima oportunidad de sincronizar es el siguiente arranque de Windows.
- Caso esperado y no grave: si Windows arranca el servicio antes de que la
  red/Wi-Fi esté lista, `Connect_Net` puede fallar por pura cuestión de
  timing, sin que el K30 esté realmente inaccesible. Loguearlo como
  advertencia distinguible de un fallo real de conectividad, para no generar
  ruido/alarmas falsas.
- Contraseña de comunicación incorrecta: loguear el código de error de
  `GetLastError` de forma explícita.
- El servicio nunca debe quedar en estado "detenido" por una excepción no
  manejada durante la sincronización — todo el cuerpo de
  `SincronizarMarcacionesUseCase.Ejecutar()` debe estar protegido para que
  una falla puntual no mate el proceso del servicio.
- **Riesgo conocido y aceptado por ahora**: si la PC no se enciende un día
  determinado, el registro de "ayer" correspondiente a ese día nunca se
  sincroniza — el filtro de fecha fija (sección 6, punto 4) no reintenta
  días salteados. Mitigación futura posible (fuera de alcance de esta
  versión): que `MarcacionRepository` exponga la fecha del último lote
  guardado, y filtrar por "todo lo no sincronizado aún" en vez de "ayer"
  fijo. No implementar esto salvo que se pida explícitamente.

## 15. Fuera de alcance (explícito)

- No se implementa inscripción de usuarios ni gestión de huellas desde este
  servicio — solo lectura de marcaciones ya registradas en el dispositivo.
- No se borra el log del dispositivo (`ClearGLog`) automáticamente; eso queda
  como operación manual/futura, no parte de este servicio.
- No se usa ningún contenedor de inyección de dependencias de terceros —
  `CompositionRoot` es manual, por simplicidad dado el tamaño del proyecto.

## 16. Criterios de aceptación

- [ ] La solución compila en x86 sin advertencias de plataforma.
- [ ] `Domain` no tiene ninguna referencia a paquetes NuGet ni a `zkemkeeper`.
- [ ] `Application` compila sin referenciar `Infrastructure`.
- [ ] El servicio, instalado con `StartType = Automatic`, al arrancar Windows
  se conecta una sola vez al K30 configurado en `App.config`, descarga el
  buffer completo, filtra solo los registros del día anterior, y los escribe
  en el log con el total descargado y el total filtrado.
- [ ] Si el K30 está apagado o inalcanzable al momento del arranque, el
  servicio loguea el fallo y termina sin crashear ni reintentar en el mismo
  ciclo (el próximo intento es el siguiente arranque de Windows).
- [ ] El servicio no contiene ningún `Timer` ni lógica de polling en segundo
  plano — toda su ejecución ocurre dentro de `OnStart`.
- [ ] `SincronizarMarcacionesUseCase` tiene al menos un test unitario con
  mocks que verifique el flujo feliz y el flujo de fallo de conexión.
