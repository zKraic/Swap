# Swap · KraicDev

**Prepara Windows después de conectar una gráfica de otra marca.** NVIDIA, AMD e Intel Arc. Gratis, sin cuenta ni suscripción.

[Página](https://zkraic.github.io/Swap/) · [Descargar](https://github.com/zKraic/Swap/releases) · [English](README.md)

**Versión experimental 0.4.2.** La instalación y retirada se probaron con un backend simulado. Falta validar un cambio físico de tarjeta; la firma depende de aprobación externa. [Resultados](docs/validation/V042-RESULTS.md).

![Interfaz real de Swap en modo demo](.github/media/app-dark.png)

Con la nueva tarjeta conectada, **Instalar y limpiar** busca un driver de pantalla compatible en Windows Update, lo descarga, respalda los paquetes actuales, instala y verifica el driver activo firmado y Direct3D en hardware. Solo después retira paquetes Display sin uso de marcas ausentes. Conserva GPU presentes, integradas y configuraciones híbridas.

**Nuevo: Crear respaldo** exporta los drivers gráficos sin modificarlos. Comprueba archivos alterados, añadidos o ausentes antes de restaurar. Incluye biblioteca de INF firmados, diagnóstico de GPU/pantallas/Hz, eventos gráficos y comparación antes/después.

Descarga **Swap.exe**, ábrelo y acepta UAC. No hay instalador de Swap ni ZIP ni instalación separada de .NET. El diagnóstico no necesita reiniciar; Windows puede exigirlo después de cambiar drivers. Swap guarda la sesión y conserva lo pendiente.

Windows 10/11 x64, PowerShell 5.1, conexión y Windows Update operativo para descarga automática. 4 GB libres más el espacio del driver. No instala toda la suite del fabricante ni utiliza DDU, limpieza del registro o retirada forzada. Bloquea cambios en RDP, mientras se ejecutan herramientas remotas conocidas (incluido TeamViewer inactivo) y ante reinicios pendientes de Windows. El diagnóstico y respaldo independiente siguen disponibles. Si hay varias gráficas candidatas, exige elegir la nueva.

Seis idiomas según el PC y temas claro/oscuro/sistema. Datos en `%LOCALAPPDATA%\Swap`; una carpeta `Data` escribible junto al EXE permite almacenamiento portable.

Código en `main`. [Contribuir](CONTRIBUTING.md) · [Seguridad](SECURITY.md) · [Recuperación](docs/RECOVERY.md) · [Firma](docs/SIGNING.md).

Copyright © 2026 **KraicDev** · [GPL-3.0-or-later](LICENSE). Las modificaciones distribuidas deben conservar la GPL, avisos y código correspondiente. Permite usos comerciales; la [política de marca](TRADEMARKS.md) no autoriza suplantar al autor ni presentar un fork como oficial. Las descargas oficiales son gratuitas.

La vista de limpieza muestra qué paquetes puede retirar y por qué conserva los demás. Los respaldos del cambio automático se verifican antes de instalar, limpiar y continuar tras un reinicio. [Revisión crítica del código](docs/REVIEW-042.md).

La 0.4.2 repite la verificación gráfica al terminar y guarda un resultado de preparación en la sesión. El informe indica requisitos pendientes y motivos para conservar paquetes. [Comunidad y pruebas de hardware](docs/COMMUNITY.md) · [Demostración](https://www.tiktok.com/@kraicdev/video/7695648273075375380).
