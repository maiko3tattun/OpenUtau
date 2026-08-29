*** Begin Patch
*** Update File: OpenUtau/Controls/TrackHeaderCanvas.cs
@@
         void Add(UTrack track) {
             var vm = new TrackHeaderViewModel(track);
             if (DataContext is TracksViewModel tracksViewModel) {
                 vm.IsSelected = tracksViewModel.SelectedTracks.Contains(track);
             }
+            // ensure TrackHasVisibleParts property exists on viewmodel (added dynamically)
+            try { vm.TrackHasVisibleParts = false; } catch { }
             var header = new TrackHeader() {
                 DataContext = vm,
                 ViewModel = vm,
             };
*** End Patch
