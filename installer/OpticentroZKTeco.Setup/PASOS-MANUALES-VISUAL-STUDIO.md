# Pasos manuales en Visual Studio para crear el Setup Project

Esta parte **no se puede automatizar por línea de comandos**: la extensión
"Microsoft Visual Studio Installer Projects" (que da soporte a los `.vdproj`
clásicos) expone su configuración únicamente a través de 4 editores visuales
dentro de Visual Studio. El `.vdproj` es un formato de texto propietario y
frágil de editar a mano — intentar generarlo manualmente fuera de la IDE
arriesga producir un proyecto corrupto que ni siquiera abre correctamente.

Todo lo que sí se pudo automatizar ya está hecho:

- `App.config` del Service neutralizado (sin IP/API key reales del piloto).
- Proyecto `OpticentroZKTeco.Setup.Actions` (Custom Actions en C#) creado,
  agregado a la solución y compilando correctamente en Release/x86.
- Script `installer\verify-install.ps1` listo para usar tras la instalación.

Lo que sigue es 100% manual, dentro de Visual Studio.

## 0. Prerrequisito: instalar la extensión

1. Extensions → Manage Extensions → pestaña "Online" → buscar
   **"Microsoft Visual Studio Installer Projects"**.
2. Descargar e instalar (requiere cerrar y reabrir Visual Studio para
   completar la instalación del VSIX).

## 1. Crear el Setup Project

1. En el Explorador de soluciones, la carpeta de solución `installer` ya
   existe (fue creada al agregar `OpticentroZKTeco.Setup.Actions`). Clic
   derecho sobre ella → Add → New Project.
2. Buscar **"Setup Project"** → nombrarlo `OpticentroZKTeco.Setup` →
   ubicación `D:\OpticentroZKTeco\installer\OpticentroZKTeco.Setup\`
   (la carpeta ya existe, vacía).
3. En la ventana Properties del proyecto Setup:
   - `ProductName` = `Opticentro ZKTeco - Sincronizacion de Asistencia`
   - `Manufacturer` = `Opticentro`
   - `ProductVersion` = `1.0.0` (alinear con el versionado del Service)
   - `TargetPlatform` = `x86`
   - `InstallAllUsers` = `true`
   - `DefaultLocation` = `[ProgramFilesFolder]OpticentroZKTeco\`

## 2. File System Editor

Clic derecho en el proyecto Setup → View → File System.

1. **Application Folder**:
   - Add → Project Output... → `OpticentroZKTeco.Service` → "Primary output".
     Verificar que `Interop.zkemkeeper.dll` haya quedado incluida (si no,
     Add → File... → `D:\OpticentroZKTeco\lib\Interop.zkemkeeper.dll`).
   - Add → Project Output... → `OpticentroZKTeco.Setup.Actions` → "Primary
     output".
2. **DLLs del SDK ZKTeco — copiar a AMBAS carpetas** (ya están descomprimidas en
   `D:\OpticentroZKTeco\sdk\`, no hace falta extraer el `.zip` de nuevo). Son
   13 archivos: `commpro.dll`, `comms.dll`, `plcommpro.dll`, `plcomms.dll`,
   `plrscagent.dll`, `plrscomm.dll`, `pltcpcomm.dll`, `rscagent.dll`,
   `rscomm.dll`, `tcpcomm.dll`, `usbcomm.dll`, `zkemkeeper.dll`, `zkemsdk.dll`.
   - Clic derecho en "File System on Target Machine" → Add Special Folder →
     **"System32 Folder"** (mapea a `[SystemFolder]`, que en un `.msi` x86
     debería resolver a `SysWOW64` en Windows x64 — aquí es donde se registra
     el COM). Add → File... → agregar las 13 DLLs desde `D:\OpticentroZKTeco\sdk\`.
   - Clic derecho en "File System on Target Machine" → Add Special Folder →
     **"System64 Folder"** (mapea a `[System64Folder]`, siempre el System32
     real de 64 bits, sin redirección — copia defensiva, igual que la
     práctica manual ya usada por el equipo). Add → File... → agregar las
     mismas 13 DLLs ahí también.
   - **No** registrar COM contra la copia de "System64 Folder" (un
     `regsvr32` de 64 bits no puede registrar una DLL de 32 bits) — el
     registro solo se hace contra la copia de "System32 Folder" (ver
     `ComRegistrar.cs`, sin cambios).
3. **ProgramData** (Add Special Folder → "Custom Folder", renombrar a
   `ProgramDataFolder`, `DefaultLocation` = `[CommonAppDataFolder]OpticentroZKTeco\`):
   - Dentro, crear (Add → Folder) las subcarpetas `log` y
     `marcaciones\sincronizacion` (sin archivos dentro).

## 3. User Interface Editor

Clic derecho en el proyecto Setup → View → User Interface.

1. Rama Install → después de "Installation Folder": Add Dialog →
   **"Textboxes (A)"**.
2. Propiedades del diálogo:
   - `Edit1Label` = `Dirección IP`, `Edit1Value` = `192.168.1.201`,
     `Edit1Property` = `EDITA1`
   - `Edit2Label` = `Puerto`, `Edit2Value` = `4370`, `Edit2Property` = `EDITA2`
   - `Edit3Label` = `Hora de sincronización (HH:mm)`, `Edit3Value` = `10:00`,
     `Edit3Property` = `EDITA3`
   - `Edit4Visible` = `False`

## 4. Custom Actions Editor

Clic derecho en el proyecto Setup → View → Custom Actions. 4 carpetas:
Install, Commit, Rollback, Uninstall.

- **Install** (en este orden):
  1. Primary Output de `OpticentroZKTeco.Setup.Actions` con
     `CustomActionData` = `/action="registercom" /systemdir="[SystemFolder]"`
  2. Primary Output de `OpticentroZKTeco.Setup.Actions` con
     `CustomActionData` = `/action="config" /ip="[EDITA1]" /puerto="[EDITA2]" /hora="[EDITA3]" /targetdir="[TARGETDIR]"`
  3. Primary Output de `OpticentroZKTeco.Service` (registra el servicio
     Windows vía `ProjectInstaller.cs`)
- **Commit** (en este orden):
  1. Primary Output de `OpticentroZKTeco.Service` (requerido por VS
     Installer Projects para completar `Installer.Commit()`)
  2. Primary Output de `OpticentroZKTeco.Setup.Actions` con
     `CustomActionData` = `/action="testconnection" /ip="[EDITA1]" /puerto="[EDITA2]"`
- **Rollback**: Primary Output de `OpticentroZKTeco.Service` únicamente.
- **Uninstall**: Primary Output de `OpticentroZKTeco.Service` únicamente.

## 5. Launch Conditions Editor

Clic derecho en el proyecto Setup → View → Launch Conditions.

1. Agregar condición de .NET Framework 4.8 (clave de registro
   `HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\Release` ≥ `528040`).
2. **No** agregar condición sobre `VC_redist.x64.exe` (ver Decisiones
   abiertas en `plan-instalador.md` — es sospechoso dado que toda la
   solución es x86).

## 6. Compilar y probar

1. Compilar la solución completa en `Release|x86` primero (para que el
   Setup Project tome los Primary Outputs actualizados).
2. Clic derecho en `OpticentroZKTeco.Setup` → Build.
3. El `.msi` queda en `installer\OpticentroZKTeco.Setup\Release\`.
4. Seguir el checklist de testing en VM limpia descrito en
   `plan-instalador.md` (sección "Plan de testing exhaustivo"), y correr
   `installer\verify-install.ps1` tras cada instalación de prueba.

Referencia completa de diseño, justificaciones y decisiones abiertas:
ver `D:\OpticentroZKTeco\plan-instalador.md`.
