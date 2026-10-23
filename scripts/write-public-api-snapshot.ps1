[CmdletBinding(DefaultParameterSetName = 'Single')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Single')][string]$Assembly,
    [Parameter(Mandatory = $true, ParameterSetName = 'Single')][string]$Output,
    [Parameter(Mandatory = $true, ParameterSetName = 'All')][switch]$All,
    [string]$Configuration = 'Release',
    [string]$Dotnet = $(if ($env:DOTNET_HOST_PATH) { $env:DOTNET_HOST_PATH } else { 'dotnet' })
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskTool = Join-Path $taskRoot "eng/BGCS.RepositoryTools/bin/$Configuration/net10.0/BGCS.RepositoryTools.dll"
if (-not (Test-Path -LiteralPath $taskTool -PathType Leaf)) { throw 'Build the complete solution and repository tools before reviewing API snapshots.' }
if (-not $All) {
    & $Dotnet $taskTool validate api-snapshot $Assembly $Output
    exit $LASTEXITCODE
}
$taskManifest = Get-Content -LiteralPath (Join-Path $taskRoot 'distribution/manifests/product-packages.json') -Raw | ConvertFrom-Json
$taskEntries = @()
foreach ($taskName in $taskManifest.publicApiAssemblies) {
    $taskProject = $taskManifest.assemblyProjects.$taskName
    if (-not $taskProject) { throw "Missing explicit project mapping for $taskName" }
    $taskAssembly = Join-Path $taskRoot "$taskProject/bin/$Configuration/net10.0/$taskName.dll"
    if (-not (Test-Path -LiteralPath $taskAssembly -PathType Leaf)) { throw "Compiled API assembly is missing: $taskAssembly" }
    $taskSnapshot = Join-Path $taskRoot "tests/public-api/$taskName.txt"
    $taskTemporaryRoot = Join-Path $taskRoot 'artifacts/temporary'
    [IO.Directory]::CreateDirectory($taskTemporaryRoot) | Out-Null
    $taskTemporarySnapshot = Join-Path $taskTemporaryRoot ("api-$taskName-" + [Guid]::NewGuid().ToString('N') + '.txt')
    try {
        & $Dotnet $taskTool validate api-snapshot $taskAssembly $taskTemporarySnapshot
        if ($LASTEXITCODE -ne 0) { throw "API extraction failed for $taskName" }
        $taskNewHash = (Get-FileHash -LiteralPath $taskTemporarySnapshot -Algorithm SHA256).Hash
        $taskExistingHash = if (Test-Path -LiteralPath $taskSnapshot -PathType Leaf) { (Get-FileHash -LiteralPath $taskSnapshot -Algorithm SHA256).Hash } else { $null }
        if ($taskNewHash -cne $taskExistingHash) { [IO.File]::Copy($taskTemporarySnapshot, $taskSnapshot, $true) }
    }
    finally { [IO.File]::Delete($taskTemporarySnapshot) }
    $taskLines = [IO.File]::ReadAllLines($taskSnapshot)
    $taskEntries += [ordered]@{ assembly = $taskName; project = $taskProject; baseline = "tests/public-api/$taskName.txt"; sha256 = (Get-FileHash -LiteralPath $taskSnapshot -Algorithm SHA256).Hash.ToLowerInvariant(); assemblySha256 = (Get-FileHash -LiteralPath $taskAssembly -Algorithm SHA256).Hash.ToLowerInvariant(); lines = $taskLines.Count; exportedTypes = @($taskLines | Where-Object { $_ -match '^(class|struct|interface|enum) ' }).Count }
}
[xml]$taskProps = Get-Content -LiteralPath (Join-Path $taskRoot 'Directory.Build.props') -Raw
$taskReview = [ordered]@{
    schemaVersion = 2
    productVersion = [string]$taskProps.Project.PropertyGroup[0].Version
    scope = 'Current 4.0 candidate public API shape extracted from compiled assemblies; not feature, runtime, platform, package or release certification.'
    releaseCertification = $false
    sourceRevision = (& git -C $taskRoot rev-parse HEAD).Trim()
    sourceWorktreeDirtyAtReview = [bool]((& git -C $taskRoot status --porcelain).Length)
    snapshotEncoding = 'UTF-8 without BOM, invariant LF, final newline'
    reviewedBoundaries = @('Core and Runtime are independent products', 'Engine contains no frontend, host, SDK, target or managed-runtime defaults', 'The BGCS facade explicitly composes replaceable foundation implementations', 'Optional extensions depend on Core and share exact foundation assembly bytes where needed', 'No friend-assembly or private test-access API', 'No consumer compilation, native build or MSBuild integration')
    limitations = @('Snapshot extraction is not a universal nullable/custom-attribute compatibility proof; validate implementation and behavioral contracts separately.')
    assemblies = $taskEntries
}
$taskReview | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $taskRoot 'eng/acceptance/public-api-baseline-review.json') -Encoding utf8NoBOM
Write-Output "Reviewed $($taskEntries.Count) compiled public API assemblies."
