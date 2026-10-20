# KeepAwake

Utilidad portátil para Windows que mantiene el equipo y la pantalla despiertos mientras está activada. Útil durante descargas, presentaciones o procesos largos.

## Uso

1. Abre `KeepAwake.exe` y pulsa **Activar**.
2. Ajusta el intervalo de renovación entre 5 y 3600 segundos si lo necesitas (30 por defecto).
3. Cierra la ventana o pulsa **A la bandeja** para dejarla junto al reloj.
4. Haz doble clic en el icono para volver a abrirla. También puedes activar o desactivar desde su menú contextual.
5. Pulsa **Desactivar** para liberar la solicitud de energía o **Salir** para cerrar la aplicación.

Arranca desactivada. El intervalo se guarda en `KeepAwake.settings.json`, junto al ejecutable. La carpeta debe permitir escritura para conservar ese ajuste.

KeepAwake solicita a Windows que mantenga el sistema y la pantalla despiertos mediante `SetThreadExecutionState`. No mueve el ratón ni simula teclas. No modifica las políticas de bloqueo o seguridad del equipo.

## Requisitos

- Windows x64 compatible con .NET 8.
- El ejecutable publicado es autónomo: no requiere instalar .NET.
- Para compilar: SDK de .NET 8 en Windows.

## Compilar y verificar

```powershell
dotnet publish KeepAwake.csproj -c Release -r win-x64 --self-contained true -o publish
$test = Start-Process -FilePath .\publish\KeepAwake.exe -ArgumentList '--self-test' -PassThru -Wait -WindowStyle Hidden
Get-Content .\publish\verification.txt
if ($test.ExitCode -ne 0) { throw 'La verificación falló' }
```

La verificación integrada comprueba la ventana, el icono de bandeja, el intervalo, la activación, la renovación, la desactivación y la reapertura desde la bandeja.

## Historial

Los cambios se registran en Git y se resumen en [CHANGELOG.md](CHANGELOG.md). El primer commit conserva el código original; el siguiente aplica el nombre KeepAwake.
