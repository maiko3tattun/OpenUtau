using System;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Events {
    public class PartVisibilityChangedEvent {
        public UPart Part { get; }
        public bool Visible { get; }
        public PartVisibilityChangedEvent(UPart part, bool visible) {
            Part = part;
            Visible = visible;
        }
    }
}
