# Arquitectura

## Tecnología

C# + WPF + .NET 10 LTS. Un EXE autocontenido sin MSIX, WebView2, servicio residente ni biblioteca de UI externa. El SDK local en `artifacts/tools` permite compilar sin cambiar el PATH del sistema. El runtime incluido debe actualizarse republicando.

## Componentes

- `Swap.Core`: tipos, `Planner`, `Engine`, `Maintenance`, `DeepCleanPlanner`/`DeepCleanEngine`, lector de INF y de líneas de comando. Reglas y flujos independientes de Windows, probados con fallos simulados.
- `Swap.Windows`: inventario CIM/PnpDevice/DISM y registro de dispositivos (incluidos los desconectados); firma y hashing; PnPUtil; `AutomaticPlatform`, `WindowsCleanupPlatform`, `DemoCleanupPlatform` y scripts embebidos.
- `Swap.App`: WPF, temas por recursos dinámicos, seis idiomas, elevación al iniciar, navegación, biblioteca, recuperación y diagnósticos.
- `Swap.Tests`: ejecutor de pruebas con backend falso; pruebas de rechazo que no escriben drivers y sondas optativas de lectura real.

## Ruta automática

1. UAC al abrir normalmente; denegación permite solo inventario. `--demo`, `--readonly` y `--ui-audit` no solicitan elevación.
2. Una acción principal adquiere un mutex global, analiza otra vez y comprueba permisos, sesión local, reinicio pendiente, disco y destino.
3. Windows Update Agent busca drivers aplicables no instalados ni ocultos, del servicio configurado. Solo admite Display, sin beta ni instalación interactiva. La selección exige GUID/revisión válidos, ID real de hardware/compatible de la GPU y tamaño limitado. Ordena por fecha y versión. No propone downgrade/reinstalación de un driver OEM sano cuya versión ya sea igual o superior.
4. La oferta se conserva solo en memoria: no se ejecuta una URL ni se confía en metadatos de disco. Antes de descargar e instalar, se resuelve de nuevo el GUID/revisión y se comprueba clase, hardware, título y fabricante. Windows comprueba el paquete.
5. Descarga mediante WUA a su caché del sistema; no copia esa caché al USB ni la presenta como paquete portable. Se acepta la licencia dentro de la acción autorizada por el usuario.
6. Revalidación del plan; exportación obligatoria de todos los paquetes OEM Display. Fallo o exportación sin INF detiene el flujo antes de instalar.
7. Solicitud de punto de restauración, instalación silenciosa de la oferta concreta, inventario posterior.
8. La verificación exige firma, problema PnP cero, OEM activo y versión esperada cuando el título la permite conocer. Sin versión, exige cambio real de INF/versión. Un código de éxito no es suficiente.
9. Si requiere reinicio o revisión, preserva los paquetes anteriores y guarda estado pendiente. Nunca retoma escrituras al abrirse.
10. Limpieza solo después de verificar. Antes de cada retirada se analiza otra vez; se protegen GPU presentes, chipset, audio, paquetes críticos y clases ajenas a Display. La tarjeta retirada sigue registrada como dispositivo desconectado que usa su paquete y Windows rechaza borrarlo; antes se retira solo esa entrada desconectada de la misma marca (`pnputil` con la opción de retirar dispositivo). No usa `/force`. Inventario final confirma el resultado.
11. Con el driver nuevo verificado, la limpieza profunda (si está activada en Ajustes) retira los restos de las marcas desconectadas con la selección por defecto. Un problema ahí se registra y no convierte el cambio verificado en fallo.

Sin oferta, `Maintenance` exige un driver OEM firmado y sano, respalda, revalida identidad y estado, retira solo paquetes elegibles y comprueba la retirada. Nunca llama a instalación. Si el driver es básico o tiene errores, se informa que no hay paquete compatible sin eliminar nada.

En laptops se permite únicamente la ruta de catálogo y se preservan todos los paquetes anteriores; esto no certifica cada configuración OEM. Varias GPU de destino de una marca requieren revisión individual. La presencia de una GPU de origen preserva sus drivers y no impide actualizar el destino.

## Paquetes locales y recuperación

EXE: Authenticode válido del fabricante y metadatos de gráficos reconocidos; conserva UI/EULA del fabricante. INF: clase Display, catálogo local válido y firmado, ID PCI compatible y versión. No utiliza opciones silenciosas supuestas. Hash SHA-256 de archivo/árbol, revalidación y bloqueo contra escritura durante la operación. Windows valida el catálogo al incorporar el INF.

El JSON de biblioteca es información para la lista, no prueba de confianza. Al instalar se vuelve a inspeccionar el paquete real. Recuperación aplica un plan de la misma marca que conserva los paquetes actuales. No fuerza downgrades.

## Limpieza profunda

`DeepCleanPlanner` recibe un inventario completo (dispositivos presentes y desconectados con su INF, paquetes OEM con los IDs de hardware de sus secciones de modelos, servicios, tareas, inicios, entradas de Programas, apps de la Tienda, carpetas, claves y procesos) y una lista de marcas. Nunca planifica una marca conectada.

- Dominio estricto de GPU: NVIDIA `PCI\VEN_10DE`, `HDAUDIO\FUNC_01&VEN_10DE`, `SWC\VEN_10DE`, `USB\VID_0955`, `ACPI\NVDA`; AMD `PCI\VEN_1002`, `HDAUDIO\FUNC_01&VEN_1002`, `SWC\VEN_1002`. El chipset AMD (`VEN_1022`) nunca coincide. Intel no tiene dominio estricto: solo paquetes Display, audio de pantalla (`HDAUDIO\FUNC_01&VEN_8086&DEV_28xx`) y extensiones o componentes cuyos IDs son todos de una GPU Intel conocida.
- Paquete retirable: proveedor exacto de la marca, clase gráfica permitida, no crítico de arranque, todos sus IDs en el dominio (o solo usado por dispositivos desconectados retirables) y sin dispositivos conectados que lo usen. IDs mixtos se conservan.
- Servicios, tareas, inicios y entradas de Programas solo si su programa desaparece con la limpieza o ya no existe en una ubicación del fabricante. Un servicio que usa un dispositivo conectado se conserva.
- Carpetas fijas por marca. PhysX se protege. La GPU integrada de la marca del procesador y los portátiles quedan sin marcar.

`DeepCleanEngine` vuelve a inventariar y planificar antes de actuar: solo ejecuta elementos que siguen siendo elegibles y se detiene si la marca volvió a conectarse o si es otro equipo. Orden: procesos, desinstaladores opcionales, dispositivos, paquetes (exportados y sellados antes), servicios (exportados), tareas (XML), inicios (valor), apps de la Tienda, carpetas (cuarentena sellada en el mismo volumen), cachés, claves y entradas de Programas (exportadas). Un fallo de un elemento no detiene los demás; un respaldo fallido impide borrar ese elemento. El inventario final marca lo que sigue presente.

Los respaldos que podrían restaurar servicios o registro se guardan en `%ProgramData%\Swap\Cleanup`, con ACL protegida solo para Administradores y SYSTEM; `undo.json` vive allí y las rutas de respaldo se derivan de esa carpeta, nunca de la copia informativa de la sesión. Deshacer valida el sellado de la cuarentena antes de devolverla.

## Persistencia

Por defecto `%LOCALAPPDATA%/Swap`; si existe `Data` junto al EXE al iniciar, se usa esa carpeta. Demo tiene un subdirectorio separado. `Vault` guarda importaciones, `Sessions` inventarios/plan/estado/diario/respaldos y `Reports` los diagnósticos. Estado: temporal más reemplazo. El diario se escribe antes de cada operación. Ayuda y licencia están embebidas y se extraen bajo `Help` a petición.

## Concurrencia y cancelación

Mutex global para operaciones reales de Swap; mutex separado para demo. No elimina las carreras con otras herramientas externas o Windows Update: revalida y nunca fuerza retiradas. Cancelación entre operaciones; no mata instalación, servicing ni PnPUtil. Mientras Windows trabaja, impide cerrar. La UI muestra etapas reales e indicador indeterminado, sin porcentajes inventados. Una interrupción externa puede dejar cambios parciales: no hay transacción atómica ni rollback total.

---

KraicDev · Swap · Copyright © 2026 KraicDev.
