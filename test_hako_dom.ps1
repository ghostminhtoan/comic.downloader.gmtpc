$ErrorActionPreference = "Stop"

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$bookPath = Join-Path $repoRoot ".tmp\hako_book_26990.html"
$chapterPath = Join-Path $repoRoot ".tmp\hako_chapter_sample.html"

Assert-True (Test-Path $bookPath) "Missing sample book HTML: $bookPath"
Assert-True (Test-Path $chapterPath) "Missing sample chapter HTML: $chapterPath"

$bookHtml = Get-Content -Raw $bookPath
$chapterHtml = Get-Content -Raw $chapterPath

$volumeSections = [regex]::Matches(
    $bookHtml,
    '<section[^>]*class\s*=\s*["''][^"'']*\bvolume-list\b[^"'']*["''][^>]*>',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

$chapterLinks = [regex]::Matches(
    $bookHtml,
    'https://ln\.hako\.vn/truyen/26990-[^"''\s<]+/c\d+[^"''\s<]*',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

Assert-True ($volumeSections.Count -ge 3) "Expected at least 3 Hako volume sections."
Assert-True ($bookHtml.Contains('volume_40657')) "Missing first volume id."
Assert-True ($bookHtml.Contains('volume_40982')) "Missing second volume id."
Assert-True ($bookHtml.Contains('volume_41239')) "Missing third volume id."
Assert-True ($chapterLinks.Count -ge 40) "Expected many chapter links inside sample book HTML."
Assert-True ($bookHtml.Contains('Chương I: Nghĩa Vụ Quý Tộc (1 - 17)')) "Missing sample volume title."

Assert-True ($chapterHtml.Contains('class="title-top"')) "Missing title-top block in sample chapter HTML."
Assert-True ($chapterHtml.Contains('id="chapter-content"')) "Missing chapter-content block in sample chapter HTML."
Assert-True ($chapterHtml.Contains('Chapter 1: Tên Quý Tộc Phản Diện Đồi Bại')) "Missing sample chapter title."

Write-Output "Hako DOM check passed."
