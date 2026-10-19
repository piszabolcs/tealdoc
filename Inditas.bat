@echo off
setlocal

rem TealDoc forditasa es inditasa forrasbol (.NET 10 SDK szukseges).
set PROJECT=%~dp0src\TealDocAiTool\TealDocAiTool.csproj
set EXE=%~dp0src\TealDocAiTool\bin\Release\net10.0-windows\TealDocAiTool.exe

echo Forditas...
dotnet build "%PROJECT%" -c Release -v q
if errorlevel 1 (
    echo.
    echo HIBA a forditasnal. Ha fut egy TealDoc peldany, zard be, es probald ujra.
    pause
    exit /b 1
)

echo Inditas...
start "" "%EXE%"
exit /b 0
