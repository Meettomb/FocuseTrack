using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace FocusTrack.Pages
{
    /// <summary>
    /// Interaction logic for PrivacyPolicyPage.xaml
    /// </summary>
    public partial class PrivacyPolicyPage : Page
    {
        public PrivacyPolicyPage()
        {
            InitializeComponent();
        }

        private void BackIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!NavigationService.CanGoBack)
            {
                return;
            }

            var transform = new TranslateTransform();
            this.RenderTransform = transform;

            double pageWidth = this.ActualWidth;
            var animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = pageWidth,
                Duration = TimeSpan.FromMilliseconds(300),
                AccelerationRatio = 0.2,
                DecelerationRatio = 0.8
            };
            animation.Completed += (s, a) => NavigationService.GoBack();

            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

    }
}
