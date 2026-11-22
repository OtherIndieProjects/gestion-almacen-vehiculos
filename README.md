# Gestión almacén de vehículos

Cliente de escritorio y Android para gestionar el aparcamiento de vehículos en campa o almacén.

Trabaja contra [Logi Compass](https://github.com/OtherIndieProjects/logi-compass): el servidor, las cuentas y el stock viven ahí. Esta aplicación es la interfaz del departamento de vehículos (mapa de plazas, aparcar, corregir y sacar).

## Requisitos

- Servidor **Logi Compass** en marcha (API en `http://127.0.0.1:5088` por defecto).
- Usuario con permisos de almacén / stock.

## Descargas

| | Archivo |
| --- | --- |
| Windows (oficina) | [`dist/pc/GestionAlmacenVehiculos.PC.exe`](dist/pc/GestionAlmacenVehiculos.PC.exe) |
| Android (campo) | [`dist/android/com.gestionalmacenvehiculos-Signed.apk`](dist/android/com.gestionalmacenvehiculos-Signed.apk) |

## Uso

1. Arranca el servidor de Logi Compass.
2. Abre el `.exe` o instala la APK.
3. Inicia sesión con tu usuario de Logi.

En el PC puedes ver el mapa, aparcar, corregir posiciones, consultar el historial y personalizar nombre/logo de la empresa (menú **Empresa**). En el móvil se aparca desde el campo; la marca se puede cambiar en la pantalla de acceso.

## Personalizar nombre y logo

Por defecto la interfaz muestra **Vehicle**. Para otra empresa, deja junto al `.exe` una carpeta `branding/`:

```
branding/
  branding.json
  logo.png
```

Ejemplo de `branding.json`:

```json
{
  "productName": "MiEmpresa",
  "brandDisplay": "MI EMPRESA",
  "appTitle": "MiEmpresa · Almacén",
  "tagline": "PC · Logi Compass",
  "accent": "#1C69D4",
  "accentDeep": "#0653B6",
  "logoFileName": "logo.png"
}
```

Si no hay carpeta junto al ejecutable, se usa lo guardado en `%LocalAppData%\GestionAlmacenVehiculos\branding\` (editor de la propia app).

## Compilar

```powershell
.\build-pc.ps1
dotnet publish src\GestionAlmacenVehiculos.Mobile\GestionAlmacenVehiculos.Mobile.csproj `
  -c Release -f net9.0-android -p:AndroidPackageFormat=apk -o dist\android
```

Solución: `GestionAlmacenVehiculos.sln` (.NET 8 PC / .NET 9 Android).
