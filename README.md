# KeepAwake

Utilidad portátil para Windows que mantiene el equipo y la pantalla despiertos mientras está activada. Útil durante descargas, presentaciones o procesos largos.

## Uso

1. Abre `KeepAwake.exe` y elige la duración: **15 minutos**, **30 minutos** (por defecto), **1 hora**, **2 horas**, **Personalizado** (1 a 10080 minutos) o **Hasta desactivar**.
2. Pulsa **Activar**. La duración queda fijada para esa sesión; para cambiarla, desactiva y vuelve a activar. El intervalo de renovación (5 a 3600 segundos, 30 por defecto) es independiente de la duración.
3. Cierra la ventana o pulsa **A la bandeja** para dejarla junto al reloj.
4. Haz doble clic en el icono para volver a abrirla. También puedes activar o desactivar desde su menú contextual.
5. Pulsa **Desactivar** para liberar la solicitud de energía o **Salir** para cerrar la aplicación.

Arranca desactivada. La duración y el intervalo se guardan en `KeepAwake.settings.json`, junto al ejecutable. La carpeta debe permitir escritura para conservar esos ajustes. Los archivos de ajustes de versiones anteriores siguen siendo compatibles.

El icono de la bandeja muestra una **marca verde** cuando está activada y una **pausa gris** cuando está desactivada. Al pasar el puntero sobre el icono o abrir su menú puedes consultar el tiempo restante. Windows puede colocar el icono dentro del menú de iconos ocultos de la bandeja.

Al terminar el tiempo, la aplicación se desactiva automáticamente, libera la solicitud de energía y muestra un aviso de Windows. Funciona también con la ventana oculta. El aviso está sujeto a la configuración de notificaciones de Windows. El equipo vuelve a sus opciones normales de energía; KeepAwake no fuerza su suspensión. Cada activación inicia una cuenta regresiva nueva.

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

La verificación integrada comprueba duraciones predefinidas y personalizadas, compatibilidad de ajustes, cuenta regresiva, iconos y textos de bandeja, renovación, cancelación manual, modo sin límite, vencimiento automático con la ventana oculta, reactivación y liberación de solicitudes de energía.

## Historial

Los cambios se registran en Git y se resumen en [CHANGELOG.md](CHANGELOG.md). El primer commit conserva el código original; el siguiente aplica el nombre KeepAwake.
