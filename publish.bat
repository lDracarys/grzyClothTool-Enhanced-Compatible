@echo off
setlocal

rem === grzyClothTool - Publish script ===
rem A placer et lancer depuis le dossier racine du repo (a cote du dossier "grzyClothTool")

set "PROJECT=grzyClothTool\grzyClothTool.csproj"
set "OUTDIR=grzyClothTool\bin\Release\net10.0-windows\win-x64\publish"

echo.
echo === grzyClothTool - Publish ===
echo.

rem Ferme un eventuel exe encore ouvert pour eviter les erreurs de fichier verrouille
echo Fermeture d'une eventuelle instance de grzyClothTool.exe en cours...
taskkill /IM grzyClothTool.exe /F >nul 2>&1

echo.
echo Lancement de dotnet publish...
echo.

dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "%OUTDIR%"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ========================================
    echo   ECHEC du publish ^(code %ERRORLEVEL%^)
    echo   Verifie qu'aucune fenetre Explorer n'est
    echo   ouverte sur le dossier publish, et qu'aucun
    echo   antivirus ne bloque le fichier.
    echo ========================================
    echo.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================
echo   Publish termine avec succes !
echo   Dossier : %OUTDIR%
echo ========================================
echo.
pause
