*** Begin Patch
*** Update File: OpenUtau/Controls/PartsCanvas.cs
@@
         void Add(UPart part) {
             var control = new PartControl(part, this);
             Children.Add(control);
             partControls.Add(part, control);
         }
*** End Patch
