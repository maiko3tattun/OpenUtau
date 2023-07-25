using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DynamicData.Binding;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace OpenUtau.App.ViewModels {
    public class SingerSelectionViewModel : ViewModelBase {
        [Reactive] public ObservableCollection<string> Categories { get; set; } = new ObservableCollection<string>();
        [Reactive] public ObservableCollection<USinger> ClassicList { get; set; } = new ObservableCollection<USinger>();
        [Reactive] public ObservableCollection<USinger> ENUNUList { get; set; } = new ObservableCollection<USinger>();
        [Reactive] public string ClassicHeader { get; set; } = "Classic";
        [Reactive] public string ENUNUHeader { get; set; } = "ENUNU";
        [Reactive] public int SelectedCategory { get; set; } = 0;
        [Reactive] USinger? SelectedSinger { get; set; }
        [Reactive] string Search { get; set; } = "";
        [Reactive] bool LoadAllFolders { get; set; } = true;

        public IEnumerable<USinger> Singers => SingerManager.Inst.SingerGroups.Values.SelectMany(l => l);

        public SingerSelectionViewModel() {

        }
    }
}
