param(
    [Parameter(Mandatory = $true)][string]$Assembly,
    [Parameter(Mandatory = $true)][string]$Output
)

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'eng\BGCS.RepositoryTools\BGCS.RepositoryTools.csproj'
& dotnet run --project $project --configuration Release -- validate api-snapshot $Assembly $Output
exit $LASTEXITCODE
