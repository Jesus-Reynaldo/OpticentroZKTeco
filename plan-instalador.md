# Plan: Instalador Windows (.msi) para OpticentroZKTeco con Visual Studio Installer Projects

## Contexto

Hoy no existe ningún mecanismo automatizado de despliegue para OpticentroZKTeco. El
servicio (`OpticentroZKTeco.Service.exe`) se instala manualmente en cada máquina cliente
siguiendo un procedimiento no documentado en el repo que probablemente incluye:

1. Copiar los binarios compilados (x86, .NET Framework 4.8) a la máquina destino.
2. Ejecutar `Auto-install_sdk.bat` (dentro de `Standalone-SDK-master.zip`) para copiar las
   DLLs del SDK ZKTeco a `system32`/`SysWOW64` y registrar `zkemkeeper.dll` con `regsvr32`.
3. Editar manualmente `OpticentroZKTeco.Service.exe.config` con la IP/puerto del
   dispositivo biométrico de ese cliente.
4. Ejecutar `installutil.exe OpticentroZKTeco.Service.exe` (o `sc create`) para registrar
   el servicio Windows.
5. Crear manualmente `C:\ProgramData\OpticentroZKTeco\log\` y
   `C:\ProgramData\OpticentroZKTeco\marcaciones\sincronizacion\`.

Este proceso es propenso a errores humanos (olvidar el registro COM, dejar la IP del
cliente piloto, permisos de carpeta incorrectos) y no es repetible ni auditable. El
objetivo de este plan es reemplazarlo por un instalador `.msi` de doble clic que:

- Instale los binarios y registre el servicio Windows automáticamente.
- Pida únicamente IP y Puerto del dispositivo biométrico durante el wizard (decisión
  confirmada con el usuario: `ErpEndpointUrl`/`ErpApiKey` NO se piden, quedan fijos en el
  paquete).
- Verifique la conectividad real contra el dispositivo al finalizar la instalación, sin
  bloquear/revertir la instalación si el dispositivo no responde en ese momento (decisión
  confirmada con el usuario).

⚠️ **Cambio de alcance (2026-09-17)**: el paso 2 (SDK ZKTeco — copiar las 13 DLLs y
registrar `zkemkeeper.dll` con `regsvr32`) **queda fuera del `.msi`, por decisión explícita
del usuario**, tras detectar un bug de parseo de argumentos en la Custom Action que lo
automatizaba (ver Decisión #2 más abajo). Se sigue haciendo manualmente con
`Auto-install_sdk.bat` **antes o después** de correr el instalador, en cada máquina
cliente. El `.msi` ya no toca `System32`/`SysWOW64` en absoluto.

Se usa la extensión **Microsoft Visual Studio Installer Projects** (Setup Project clásico,
`.vdproj`) porque así fue decidido explícitamente por el usuario, quien mencionó
literalmente "Setup Project" — no WiX, no Inno Setup. La limitación conocida de esta
herramienta (sin validación en vivo dentro del wizard, sin poder retroceder a un diálogo
anterior) se resuelve moviendo la validación y el test de conexión a Custom Actions que
corren después del wizard (ver más abajo).

⚠️ **Nota de seguridad (ya detectada)**: `src\OpticentroZKTeco.Service\App.config` tiene
hoy una IP real de cliente y una API key real en texto plano
(`ErpApiKey`) hardcodeadas. Como este proyecto no es un repositorio git todavía, no aplica
el riesgo de fuga por commit, pero si se compila el instalador tal cual, el `.msi`
distribuirá esos secretos a cualquier máquina donde se instale. El paso 0 de este plan
neutraliza esto.

---

## Paso 0 — Neutralizar credenciales antes de empaquetar

Editar `src\OpticentroZKTeco.Service\App.config` para reemplazar los valores reales del
cliente piloto por placeholders neutros (los de `ZktecoIp`/`ZktecoPuerto`/
`HoraSincronizacion` los sobrescribe la Custom Action de todas formas; `ErpEndpointUrl`/
`ErpApiKey` no se piden en el wizard y quedan "congelados" con lo que haya en este archivo
al compilar):

```xml
<add key="ZktecoIp" value="0.0.0.0" />
<add key="ZktecoPuerto" value="4370" />
<add key="ZktecoMachineNumber" value="1" />
<add key="ZktecoPassword" value="" />
<add key="HoraSincronizacion" value="10:00" />
<add key="ErpEndpointUrl" value="https://REEMPLAZAR-ENDPOINT-CLIENTE.example.com/api/asistencia/marcaciones/biometrico" />
<add key="ErpApiKey" value="REEMPLAZAR-API-KEY" />
<add key="LogFilePath" value="C:\ProgramData\OpticentroZKTeco\log\asistencia.log" />
<add key="MarcacionesFolderPath" value="C:\ProgramData\OpticentroZKTeco\marcaciones" />
<add key="MarcacionesMarcadorPath" value="C:\ProgramData\OpticentroZKTeco\marcaciones\sincronizacion" />
```

Añadir un checklist de release: antes de compilar el `.vdproj` para entregar a un cliente,
verificar que `App.config` no tenga secretos reales de otro cliente.

---

## Estructura del nuevo proyecto en la solución

```
D:\OpticentroZKTeco\
├── OpticentroZKTeco.sln
├── installer\
│   ├── OpticentroZKTeco.Setup\                 <- Setup Project (.vdproj)
│   │   └── OpticentroZKTeco.Setup.vdproj
│   ├── OpticentroZKTeco.Setup.Actions\         <- Class Library con las Custom Actions
│   │   ├── OpticentroZKTeco.Setup.Actions.csproj
│   │   ├── SetupActionsInstaller.cs            <- [RunInstaller(true)] : Installer
│   │   ├── ConfigWriter.cs                     <- validar y escribir .exe.config
│   │   ├── ConexionTester.cs                   <- probar Connect_Net + MessageBox
│   │   └── MessageBoxHelper.cs                 <- wrapper de MessageBox.Show
│   └── verify-install.ps1                      <- script de verificación post-instalación
└── src\...
```

Ambos proyectos nuevos se agregan a `OpticentroZKTeco.sln` (nueva carpeta de solución
`installer`). El `.vdproj` se edita casi enteramente desde los editores visuales de
Visual Studio (File System, User Interface, Custom Actions, Launch Conditions), no a mano.

**Por qué un proyecto Class Library separado (`OpticentroZKTeco.Setup.Actions`) y no
reutilizar `OpticentroZKTeco.Service`**: si se reutilizara el ensamblado del Service como
target de Custom Action, el instalador ejecutaría **todos** los `Installer` marcados
`[RunInstaller(true)]` de ese ensamblado (incluido `ProjectInstaller.cs`, que ya registra
el servicio) en cada Custom Action que apunte a él, mezclando y descontrolando el orden
Install/Commit/Rollback/Uninstall. Un ensamblado propio permite referenciarlo de forma
independiente en cada evento del Custom Actions Editor. Este proyecto referenciará
`OpticentroZKTeco.Infrastructure` para reutilizar `ZktecoDeviceClient` real en el test de
conexión (evita duplicar la llamada COM), y debe compilar en `x86` / `.NET Framework 4.8`
igual que el resto de la solución.

---

## Configuración del Setup Project (editores visuales de Visual Studio)

Requiere la extensión **"Microsoft Visual Studio Installer Projects"** instalada
(Extensions → Manage Extensions o Visual Studio Marketplace).

### 1. Crear el proyecto

- Solution folder `installer` → Add → New Project → "Setup Project" →
  `OpticentroZKTeco.Setup` en `installer\OpticentroZKTeco.Setup\`.
- Propiedades: `ProductName = Opticentro ZKTeco - Sincronizacion de Asistencia`,
  `Manufacturer = Opticentro`, `ProductVersion` alineado con el versionado del Service,
  `TargetPlatform = x86` (obligatorio, coherente con toda la solución),
  `InstallAllUsers = true`, `DefaultLocation = [ProgramFilesFolder]OpticentroZKTeco\`.

### 2. File System Editor

- **Application Folder**: Add → Project Output → `OpticentroZKTeco.Service` → "Primary
  output" (arrastra Domain/Application/Infrastructure y, normalmente, `Interop.zkemkeeper.dll`
  vía HintPath — **verificar manualmente** que aparezca tras el Add; si no, agregarla a
  mano desde `lib\Interop.zkemkeeper.dll`). También agregar el Primary Output de
  `OpticentroZKTeco.Setup.Actions`.
- **SDK ZKTeco: fuera del instalador (decisión 2026-09-17)** — el `.msi` NO copia ni
  registra ninguna de las 13 DLLs del SDK (`commpro.dll`, `comms.dll`, `plcommpro.dll`,
  `plcomms.dll`, `plrscagent.dll`, `plrscomm.dll`, `pltcpcomm.dll`, `rscagent.dll`,
  `rscomm.dll`, `tcpcomm.dll`, `usbcomm.dll`, `zkemkeeper.dll`, `zkemsdk.dll`). No hay
  carpeta especial "System32 Folder" en el File System Editor. Este paso se hace manual y
  por separado en cada máquina cliente con `Auto-install_sdk.bat` (dentro de
  `Standalone-SDK-master.zip`), tal como se hacía antes de este plan — ver Decisión #2 para
  el motivo (bug de parseo de argumentos descubierto al automatizarlo).
- **ProgramData**: Custom Folder con `DefaultLocation = [CommonAppDataFolder]OpticentroZKTeco\`,
  con subcarpetas `log` y `marcaciones\sincronizacion` (sin archivos — VS Installer
  Projects agrega automáticamente una entrada `CreateFolder` en la tabla MSI). No requiere
  ACLs adicionales: `LocalSystem` tiene acceso total a `ProgramData` por defecto.

### 3. User Interface Editor

- Rama Install → después de "Installation Folder": Add Dialog → **"Textboxes (A)"**.
- `Edit1Label = Dirección IP`, `Edit1Value = 192.168.1.201` (placeholder neutro),
  `Edit1Property = EDITA1`; `Edit2Label = Puerto`, `Edit2Value = 4370`,
  `Edit2Property = EDITA2`; `Edit3Label = Hora de sincronización (HH:mm)`,
  `Edit3Value = 10:00`, `Edit3Property = EDITA3`; `Edit4Visible = False`.
- `HoraSincronizacion` reemplaza el valor que antes estaba hardcodeado en
  `AsistenciaService.cs` (`HoraDisparo = new TimeSpan(10, 0, 0)`); ahora se lee de
  `App.config` en `OnStart` (ver `ObtenerHoraSincronizacion()`), con el mismo valor
  `10:00` como default si el config viene inválido o ausente.
- **Limitación aceptada**: no hay validación en vivo ni forma de retroceder al diálogo. Si
  el formato es inválido, la Custom Action (`ConfigWriter`, ver abajo) muestra un
  MessageBox de error y lanza excepción → Windows Installer revierte toda la instalación;
  el usuario debe re-ejecutar el instalador desde cero.

### 4. Custom Actions Editor

4 carpetas: Install, Commit, Rollback, Uninstall.

- **Install**: (a) Primary Output de `Setup.Actions` con
  `CustomActionData = /opaction="config" /ip="[EDITA1]" /puerto="[EDITA2]" /hora="[EDITA3]" /targetdir="[TARGETDIR] "`
  (nota el espacio antes de la comilla de cierre — ver Decisión #2); (b) Primary Output de
  `OpticentroZKTeco.Service` (dispara `ProjectInstaller.cs`, registra el servicio Windows).
  Orden: config → servicio.
- **Commit**: Primary Output de `Setup.Actions` con
  `CustomActionData = /opaction="testconnection" /ip="[EDITA1]" /puerto="[EDITA2]"`, **al
  final**, después de repetir el Primary Output de `OpticentroZKTeco.Service` (requerido
  por VS Installer Projects para completar el ciclo `Installer.Commit()`).
- **Rollback**: Primary Output de `OpticentroZKTeco.Service` únicamente.
- **Uninstall**: Primary Output de `OpticentroZKTeco.Service` únicamente.

**Nota sobre `/opaction` (no `/action`)**: el nombre `action` colisiona con el parámetro
reservado `/action=install` que el propio motor `.NET Installer`
(`System.Configuration.Install`) usa para decidir si llama `Install()`/`Commit()`/
`Rollback()`/`Uninstall()`. Usar `/opaction` evita la colisión. Ver Decisión #2 para el bug
más grave (el `\"` final de `[TARGETDIR]`) que este nombre por sí solo no resolvía.

**Por qué el test de conexión va en Commit y no en Install**: en Install, Windows Installer
sigue en modo transaccional — cualquier excepción revierte todo, incluido el servicio ya
registrado. Como un fallo de conexión NO debe abortar la instalación (requisito
confirmado), el test debe vivir en Commit, que corre después de que la transacción ya fue
confirmada. La Custom Action de Commit debe capturar toda excepción internamente
(try/catch) y nunca relanzar. Nota: `StartType=Automatic` solo configura el arranque para
el próximo boot de Windows — el servicio no arranca automáticamente al terminar el
instalador, así que el orden relativo entre config/COM/servicio no tiene una dependencia
técnica dura, solo claridad operativa.

### 5. Launch Conditions Editor

- Condición de .NET Framework 4.8 (clave de registro
  `HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\Release` ≥ `528040`).
- **No** incluir `VC_redist.x64.exe` por defecto (ver Decisiones abiertas — es sospechoso
  dado que toda la solución es x86).

---

## Diseño de las Custom Actions en C#

`OpticentroZKTeco.Setup.Actions\SetupActionsInstaller.cs` — punto de entrada
`[RunInstaller(true)] : Installer`, despacha por `Context.Parameters["opaction"]`:

```csharp
public override void Install(IDictionary savedState)
{
    base.Install(savedState);
    switch (Context.Parameters["opaction"])
    {
        case "config":
            ConfigWriter.EscribirConfig(Context.Parameters["targetdir"],
                Context.Parameters["ip"], Context.Parameters["puerto"],
                Context.Parameters["hora"]);
            break;
    }
}

public override void Commit(IDictionary savedState)
{
    base.Commit(savedState);
    if (Context.Parameters["opaction"] == "testconnection")
        ConexionTester.ProbarConexionSegura(Context.Parameters["ip"], Context.Parameters["puerto"]);
}
```

- **`ConfigWriter.EscribirConfig`**: recibe `targetDir` con `.Trim()` (ver Decisión #2 sobre
  por qué `CustomActionData` le agrega un espacio final a propósito). Valida
  `IPAddress.TryParse(ip)`, `int.TryParse(puerto)` en rango 1-65535, y
  `TimeSpan.TryParseExact(hora, "hh\:mm", ...)`; si alguna falla, muestra `MessageBox` de
  error y lanza `InstallException` (dispara rollback). Si todo es válido, abre
  `{targetDir}\OpticentroZKTeco.Service.exe.config` como `XmlDocument` (no se puede usar
  `ConfigurationManager` porque la Custom Action corre en otro proceso/AppDomain), localiza
  `//appSettings/add[@key='ZktecoIp']`, `[@key='ZktecoPuerto']` y
  `[@key='HoraSincronizacion']`, y sobrescribe `value`.
- **`ConexionTester.ProbarConexionSegura`**: instancia `new ZktecoDeviceClient(ip, puerto)`
  (reutilizando la clase real de `OpticentroZKTeco.Infrastructure.Zkteco`), llama
  `Conectar()`; si tiene éxito muestra MessageBox de éxito y `Desconectar()`; si falla,
  muestra MessageBox indicando que **la instalación sí se completó** pero la conexión
  falló (incluye `ObtenerUltimoError()`), sin lanzar excepción. Envuelto en un `try/catch`
  general adicional como blindaje — Commit nunca debe relanzar.

---

## Plan de testing exhaustivo

Requiere Windows real. Usar una **VM limpia** (Hyper-V, disponible nativamente en la
máquina de desarrollo con Windows 10 Enterprise LTSC, o VirtualBox) con snapshot "limpio
sin .NET" para repetir pruebas desde cero.

**Prerrequisito manual (fuera del `.msi`)**
- [ ] Antes de probar la sincronización real (no necesario solo para probar que el
  instalador registra el servicio), correr `Auto-install_sdk.bat` para copiar las 13 DLLs
  del SDK a `SysWOW64` y registrar `zkemkeeper.dll` con `regsvr32`.

**Instalación fresca**
- [ ] Restaurar snapshot limpio, copiar el `.msi` (Release, x86), ejecutar como
  administrador.
- [ ] Confirmar que el diálogo Textboxes (A) muestra exactamente 3 campos con los
  placeholders esperados (IP, Puerto, Hora de sincronización).
- [ ] Completar con IP/puerto/hora válidos (dispositivo alcanzable o no) y verificar que
  termina sin errores, mostrando el MessageBox final correspondiente.

**Verificación post-instalación**
- [ ] `Get-Service OpticentroZKTeco` → existe, `StartType = Automatic` (Status puede ser
  `Stopped`, normal).
- [ ] `OpticentroZKTeco.Service.exe.config` en `Program Files (x86)\OpticentroZKTeco\`
  refleja exactamente la IP/puerto/hora ingresados.
- [ ] Iniciar el servicio y confirmar en `asistencia.log` que la "próxima sincronización
  programada" coincide con la hora ingresada en el wizard, no con `10:00` por defecto.
- [ ] Si ya corriste el prerrequisito manual del SDK: confirmar que el servicio **no** lanza
  `COMException 0x80040154` ("Clase no registrada") al iniciar — prueba end-to-end de que
  el registro COM manual quedó bien.
- [ ] Revisar `C:\ProgramData\OpticentroZKTeco\log\asistencia.log` tras iniciar.

**IP inválida / dispositivo apagado**
- [ ] IP con formato inválido → MessageBox de error, instalación revertida por completo
  (servicio y archivos ya no existen).
- [ ] IP válida pero dispositivo inalcanzable → instalación se completa igual, MessageBox
  final indica claramente el fallo de conexión sin sugerir que la instalación falló.

**Desinstalación**
- [ ] Servicio removido, `Program Files (x86)\OpticentroZKTeco\` eliminada.
- [ ] Confirmar comportamiento real sobre `C:\ProgramData\OpticentroZKTeco\` (logs) —
  recomendación: preservarla por defecto (valor de soporte/histórico).

**Reinstalación / upgrade**
- [ ] Ejecutar el mismo `.msi` dos veces seguidas → Windows Installer debe ofrecer
  Reparar/Desinstalar sin quedar en estado inconsistente.
- [ ] Con `ProductVersion` incrementado, instalar sobre una instalación previa y confirmar
  upgrade limpio (documentar si se prefiere preservar la IP/puerto ya configurados en vez
  de volver a pedirlos — ver Decisiones abiertas).

**Script complementario** `installer\verify-install.ps1` — automatiza la verificación
mecánica (servicio, config, CLSID, arranque, log) para acelerar iteraciones del `.vdproj`
sin reemplazar la verificación manual del wizard/MessageBoxes:

```powershell
$svc = Get-Service -Name "OpticentroZKTeco" -ErrorAction SilentlyContinue
if (-not $svc) { throw "Servicio no encontrado." }
if ($svc.StartType -ne "Automatic") { throw "StartType inesperado: $($svc.StartType)" }

[xml]$xml = Get-Content "C:\Program Files (x86)\OpticentroZKTeco\OpticentroZKTeco.Service.exe.config"
$ip = ($xml.configuration.appSettings.add | Where-Object key -eq "ZktecoIp").value
$puerto = ($xml.configuration.appSettings.add | Where-Object key -eq "ZktecoPuerto").value
Write-Host "ZktecoIp=$ip ZktecoPuerto=$puerto"

Start-Service -Name "OpticentroZKTeco"
Start-Sleep -Seconds 5
(Get-Service -Name "OpticentroZKTeco").Status

Get-Content "C:\ProgramData\OpticentroZKTeco\log\asistencia.log" -Tail 20
```

---

## Decisiones abiertas (a confirmar antes o durante la implementación)

1. **`VC_redist.x64.exe`** (presente en la raíz del repo): es x64 pero toda la solución es
   x86 — sospechoso. No incluir en el instalador por defecto; investigar su origen y, si
   hiciera falta algún redistribuible, probablemente sería la versión **x86**.
2. **Registro COM automático (`registercom`): removido del instalador, por decisión del
   usuario (2026-09-17)**. Historial de por qué fue problemático, dejado como referencia:
   - Se probó agregar "System64 Folder" (`[System64Folder]`) como copia defensiva de las
     DLLs, pero VS Installer Projects rechaza esa carpeta especial en un proyecto
     `TargetPlatform = x86` (error de build).
   - Con solo "System32 Folder" (resuelve a `SysWOW64`, correcto), la Custom Action
     `registercom` fallaba en la máquina de prueba con `Error 1001` /
     `System.IO.FileNotFoundException` — causa raíz: `[SystemFolder]` y `[TARGETDIR]`
     **siempre terminan en `\`**, y al envolverlos entre comillas en `CustomActionData`
     (`"[SystemFolder]"`), el `\"` final se interpreta como comilla escapada, no como cierre
     — rompe el parseo de argumentos de la Custom Action y mezcla los tokens siguientes. El
     fix (agregar un espacio antes de la comilla de cierre + `.Trim()` en C#) sí se aplicó y
     quedó vigente para `/targetdir` (ver `config` en Custom Actions Editor), pero para
     `/systemdir`/`registercom` el usuario decidió, en cambio, sacar esa Custom Action del
     instalador por completo y volver al proceso manual (`Auto-install_sdk.bat`) para evitar
     cualquier riesgo de que `zkemkeeper.dll` quede mal registrado.
   - De paso se encontró y corrigió un bug separado: el parámetro propio `/action=` colisionaba
     con el `/action=install` reservado del framework `.NET Installer` — se renombró a
     `/opaction=` en las Custom Actions que sí quedaron (`config`, `testconnection`).
3. **Preservar o borrar `C:\ProgramData\OpticentroZKTeco\` al desinstalar**: recomendación
   de este plan es preservar, pero es decisión de producto.
4. ~~Desregistro COM al desinstalar~~ — **N/A**: ya no aplica, el instalador no registra COM.
5. **Upgrade y preservación de IP/Puerto ya configurados**: hoy cada ejecución del
   instalador vuelve a pedir IP/Puerto desde cero. Precargar los valores existentes en un
   upgrade requeriría una Custom Action adicional más temprana en la secuencia — no
   incluido en esta primera versión, documentado como mejora futura.
6. **Arrancar el servicio inmediatamente tras instalar** (en vez de esperar al próximo
   boot): extensión trivial sobre el diseño actual (agregar `ServiceController.Start()` al
   final de Commit) si se decide que es deseable.
7. **Firma de código del `.msi`**: no incluida en este plan; sin firma puede disparar
   advertencias de SmartScreen/Defender en las máquinas cliente.

---

## Archivos críticos

- `installer\OpticentroZKTeco.Setup\OpticentroZKTeco.Setup.vdproj` — nuevo, generado por
  el editor visual de Visual Studio.
- `installer\OpticentroZKTeco.Setup.Actions\SetupActionsInstaller.cs`,
  `ConfigWriter.cs`, `ConexionTester.cs`, `MessageBoxHelper.cs` — nuevos.
- `installer\verify-install.ps1` — nuevo, script de verificación post-instalación.
- `src\OpticentroZKTeco.Service\App.config` — editar primero (Paso 0) para neutralizar
  `ZktecoIp` y `ErpApiKey` reales del piloto; incluye la nueva clave `HoraSincronizacion`.
- `src\OpticentroZKTeco.Service\AsistenciaService.cs` — ya no hardcodea la hora de disparo
  (`HoraDisparo`); ahora la lee de `App.config` en `OnStart` vía `ObtenerHoraSincronizacion()`,
  con `10:00` como valor por defecto si el config viene ausente o inválido.
- `src\OpticentroZKTeco.Service\ProjectInstaller.cs` — no se modifica, pero es invocado
  automáticamente por el Setup Project vía su Primary Output.
- `src\OpticentroZKTeco.Infrastructure\Zkteco\ZktecoDeviceClient.cs` — reutilizado
  directamente por `ConexionTester.cs`.
- `sdk\` — las 13 DLLs del SDK ZKTeco, ya **fuera del alcance del instalador**; se
  instalan/registran manualmente con `Auto-install_sdk.bat` en cada máquina cliente.
