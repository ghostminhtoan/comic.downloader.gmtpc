using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private static readonly HashSet<string> HardcodedSpecialDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "truyenqq", "truyenqqko", "hako", "truyenggvn", "sayhentai"
        };

        public CaptchaWindow CreateSpecialCaptcha(string url, bool autoDeleteCookiesOnLoad = true, bool headlessAutomation = false)
        {
            return new CaptchaWindow(url, CaptchaType.Special, autoDeleteCookiesOnLoad, headlessAutomation);
        }

        public bool IsSpecialDomain(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            // Load user-specified special domains from special_domains.txt if exists
            try
            {
                string configPath = Path.Combine(PortablePaths.AppRoot, "special_domains.txt");
                if (File.Exists(configPath))
                {
                    var domains = File.ReadAllLines(configPath);
                    foreach (var d in domains)
                    {
                        if (!string.IsNullOrWhiteSpace(d) && url.IndexOf(d.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                }
            }
            catch {}

            // Fallback to hardcoded list
            foreach (var domain in HardcodedSpecialDomains)
            {
                if (url.IndexOf(domain, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
