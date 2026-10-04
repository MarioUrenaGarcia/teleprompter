# Teleprompter

Teleprompter de escritorio para Windows que flota encima de las demás ventanas y **no aparece al compartir pantalla**, en grabaciones ni en capturas. Pensado para leer un guion durante videollamadas, presentaciones o grabaciones sin que nadie más lo vea.

## Funciones

- **Invisible al compartir pantalla**: ni la ventana, ni sus menús, tooltips, ajustes o diálogos de archivo aparecen en Teams, Zoom, Google Meet, OBS, la herramienta Recortes o cualquier captura.
- **Siempre encima** de las demás aplicaciones, con fondo semitransparente ajustable.
- **Pantalla completa o modo ventana** con un botón, `F11` o doble clic.
- **Desplazamiento automático** suave con velocidad en líneas por minuto, cuenta regresiva y tiempo restante estimado.
- **Desplazamiento manual** con la rueda del ratón, el teclado o la barra de posición, incluso mientras avanza solo.
- **Abre PDF, Word (.docx), Markdown y texto**. Los PDF escaneados (imágenes) se leen con el reconocimiento de texto (OCR) que incluye Windows.
- **Editor integrado** para escribir, pegar o corregir el guion.
- **Parámetros ajustables**: fuente, tamaño, grosor, interlineado, alineación, márgenes, colores, opacidad, franja de lectura, modo espejo y más.
- **Atajos globales** que funcionan aunque la videollamada tenga el foco.
- Modo que **deja pasar los clics** a la ventana de abajo.
- Recuerda la posición de la ventana, los ajustes y el último guion.

## Requisitos

- Windows 10 versión 2004 o posterior, o Windows 11 (64 bits).
- No necesita instalar .NET. Solo para compilar desde el código hace falta el [SDK de .NET 10](https://dotnet.microsoft.com/download).

## Instalación

### Opción 1: versión compilada (recomendada)

1. Descarga el archivo `Teleprompter-<versión>-win-x64.zip` desde la sección [Releases](../../releases/latest).
2. Descomprímelo y haz doble clic en `instalar.cmd`.

Si prefieres no instalar nada, también puedes abrir directamente `app\Teleprompter.exe` dentro de la carpeta descomprimida.

La aplicación no está firmada digitalmente, así que Windows puede mostrar un aviso de SmartScreen la primera vez. Para continuar elige **Más información** y luego **Ejecutar de todas formas**.

### Opción 2: compilar desde el código

1. Instala el SDK de .NET 10.
2. Abre la carpeta `installer` y haz doble clic en `instalar.cmd`.

En ambos casos el instalador copia la aplicación a `%LOCALAPPDATA%\Programs\Teleprompter`, crea los accesos directos en el menú Inicio y en el escritorio y la registra en **Configuración > Aplicaciones**. No requiere permisos de administrador. Después puedes buscarla escribiendo "Teleprompter" en el menú Inicio.

Opciones desde PowerShell:

```powershell
.\instalar.ps1 -SinEscritorio   # sin acceso directo en el escritorio
.\instalar.ps1 -NoAbrir         # no abrir la aplicación al terminar
```

Para actualizar, vuelve a ejecutar el instalador de la versión nueva: conserva los ajustes y el último guion.

## Uso

1. Abre un documento con el botón de carpeta, con `Ctrl + O` o arrastrándolo a la ventana. También puedes pulsar **Escribir o pegar**.
2. Coloca la ventana cerca de la cámara y ajusta su tamaño desde los bordes, o usa el botón de pantalla completa (junto al candado) para ocupar todo el monitor.
3. Pulsa `Espacio` para iniciar. La barra de herramientas se oculta sola mientras el texto avanza y reaparece al pasar el ratón.

El candado de la barra indica el estado: verde, invisible al compartir; rojo, visible.

### Teclado con la ventana activa

| Tecla | Acción |
|---|---|
| `Espacio` | Iniciar o pausar |
| `↑` `↓` | Mover una línea |
| `RePág` `AvPág` | Mover casi una pantalla |
| `←` `→` | Bajar o subir la velocidad |
| `Ctrl + ←` `Ctrl + →` o `Ctrl + rueda` | Tamaño de letra |
| `Inicio` `Fin` | Ir al principio o al final |
| `F11` o doble clic en la ventana | Pantalla completa o modo ventana |
| `Ctrl + O` | Abrir documento |
| `Ctrl + E` | Editar el guion |
| `Ctrl + S` | Guardar el guion como `.md` o `.txt` |
| `Ctrl + ,` | Ajustes |
| `Esc` | Pausar, terminar la edición o salir de pantalla completa |

### Atajos globales (funcionan desde cualquier aplicación)

| Atajo | Acción |
|---|---|
| `Ctrl + Alt + Espacio` | Iniciar o pausar |
| `Ctrl + Alt + ↑` `Ctrl + Alt + ↓` | Subir o bajar la velocidad |
| `Ctrl + Alt + RePág` `Ctrl + Alt + AvPág` | Retroceder o adelantar el texto |
| `Ctrl + Alt + Inicio` | Volver al inicio |
| `Ctrl + Alt + H` | Mostrar u ocultar el teleprompter |
| `Ctrl + Alt + T` | Dejar pasar los clics o volver a usar el ratón |

Si otra aplicación ya usa alguno de estos atajos, la ventana de ajustes lo indica.

### Formato del guion en el editor

El editor usa Markdown sencillo:

- Línea en blanco: párrafo nuevo. Un salto de línea simple se respeta tal cual.
- `# Título`, `## Subtítulo`
- `**negrita**`, `*cursiva*`
- `- elemento` para listas y `1. elemento` para listas numeradas
- `> texto` para resaltar una cita
- `---` para una línea separadora

## Formatos compatibles

| Formato | Qué se conserva |
|---|---|
| PDF | Párrafos reconstruidos, títulos detectados por tamaño de letra, sin encabezados, pies ni números de página repetidos. Las páginas escaneadas pasan por OCR. |
| Word (.docx) | Títulos, negritas, cursivas, listas con viñetas y numeradas, saltos de línea y tablas. |
| Markdown (.md) | Todo el formato anterior. |
| Texto (.txt) | Texto tal cual, con detección automática de codificación. |

Los `.doc` antiguos no son compatibles: guárdalos como `.docx` desde Word.

El OCR usa los idiomas instalados en Windows. Si un PDF escaneado no se lee, agrega el idioma en **Configuración > Hora e idioma > Idioma y región** con la opción de reconocimiento óptico de caracteres.

## Limitaciones

- La invisibilidad aplica a capturas por software. Una cámara o un teléfono apuntando a la pantalla sí la ven.
- Si compartes la pantalla completa, el ícono de la barra de tareas sí se ve. Puedes ocultarlo en **Ajustes > Ventana > Mostrar en la barra de tareas** y usar `Ctrl + Alt + H` para mostrar u ocultar la ventana.
- En Windows anteriores a la versión 2004 la ventana se ve como un recuadro negro en lugar de desaparecer.

## Datos y desinstalación

Los ajustes y el último guion se guardan en `%LOCALAPPDATA%\Teleprompter`. Si ocurre un error inesperado, los detalles quedan en `errores.log` en esa misma carpeta.

Para desinstalar, busca Teleprompter en **Configuración > Aplicaciones > Aplicaciones instaladas** y elige **Desinstalar**. Al final pregunta si quieres borrar también tus ajustes.

## Compilar sin instalar

```powershell
dotnet run --project src/Teleprompter
```

## Generar el paquete para Releases

```powershell
.\installer\empaquetar.ps1
```

Deja en `artifacts` el `.zip` con la aplicación compilada, el instalador, este README y la licencia, junto con su huella SHA256. La versión se toma de `<Version>` en `src/Teleprompter/Teleprompter.csproj`.

## Estructura

```
src/Teleprompter/
  Import/       Lectura de PDF (con OCR), DOCX, Markdown y texto
  Rendering/    Conversión del guion a texto en pantalla
  Services/     Invisibilidad en capturas, atajos globales, almacenamiento
  Controls/     Selector de color y convertidores
installer/      Instalador, desinstalador y script de empaquetado
```

## Licencia

Distribuido bajo la licencia MIT. Consulta el archivo [LICENSE](LICENSE).
