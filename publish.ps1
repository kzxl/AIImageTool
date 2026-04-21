$slnDir = "e:\15. Other\ImageTool"
$hostProj = "$slnDir\ImageTool.Host\ImageTool.Host.csproj"
$pluginsDir = @(
    "$slnDir\ImageTool.Plugins.FaceRestorer\ImageTool.Plugins.FaceRestorer.csproj",
    "$slnDir\ImageTool.Plugins.Upscaler\ImageTool.Plugins.Upscaler.csproj",
    "$slnDir\ImageTool.Plugins.MetaEditor\ImageTool.Plugins.MetaEditor.csproj",
    "$slnDir\ImageTool.Plugins.ColorLab\ImageTool.Plugins.ColorLab.csproj"
)

# Lite Build
Write-Host "Publishing Host Lite..."
dotnet publish $hostProj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "$slnDir\Publish\Lite"

foreach ($plugin in $pluginsDir) {
    if (Test-Path $plugin) {
        $pluginName = (Get-Item $plugin).BaseName
        Write-Host "Publishing $pluginName Lite..."
        dotnet publish $plugin -c Release -r win-x64 --self-contained false -o "$slnDir\Publish\Lite\Plugins\$pluginName"
    }
}

# Full Build
Write-Host "Publishing Host Full..."
dotnet publish $hostProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "$slnDir\Publish\Full"

foreach ($plugin in $pluginsDir) {
    if (Test-Path $plugin) {
        $pluginName = (Get-Item $plugin).BaseName
        Write-Host "Publishing $pluginName Full..."
        dotnet publish $plugin -c Release -r win-x64 --self-contained false -o "$slnDir\Publish\Full\Plugins\$pluginName"
    }
}

Write-Host "Build Completed!"
