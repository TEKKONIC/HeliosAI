using System.Windows.Controls;
using Shared.Plugin;

namespace HeliosAI
{
    // ReSharper disable once UnusedType.Global
    // ReSharper disable once RedundantExtendsListEntry
    public partial class ConfigView : UserControl
    {
        public ConfigView()
        {
            InitializeComponent();
            DataContext = Common.Config;
        }
    }
}