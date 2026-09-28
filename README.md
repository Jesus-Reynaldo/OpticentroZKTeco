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

## Valores sensibles de `App.config`

`src/OpticentroZKTeco.Service/App.config` se guarda en git con placeholders en `ErpEndpointUrl`,
`ErpApiKey`, `ZktecoIp` y `ZktecoPassword`. Un filtro de git (`tools/ocultar-secretos.sed`) los
reemplaza al hacer commit, así tu copia local puede conservar los valores reales para compilar
el `.msi`. Después de clonar, configura el filtro una vez:

```sh
git config filter.ocultar-secretos.clean "sed -E -f tools/ocultar-secretos.sed"
git config filter.ocultar-secretos.smudge cat
git config filter.ocultar-secretos.required true
```

Luego coloca los valores reales en tu `App.config` local antes de compilar el instalador.
Si agregas otra clave sensible, añádela a `tools/ocultar-secretos.sed`.
