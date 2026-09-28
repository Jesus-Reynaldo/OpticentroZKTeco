# Filtro git "ocultar-secretos": reemplaza valores sensibles de App.config por placeholders al hacer commit.
s/(key="ErpEndpointUrl" value=")[^"]*"/\1https:\/\/REEMPLAZAR-ENDPOINT-CLIENTE.example.com\/api\/asistencia\/marcaciones\/biometrico"/
s/(key="ErpApiKey" value=")[^"]*"/\1REEMPLAZAR-API-KEY"/
s/(key="ZktecoIp" value=")[^"]*"/\10.0.0.0"/
s/(key="ZktecoPassword" value=")[^"]*"/\1"/
