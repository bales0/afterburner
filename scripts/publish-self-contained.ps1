param([ValidateSet('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64')][string]$Rid = 'win-x64')
$ErrorActionPreference = "Stop"
dotnet publish 'AfterburnerStudio/AfterburnerStudio.csproj' -c Release -r $Rid --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "publish/$Rid"
