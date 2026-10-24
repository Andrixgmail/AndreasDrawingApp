using System.Windows;
using QuestPDF.Infrastructure;

namespace AndreasDrawingApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // QuestPDF Community license is free for small businesses / individuals / OSS.
            // See https://www.questpdf.com/license/ before shipping commercially.
            QuestPDF.Settings.License = LicenseType.Community;
        }
    }
}
