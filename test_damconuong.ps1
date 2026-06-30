$ErrorActionPreference = 'Stop'

function Assert($cond, $msg) {
    if (-not $cond) { throw $msg }
}

$category = (Invoke-WebRequest 'https://damconuong.shop/the-loai/elf').Content
Assert ($category -match 'page=142') 'Category page max page not found.'

$book = (Invoke-WebRequest 'https://damconuong.shop/truyen/truyen-san-vo-nguoi-o-the-gioi-khac').Content
Assert ($book -match '/truyen/truyen-san-vo-nguoi-o-the-gioi-khac/chapter-7') 'Book chapter link not found.'

$chapter = (Invoke-WebRequest 'https://damconuong.shop/truyen/truyen-san-vo-nguoi-o-the-gioi-khac/chapter-7').Content
Assert ($chapter -match 'id="chapter-content"') 'chapter-content block not found.'
Assert ($chapter -match 'https://dcnvn2\.mbpro\.vip/dcn/truyen-san-vo-nguoi-o-the-gioi-khac/chapter-7/3\.jpg') 'Chapter image URL not found.'

$mixedBook = (Invoke-WebRequest 'https://damconuong.shop/truyen/readers-paradise').Content
Assert ($mixedBook -match '/truyen/readers-paradise/caera-denoir-trong-anh-sang-cuoi-con-duong') 'Mixed-text chapter link not found.'
Assert ($mixedBook -match '/truyen/readers-paradise/chapter-15-2') 'Decimal chapter link not found.'

Write-Host 'damconuong smoke test ok'
