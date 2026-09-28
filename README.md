# OpticentroZKTeco

Servicio de Windows (.NET Framework 4.8, x86) que descarga las marcaciones de un reloj
biométrico ZKTeco y las sincroniza con el ERP de Opticentro.

## Estructura

- `src/` — Domain, Application, Infrastructure y el servicio (`OpticentroZKTeco.Service`).
- `test/` — pruebas de la capa de aplicación.
- `installer/` — proyecto de instalación (.vdproj) y sus Custom Actions.
- `lib/`, `sdk/` — DLLs del SDK de ZKTeco (zkemkeeper), necesarias para compilar y ejecutar.

Detalles de requisitos e instalador: `requirements.md`, `requirements_horario.md`,
`plan-instalador.md`.

## Configuración

Antes de compilar, edita `src/OpticentroZKTeco.Service/App.config` y reemplaza los valores de ejemplo:

| Clave | Descripción |
|---|---|
| `ZktecoIp` | IP del reloj biométrico ZKTeco. |
| `ZktecoPuerto` | Puerto del reloj (por defecto `4370`). |
| `ZktecoPassword` | Contraseña de comunicación del reloj (vacío si no tiene). |
| `ErpEndpointUrl` | URL del endpoint del ERP que recibe las marcaciones. |
| `ErpApiKey` | API key del ERP (se envía en el header `X-Api-Key`). |
| `HoraSincronizacion` | Hora diaria de sincronización (`HH:mm`). |
| `DiasRecuperacion` | Días hacia atrás que se recuperan en cada sincronización. |

No subas tus valores reales al repositorio.
