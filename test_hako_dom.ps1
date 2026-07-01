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

$volume40657 = [regex]::Match(
    $bookHtml,
    '(?s)<header id="volume_40657".*?<ul class="list-chapters at-series">(?<body>.*?)</ul>')

$volume40982 = [regex]::Match(
    $bookHtml,
    '(?s)<header id="volume_40982".*?<ul class="list-chapters at-series">(?<body>.*?)</ul>')

$volume41239 = [regex]::Match(
    $bookHtml,
    '(?s)<header id="volume_41239".*?<ul class="list-chapters at-series">(?<body>.*?)</ul>')

$titleTop = [regex]::Match(
    $chapterHtml,
    '(?s)<div class="title-top".*?<h2[^>]*>(?<volume>.*?)</h2>.*?<h4[^>]*>(?<chapter>.*?)</h4>.*?<h6[^>]*>(?<meta>.*?)</h6>')

Assert-True ($volumeSections.Count -ge 3) "Expected at least 3 Hako volume sections."
Assert-True ($bookHtml.Contains('volume_40657')) "Missing first volume id."
Assert-True ($bookHtml.Contains('volume_40982')) "Missing second volume id."
Assert-True ($bookHtml.Contains('volume_41239')) "Missing third volume id."
Assert-True ($chapterLinks.Count -ge 40) "Expected many chapter links inside sample book HTML."
Assert-True ($bookHtml.Contains('Chương I: Nghĩa Vụ Quý Tộc (1 - 17)')) "Missing sample volume title."
Assert-True ($volume40657.Success) "Missing volume_40657 chapter list."
Assert-True ($volume40982.Success) "Missing volume_40982 chapter list."
Assert-True ($volume41239.Success) "Missing volume_41239 chapter list."
Assert-True (([regex]::Matches($volume40657.Groups['body'].Value, '<div class="chapter-name">', 'IgnoreCase')).Count -eq 17) "Expected 17 chapters in volume_40657."
Assert-True (([regex]::Matches($volume40982.Groups['body'].Value, '<div class="chapter-name">', 'IgnoreCase')).Count -eq 20) "Expected 20 chapters in volume_40982."
Assert-True (([regex]::Matches($volume41239.Groups['body'].Value, '<div class="chapter-name">', 'IgnoreCase')).Count -eq 4) "Expected 4 chapters in volume_41239."

Assert-True ($chapterHtml.Contains('class="title-top"')) "Missing title-top block in sample chapter HTML."
Assert-True ($chapterHtml.Contains('id="chapter-content"')) "Missing chapter-content block in sample chapter HTML."
Assert-True ($chapterHtml.Contains('Chapter 1: Tên Quý Tộc Phản Diện Đồi Bại')) "Missing sample chapter title."
Assert-True ($titleTop.Success) "Missing expected title-top h2/h4/h6 structure."
Assert-True (($titleTop.Groups['volume'].Value -replace '<[^>]+>', '').Contains('Chương I: Nghĩa Vụ Quý Tộc (1 - 17)')) "Missing expected volume heading in chapter page."
Assert-True (($titleTop.Groups['chapter'].Value -replace '<[^>]+>', '').Contains('Chapter 1: Tên Quý Tộc Phản Diện Đồi Bại')) "Missing expected chapter heading in chapter page."

Write-Output "Hako DOM check passed."
