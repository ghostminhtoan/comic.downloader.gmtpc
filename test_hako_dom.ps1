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

function Get-DivInnerHtmlById {
    param(
        [string]$Html,
        [string]$Id
    )

    $startMatch = [regex]::Match(
        $Html,
        '<div[^>]*id\s*=\s*["'']' + [regex]::Escape($Id) + '["''][^>]*>',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    if (-not $startMatch.Success) {
        return ""
    }

    $startIndex = $startMatch.Index + $startMatch.Length
    $depth = 1
    $scanIndex = $startIndex
    $tokenRegex = [regex]::new('<div\b|</div>', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    while ($depth -gt 0) {
        $tokenMatch = $tokenRegex.Match($Html, $scanIndex)
        if (-not $tokenMatch.Success) {
            return $Html.Substring($startIndex)
        }

        if ($tokenMatch.Value.StartsWith('</div', [System.StringComparison]::OrdinalIgnoreCase)) {
            $depth--
        }
        else {
            $depth++
        }

        $scanIndex = $tokenMatch.Index + $tokenMatch.Length
        if ($depth -eq 0) {
            return $Html.Substring($startIndex, $tokenMatch.Index - $startIndex)
        }
    }

    return ""
}

function Get-HakoProtectedText {
    param(
        [string]$Html
    )

    $tagMatch = [regex]::Match(
        $Html,
        '(?s)<div[^>]*id\s*=\s*["'']chapter-c-protected["''][^>]*>',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    if (-not $tagMatch.Success) {
        return ""
    }

    $tag = $tagMatch.Value
    $keyMatch = [regex]::Match($tag, 'data-k\s*=\s*["''](?<value>[^"'']*)["'']')
    $chunksMatch = [regex]::Match($tag, 'data-c\s*=\s*["''](?<value>[^"'']*)["'']')

    if (-not $keyMatch.Success -or -not $chunksMatch.Success) {
        return ""
    }

    $key = [System.Net.WebUtility]::HtmlDecode($keyMatch.Groups['value'].Value)
    $json = [System.Net.WebUtility]::HtmlDecode($chunksMatch.Groups['value'].Value).Replace('\/', '/')
    $matches = [regex]::Matches($json, '"(?<value>(?:\\.|[^"\\])*)"')

    $chunks = foreach ($match in $matches) {
        [regex]::Unescape($match.Groups['value'].Value)
    }

    $orderedChunks = $chunks |
        Where-Object { $_.Length -gt 4 } |
        Sort-Object { [int]$_.Substring(0, 4) }

    $decoded = New-Object System.Text.StringBuilder
    foreach ($chunk in $orderedChunks) {
        $payload = $chunk.Substring(4)
        $bytes = [Convert]::FromBase64String($payload)
        for ($i = 0; $i -lt $bytes.Length; $i++) {
            $bytes[$i] = $bytes[$i] -bxor [byte][char]$key[$i % $key.Length]
        }

        [void]$decoded.Append([System.Text.Encoding]::UTF8.GetString($bytes))
    }

    return $decoded.ToString()
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

$chapterContentHtml = Get-DivInnerHtmlById -Html $chapterHtml -Id "chapter-content"
$chapterDecodedHtml = Get-HakoProtectedText -Html $chapterContentHtml
$chapterPlainText = [System.Net.WebUtility]::HtmlDecode(([regex]::Replace($chapterDecodedHtml, '<[^>]+>', ' ')))
$chapterPlainText = [regex]::Replace($chapterPlainText, '\s+', ' ').Trim()

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
Assert-True (-not [string]::IsNullOrWhiteSpace($chapterContentHtml)) "Failed to extract nested chapter-content HTML."
Assert-True (-not [string]::IsNullOrWhiteSpace($chapterDecodedHtml)) "Failed to decode protected chapter-content."
Assert-True ($chapterPlainText.Contains('Hyaaa') -or $chapterPlainText.Contains('quý tộc')) "Missing expected copied text from decoded chapter-content."
Assert-True ($chapterPlainText.Length -gt 500) "Expected substantial text content in chapter-content."

Write-Output "Hako DOM check passed."
