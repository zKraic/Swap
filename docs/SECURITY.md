# Seguridad

Swap solicita UAC al abrir normalmente; no almacena credenciales ni automatiza la aceptación del aviso. Denegar permite análisis sin cambios de drivers. Demo y modo de lectura no solicitan UAC. La acción principal autoriza el flujo y aceptación de licencia descritos junto al botón. Importaciones locales y recuperación requieren confirmación propia; los reinicios requieren confirmación y no usan cierre forzado.

## Protección de operaciones

- El cambio automático solo instala y retira drivers de clase Display. Los nombres OEM admiten exclusivamente `oem` más dígitos ASCII y `.inf`; no comodines, rutas o argumentos añadidos.
- No `/force`, `/uninstall`, reinicios automáticos ni cambios de arranque, modo seguro, seguridad, red, clocks, voltajes o políticas Windows Update. Solo se retira la entrada de un dispositivo **desconectado** de la marca retirada cuando Windows lo exige para borrar su paquete; un dispositivo conectado nunca se retira.
- Limpieza profunda: solo marcas sin hardware conectado, con evidencia de hardware (IDs de instancia e INF) y listas fijas de ubicaciones, servicios, apps y claves por marca. Antes de cada cambio exporta paquetes y registro, guarda tareas e inicios y mueve carpetas a una cuarentena sellada con SHA-256. Rechaza raíces de unidad, Windows, Program Files, perfiles y enlaces; no sigue uniones al borrar cachés.
- Los respaldos que pueden restaurar servicios o registro viven en `%ProgramData%\Swap\Cleanup`, con ACL protegida para Administradores y SYSTEM; si la carpeta existe con otro propietario o es un enlace, la limpieza se detiene. Deshacer lee el manifiesto de esa carpeta y no acepta rutas fuera de ella; una cuarentena alterada no se restaura.
- Desinstaladores opcionales del fabricante: solo entradas de HKLM cuyo programa está en Program Files o Windows, o productos Windows Installer por GUID. Nunca entradas de HKCU (escribibles sin elevación).
- WUA busca en el servicio configurado, identifica ofertas aplicables por ID de hardware real y vuelve a comprobar identidad, revisión y metadatos antes del uso. No instala otras actualizaciones de Windows ni acepta archivos arbitrarios como ofertas.
- La descarga usa Windows Update, cuya validación de confianza y transporte administra Windows. No hay scraping ni mirrors en el flujo principal.
- Importación: INF Display, catálogo local sin traversal, firma de confianza, versión e IDs compatibles. EXE firmado del fabricante y metadatos reconocidos. Firma no equivale a compatibilidad ni estabilidad.
- SHA-256 y bloqueo de escritura durante uso de paquetes locales; rechazo de enlaces simbólicos/reparse points dentro del árbol.
- Procesos de sistema por ruta nativa absoluta y argumentos separados; datos para PowerShell en Base64/JSON. Sin ExecutionPolicy Bypass ni comandos construidos desde contenido de drivers.
- Identificación de máquina y revalidación antes de actuar; respaldo obligatorio y análisis nuevo antes de cada retirada. GPU presentes conservan sus drivers; en laptops no se retiran anteriores.
- Mutex global entre instancias de Swap. No interrumpe otros programas: un cambio externo detiene la limpieza.

El descargador HTTPS auxiliar reconoce dominios AMD/NVIDIA/Intel, puerto 443, sin credenciales ni redirecciones externas; no es el mecanismo principal de resolución automática. Archivos no firmados no pasan la importación.

## Límites

El EXE de Swap no tiene firma comercial. Un hash acredita integridad respecto al archivo publicado, no identidad del editor. No desactives SmartScreen u otras medidas para ejecutar una copia de origen dudoso. Malware con permisos de administrador supera este modelo.

Punto de restauración: puede no estar disponible por política, configuración o frecuencia. Se registra y se conserva el respaldo de paquetes; no activa la protección del sistema. Ese respaldo no cubre perfiles, aplicaciones, archivos personales ni una imagen completa de Windows.

La cancelación espera el final de la operación actual de Windows. Pérdida de energía, disco averiado, reinicio externo o fallo del instalador pueden dejar cambios parciales. La app no garantiza restauración completa, cero errores ni estabilidad en juegos. Los drivers de catálogo pueden ser más antiguos que los del fabricante.

## Datos

Sin telemetría propia, cuentas ni envío automático de informes. Windows Update conserva su comportamiento de red/privacidad y contacta su servicio configurado. Abrir fuentes o el fabricante usa el navegador. Las sesiones incluyen UUID e IDs para no aplicar un plan a otro equipo; DirectX y eventos pueden incluir nombres/rutas. Revisa los informes antes de compartir. Perfil, Data, artifacts y dist no se incluyen en el código fuente.

---

KraicDev · Swap · Copyright © 2026 KraicDev.
