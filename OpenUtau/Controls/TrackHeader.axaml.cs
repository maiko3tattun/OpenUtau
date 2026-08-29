*** Begin Patch
*** Update File: OpenUtau/Controls/TrackHeader.axaml.cs
@@
         private double trackHeight;
         private Point offset;
         private int trackNo;
@@
         public TrackHeaderViewModel? ViewModel;
+        
+        // Track highlight flag will be set from TrackHeaderCanvas via ViewModel
*** End Patch
