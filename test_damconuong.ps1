$ErrorActionPreference = 'Stop'

function Assert($cond, $msg) {
    if (-not $cond) { throw $msg }
}

$category = (Invoke-WebRequest 'https://damconuong.shop/the-loai/elf').Content
Assert ($category -match 'page=142') 'Category page max page not found.'

$book = (Invoke-WebRequest 'https://damconuong.shop/truyen/truyen-san-vo-nguoi-o-the-gioi-khac').Content
Assert ($book -match '/truyen/truyen-san-vo-nguoi-o-the-gioi-khac/chapter-97') 'Book chapter link not found.'

$chapter = (Invoke-WebRequest 'https://damconuong.shop/truyen/truyen-san-vo-nguoi-o-the-gioi-khac/chapter-97').Content
Assert ($chapter -match 'id="chapter-content"') 'chapter-content block not found.'
Assert ($chapter -match 'https://dcnvn2\.mbpro\.vip/dcn/truyen-san-vo-nguoi-o-the-gioi-khac/chapter-97/3\.jpg') 'Chapter image URL not found.'

Write-Host 'damconuong smoke test ok'
