<#
.SYNOPSIS
    Verificación post-instalación del instalador de OpticentroZKTeco.
    Complementa (no reemplaza) el testing manual del wizard/MessageBoxes
    descrito en plan-instalador.md.

.DESCRIPTION
    Corre en la máquina donde se instaló el .msi. Requiere PowerShell con
    privilegios suficientes para consultar servicios y el registro (no
    necesita ser administrador para las lecturas, sí para Start-Service
    si el servicio está detenido).
#>

$ErrorActionPreference = "Stop"
$installDir = "C:\Program Files (x86)\OpticentroZKTeco"
$exeConfig = Join-Path $installDir "OpticentroZKTeco.Service.exe.config"
$logPath = "C:\ProgramData\OpticentroZKTeco\log\asistencia.log"

function Write-Section($titulo) {
    Write-Host ""
    Write-Host "== $titulo ==" -ForegroundColor Cyan
}

Write-Section "Servicio Windows"
$svc = Get-Service -Name "OpticentroZKTeco" -ErrorAction SilentlyContinue
if (-not $svc) {
    throw "Servicio 'OpticentroZKTeco' no encontrado. ¿Se instaló correctamente?"
}
Write-Host "Nombre: $($svc.Name)"
Write-Host "Status: $($svc.Status)"
Write-Host "StartType: $($svc.StartType)"
if ($svc.StartType -ne "Automatic") {
    Write-Warning "StartType esperado 'Automatic', encontrado '$($svc.StartType)'."
}

Write-Section "App.config instalado"
if (-not (Test-Path $exeConfig)) {
    throw "No se encontró '$exeConfig'."
}
[xml]$xml = Get-Content $exeConfig
$ip = ($xml.configuration.appSettings.add | Where-Object key -eq "ZktecoIp").value
$puerto = ($xml.configuration.appSettings.add | Where-Object key -eq "ZktecoPuerto").value
$hora = ($xml.configuration.appSettings.add | Where-Object key -eq "HoraSincronizacion").value
Write-Host "ZktecoIp = $ip"
Write-Host "ZktecoPuerto = $puerto"
Write-Host "HoraSincronizacion = $hora"
if ([string]::IsNullOrWhiteSpace($ip) -or $ip -eq "0.0.0.0") {
    Write-Warning "ZktecoIp parece no haber sido sobrescrito por el instalador (sigue en placeholder)."
}

Write-Section "Registro COM (zkemkeeper)"
$clsidHits = & reg query "HKLM\SOFTWARE\Classes\WOW6432Node\CLSID" /s /f "zkemkeeper" 2>$null
if (-not $clsidHits) {
    Write-Warning ("No se encontraron entradas CLSID para 'zkemkeeper' bajo WOW6432Node. " +
        "Verificar manualmente si el .msi es x86 sobre Windows x64 (se esperaría redirección a SysWOW64).")
} else {
    Write-Host "Se encontraron entradas CLSID para 'zkemkeeper'."
}

Write-Section "Ubicación física de las DLLs del SDK"
$system32Path = Join-Path $env:WINDIR "System32\zkemkeeper.dll"
$sysWow64Path = Join-Path $env:WINDIR "SysWOW64\zkemkeeper.dll"
Write-Host "System32: $(if (Test-Path $system32Path) { 'presente' } else { 'ausente' }) ($system32Path)"
Write-Host "SysWOW64: $(if (Test-Path $sysWow64Path) { 'presente' } else { 'ausente' }) ($sysWow64Path)"

Write-Section "Arranque del servicio (prueba de excepción COM)"
try {
    if ($svc.Status -ne "Running") {
        Start-Service -Name "OpticentroZKTeco"
        Start-Sleep -Seconds 5
    }
    $svc.Refresh()
    Write-Host "Status tras intentar iniciar: $($svc.Status)"
} catch {
    Write-Warning "Fallo al iniciar el servicio: $($_.Exception.Message)"
    Write-Warning ("Si el error menciona una excepción COM (0x80040154, 'Clase no registrada'), " +
        "el registro de zkemkeeper.dll no se completó correctamente.")
}

Write-Section "Últimas líneas del log"
if (Test-Path $logPath) {
    Get-Content $logPath -Tail 20
} else {
    Write-Warning "No se encontró '$logPath' (el servicio puede no haber corrido aún)."
}

Write-Host ""
Write-Host "Verificación automática completa. Recuerde complementar con el testing manual" -ForegroundColor Green
Write-Host "del wizard (diálogo de IP/Puerto, MessageBoxes de éxito/fallo de conexión," -ForegroundColor Green
Write-Host "desinstalación y upgrade) descrito en plan-instalador.md." -ForegroundColor Green
