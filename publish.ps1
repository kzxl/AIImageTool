$ErrorActionPreference = "Stop"

Write-Host "Cleaning up old releases in Publish folder..."
if (Test-Path "Publish") {
    Remove-Item -Recurse -Force "Publish\*" -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Force -Path "Publish"
}

Write-Host "Publishing FULL (Self-Contained)..."
$solDir = (Get-Item .).FullName + "\"

# Build các plugin trước
dotnet build ImageTool.Plugins.FaceRestorer\ImageTool.Plugins.FaceRestorer.csproj -c Release -p:SolutionDir=$solDir
dotnet build ImageTool.Plugins.Upscaler\ImageTool.Plugins.Upscaler.csproj -c Release -p:SolutionDir=$solDir

# Publish Host dự án chính (bỏ SolutionDir để xuất đúng thư mục chỉ định -o)
dotnet publish ImageTool.Host\ImageTool.Host.csproj -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -o "Publish\Full"

# Copy Plugins vào thư mục phát hành
Copy-Item -Path "ImageTool.Host\bin\Release\net8.0-windows\win-x64\Plugins" -Destination "Publish\Full\Plugins" -Recurse -Force

# Đổi tên exe và pdb sang thương hiệu AuroraStudio
Rename-Item -Path "Publish\Full\ImageTool.Host.exe" -NewName "AuroraStudio.exe" -Force
if (Test-Path "Publish\Full\ImageTool.Host.pdb") {
    Rename-Item -Path "Publish\Full\ImageTool.Host.pdb" -NewName "AuroraStudio.pdb" -Force
}


Write-Host "Publishing LITE (Framework-Dependent)..."
# Publish Host dự án chính
dotnet publish ImageTool.Host\ImageTool.Host.csproj -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o "Publish\Lite"

# Copy Plugins vào thư mục phát hành
Copy-Item -Path "ImageTool.Host\bin\Release\net8.0-windows\win-x64\Plugins" -Destination "Publish\Lite\Plugins" -Recurse -Force

# Đổi tên exe và pdb sang thương hiệu AuroraStudio
Rename-Item -Path "Publish\Lite\ImageTool.Host.exe" -NewName "AuroraStudio.exe" -Force
if (Test-Path "Publish\Lite\ImageTool.Host.pdb") {
    Rename-Item -Path "Publish\Lite\ImageTool.Host.pdb" -NewName "AuroraStudio.pdb" -Force
}


Write-Host "Compressing ZIP packages..."
Compress-Archive -Path "Publish\Full\*" -DestinationPath "Publish\AuroraStudio_Full_Win_x64.zip" -Force
Compress-Archive -Path "Publish\Lite\*" -DestinationPath "Publish\AuroraStudio_Lite_Win_x64.zip" -Force

Write-Host "Publish Process Completed Successfully!"
