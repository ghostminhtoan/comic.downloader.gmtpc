$html = [System.IO.File]::ReadAllText("page_chapter.html")
# Extract SCRIPT 10 payload
$pattern = 'eval\(function\(h,u,n,t,e,r\).*?\((?<args>".*?",\s*\d+,\s*".*?",\s*\d+,\s*\d+,\s*\d+)\)'
$match = [regex]::Match($html, $pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)

if (-not $match.Success) {
    Write-Error "Could not find eval pattern in HTML."
    exit 1
}

$argsStr = $match.Groups["args"].Value
Write-Host "Found packed script args: $argsStr"

# Parse arguments using CSV parser
$csv = ConvertFrom-Csv ("h,u,n,t,e,r`n" + $argsStr)
$h = $csv.h
$u = [int]$csv.u
$n = $csv.n
$t = [int]$csv.t
$e = [int]$csv.e
$r = [int]$csv.r

# Implement decoder
function Get-Val($d, $e_val, $f) {
    $g = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ+/".ToCharArray()
    $h_arr = $g[0..($e_val-1)]
    $i_arr = $g[0..($f-1)]
    
    $d_chars = $d.ToCharArray()
    [array]::Reverse($d_chars)
    
    $j = 0
    for ($idx = 0; $idx -lt $d_chars.Length; $idx++) {
        $char = $d_chars[$idx]
        $charIdx = [array]::IndexOf($h_arr, $char)
        if ($charIdx -ne -1) {
            $j += $charIdx * [Math]::Pow($e_val, $idx)
        }
    }
    
    $k = ""
    while ($j -gt 0) {
        $k = $i_arr[$j % $f] + $k
        $j = [Math]::Floor(($j - ($j % $f)) / $f)
    }
    if ($k -eq "") { return "0" }
    return $k
}

$result = ""
$i = 0
$len = $h.Length
$n_chars = $n.ToCharArray()
$delim = $n_chars[$e]

while ($i -lt $len) {
    $s = ""
    while ($i -lt $len -and $h[$i] -ne $delim) {
        $s += $h[$i]
        $i++
    }
    $i++ # skip delimiter
    
    if ($s -ne "") {
        for ($j = 0; $j -lt $n_chars.Length; $j++) {
            $s = $s.Replace($n_chars[$j], $j.ToString())
        }
        $val = [int](Get-Val $s $e 10)
        $char_code = $val - $t
        $result += [char]$char_code
    }
}

$decoded = [System.Uri]::UnescapeDataString($result)
[System.IO.File]::WriteAllText("decoded_script.js", $decoded)
Write-Host "Unpacked script written to decoded_script.js (Length: $($decoded.Length))"
