using System;
using System.Threading.Tasks;
using System.Windows;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        public CaptchaWindow CreateGeneralCaptcha(string url, bool autoDeleteCookiesOnLoad = true, bool headlessAutomation = false)
        {
            return new CaptchaWindow(url, CaptchaType.General, autoDeleteCookiesOnLoad, headlessAutomation);
        }
    }
}
