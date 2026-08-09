using System;
using System.Threading.Tasks;
using System.Windows;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        public CaptchaWindow CreateWatchMoreCaptcha(string url, bool autoDeleteCookiesOnLoad = true, bool headlessAutomation = false)
        {
            return new CaptchaWindow(url, CaptchaType.WatchMore, autoDeleteCookiesOnLoad, headlessAutomation);
        }

        public bool IsWatchMoreDomain(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            // nettruyen has multiple domains, check for "nettruyen"
            return url.IndexOf("nettruyen", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
