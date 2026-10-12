# Contribuir

Necesitas Windows y .NET SDK 10 con soporte del destino net10.0-windows. No hay paquetes externos de UI ni framework de pruebas. Visual Studio 2026 compatible con la banda de tu SDK y con desarrollo de escritorio .NET es el IDE recomendado; también se puede usar VS Code con C# y la CLI de .NET. Consulta la [compatibilidad oficial de SDK/Visual Studio](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs).

1. Ejecuta `./scripts/Test.ps1`.
2. Para el inventario de lectura opcional: `./scripts/Test.ps1 -ReadOnlyInventory`.
3. Ejecuta `./scripts/Build.ps1` para generar el portable.
4. Prueba la UI con `Swap.exe --demo` (incluye una limpieza profunda simulada con deshacer).
5. Solo en la VM aislada `SWAPTESTVM`: `Swap.Tests.exe --deep-clean-lab=<carpeta con paquetes nv*.inf>` ejecuta la limpieza profunda real y su deshacer. Nunca en tu PC.

Las pruebas no deben elevar privilegios, tocar drivers reales, reiniciar, cambiar red/registro/arranque ni ejecutar instaladores externos en el host. Los cambios del motor requieren pruebas de fallos y revisión del alcance. No declares que una prueba simulada certifica una GPU real.

No subas Data, informes personales, drivers propietarios, bin/obj, claves ni credenciales. Distribuye el EXE único y su SHA-256 en Releases cuando el mantenedor lo decida. Las contribuciones se entregan bajo GPL-3.0-or-later.

---

KraicDev · Swap · Copyright © 2026 KraicDev.
