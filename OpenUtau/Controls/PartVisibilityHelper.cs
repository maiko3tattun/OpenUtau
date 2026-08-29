*** Begin Patch
*** Add File: OpenUtau/Controls/PartVisibilityHelper.cs
+namespace OpenUtau.App.Controls {
+    public static class PartVisibilityHelper {
+        public static bool IsPartVisible(double partPos, double partDur, int partTrackNo,
+            double tickOffset, double tickWidth, double viewWidth,
+            double trackOffset, double trackHeight, double viewHeight) {
+            var visibleLeft = tickOffset;
+            var visibleRight = tickOffset + viewWidth / Math.Max(1.0, tickWidth);
+            var horizontally = !(partPos + partDur < visibleLeft || partPos > visibleRight);
+            var visibleTop = trackOffset;
+            var visibleBottom = trackOffset + viewHeight / Math.Max(1.0, trackHeight);
+            var vertically = partTrackNo >= visibleTop && partTrackNo < visibleBottom;
+            return horizontally && vertically;
+        }
+    }
+}
*** End Patch
