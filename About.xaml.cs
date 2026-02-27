using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WindRose
{
    /// <summary>
    /// Interaction logic for About.xaml
    /// </summary>
    public partial class About : Window
    {
        public About(string aboutText, string about)
        {
            InitializeComponent();
            var main = (mainWindow)Application.Current.MainWindow;
            string text = main.T("ButtonText");
            btnClose.Content = text;
        }
    }
}
