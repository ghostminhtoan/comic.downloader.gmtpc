import re

def main():
    try:
        with open('page_genre.html', 'r', encoding='utf-8') as f:
            html = f.read()
        
        print("TOTAL LENGTH:", len(html))
        
        # Find all hrefs
        hrefs = re.findall(r'href=["\']([^"\']+)["\']', html)
        print("TOTAL HREFS:", len(hrefs))
        
        # Show top 50 hrefs
        print("\nTOP 50 HREFS:")
        for h in hrefs[:50]:
            print("  ", h)
            
        # Find hrefs containing /truyen/
        truyen_hrefs = [h for h in hrefs if '/truyen/' in h]
        print(f"\nFOUND {len(truyen_hrefs)} TRUYEN HREFS:")
        for h in truyen_hrefs[:30]:
            print("  ", h)
            
    except Exception as e:
        print("ERROR:", e)

if __name__ == '__main__':
    main()
