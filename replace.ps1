$content = Get-Content MainWindow.xaml -Raw -Encoding UTF8
$content = $content.Replace('CYBERPUNK LINK SCRAPE // CRAWLER SYSTEM', '')
$content = $content.Replace('NET FRAMEWORK 4.8 / HANDY CONTROL POWERED', '')
$content = $content.Replace('TARGET TAG URL', 'DOWNLOAD MULTILE BOOKS FROM TAG OR ARTIST')
$content = $content.Replace('PASTE DIRECT LINK', 'PASTE URLS LINKS')
$content = $content.Replace('START SCRAPE', 'START GET LINK')
$content = $content.Replace('CRAWL MORE', 'GET LINK MORE')
Set-Content MainWindow.xaml -Value $content -Encoding UTF8
