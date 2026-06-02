$ErrorActionPreference = "Stop"

Write-Host "Cleaning up old releases in Publish folder..."
if (Test-Path "Publish") {
    Remove-Item -Recurse -Force "Publish\*" -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Force -Path "Publish"
}

Write-Host "Publishing FULL (Self-Contained)..."
$solDir = (Get-Item .).FullName + "\"

dotnet build ImageTool.Plugins.FaceRestorer\ImageTool.Plugins.FaceRestorer.csproj -c Release -p:SolutionDir=$solDir
dotnet build ImageTool.Plugins.Upscaler\ImageTool.Plugins.Upscaler.csproj -c Release -p:SolutionDir=$solDir

dotnet publish ImageTool.Host\ImageTool.Host.csproj -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -p:SolutionDir=$solDir -o "Publish\Full"
Copy-Item -Path "ImageTool.Host\bin\Release\net8.0-windows\win-x64\Plugins" -Destination "Publish\Full\Plugins" -Recurse -Force

Write-Host "Publishing LITE (Framework-Dependent)..."
dotnet publish ImageTool.Host\ImageTool.Host.csproj -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -p:SolutionDir=$solDir -o "Publish\Lite"
Copy-Item -Path "ImageTool.Host\bin\Release\net8.0-windows\win-x64\Plugins" -Destination "Publish\Lite\Plugins" -Recurse -Force

Write-Host "Compressing ZIP packages..."
Compress-Archive -Path "Publish\Full\*" -DestinationPath "Publish\AuroraStudio_Full_Win_x64.zip" -Force
Compress-Archive -Path "Publish\Lite\*" -DestinationPath "Publish\AuroraStudio_Lite_Win_x64.zip" -Force

Write-Host "Publish Process Completed Successfully!"
