$content = Get-Content MainWindow.xaml -Raw -Encoding UTF8

function ReplaceTabInfo {
    param ($content, $prefix)
    
    # We need to construct the exact pattern or just use Regex
    $pattern = '(?s)<!-- Page Info Status -->\s*<Border Grid\.Row="\d+".*?<!-- Page Range Input -->\s*<Grid Grid\.Row="\d+".*?</Grid>'
    
    # Instead of full regex which might be tricky, we'll do 4 specific replacements using exact strings or simpler regex
}
