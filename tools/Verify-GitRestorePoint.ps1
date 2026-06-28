param(
    [string]$ProjectFile = "Comic-GMTPC.csproj"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Normalize-GitPath {
    param([string]$PathValue)

    $normalizedValue = $PathValue.Trim()
    if ($normalizedValue.Length -ge 2 -and $normalizedValue.StartsWith('"') -and $normalizedValue.EndsWith('"')) {
        $normalizedValue = $normalizedValue.Substring(1, $normalizedValue.Length - 2)
    }

    return ($normalizedValue -replace "\\", "/").TrimStart("./")
}

function Get-RelativeRepoPath {
    param(
        [string]$BasePath,
        [string]$TargetPath
    )

    $baseUri = New-Object System.Uri((Join-Path $BasePath "."))
    $targetUri = New-Object System.Uri($TargetPath)
    $relativeUri = $baseUri.MakeRelativeUri($targetUri)
    return [System.Uri]::UnescapeDataString($relativeUri.ToString())
}

function Resolve-ProjectIncludePaths {
    param(
        [string]$RepoRoot,
        [string]$IncludeValue
    )

    $normalizedInclude = $IncludeValue -replace "/", "\"

    if ($normalizedInclude.Contains("**")) {
        $prefix = $normalizedInclude.Split("**")[0].TrimEnd("\")
        $prefixPath = Join-Path $RepoRoot $prefix
        if (-not (Test-Path -LiteralPath $prefixPath)) {
            return [pscustomobject]@{
                Missing = @($IncludeValue)
                Files = @()
            }
        }

        $files = Get-ChildItem -LiteralPath $prefixPath -Recurse -File | ForEach-Object {
            Normalize-GitPath (Get-RelativeRepoPath -BasePath $RepoRoot -TargetPath $_.FullName)
        }

        return [pscustomobject]@{
            Missing = @()
            Files = $files
        }
    }

    if ([System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($normalizedInclude)) {
        $files = Get-ChildItem -Path (Join-Path $RepoRoot $normalizedInclude) -File | ForEach-Object {
            Normalize-GitPath (Get-RelativeRepoPath -BasePath $RepoRoot -TargetPath $_.FullName)
        }

        return [pscustomobject]@{
            Missing = @()
            Files = $files
        }
    }

    $fullPath = Join-Path $RepoRoot $normalizedInclude
    if (-not (Test-Path -LiteralPath $fullPath)) {
        return [pscustomobject]@{
            Missing = @($IncludeValue)
            Files = @()
        }
    }

    return [pscustomobject]@{
        Missing = @()
        Files = @(Normalize-GitPath (Get-RelativeRepoPath -BasePath $RepoRoot -TargetPath $fullPath))
    }
}

function Test-ImportantPath {
    param([string]$RelativePath)

    $normalizedPath = Normalize-GitPath $RelativePath
    $ignoredPrefixes = @(".vs/", ".tmp/", ".agents/", ".nuget/", "bin/", "build_verify/", "build_verify_ui/", "build_verify_ui2/", "build_verify_ui3/", "build_verify_ui4/", "build_verify_ui5/", "obj/", "release/", "Bandiview/")
    foreach ($ignoredPrefix in $ignoredPrefixes) {
        if ($normalizedPath.StartsWith($ignoredPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
    }

    $importantPrefixes = @("7-Zip/", "Properties/")
    foreach ($prefix in $importantPrefixes) {
        if ($normalizedPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    $importantExactPaths = @(
        ".gitignore",
        "App.config",
        "build.bat",
        "Comic-GMTPC.csproj",
        "FodyWeavers.xml",
        "nuget.config",
        "workflow.md"
    )
    foreach ($exactPath in $importantExactPaths) {
        if ($normalizedPath.Equals($exactPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    $importantExtensions = @(
        ".bat",
        ".config",
        ".cs",
        ".csproj",
        ".ico",
        ".md",
        ".ps1",
        ".resx",
        ".settings",
        ".sln",
        ".slnx",
        ".targets",
        ".props",
        ".xaml",
        ".xml"
    )

    $extension = [IO.Path]::GetExtension($normalizedPath)
    return $importantExtensions -icontains $extension
}

function Test-GitTracked {
    param(
        [string]$RepoRoot,
        [string]$RelativePath
    )

    $output = git -C $RepoRoot ls-files -- "$RelativePath"
    return -not [string]::IsNullOrWhiteSpace(($output | Out-String))
}

$repoRoot = (git rev-parse --show-toplevel).Trim()
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    throw "Khong tim thay repo git."
}

$projectPath = Join-Path $repoRoot $ProjectFile
if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Khong tim thay file project: $ProjectFile"
}

[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
$namespaceManager = New-Object System.Xml.XmlNamespaceManager($projectXml.NameTable)
$namespaceManager.AddNamespace("msb", $projectXml.DocumentElement.NamespaceURI)

$projectIncludeNodes = $projectXml.SelectNodes("//msb:Compile[@Include][not(ancestor::msb:Target)] | //msb:Page[@Include][not(ancestor::msb:Target)] | //msb:ApplicationDefinition[@Include][not(ancestor::msb:Target)] | //msb:EmbeddedResource[@Include][not(ancestor::msb:Target)] | //msb:None[@Include][not(ancestor::msb:Target)]", $namespaceManager)
$projectMissingPaths = New-Object System.Collections.Generic.List[string]
$projectIncludedFiles = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)

foreach ($node in $projectIncludeNodes) {
    $resolved = Resolve-ProjectIncludePaths -RepoRoot $repoRoot -IncludeValue $node.Include
    foreach ($missingPath in $resolved.Missing) {
        [void]$projectMissingPaths.Add((Normalize-GitPath $missingPath))
    }

    foreach ($filePath in $resolved.Files) {
        [void]$projectIncludedFiles.Add($filePath)
    }
}

$untrackedProjectFiles = New-Object System.Collections.Generic.List[string]
foreach ($filePath in $projectIncludedFiles) {
    if (-not (Test-GitTracked -RepoRoot $repoRoot -RelativePath $filePath)) {
        [void]$untrackedProjectFiles.Add($filePath)
    }
}

$importantUntrackedFiles = New-Object System.Collections.Generic.List[string]
$importantDeletedFiles = New-Object System.Collections.Generic.List[string]
$statusLines = git -C $repoRoot status --porcelain=v1 --untracked-files=all
foreach ($statusLine in $statusLines) {
    if ($statusLine.Length -lt 4) {
        continue
    }

    $statusCode = $statusLine.Substring(0, 2)
    $pathText = $statusLine.Substring(3)
    if ($pathText.Contains(" -> ")) {
        $pathText = $pathText.Split(" -> ")[-1]
    }

    $normalizedPath = Normalize-GitPath $pathText
    if ($statusCode -eq "??" -and (Test-ImportantPath $normalizedPath)) {
        [void]$importantUntrackedFiles.Add($normalizedPath)
        continue
    }

    if (($statusCode[0] -eq "D" -or $statusCode[1] -eq "D") -and (Test-ImportantPath $normalizedPath)) {
        [void]$importantDeletedFiles.Add($normalizedPath)
    }
}

$errors = New-Object System.Collections.Generic.List[string]

if ($projectMissingPaths.Count -gt 0) {
    $missingList = ($projectMissingPaths | Sort-Object -Unique) -join ", "
    [void]$errors.Add("Project dang tham chieu file/thu muc khong ton tai: $missingList")
}

if ($untrackedProjectFiles.Count -gt 0) {
    $untrackedProjectList = ($untrackedProjectFiles | Sort-Object -Unique) -join ", "
    [void]$errors.Add("File da gan vao .csproj nhung chua duoc git track: $untrackedProjectList")
}

if ($importantUntrackedFiles.Count -gt 0) {
    $importantUntrackedList = ($importantUntrackedFiles | Sort-Object -Unique) -join ", "
    [void]$errors.Add("Dang co file quan trong chua duoc track: $importantUntrackedList")
}

if ($importantDeletedFiles.Count -gt 0) {
    $importantDeletedList = ($importantDeletedFiles | Sort-Object -Unique) -join ", "
    [void]$errors.Add("Dang co file quan trong bi xoa khoi working tree: $importantDeletedList")
}

if ($errors.Count -gt 0) {
    Write-Host "Git restore-point check that bai :(" -ForegroundColor Red
    foreach ($errorMessage in $errors) {
        Write-Host "- $errorMessage" -ForegroundColor Yellow
    }
    Write-Host "Fix git status truoc roi moi xem day la restore point an toan." -ForegroundColor Yellow
    exit 1
}

Write-Host "Git restore-point check OK :) Khong thay file quan trong bi bo sot." -ForegroundColor Green
