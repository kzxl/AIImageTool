@echo off
echo Building Solution...
dotnet build
echo.
echo Starting ImageTool.Host...
cd ImageTool.Host
dotnet run
