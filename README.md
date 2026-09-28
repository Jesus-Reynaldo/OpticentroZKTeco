# OpticentroZKTeco

Servicio de Windows que **descarga automáticamente las marcaciones de asistencia de un reloj
biométrico ZKTeco y las envía a un ERP** mediante una API HTTP.

Se instala una vez en una PC de la misma red que el reloj. A partir de ahí trabaja solo: cada día, a
la hora configurada, se conecta al reloj, toma las marcaciones del día anterior y las envía al ERP.

```
 Reloj ZKTeco  ──(red local, puerto 4370)──►  Servicio en la PC  ──(HTTPS + API key)──►  ERP
                                                     │
                                                     ├─ respaldo local en JSON
                                                     └─ log de cada ejecución
```

---

## ¿Qué hace exactamente?

- **Sincronización diaria automática.** A la hora configurada (por defecto `10:00`), envía al ERP
  las marcaciones del día anterior: usuario, fecha y hora, tipo de marca (entrada/salida) y método
  de verificación (huella, tarjeta o contraseña).
- **Recupera días perdidos.** Si la PC estuvo apagada, sin red o el ERP no respondía, en la
  siguiente ejecución también envía los días pendientes (hasta 7 hacia atrás, configurable).
- **Se pone al día al encender.** Si la PC se enciende después de la hora programada, sincroniza
  en ese momento, sin esperar al día siguiente.
- **Una sola vez por día.** Aunque la PC se reinicie varias veces, no repite la sincronización
  automática del mismo día.
- **Respaldo local.** Antes de enviar, guarda las marcaciones de cada día en un archivo JSON en la
  PC.
- **Aviso en pantalla.** Al terminar, muestra un mensaje al usuario conectado con la cantidad de
  marcaciones enviadas.
- **Sincronización manual.** Se puede reenviar cualquier rango de fechas desde la línea de
  comandos (ver [Sincronización manual](#sincronización-manual)).

---

## Requisitos

| Requisito | Detalle |
|---|---|
| Sistema operativo | Windows 10/11 o Windows Server (32 o 64 bits). |
| .NET Framework | 4.8. |
| Red | La PC debe poder llegar a la IP del reloj (puerto `4370` por defecto) y a la URL del ERP. |
| Reloj biométrico | ZKTeco compatible con el SDK `zkemkeeper` (conexión TCP/IP). |
| ERP | Endpoint HTTP que reciba las marcaciones en JSON (ver [Integración con el ERP](#integración-con-el-erp)). |
| Para compilar | Visual Studio 2022 con la extensión **Microsoft Visual Studio Installer Projects**. |

---

## Instalación

La instalación tiene 5 pasos. Los pasos 1, 4 y 5 se hacen en **cada PC** donde se instale el
servicio. Los pasos 2 y 3 se hacen una sola vez, en la PC de desarrollo, para generar el instalador.

### Paso 1 — Instalar el SDK de ZKTeco (en cada PC)

El servicio usa el SDK oficial de ZKTeco, que el instalador **no** incluye. Las 13 DLLs están en
la carpeta [`sdk/`](sdk/) de este repositorio.

1. Copia **todas** las DLLs de `sdk/` a la carpeta de sistema de 32 bits:
   - Windows de 64 bits: `C:\Windows\SysWOW64\`
   - Windows de 32 bits: `C:\Windows\System32\`
2. Abre una consola **como administrador** y registra el componente:

   ```bat
   :: Windows de 64 bits
   C:\Windows\SysWOW64\regsvr32.exe C:\Windows\SysWOW64\zkemkeeper.dll

   :: Windows de 32 bits
   regsvr32 C:\Windows\System32\zkemkeeper.dll
   ```

   Debe aparecer un mensaje de que el registro se completó correctamente.

### Paso 2 — Configurar la conexión al ERP

Edita [`src/OpticentroZKTeco.Service/App.config`](src/OpticentroZKTeco.Service/App.config) y
reemplaza los valores de ejemplo por los de tu ERP:

```xml
<add key="ErpEndpointUrl" value="https://tu-erp.com/api/asistencia/marcaciones/biometrico" />
<add key="ErpApiKey" value="tu-api-key" />
```

Estos dos valores quedan **dentro del instalador** al compilarlo, porque el asistente de instalación
no los pide. Los datos del reloj (IP, puerto y hora) sí se piden al instalar.

> ⚠️ No subas tu `ErpEndpointUrl` ni tu `ErpApiKey` reales al repositorio. Después de compilar el
> instalador, vuelve a dejar los valores de ejemplo.

### Paso 3 — Compilar el instalador

1. Abre `OpticentroZKTeco.slnx` en Visual Studio 2022.
2. Si no la tienes, instala la extensión **Microsoft Visual Studio Installer Projects**
   (*Extensiones → Administrar extensiones*) y reinicia Visual Studio.
3. Selecciona la configuración **Release** y la plataforma **x86**.
4. Compila la solución (*Compilar → Compilar solución*).
5. En el Explorador de soluciones, haz clic derecho en **OpticentroZKTeco.Setup → Compilar**.

   > El proyecto de instalación (`.vdproj`) solo se puede compilar desde Visual Studio; no funciona
   > con `msbuild` ni `dotnet build`.

El instalador queda en `installer/OpticentroZKTeco.Setup/Release/` (`OpticentroZKTeco.Setup.msi` y
`setup.exe`).

### Paso 4 — Ejecutar el instalador (en cada PC)

1. Ejecuta `OpticentroZKTeco.Setup.msi` como administrador.
2. El asistente pide tres datos:

   | Campo | Ejemplo | Descripción |
   |---|---|---|
   | Dirección IP | `192.168.1.201` | IP del reloj biométrico en la red local. |
   | Puerto | `4370` | Puerto del reloj (4370 es el valor de fábrica). |
   | Hora de sincronización | `10:00` | Hora diaria del envío, en formato 24 h (`HH:mm`). |

3. Al terminar, el instalador **prueba la conexión con el reloj** y muestra el resultado:
   - ✅ Conexión exitosa: todo listo.
   - ⚠️ Conexión fallida: la instalación **sí se completó**, pero revisa la IP, que el reloj esté
     encendido y que el firewall permita el puerto. Puedes corregir la IP después (ver
     [Cambiar la configuración](#cambiar-la-configuración-después-de-instalar)).

Si la IP, el puerto o la hora tienen un formato inválido, el instalador lo avisa y revierte la
instalación. Solo hay que volver a ejecutarlo.

### Paso 5 — Iniciar el servicio

El servicio queda configurado para iniciar **automáticamente con Windows**, pero no arranca hasta el
próximo reinicio. Para iniciarlo ya, abre PowerShell como administrador:

```powershell
Start-Service OpticentroZKTeco
```

También se puede iniciar desde `services.msc`: busca **"Opticentro ZKTeco - Sincronizacion de
Asistencia"** y haz clic en *Iniciar*.

Para comprobar que todo quedó bien, ejecuta el script de verificación como administrador:

```powershell
powershell -ExecutionPolicy Bypass -File installer\verify-install.ps1
```

El script revisa el servicio, la configuración instalada, el registro del SDK y las últimas líneas
del log.

---

## Cambiar la configuración después de instalar

La configuración instalada está en:

```
C:\Program Files (x86)\OpticentroZKTeco\OpticentroZKTeco.Service.exe.config
```

(En Windows de 32 bits: `C:\Program Files\OpticentroZKTeco\`.)

Ábrela con el Bloc de notas **como administrador**, cambia los valores y reinicia el servicio:

```powershell
Restart-Service OpticentroZKTeco
```

| Clave | Valor por defecto | Descripción |
|---|---|---|
| `ZktecoIp` | *(se pide al instalar)* | IP del reloj biométrico. |
| `ZktecoPuerto` | `4370` | Puerto TCP del reloj. |
| `ZktecoMachineNumber` | `1` | Número de máquina del reloj (normalmente `1`). |
| `ZktecoPassword` | *(vacío)* | Contraseña de comunicación del reloj, solo si tiene una configurada (numérica). |
| `HoraSincronizacion` | `10:00` | Hora diaria del envío (`HH:mm`, 24 h). |
| `DiasRecuperacion` | `7` | Cuántos días hacia atrás se revisan para enviar días pendientes. |
| `ErpEndpointUrl` | — | URL del ERP que recibe las marcaciones. |
| `ErpApiKey` | — | API key del ERP; se envía en el header `X-Api-Key`. |
| `LogFilePath` | `C:\ProgramData\OpticentroZKTeco\log\asistencia.log` | Archivo de log. |
| `MarcacionesFolderPath` | `C:\ProgramData\OpticentroZKTeco\marcaciones` | Carpeta de respaldos JSON. |
| `MarcacionesMarcadorPath` | `C:\ProgramData\OpticentroZKTeco\marcaciones\sincronizacion` | Registro de los días ya sincronizados. |

---

## Sincronización manual

Para reenviar marcaciones de fechas específicas (por ejemplo, después de corregir datos en el
reloj), ejecuta el programa desde una consola **como administrador** en la carpeta de instalación:

```bat
cd "C:\Program Files (x86)\OpticentroZKTeco"

:: Sincronización normal (lo mismo que hace el servicio cada día)
OpticentroZKTeco.Service.exe

:: Desde una fecha hasta ayer
OpticentroZKTeco.Service.exe --desde 2026-09-01

:: Un rango específico (o un solo día, repitiendo la fecha)
OpticentroZKTeco.Service.exe --desde 2026-09-01 --hasta 2026-09-15
```

Las fechas van en formato `AAAA-MM-DD`. La sincronización manual siempre reenvía el rango pedido,
aunque ya se haya enviado antes. El resultado queda en el log.

---

## Archivos que genera

Todo queda en `C:\ProgramData\OpticentroZKTeco\`:

| Ruta | Contenido |
|---|---|
| `log\asistencia.log` | Registro de cada ejecución: conexión al reloj, cantidad de marcaciones, respuesta del ERP y errores. **Es lo primero que hay que revisar si algo falla.** |
| `marcaciones\AAAA-MM-DD.json` | Respaldo de las marcaciones enviadas de cada día. |
| `marcaciones\sincronizacion\` | Registro de qué días ya se sincronizaron (evita envíos repetidos). |

---

## Integración con el ERP

El servicio hace un `POST` al `ErpEndpointUrl` con el header `X-Api-Key: <ErpApiKey>` y un cuerpo
JSON como este:

```json
[
  {
    "usuarioId": "25",
    "verificacion": "Huella",
    "tipoMarca": "Entrada",
    "fecha": "2026-09-27T08:02:15"
  }
]
```

- `verificacion`: `Huella`, `Tarjeta` o `Password`.
- `tipoMarca`: `Entrada`, `Salida`, `BreakOut`, `BreakIn`, `OtIn` u `OtOut`.
- `fecha`: hora local del reloj, formato `yyyy-MM-ddTHH:mm:ss`.

El ERP debe responder con un código `2xx`. Como un mismo día puede enviarse más de una vez
(recuperación de días o sincronización manual), **el ERP debe evitar duplicados**: si recibe una
marcación que ya existe (mismo `usuarioId`, `fecha` y `tipoMarca`), debe actualizarla o ignorarla.

Opcionalmente, la respuesta puede incluir `total`, `procesadas`, `duplicadas`,
`descartadasPorIntervalo` y `errores`. Si vienen, se escriben resumidos en el log.

---

## Solución de problemas

| Síntoma | Causa probable y solución |
|---|---|
| El servicio no inicia y el log o el Visor de eventos muestran `0x80040154` / *"Clase no registrada"* | El SDK de ZKTeco no está registrado. Repite el [Paso 1](#paso-1--instalar-el-sdk-de-zkteco-en-cada-pc). |
| *"No se pudo conectar al dispositivo de asistencia"* | IP o puerto incorrectos, reloj apagado o firewall bloqueando el puerto `4370`. Prueba con `Test-NetConnection <IP> -Port 4370`. Si el reloj tiene contraseña de comunicación, configura `ZktecoPassword`. |
| *"ERP respondio 401"* o *"403"* | `ErpApiKey` incorrecta. Corrígela en el archivo de configuración y reinicia el servicio. |
| *"ERP respondio 404"* o error de conexión | `ErpEndpointUrl` incorrecta o la PC no tiene salida a internet. |
| No se sincronizó hoy | Revisa el log. Si ya se sincronizó en el día, no se repite hasta mañana; usa la [sincronización manual](#sincronización-manual) si hace falta. |
| El servicio está detenido después de instalar | Es normal: arranca con el próximo reinicio de Windows, o inícialo con `Start-Service OpticentroZKTeco`. |

---

## Desinstalar

Desde *Configuración → Aplicaciones* (o *Panel de control → Programas*), desinstala **"Opticentro
ZKTeco - Sincronizacion de Asistencia"**. Esto elimina el servicio y los archivos del programa.

Si ya no necesitas los logs ni los respaldos, borra también `C:\ProgramData\OpticentroZKTeco\`.

---

## Para desarrolladores

**Tecnología:** C#, .NET Framework 4.8, x86 (obligatorio, porque el SDK `zkemkeeper` es de 32 bits).

```
src/
  OpticentroZKTeco.Domain/           Entidades (Marcacion), enums e interfaces
  OpticentroZKTeco.Application/      Caso de uso: SincronizarMarcacionesUseCase
  OpticentroZKTeco.Infrastructure/   Cliente ZKTeco, cliente HTTP del ERP, respaldo JSON, log, avisos
  OpticentroZKTeco.Service/          Servicio de Windows, programación diaria y configuración
test/
  OpticentroZKTeco.Application.Tests/  Pruebas del caso de uso
installer/
  OpticentroZKTeco.Setup/            Proyecto del instalador (.msi)
  OpticentroZKTeco.Setup.Actions/    Acciones del instalador: escribir la configuración y probar la conexión
  verify-install.ps1                 Verificación posterior a la instalación
lib/  Interop.zkemkeeper.dll (referencia para compilar)
sdk/  DLLs del SDK de ZKTeco (se instalan en cada PC, ver Paso 1)
```

- **Probar sin instalar el servicio:** compila en `Debug|x86`, completa `App.config` con datos reales
  y ejecuta `OpticentroZKTeco.Service.exe` desde una consola. Detecta que no corre como servicio y
  hace una sincronización inmediata. Necesita el SDK registrado (Paso 1).
- **Documentación de diseño:** [`requirements.md`](requirements.md),
  [`requirements_horario.md`](requirements_horario.md),
  [`plan-instalador.md`](plan-instalador.md) e
  [`installer/OpticentroZKTeco.Setup/PASOS-MANUALES-VISUAL-STUDIO.md`](installer/OpticentroZKTeco.Setup/PASOS-MANUALES-VISUAL-STUDIO.md).
