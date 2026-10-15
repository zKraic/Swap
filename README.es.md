# Swap · KraicDev

**Cambia de marca de tarjeta gráfica en Windows sin dejar restos.** NVIDIA, AMD e Intel Arc. Antes del cambio Swap lo prepara todo; después instala el driver nuevo y retira lo que dejó la tarjeta anterior, con respaldo y botón de deshacer. Gratis, de código abierto, portable, sin cuenta ni suscripción.

[Página](https://zkraic.github.io/Swap/) · [Descargar](https://github.com/zKraic/Swap/releases/latest) · [English](README.md)

**Swap 1.1 · Windows 10/11 x64 · probado en un cambio de tarjeta real.** Se reemplazó una GeForce GTX 1060 por una Radeon RX 7600 con **Voy a cambiar de tarjeta**: Swap se abrió solo al iniciar sesión, sin aviso de UAC, verificó el driver de AMD que instaló Windows, retiró los dispositivos, paquetes, servicios, carpetas, entradas de Programas y entradas de protección PnP de NVIDIA, y Direct3D 12, Vulkan y OpenCL vieron la tarjeta nueva. Además, cada EXE publicado se valida tal cual en CI, en un Windows aislado y con la auditoría de la interfaz. [Resultados](docs/validation/V100-RESULTS.md) · [Comparación con DDU](docs/COMPARISON-DDU.md). Aún sin firma de código: la firma de confianza depende de aprobación externa ([política](docs/SIGNING.md)).

![Interfaz real de Swap en modo demo](.github/media/app-dark.png)

Con la nueva tarjeta conectada, **Instalar y limpiar** busca un driver de pantalla compatible en Windows Update, lo descarga, respalda los paquetes actuales, instala y verifica el driver activo firmado y Direct3D en hardware. Solo después limpia la marca que ya no está conectada. Conserva GPU presentes, integradas y configuraciones híbridas.

Windows guarda la tarjeta retirada como *dispositivo desconectado* que sigue usando su paquete, y se niega a borrar ese paquete. Swap retira primero solo esa entrada desconectada de la misma marca y luego el paquete. (Hasta la 0.4.2 la limpieza se detenía aquí en un cambio real.)

## Voy a cambiar de tarjeta

Pulsa **Voy a cambiar de tarjeta** antes de apagar. Swap respalda los drivers gráficos y guarda cómo ven la tarjeta actual los juegos y las pantallas. Pulsa **Apagar el equipo** en Swap (apagado completo, para poder desconectarlo de la corriente), cambia la tarjeta y enciende: al iniciar sesión Swap se abre solo con permisos de administrador y sin aviso de UAC, reconoce la nueva, te da 20 segundos para cancelar, instala su driver, retira la marca anterior con respaldo y te entrega un **informe de validación**. Si la red aún se está conectando o Windows instala el mismo driver por su cuenta, Swap espera y reintenta hasta ocho minutos, y conserva el driver que instaló Windows una vez verificado. Contempla que la tarjeta anterior siga conectada, que no aparezca la nueva, que haya varias nuevas y que el driver pida reiniciar; nunca apaga ni reinicia sin tu confirmación.

La barra inferior muestra cada paso con su porcentaje, y la limpieza avanza elemento por elemento. Al terminar, el aviso ofrece en un clic la frecuencia máxima de la pantalla o, si Windows Update instaló el driver sin él, instalar el panel opcional del fabricante.

## Software del fabricante

**Diagnóstico → Software y componentes** comprueba lo que los juegos y la tarjeta necesitan además del driver: el panel del fabricante, Visual C++ 2015–2022 (x64 y x86), los archivos de DirectX 9 que usan juegos antiguos, el audio HDMI/DisplayPort de la tarjeta, PhysX en NVIDIA, la programación de GPU acelerada por hardware y si hay un reinicio pendiente. Lo opcional se nombra, nunca se muestra como error.

Si falta **NVIDIA App** o **AMD Software: Adrenalin Edition**, un clic instala la última versión: Swap lee el enlace del instalador en la página oficial del fabricante, lo descarga por HTTPS solo desde el servidor de descargas del propio fabricante, lo rechaza si Windows no confirma una firma válida exactamente de ese fabricante y lo ejecuta (NVIDIA App sin preguntas; el instalador de AMD para que lo completes). Después comprueba que Windows lo registra, borra el instalador y guarda un informe de instalación. Nada se descarga sin tu confirmación. Sin permisos de administrador o en una sesión remota, y con Intel (cuyo sitio no permite descargas automáticas), Swap abre la página oficial.

## Limpieza profunda

![Limpieza profunda en modo demo](.github/media/cleanup-dark.png)

La página **Limpieza** hace lo que normalmente se hace con Display Driver Uninstaller, sin modo seguro, porque la tarjeta antigua ya no está:

| Se retira con evidencia | Se conserva a propósito |
|---|---|
| Dispositivos desconectados de GPU, audio HDMI y USB-C; dispositivos virtuales de la marca | Todo lo de una marca que siga conectada |
| Paquetes de todas las clases gráficas (pantalla, audio HDMI, USB-C, extensiones) y los componentes que crean (OpenCL, componentes de soporte) | Chipset AMD (VEN_1022) y paquetes Intel de red, almacenamiento y plataforma |
| Servicios, tareas e inicios cuyo programa ya no existe o se retira | PhysX (algunos juegos lo necesitan con cualquier GPU) y la carpeta del SDK de GeForce NOW que usan los lanzadores de juegos |
| Archivos que un driver o el instalador del fabricante dejó en System32/SysWOW64, registros de Vulkan/OpenCL, códecs COM y transformaciones de Media Foundation, entradas de protección PnP (también las que Windows 11 reserva a TrustedInstaller) | Los cargadores comunes de Vulkan/OpenCL y runtimes de Microsoft; capas de Steam, OBS, RTSS, Overwolf, Epic o Rockstar |
| Paneles de la Tienda, carpetas del fabricante (a cuarentena), cachés de sombreadores, ajustes y entradas huérfanas de Programas en cualquier idioma de Windows | GPU integrada del procesador y portátiles: aparecen sin marcar |

La propiedad se decide por las instancias de dispositivo, los IDs de hardware de cada INF y el autor del archivo, no por nombres de productos. Cada elemento explica por qué se puede retirar y se puede desmarcar; **Marcar todo** y **Desmarcar todo** ayudan, y lo que desmarcas sigue desmarcado al rehacer el plan. Los programas abiertos de la marca anterior se nombran antes de cerrarlos, y a un programa con ventana primero se le pide que se cierre. Antes de cambiar algo, Swap crea un punto de restauración, exporta paquetes y registro, y mueve las carpetas a una cuarentena sellada en `%ProgramData%\Swap\Cleanup` (solo Administradores y SYSTEM). Las entradas protegidas de Windows se guardan como copia exacta del registro y se retiran con el privilegio de restauración del administrador, sin cambiar sus permisos. Si un programa del fabricante se reinicia y deja una carpeta abierta, Swap lo cierra y reintenta; lo que mantenga abierto un programa ajeno se queda en su sitio. Tras un cambio de tarjeta, una segunda pasada retira lo que la primera dejó huérfano. **Deshacer la última limpieza** restaura todo lo que tiene respaldo. Al terminar, la página muestra **lo que no se pudo retirar y por qué**, en palabras simples, y se guarda un informe de limpieza en tu idioma: lo retirado, lo que no y por qué, y lo que se conservó a propósito. Cuando todo funcione, **Liberar respaldos de limpieza** recupera ese espacio para siempre.

La misma página retira **versiones antiguas sin uso del driver actual** (a menudo varios GB) y conserva la activa y la anterior más reciente.

**Diagnóstico → Juegos** pregunta a Direct3D 12, Vulkan y OpenCL, en un proceso aparte de Swap, qué GPU ven, y avisa si Vulkan u OpenCL siguen apuntando a la marca anterior o no ven la nueva. También detecta una tarjeta con menos carriles PCIe de los que admite (ranura equivocada o mal encajada) y ofrece la frecuencia máxima del monitor, que tras un cambio suele quedar en 60 Hz, con confirmación de 15 segundos para mantener o revertir. El **informe de validación** está en inglés para un issue de GitHub y no lleva identificadores del equipo ni nombres de cuenta. **Crear respaldo** exporta los drivers gráficos sin modificarlos. [Comparación medida con DDU](docs/COMPARISON-DDU.md).

Descarga **[Swap.exe 1.0.0](https://github.com/zKraic/Swap/releases/download/v1.0.0/Swap.exe)** y, si quieres, su [SHA-256](https://github.com/zKraic/Swap/releases/download/v1.0.0/Swap.exe.sha256) (`035C7A91…B9980A`); [notas de la versión](https://github.com/zKraic/Swap/releases/tag/v1.0.0). Ábrelo y acepta UAC. No hay instalador de Swap ni ZIP ni instalación separada de .NET. El diagnóstico no necesita reiniciar; Windows puede exigirlo después de cambiar drivers. Swap guarda la sesión y conserva lo pendiente.

Windows 10/11 x64, PowerShell 5.1, conexión y Windows Update operativo para descarga automática. 4 GB libres más el espacio del driver. El panel del fabricante nunca se instala por su cuenta: solo se ofrece y se instala si lo confirmas. No fuerza retiradas, modo seguro o ajustes de juegos. Bloquea cambios en RDP, durante una sesión de control remoto (TeamViewer mientras alguien está conectado; AnyDesk, RustDesk, Escritorio remoto de Chrome o Parsec siempre que se ejecuten) y mientras Windows espera un reinicio de mantenimiento; los borrados pendientes de archivos sobrantes de instaladores no cuentan. El diagnóstico y respaldo independiente siguen disponibles. Si hay varias gráficas candidatas, exige elegir la nueva.

Seis idiomas según el PC y temas claro/oscuro/sistema. Datos en `%LOCALAPPDATA%\Swap`; una carpeta `Data` escribible junto al EXE permite almacenamiento portable. Sin servicios ni tareas permanentes: un cambio de tarjeta preparado, o un cambio de driver que pide reiniciar, registra una tarea de inicio de sesión para tu cuenta (con RunOnce de respaldo, que pide UAC solo si la tarea no puede ejecutarse). Abre una copia de Swap en `%ProgramData%\Swap\Continuation` que solo los administradores pueden modificar, y Swap borra la tarea al terminar o al cancelar.

Código en `main`. [Contribuir](CONTRIBUTING.md) · [Seguridad](SECURITY.md) · [Recuperación](docs/RECOVERY.md) · [Firma](docs/SIGNING.md).

Copyright © 2026 **KraicDev** · [GPL-3.0-or-later](LICENSE). Las modificaciones distribuidas deben conservar la GPL, avisos y código correspondiente. Permite usos comerciales; la [política de marca](TRADEMARKS.md) no autoriza suplantar al autor ni presentar un fork como oficial. Las descargas oficiales son gratuitas. Display Driver Uninstaller es un proyecto independiente de Wagnardsoft; no se usa ni se incluye, y solo se ejecutó dentro de una máquina virtual aislada para la comparación publicada.

La 1.1.0 añade el panel del fabricante en un clic (página oficial, servidor del fabricante, firma del fabricante), la comprobación de software y componentes, y una limpieza que se puede seguir: marcar o desmarcar todo, elecciones que se mantienen, lo que no se pudo retirar y por qué, e informe de limpieza. La 1.0.0 fue la primera versión validada en un cambio de tarjeta real. [Cambios](CHANGELOG.md) · [Comunidad y pruebas de hardware](docs/COMMUNITY.md) · [Revisión crítica de la 0.5.0](docs/REVIEW-050.md).
