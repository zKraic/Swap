# Fuentes oficiales y decisiones

Consultadas el 11 de octubre de 2026. Las APIs son oficiales; Swap no está certificado por Microsoft ni por los fabricantes. Las páginas, versiones y paquetes pueden cambiar.

| Fuente | Uso en Swap |
|---|---|
| [Windows Update Agent: buscar, descargar e instalar](https://learn.microsoft.com/en-us/windows/win32/wua_sdk/searching--downloading--and-installing-updates) | API del flujo automático. Búsqueda de drivers aplicables, colección concreta, descarga e instalación; comprueba resultado general e individual. |
| [IUpdateSearcher.Search](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdatesearcher-search) | Filtro `Type='Driver'`, no instalados ni ocultos; servicio configurado, sin cambiar políticas. |
| [IWindowsDriverUpdate](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nn-wuapi-iwindowsdriverupdate) | Clase Display, ID de hardware, fabricante y fecha. Contrasta el ID con las propiedades reales del dispositivo. |
| [IUpdateInstaller](https://learn.microsoft.com/en-us/windows/win32/wua_sdk/iupdateinstaller-properties) | Colección de una oferta, control de operación ocupada y reinicio previo; sin prompts de origen. |
| [IUpdateInstaller2.ForceQuiet](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdateinstaller2-put_forcequiet) | Instalación silenciosa documentada; no supone parámetros de instaladores de fabricantes. |
| [PnPUtil](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax) | Exportar respaldos, instalar INF y retirar OEM sin fuerza. Windows conserva ranking y protección de paquetes en uso. |
| [Get-WindowsDriver](https://learn.microsoft.com/en-us/powershell/module/dism/get-windowsdriver) | Inventario estructurado del almacén, sin analizar texto de consola localizado. |
| [Get-AuthenticodeSignature](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.security/get-authenticodesignature?view=powershell-5.1) | Confianza de EXE/catálogos en la ruta local; no es certificación de compatibilidad o juegos. |
| [Publicación en archivo único](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) | EXE autocontenido con bibliotecas nativas, sin trimming WPF, posible extracción temporal. |
| [Soporte de .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) | .NET 10 LTS; republicar para actualizar el runtime. |
| [Diseño Windows 11](https://learn.microsoft.com/en-us/windows/apps/design/design-principles) | Jerarquía clara, pocos comandos, estados visibles, tipografía y superficies sobrias. |
| [Buenas prácticas de apps Windows](https://learn.microsoft.com/en-us/windows/apps/get-started/best-practices) | Ventana familiar, entradas por teclado, tamaños/DPI y coherencia con el sistema. |
| [Ajustes de apps](https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings) | Preferencias secundarias fuera del flujo principal; idioma y apariencia se aplican inmediatamente. |
| [NVIDIA](https://www.nvidia.com/en-us/drivers/) · [AMD](https://www.amd.com/en/support/download/drivers.html) · [Intel](https://www.intel.com/content/www/us/en/download-center/home.html) | Paquetes completos y ramas del fabricante para importación local; no incluidos en la distribución. |
| [AMD instalación](https://www.amd.com/en/resources/support-articles/faqs/RS-INSTALL.html) | El paquete completo puede incluir componentes y requisitos propios; no equiparar driver Display con toda la suite. |
| [Windows Sandbox](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file) | Configuración de demo con carpeta del host de solo lectura, red y vGPU deshabilitadas. No disponible para ejecutar aquí. |

La identidad se generó específicamente para Swap: símbolo geométrico de intercambio en azul y blanco. El PNG transparente original y el ICO multirresolución están en `assets`. No se copian iconos ni marcas de fabricantes; los glifos de navegación usan la fuente de sistema Segoe MDL2 Assets.

---

KraicDev · Swap · Copyright © 2026 KraicDev.
