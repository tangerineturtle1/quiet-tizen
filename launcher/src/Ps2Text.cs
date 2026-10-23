using System;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Text drawn with the PS2 BIOS system font. Each on-screen size has its own pre-rendered atlas,
// and every glyph is drawn 1:1 at an integer pixel position, so the TV never rescales the letters.
sealed class Ps2Text : View
{
 public static string AtlasPrefix = "";     // e.g. ".../ps2/font/ps2font_" + key + ".png"
 const int Overlap = 2;
 readonly float scale;
 readonly int key;
 Color tint = new Color(1, 1, 1, 1);
 public string Value { get; private set; }
 // DALi children inherit only alpha from their parent, so the tint is applied to every glyph.
 public Color Tint
 {
  get => tint;
  set { tint = value; for (uint i = 0; i < ChildCount; i++) GetChildAt(i).Color = value; }
 }

 public Ps2Text(string text, float scale)
 {
  this.scale = scale;
  key = NearestKey(scale);
  Set(text ?? "");
 }

 static int NearestKey(float scale)
 {
  int want = (int)Math.Round(scale * 100), best = 100, diff = int.MaxValue;
  foreach (var k in Ps2FontMetrics.Atlases.Keys) { int d = Math.Abs(k - want); if (d < diff) { diff = d; best = k; } }
  return best;
 }

 static int Index(char ch) { int c = ch; if (c < 32 || c > 126) c = '?'; return c - 32; }

 public static int Measure(string text, float scale)
 {
  if (string.IsNullOrEmpty(text)) return 0;
  float w = 0;
  foreach (char ch in text) w += Ps2FontMetrics.Width[Index(ch)];
  w -= Overlap * (text.Length - 1);
  return (int)Math.Ceiling(w * scale);
 }

 public void Set(string text)
 {
  if (text == Value) return;
  Value = text;
  while (ChildCount > 0) { var child = GetChildAt(0); Remove(child); child.Dispose(); }
  var atlas = Ps2FontMetrics.Atlases[key];
  float x = 0; int height = 0;
  foreach (char ch in text)
  {
   int i = Index(ch);
   int gx = atlas.Rects[4 * i], gy = atlas.Rects[4 * i + 1], gw = atlas.Rects[4 * i + 2], gh = atlas.Rects[4 * i + 3];
   height = Math.Max(height, gh);
   if (ch != ' ')
   {
    Add(new ImageView
    {
     ResourceUrl = AtlasPrefix + key + ".png",
     PixelArea = new RelativeVector4(gx / (float)atlas.W, gy / (float)atlas.H, gw / (float)atlas.W, gh / (float)atlas.H),
     Size2D = new Size2D(gw, gh),
     Position2D = new Position2D((int)Math.Round(x), 0),
     Color = tint,
    });
   }
   x += (Ps2FontMetrics.Width[i] - Overlap) * scale;
  }
  Size2D = new Size2D(Math.Max(1, Measure(text, scale)), Math.Max(1, height));
 }
}
