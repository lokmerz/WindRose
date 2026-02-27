using System.Windows;
using System.Windows.Input;

namespace WindRose
{
    public partial class CustomAlertWindow : Window
    {
        public CustomAlertWindow(string error, string inputError)
        {
            InitializeComponent();
            this.Title = error;
            txtMessage.Text = inputError;

            // Automatski prilagodi visinu ako je poruka kratka
            this.SizeToContent = SizeToContent.Height;

            var main = (mainWindow)Application.Current.MainWindow;
            string text = main.T("ButtonText");
            btnOK.Content = text;
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
