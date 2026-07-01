using SodivBureau.Mobile.Models;
using SodivBureau.Mobile.PageModels;

namespace SodivBureau.Mobile.Pages
{
    public partial class MainPage : ContentPage
    {
        public MainPage(MainPageModel model)
        {
            InitializeComponent();
            BindingContext = model;
        }
    }
}