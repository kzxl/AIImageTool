$ErrorActionPreference = "Stop"

Write-Host "Cleaning up old releases..."
if (Test-Path "Publish_Release") {
    Remove-Item -Recurse -Force "Publish_Release\*" -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Force -Path "Publish_Release"
}

Write-Host "Publishing FULL (Self-Contained)..."
$solDir = (Get-Item .).FullName + "\"

dotnet publish ImageTool.Host\ImageTool.Host.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:SolutionDir=$solDir -o "Publish_Release\Full"
dotnet publish ImageTool.Worker.Upscaler\ImageTool.Worker.Upscaler.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:SolutionDir=$solDir -o "Publish_Release\Full"
dotnet publish ImageTool.Plugins.FaceRestorer\ImageTool.Plugins.FaceRestorer.csproj -c Release -p:SolutionDir=$solDir -o "Publish_Release\Full\Plugins\ImageTool.Plugins.FaceRestorer"
dotnet publish ImageTool.Plugins.Upscaler\ImageTool.Plugins.Upscaler.csproj -c Release -p:SolutionDir=$solDir -o "Publish_Release\Full\Plugins\ImageTool.Plugins.Upscaler"

Write-Host "Publishing LITE (Framework-Dependent)..."
dotnet publish ImageTool.Host\ImageTool.Host.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:SolutionDir=$solDir -o "Publish_Release\Lite"
dotnet publish ImageTool.Worker.Upscaler\ImageTool.Worker.Upscaler.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:SolutionDir=$solDir -o "Publish_Release\Lite"
dotnet publish ImageTool.Plugins.FaceRestorer\ImageTool.Plugins.FaceRestorer.csproj -c Release -p:SolutionDir=$solDir -o "Publish_Release\Lite\Plugins\ImageTool.Plugins.FaceRestorer"
dotnet publish ImageTool.Plugins.Upscaler\ImageTool.Plugins.Upscaler.csproj -c Release -p:SolutionDir=$solDir -o "Publish_Release\Lite\Plugins\ImageTool.Plugins.Upscaler"

Write-Host "Compressing ZIP packages..."
Compress-Archive -Path "Publish_Release\Full\*" -DestinationPath "Publish_Release\ImageTool_Full_Win_x64.zip" -Force
Compress-Archive -Path "Publish_Release\Lite\*" -DestinationPath "Publish_Release\ImageTool_Lite_Win_x64.zip" -Force

Write-Host "Publish Process Completed Successfully!"
