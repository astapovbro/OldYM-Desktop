using System.Windows;
using System.Windows.Media.Imaging;

namespace OldYM
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            Icon = new BitmapImage(new System.Uri(
                "pack://application:,,,/assets/oldym-about.png",
                System.UriKind.Absolute));
        }

        void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
