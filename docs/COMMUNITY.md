# Necesidades de la comunidad y funciones de Swap

KraicDev · Consulta del 11 de octubre de 2026.

Se revisaron ejemplos públicos de problemas de usuarios. Son señales cualitativas para elegir funciones, no una encuesta representativa ni una prueba de que un problema afecte a todos los equipos. Los consejos de los comentarios no se ejecutan en el PC del usuario.

| Necesidad observada | Función añadida |
|---|---|
| Windows sustituye un driver y resulta difícil saber qué cambió | Detección de cambios respecto a un punto de control: versión, INF, estado PnP y firma. Un cambio de versión sano se presenta como cambio, no como fallo. Acceso directo a los respaldos existentes. |
| Monitor conectado a otra GPU o funcionando con Hz inferiores | Comprobación de salidas activas, GPU de salida, resolución y frecuencia actual; muestra el máximo de los modos disponibles para esa resolución, profundidad y tipo de barrido. No cambia modos de pantalla ni afirma conocer el cable o la frecuencia física máxima del monitor. |
| Pantallas negras y reinicios del driver sin información útil | Resumen de eventos Display 4101 y LiveKernelEvent 117/141/142 de los últimos siete días. Los informes estructurados omiten mensajes completos, rutas y reportes privados. Un registro vacío no certifica estabilidad. |

La función adicional es una **comparación antes/después del cambio**. Conserva la referencia de la operación, incluso si necesita reiniciar, y señala GPU desaparecida, regresión a un driver genérico/no firmado/con error, pantalla ausente, resolución/Hz inferiores y nuevos eventos gráficos. El punto de control persistente permite detectar cambios al volver a abrir Swap. Se separan equipos reales, demo y distintos identificadores de máquina; el usuario puede aceptar el estado actual como nueva referencia. Esta función no cambia políticas de Windows Update ni elimina drivers por su cuenta.

La comprobación de GPU muestra evidencia distinta para dispositivo presente, error PnP, driver OEM firmado y disponibilidad de Direct3D en hardware. No usa WARP como prueba de GPU; tarjetas idénticas sin identificación individual se muestran sin esa verificación. No prueba alimentación, asiento PCIe, cableado, rendimiento bajo carga ni juegos.

## Referencias de usuarios

La 0.4.0 añade **Crear respaldo**, independiente de la instalación o limpieza. Exporta exclusivamente paquetes OEM Display, genera un manifiesto SHA-256 y comprueba integridad antes de restaurar estos respaldos. No modifica drivers ni promete una imagen completa del sistema. Los respaldos antiguos siguen sujetos a la verificación de firma del paquete.

Los usuarios piden conservar drivers para reinstalar sin conexión: [consulta sobre respaldos](https://www.reddit.com/r/windowsxp/comments/1scap92/best_way_to_backup_installed_drivers_from_old/) y [preparar drivers antes de reinstalar](https://www.reddit.com/r/LinusTechTips/comments/1lmq490/). [Driver Easy](https://www.drivereasy.com/help55/driver-backup/) documenta el respaldo como función de su versión Pro. Swap ofrece su respaldo gráfico gratis. También existen herramientas gratuitas y comandos nativos: no afirmamos que todas las alternativas cobren ni ampliamos la app a actualizaciones indiscriminadas de chipset/red.

- [Recordatorio sobre puerto de GPU y frecuencia de monitor, r/buildapc](https://www.reddit.com/r/buildapc/comments/zufg7e/)
- [Windows sobrescribe un driver AMD, r/AMDHelp](https://www.reddit.com/r/AMDHelp/comments/1rz6shc/windows_overwrote_randomly_my_amd_drivers/)
- [Sustitución repetida de drivers, r/AMDHelp](https://www.reddit.com/r/AMDHelp/comments/11wpywm/windows_update_keeps_replacing_my_amd_graphics/)
- [Errores 141 y recuperación de pantalla, r/buildapc](https://www.reddit.com/r/buildapc/comments/1w8bdes/repeated_livekernelevent_141_nvlddmkmsys_on_rtx/)

## APIs y alcance técnico

- [EnumDisplayDevices](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw): dispositivos de pantalla de la sesión actual; no inspección eléctrica.
- [EnumDisplaySettings](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsa): modos que expone el driver. Los valores 0/1 de frecuencia se muestran como desconocidos.
- [D3D11CreateDevice](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-d3d11createdevice): se pasa el adaptador DXGI concreto con tipo UNKNOWN; no fallback software, ventana, swap chain ni prueba de estrés.
- [TDR de Windows](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/timeout-detection-and-recovery): recuperación del driver y registro de incidentes; un evento no identifica por sí solo la causa física.

Copyright © 2026 KraicDev.
