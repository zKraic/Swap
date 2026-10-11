# Recuperación

Los respaldos se crean antes de cada instalación o limpieza. Están en `%LOCALAPPDATA%\Swap\Sessions\<sesión>\backup`; en modo USB, `Data\Sessions`. No sustituyen una copia del sistema ni de tus archivos.

## Cambio detenido o reinicio pendiente

1. Deja terminar la operación de Windows. No elimines DriverStore manualmente ni repitas herramientas de limpieza.
2. Consulta Recuperación y Detalles técnicos. `state.json` y `journal.jsonl` conservan el paso y el resultado.
3. Si pide reinicio, guarda tu trabajo y usa el botón con confirmación o reinicia normalmente.
4. Swap se abre al iniciar sesión para retomar la verificación y la limpieza pendiente. Windows puede pedir UAC nuevamente. Se comprueba que el equipo y la GPU sean los mismos, que haya un arranque nuevo y que el driver esté firmado, activo y sin error PnP. No vuelve a ejecutar el instalador guardado. Si cancelas UAC, puedes abrir el EXE más adelante; se conserva la sesión pendiente.
5. Para un respaldo anterior, abre **Recuperación**, selecciónalo y pulsa **Restaurar este respaldo**. Swap comprueba firma/compatibilidad y respalda el actual antes de intentar incorporar el INF.

Windows decide qué driver puede activarse. Si el antiguo tiene menor prioridad, Swap informa el resultado pendiente y no fuerza su activación. Para un downgrade deliberado usa la opción Revertir al controlador anterior del Administrador de dispositivos, si existe, o las instrucciones del fabricante. Un INF no restaura aplicaciones, audio HDMI ni perfiles.

Las entradas demo son ficticias y no se pueden restaurar en tu equipo. Si el catálogo no ofrece un paquete y la GPU sigue con Basic Display o error PnP, no se elimina nada; importa el driver oficial/OEM firmado o consulta soporte del equipo.

## Sin escritorio operativo

Una pantalla negra también puede deberse a cableado, salida de video, alimentación, BIOS o hardware. Swap requiere Windows operativo. Sigue el soporte del equipo/GPU.

Windows Recovery Environment permite **Solucionar problemas → Opciones avanzadas → Configuración de inicio → Reiniciar → Modo seguro**. Puede pedir clave BitLocker y contraseña; no guardes esas claves en Swap ni en GitHub. Si existe un punto de restauración anterior, utiliza Restaurar sistema. Swap no modifica BCD ni fuerza modo seguro.

- [Opciones de recuperación de Windows](https://support.microsoft.com/windows/recovery-options-in-windows-31ce2444-7de3-818c-d626-e3b5a3024da5)
- [Configuración de inicio](https://support.microsoft.com/windows/windows-startup-settings-1af6ec8c-4d4a-4b23-adb7-e76eef0b8470)
- [AMD Cleanup Utility](https://www.amd.com/en/resources/support-articles/faqs/GPU-601.html), externa, no ejecutada automáticamente por Swap.

---

KraicDev · Swap · Copyright © 2026 KraicDev.
