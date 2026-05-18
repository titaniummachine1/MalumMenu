@echo off
if not defined AMONG_US_PATH set AMONG_US_PATH=C:\Steam\steamapps\common\Among Us

dotnet build src\MalumMenu.csproj -c Debug -p:AmongUs="%AMONG_US_PATH%"
if %ERRORLEVEL% neq 0 (
    echo Build failed.
    pause
    exit /b %ERRORLEVEL%
)

echo Deploying to %AMONG_US_PATH%\BepInEx\plugins...
if not exist "%AMONG_US_PATH%\BepInEx\plugins" (
    echo ERROR: Plugins folder not found at %AMONG_US_PATH%\BepInEx\plugins
    pause
    exit /b 1
)

if exist "%AMONG_US_PATH%\BepInEx\plugins\MalumMenu.dll" (
    del /F /Q "%AMONG_US_PATH%\BepInEx\plugins\MalumMenu.dll"
)
copy /Y "src\bin\Debug\net6.0\MalumMenu.dll" "%AMONG_US_PATH%\BepInEx\plugins\MalumMenu.dll"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to copy DLL.
    pause
    exit /b %ERRORLEVEL%
)

echo Deploy complete.
exit
