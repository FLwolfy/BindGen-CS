param(
    [Parameter(Mandatory = $true)][string]$Assembly,
    [Parameter(Mandatory = $true)][string]$Output
)

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'scripts\BGCS.ApiSnapshot\BGCS.ApiSnapshot.csproj'
& dotnet run --project $project --configuration Release -- $Assembly $Output
exit $LASTEXITCODE
