$lines = [System.IO.File]::ReadAllLines("temp_eval.js")
$line = $lines[20] # 21st line
$start = 300
$len = 150
Write-Host "Substring around 339:"
Write-Host $line.Substring($start, $len)
