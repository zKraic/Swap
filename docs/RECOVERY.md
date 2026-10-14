# Recuperación

Los respaldos se crean antes de cada instalación o limpieza. Los del cambio de driver están en `%LOCALAPPDATA%\Swap\Sessions\<sesión>\backup` (en modo USB, `Data\Sessions`); los de la limpieza profunda, en `%ProgramData%\Swap\Cleanup`. No sustituyen una copia del sistema ni de tus archivos.

## Cambio detenido o reinicio pendiente

1. Deja terminar la operación de Windows. No elimines DriverStore manualmente ni repitas herramientas de limpieza.
2. Consulta Recuperación y Detalles técnicos. `state.json` y `journal.jsonl` conservan el paso y el resultado.
3. Si pide reinicio, guarda tu trabajo y usa el botón con confirmación o reinicia normalmente.
4. Swap se abre al iniciar sesión para retomar la verificación y la limpieza pendiente, con una tarea de inicio de sesión que no pide UAC; si la tarea no pudo registrarse, el RunOnce de respaldo pide UAC. Se comprueba que el equipo y la GPU sean los mismos, que haya un arranque nuevo y que el driver esté firmado, activo y sin error PnP. No vuelve a ejecutar el instalador guardado. Si cancelas UAC, puedes abrir el EXE más adelante; se conserva la sesión pendiente.
5. Para un respaldo anterior, abre **Recuperación**, selecciónalo y pulsa **Restaurar este respaldo**. Swap comprueba firma/compatibilidad y respalda el actual antes de intentar incorporar el INF.

Windows decide qué driver puede activarse. Si el antiguo tiene menor prioridad, Swap informa el resultado pendiente y no fuerza su activación. Para un downgrade deliberado usa la opción Revertir al controlador anterior del Administrador de dispositivos, si existe, o las instrucciones del fabricante. Un INF no restaura aplicaciones, audio HDMI ni perfiles.

Las entradas demo son ficticias y no se pueden restaurar en tu equipo. Si el catálogo no ofrece un paquete y la GPU sigue con Basic Display o error PnP, no se elimina nada; importa el driver oficial/OEM firmado o consulta soporte del equipo.

## Deshacer una limpieza profunda

1. Abre **Limpieza** y pulsa **Deshacer la última limpieza**. Swap restaura, del más reciente al más antiguo, las claves de registro y entradas de Programas, registros de Vulkan/OpenCL, archivos devueltos a System32/SysWOW64 (con su entrada de protección PnP), servicios, tareas, inicios automáticos, carpetas en cuarentena y paquetes de driver (se vuelven a añadir al almacén). Un archivo no se devuelve si ya existe otro con el mismo nombre en su lugar.
2. Reinicia para que Windows vuelva a cargar los servicios restaurados.
3. No vuelven por esta vía: entradas de dispositivos desconectados (Windows las crea de nuevo si el hardware regresa), apps de la Tienda (reinstálalas desde la Tienda) y cachés (se regeneran).

Los respaldos están en `%ProgramData%\Swap\Cleanup\<sesión>`, accesibles solo para administradores. Si una cuarentena fue modificada, no se restaura y la sesión lo indica. Para liberar ese espacio cuando ya no necesites deshacer, pulsa **Liberar respaldos de limpieza** en la página Limpieza: muestra el tamaño, pide confirmación y borra los respaldos y la cuarentena de las limpiezas anteriores (incluidos los archivos protegidos que se movieron con privilegio). Después esas limpiezas ya no se pueden deshacer. Una carpeta movida solo en parte (porque otro programa tenía archivos abiertos) vuelve junto a lo que quedó al deshacer. Windows también conserva el punto de restauración creado antes de la limpieza, si la protección del sistema está activa.

## Cambio de tarjeta preparado

Si pulsaste **Voy a cambiar de tarjeta** y al final no cambias la tarjeta, abre Swap y pulsa **Cancelar el cambio**: se borran la preparación, la tarea de inicio de sesión y la entrada RunOnce; el respaldo se conserva en Recuperación. Si Swap no puede abrirse, borra la tarea con `schtasks /Delete /TN "\KraicDev\Swap after card change" /F` en una consola de administrador. Si al encender no aparece la tarjeta nueva, Swap lo avisa y vuelve a comprobar en el siguiente inicio; tras 10 inicios sin una tarjeta nueva la preparación caduca. Con la tarjeta anterior otra vez conectada, Swap solo espera.

## Sin escritorio operativo

Una pantalla negra también puede deberse a cableado, salida de video, alimentación, BIOS o hardware. Swap requiere Windows operativo. Sigue el soporte del equipo/GPU.

Windows Recovery Environment permite **Solucionar problemas → Opciones avanzadas → Configuración de inicio → Reiniciar → Modo seguro**. Puede pedir clave BitLocker y contraseña; no guardes esas claves en Swap ni en GitHub. Si existe un punto de restauración anterior, utiliza Restaurar sistema. Swap no modifica BCD ni fuerza modo seguro.

- [Opciones de recuperación de Windows](https://support.microsoft.com/windows/recovery-options-in-windows-31ce2444-7de3-818c-d626-e3b5a3024da5)
- [Configuración de inicio](https://support.microsoft.com/windows/windows-startup-settings-1af6ec8c-4d4a-4b23-adb7-e76eef0b8470)
- [AMD Cleanup Utility](https://www.amd.com/en/resources/support-articles/faqs/GPU-601.html), externa, no ejecutada automáticamente por Swap.

---

KraicDev · Swap · Copyright © 2026 KraicDev.
