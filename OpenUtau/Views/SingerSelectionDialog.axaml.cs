using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NWaves.Audio;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.App.Views {
    public partial class SingerSelectionDialog : Window {
        public SingerSelectionDialog() {
            InitializeComponent();
        }
    }
}
