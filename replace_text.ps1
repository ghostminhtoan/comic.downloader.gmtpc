$content = Get-Content MainWindow.xaml -Raw -Encoding UTF8
$content = $content.Replace('CYBERPUNK LINK SCRAPE // CRAWLER SYSTEM', '')
$content = $content.Replace('NET FRAMEWORK 4.8 / HANDY CONTROL POWERED', '')
$content = $content.Replace('TARGET TAG URL', 'DOWNLOAD MULTILE BOOKS FROM TAG OR ARTIST')
$content = $content.Replace('PASTE DIRECT LINK', 'PASTE URLS LINKS')
$content = $content.Replace('START SCRAPE', 'START GET LINK')
$content = $content.Replace('CRAWL MORE', 'GET LINK MORE')

# SelectionChanged
$content = $content.Replace('<TabControl Name="tabLeftPanel" Grid.Column="0" Grid.Row="0" Style="{StaticResource TabControlInLine}" Background="Transparent" BorderThickness="0">', '<TabControl Name="tabLeftPanel" Grid.Column="0" Grid.Row="0" Style="{StaticResource TabControlInLine}" Background="Transparent" BorderThickness="0" SelectionChanged="TabLeftPanel_SelectionChanged">')

# GreaterThanZeroConverter
$content = $content.Replace('<Window.Resources>', '<Window.Resources>' + [Environment]::NewLine + '        <local:GreaterThanZeroConverter x:Key="GreaterThanZeroConverter"/>')
$content = $content.Replace('Converter={x:Static local:GreaterThanZeroConverter.Instance}', 'Converter={StaticResource GreaterThanZeroConverter}')

Set-Content MainWindow.xaml -Value $content -Encoding UTF8
