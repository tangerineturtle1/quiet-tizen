using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.Applications;

// Quiet Home, PS2 edition: the owner's SAMSUNG PS2 boot, the PS2 menu ring looping behind every page,
// the PS2 system font, and the owner's own cursor/select/back sounds.
public sealed class Theme
{
 public bool VideoUnderlay { get; set; } = true;
 public string BootVideo { get; set; } = "ps2/media/boot.mp4";
 public string LoopVideo { get; set; } = "ps2/media/menu_loop.mp4";
 public string Ambience { get; set; } = "ps2/media/ambience.wav";
 public string Cursor { get; set; } = "ps2/sfx/cursor.wav";
 public string Select { get; set; } = "ps2/sfx/select.wav";
 public string Back { get; set; } = "ps2/sfx/back.wav";
 public int CurtainDelayMs { get; set; } = 700;
 public int BootEndMs { get; set; } = 11700;
 public List<DockItem> Dock { get; set; } = new List<DockItem>();
}
public sealed class DockItem
{
 public string Name { get; set; }
 public string Subtitle { get; set; }
 public string Action { get; set; }
 public string Icon { get; set; }
 public int Group { get; set; }
 public int IconW { get; set; }   // optional: show a non-square logo at this size, centred, uncropped
 public int IconH { get; set; }
 public string IconBackground { get; set; }   // optional: full-bleed tile colour behind a logo, like the other app tiles
}

public sealed class QuietHome : NUIApplication
{
 const string Root = "/opt/usr/apps/com.samsung.tv.bixby-promotion/res/quiet-home/";
 const string StateFile = "/tmp/quiet-home-state.txt";
 static Theme theme = new Theme();
 static string themeError = "";

 sealed class Slot { public View Card; public Ps2Text Label; public string Name, Sub, Action; public int X, Y, W, H; }

 // scene
 Layer bgLayer;
 View ui, curtain, dim;
 VideoView video, boot;
 readonly List<View> scene = new List<View>();
 readonly List<Slot> slots = new List<Slot>();
 Ps2Text clock, title, subtitle, detail, notice;
 View bar;
 Animation focusAnimation, fadeAnimation;
 Tizen.NUI.Timer clockTimer, curtainTimer, bootGuard;

 // state
 List<LibraryApp> library = new List<LibraryApp>();
 string libraryError = "";
 int page, selected, libraryPage, homeSelected, lastSource = 13;
 bool booting, paused, underlay;
 int ambienceId = -1;
 bool prefMusic = true, prefSfx = true, prefBoot = true;
 string prefsPath = "";
 string videoStatus = "none", lastKey = "", lastLaunch = "";
 long lastKeyAt, buildMs;

 // native settings (unchanged from the original launcher)
 int currentVolume, currentMute;
 readonly Dictionary<string, string> settingValues = new Dictionary<string, string>();
 readonly Dictionary<string, string> settingNames = new Dictionary<string, string>();
 readonly Dictionary<string, bool> settingEnabled = new Dictionary<string, bool>();
 double brightnessMin, brightnessMax = 50, brightnessStep = 1;

 static readonly Color White = new Color(1, 1, 1, 1);
 static readonly Color Dim = new Color(150 / 255f, 180 / 255f, 230 / 255f, 1);
 static readonly Color Soft = new Color(190 / 255f, 205 / 255f, 235 / 255f, 1);
 static readonly Color RowIdle = new Color(175 / 255f, 190 / 255f, 220 / 255f, 1);

 [DllImport("libsource-api.so")] static extern int connect_source(int sourceType);
 [DllImport("libvconf.so.0")] static extern int vconf_get_int(string key, out int value);
 [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_create(out IntPtr app);
 [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_set_app_id(IntPtr app, string id);
 [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_set_operation(IntPtr app, string operation);
 [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_add_extra_data(IntPtr app, string key, string value);
 [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_send_launch_request(IntPtr app, IntPtr cb, IntPtr data);
 [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_destroy(IntPtr app);
 [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_get_master_volume(out int value);
 [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_set_master_volume(int value);
 [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_get_master_mute(out int value);
 [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_set_master_mute(int value);
 // "Current speaker" is whatever the TV is playing through: its own speakers, or an eARC/optical receiver
 // (here the WiiM), whose level the TV drives over HDMI-CEC. Master volume only moves the TV's own speakers.
 [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_get_current_speaker_volume(out int value);
 [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_set_current_speaker_volume(int value);
 [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_get_current_speaker_mute(out byte value);
 [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_set_current_speaker_mute(byte value);
 bool speakerApi = true;
 string volumeApi = "";
 int GetVolume(out int v)
 {
  v = -1;
  if (speakerApi) { try { if (sound_manager_get_current_speaker_volume(out v) == 0) { volumeApi = "speaker"; return 0; } } catch { } speakerApi = false; }
  volumeApi = "master";
  try { return sound_manager_get_master_volume(out v); } catch { return -1; }
 }
 int SetVolume(int v)
 {
  try { return speakerApi ? sound_manager_set_current_speaker_volume(v) : sound_manager_set_master_volume(v); } catch { return -1; }
 }
 int GetMute(out int m)
 {
  m = -1;
  if (speakerApi) { try { if (sound_manager_get_current_speaker_mute(out byte b) == 0) { m = b != 0 ? 1 : 0; return 0; } } catch { } }
  try { return sound_manager_get_master_mute(out m); } catch { return -1; }
 }
 int SetMute(int m)
 {
  try { return speakerApi ? sound_manager_set_current_speaker_mute((byte)m) : sound_manager_set_master_mute(m); } catch { return -1; }
 }
 [DllImport("libsettings-api.so")] static extern bool CSharpGetItemValue(string id, out string name, out string value);
 [DllImport("libsettings-api.so")] static extern bool CSharpSetItemValue(string id, string value);
 [DllImport("libsettings-api.so")] static extern bool CSharpGetItemEnable(string id, out bool enabled);
 [DllImport("libsettings-api.so")] static extern bool CSharpGetItemMinMaxStepValue(string id, out double min, out double max, out double step);

 public QuietHome(bool transparent) : base("", transparent ? WindowMode.Transparent : WindowMode.Opaque) { underlay = transparent; }

 public static void Main(string[] args)
 {
  try { theme = JsonSerializer.Deserialize<Theme>(File.ReadAllText(Root + "theme.json")) ?? new Theme(); }
  catch (Exception e) { themeError = e.Message; theme = new Theme(); }
  using (var app = new QuietHome(theme.VideoUnderlay)) app.Run(args);
 }

 // ---------------------------------------------------------------- lifecycle
 protected override void OnCreate()
 {
  base.OnCreate();
  Ps2Text.AtlasPrefix = Root + "ps2/font/ps2font_";
  try { library = AppLibrary.Load(Root + "apps.json"); } catch (Exception e) { libraryError = e.Message; }
  LoadPrefs();
  var win = Window.Default;
  win.BackgroundColor = underlay ? new Color(0, 0, 0, 0) : new Color(0, 0, 0, 1);
  win.Title = "Quiet Home"; win.KeyEvent += OnKey;
  bgLayer = new Layer(); win.AddLayer(bgLayer); bgLayer.LowerToBottom();
  curtain = new View { Size2D = new Size2D(1920, 1080), BackgroundColor = new Color(0, 0, 0, 1) };
  bgLayer.Add(curtain);
  ui = new View { Size2D = new Size2D(1920, 1080), Opacity = 0 };
  win.Add(ui);
  RefreshSource();
  page = 0; selected = SourceIndex();
  BuildPage();
  clockTimer = new Tizen.NUI.Timer(15000);
  clockTimer.Tick += (s, e) => { clock?.Set(Now()); return true; };
  clockTimer.Start();
  if (prefBoot) PlayBoot(); else ShowMenu(false);
  WriteState("created");
 }

 protected override void OnResume()
 {
  base.OnResume();
  paused = false;
  clockTimer?.Start();
  RefreshSource();
  if (!booting) { StartLoop(); StartAmbience(); }
  if (page == 2) { RefreshSettings(); UpdateFocus(false); }
  WriteState("resumed");
 }

 protected override void OnPause()
 {
  base.OnPause();
  paused = true;
  clockTimer?.Stop();
  if (booting) EndBoot(false);
  StopVideo(); StopAmbience();
  WriteState("paused");
 }

 protected override void OnAppControlReceived(AppControlReceivedEventArgs e)
 {
  base.OnAppControlReceived(e);
  string requested = null;
  try { e.ReceivedAppControl.ExtraData.TryGet("quiet_page", out requested); } catch { }
  if (requested == "settings") GoPage(2, 0);
  else if (requested == "apps") GoPage(1, 0);
  else GoHome("home_request");
 }

 // ---------------------------------------------------------------- boot + background
 void PlayBoot()
 {
  if (string.IsNullOrEmpty(theme.BootVideo)) { ShowMenu(false); return; }
  booting = true; bootClosing = false;
  WriteState("boot_creating");
  try
  {
   // The TV's player crashes the app when a clip reaches its end, so the boot clip is set to loop and is
   // stopped by position just before its last frame (it ends on black, so nothing is cut).
   boot = new VideoView { ResourceUrl = Root + theme.BootVideo, Looping = true, Muted = false, Underlay = underlay, Size2D = new Size2D(1920, 1080) };
   bgLayer.Add(boot); curtain.RaiseToTop();
   WriteState("boot_before_play");
   boot.Play(); videoStatus = "boot_playing";
   WriteState("boot_play_called");
   ScheduleCurtain(0, 250);
  }
  catch (Exception e) { videoStatus = "boot_error:" + e.Message; EndBoot(true); return; }
  bootStarted = Environment.TickCount64; bootLastPos = 0;
  bootGuard = new Tizen.NUI.Timer(100);
  bootGuard.Tick += (s, e) => BootTick();
  bootGuard.Start();
 }

 [DllImport("libcapi-media-player.so.0")] static extern int player_get_play_position(IntPtr player, out int milliseconds);
 long bootStarted; int bootLastPos; bool bootClosing;
 bool BootTick()
 {
  if (!booting || boot == null) return false;
  int pos = -1;
  try { var h = boot.NativeHandle; if (h != null && !h.IsInvalid && player_get_play_position(h.DangerousGetHandle(), out int ms) == 0) pos = ms; } catch { }
  long elapsed = Environment.TickCount64 - bootStarted;
  bool wrapped = pos >= 0 && pos + 1000 < bootLastPos;
  if (pos > bootLastPos) bootLastPos = pos;
  bool end = (pos >= 0 && pos >= theme.BootEndMs) || wrapped || (pos < 0 && elapsed >= theme.BootEndMs + 400) || elapsed > theme.BootEndMs + 4000;
  if (end && !bootClosing)
  {
   bootClosing = true; videoStatus = "boot_end pos=" + pos + " elapsed=" + elapsed; WriteState("boot_end_reached");
   var a = new Animation(200); a.AnimateTo(curtain, "Opacity", 1f); a.Play();
   Defer(220, () => EndBoot(true));
   return false;
  }
  return true;
 }

 void EndBoot(bool showMenu)
 {
  if (!booting) return;
  booting = false;
  bootGuard?.Stop();
  WriteState("boot_ending");
  if (boot != null)
  {
   var old = boot; boot = null;
   try { old.Stop(); } catch { }
   old.Hide();
   Defer(500, () => { bgLayer.Remove(old); old.Dispose(); WriteState("boot_disposed"); });
  }
  curtain.Opacity = 1;
  if (showMenu && !paused) ShowMenu(true);
 }

 readonly List<Tizen.NUI.Timer> deferred = new List<Tizen.NUI.Timer>();
 void Defer(uint ms, Action action)
 {
  var t = new Tizen.NUI.Timer(ms);
  t.Tick += (s, e) => { deferred.Remove(t); try { action(); } catch (Exception ex) { ShowError(ex.Message); } return false; };
  deferred.Add(t); t.Start();
 }

 void ShowMenu(bool fade)
 {
  StartLoop(); StartAmbience();
  fadeAnimation?.Stop();
  if (fade) { fadeAnimation = new Animation(700); fadeAnimation.AnimateTo(ui, "Opacity", 1f); fadeAnimation.Play(); }
  else ui.Opacity = 1;
  WriteState("menu");
 }

 void StartLoop()
 {
  if (paused || booting) return;
  StopVideo();
  if (string.IsNullOrEmpty(theme.LoopVideo)) { ScheduleCurtain(0, 400); videoStatus = "no_loop_video"; return; }
  WriteState("loop_creating");
  try
  {
   video = new VideoView { ResourceUrl = Root + theme.LoopVideo, Looping = true, Muted = true, Underlay = underlay, Size2D = new Size2D(1920, 1080) };
   bgLayer.Add(video); curtain.Opacity = 1; curtain.RaiseToTop();
   WriteState("loop_before_play");
   video.Play(); videoStatus = "loop_playing";
   WriteState("loop_play_called");
   ScheduleCurtain(theme.CurtainDelayMs, 600);
  }
  catch (Exception e) { videoStatus = "loop_error:" + e.Message; }
 }

 void StopVideo()
 {
  curtainTimer?.Stop();
  if (video == null) return;
  try { video.Stop(); } catch { }
  bgLayer.Remove(video); video.Dispose(); video = null; curtain.Opacity = 1;
  videoStatus = "stopped";
 }

 void ScheduleCurtain(int delay, int duration)
 {
  curtainTimer?.Stop();
  curtainTimer = new Tizen.NUI.Timer((uint)Math.Max(1, delay));
  curtainTimer.Tick += (s, e) => { var a = new Animation(duration); a.AnimateTo(curtain, "Opacity", 0f); a.Play(); return false; };
  curtainTimer.Start();
 }

 void StartAmbience() { if (paused || booting || !prefMusic || ambienceId >= 0) return; ambienceId = Ps2Sound.Loop(Root + theme.Ambience); }
 void StopAmbience() { Ps2Sound.Stop(ambienceId); ambienceId = -1; }
 void Sfx(string file) { if (prefSfx) Ps2Sound.Play(Root + file); }

 // ---------------------------------------------------------------- building pages
 View Add(View v) { scene.Add(v); ui.Add(v); return v; }
 Ps2Text Text(string text, int x, int y, float scale, Color tint, string align = "left")
 {
  var t = new Ps2Text(text, scale) { Tint = tint };
  t.Position2D = new Position2D(align == "right" ? x - t.Size2D.Width : align == "center" ? x - t.Size2D.Width / 2 : x, y);
  Add(t); return t;
 }
 void Retext(Ps2Text t, string text, int anchorX, string align)
 {
  t.Set(text);
  t.Position2D = new Position2D(align == "right" ? anchorX - t.Size2D.Width : align == "center" ? anchorX - t.Size2D.Width / 2 : anchorX, t.Position2D.Y);
 }
 static string Now() => DateTime.Now.ToString("h:mm tt", CultureInfo.InvariantCulture);

 void BuildPage()
 {
  var sw = Stopwatch.StartNew();
  focusAnimation?.Stop();
  foreach (var v in scene) { ui.Remove(v); v.Dispose(); }
  scene.Clear(); slots.Clear(); underlines.Clear(); bar = null; title = subtitle = detail = null;
  if (page == 1) { dim = Add(new View { Size2D = new Size2D(1920, 1080), BackgroundColor = new Color(0, 0, 0, 0.59f) }); }
  clock = Text(Now(), 1824, 52, 1.0f, White, "right");
  notice = Text("", 130, 1012, 0.62f, new Color(1f, 0.55f, 0.55f, 1));
  if (page == 0) BuildHome(); else if (page == 1) BuildApps(); else BuildSettings();
  selected = Math.Max(0, Math.Min(selected, slots.Count - 1));
  UpdateFocus(false);
  buildMs = sw.ElapsedMilliseconds;
  WriteState("page_ready");
 }

 int DockX(int i)
 {
  int x = 122, prevGroup = theme.Dock.Count > 0 ? theme.Dock[0].Group : 0;
  for (int k = 1; k <= i; k++) { x += 176 + (theme.Dock[k].Group != prevGroup ? 64 : 28); prevGroup = theme.Dock[k].Group; }
  return x;
 }

 void BuildHome()
 {
  title = Text("", 1180, 440, 1.5f, White);
  subtitle = Text("", 1184, 512, 0.8f, Dim);
  for (int i = 0; i < theme.Dock.Count; i++)
  {
   var d = theme.Dock[i]; int x = DockX(i), y = 780, s = 176;
   var card = Tile(x, y, s, s);
   bool appIcon = d.Icon != null && d.Icon.StartsWith("/");
   Ps2Text label = null;
   if (appIcon && d.IconW > 0 && d.IconH > 0)
   {
    if (!string.IsNullOrEmpty(d.IconBackground))
     card.Add(new View { Position2D = new Position2D(12, 12), Size2D = new Size2D(s - 24, s - 24), CornerRadius = new Vector4(12, 12, 12, 12), BackgroundColor = Hex(d.IconBackground) });
    card.Add(new ImageView { ResourceUrl = d.Icon, Position2D = new Position2D((s - d.IconW) / 2, (s - d.IconH) / 2), Size2D = new Size2D(d.IconW, d.IconH) });
   }
   else if (appIcon)
   {
    card.Add(new ImageView { ResourceUrl = d.Icon, Position2D = new Position2D(12, 12), Size2D = new Size2D(s - 24, s - 24), CornerRadius = new Vector4(12, 12, 12, 12),
     FittingMode = FittingModeType.ScaleToFill, SamplingMode = SamplingModeType.BoxThenLanczos, DesiredWidth = (s - 24) * 2, DesiredHeight = (s - 24) * 2 });
   }
   else
   {
    if (!string.IsNullOrEmpty(d.Icon)) card.Add(new ImageView { ResourceUrl = Root + d.Icon, Position2D = new Position2D(13, 16), Size2D = new Size2D(150, 112) });
    label = new Ps2Text(d.Name, 0.6f) { Tint = Soft };
    label.Position2D = new Position2D((s - label.Size2D.Width) / 2, s - 52);
    card.Add(label);
   }
   slots.Add(new Slot { Card = card, Label = label, Name = d.Name, Sub = d.Subtitle, Action = d.Action, X = x, Y = y, W = s, H = s });
  }
  Legend(new[] { ("select", "Enter"), ("back", "Back to TV") });
 }

 void BuildApps()
 {
  Text("Apps", 130, 120, 1.3f, White);
  Text(library.Count + " apps", 1790, 140, 0.7f, Dim, "right");
  if (library.Count == 0) { Text("Apps could not be loaded. " + libraryError, 130, 300, 0.8f, Soft); Legend(new[] { ("back", "Back") }); return; }
  libraryPage = Math.Clamp(libraryPage, 0, AppLibrary.PageCount(library.Count) - 1);
  int start = libraryPage * AppLibrary.PageSize, count = AppLibrary.VisibleCount(library.Count, libraryPage);
  for (int i = 0; i < count; i++)
  {
   var app = library[start + i]; int x = 190 + (i % 5) * 320, y = 220 + (i / 5) * 230, w = 200, h = 200;
   var card = Tile(x, y, w, h);
   if (app.Utility || string.IsNullOrEmpty(app.Asset))
   {
    var l = new Ps2Text(app.Name, 0.7f) { Tint = Soft }; l.Position2D = new Position2D((w - l.Size2D.Width) / 2, (h - l.Size2D.Height) / 2); card.Add(l);
   }
   else
   {
    string url = app.Asset.StartsWith("/") ? app.Asset : Root + app.Asset;
    if (app.Fit == "contain")
    {
     float k = Math.Min((w - 40f) / app.Width, (h - 40f) / app.Height); int iw = (int)(app.Width * k), ih = (int)(app.Height * k);
     card.Add(new ImageView { ResourceUrl = url, Position2D = new Position2D((w - iw) / 2, (h - ih) / 2), Size2D = new Size2D(iw, ih) });
    }
    else card.Add(new ImageView { ResourceUrl = url, Position2D = new Position2D(8, 8), Size2D = new Size2D(w - 16, h - 16), CornerRadius = new Vector4(16, 16, 16, 16),
     FittingMode = FittingModeType.ScaleToFill, SamplingMode = SamplingModeType.BoxThenLanczos, DesiredWidth = (w - 16) * 2, DesiredHeight = (h - 16) * 2 });
   }
   slots.Add(new Slot { Card = card, Name = app.Name, Sub = app.Description, Action = "app:" + app.Id, X = x, Y = y, W = w, H = h });
  }
  if (AppLibrary.PageCount(library.Count) > 1) Text((libraryPage + 1) + " / " + AppLibrary.PageCount(library.Count), 1790, 960, 0.7f, Dim, "right");
  detail = Text("", 130, 960, 0.8f, White);
  Legend(new[] { ("select", "Open"), ("back", "Back") });
 }

 static readonly (string id, string name)[] Rows = {
  ("brightness", "Brightness"), ("game-mode", "Game Mode"), ("sleeptimer", "Sleep Timer"),
  ("mute", "TV Sound"), ("music", "Background Music"), ("sfx", "Menu Sounds"), ("boot", "Startup Animation"),
  ("reconnect", "Reconnect PC"), ("controllers", "Controllers"), ("network", "Network"), ("advanced", "Advanced Settings") };

 void BuildSettings()
 {
  RefreshSettings();
  Text("System Configuration", 1040, 150, 1.15f, White);
  bar = Add(new ImageView { ResourceUrl = Root + "ps2/ui/bar.png", Size2D = new Size2D(820, 52), Position2D = new Position2D(1000, 246),
   BoxShadow = new Shadow(18f, new Color(0.24f, 0.47f, 1f, 0.45f), new Vector2(0, 0), new Vector2(0, 0)) });
  for (int i = 0; i < Rows.Length; i++)
  {
   int y = 250 + i * 56;
   var label = Text(Rows[i].name, 1040, y, 0.85f, RowIdle);
   var value = Text("", 1780, y, 0.85f, RowIdle, "right");
   slots.Add(new Slot { Card = label, Label = value, Name = Rows[i].name, Action = Rows[i].id, X = 1000, Y = y - 4, W = 820, H = 52 });
  }
  detail = Text("", 1040, 250 + Rows.Length * 56 + 18, 0.6f, Dim);
  Legend(new[] { ("leftright", "Change"), ("select", "Enter"), ("back", "Back") });
 }

 // PS2 browser style: no panel, icons float on the ring; a faint outline, and on focus a cyan halo
 // plus a glowing line underneath (the underline is a child so it follows the tile's focus scale).
 readonly Dictionary<View, View> underlines = new Dictionary<View, View>();
 static readonly Color EdgeIdle = new Color(120 / 255f, 170 / 255f, 1f, 60 / 255f);
 static readonly Color EdgeFocus = new Color(120 / 255f, 170 / 255f, 1f, 120 / 255f);
 View Tile(int x, int y, int w, int h)
 {
  var card = new View { Size2D = new Size2D(w, h), Position = new Position(x + w / 2f, y + h / 2f, 0), PivotPoint = new Position(.5f, .5f, .5f), PositionUsesPivotPoint = true,
   BackgroundColor = new Color(0, 0, 0, 0), CornerRadius = new Vector4(14, 14, 14, 14), BorderlineWidth = 1, BorderlineColor = EdgeIdle };
  var under = new View { Size2D = new Size2D(w - 40, 4), Position2D = new Position2D(20, h + 8), CornerRadius = new Vector4(2, 2, 2, 2),
   BackgroundColor = new Color(200 / 255f, 235 / 255f, 1f, 240 / 255f), Opacity = 0,
   BoxShadow = new Shadow(10f, new Color(80 / 255f, 160 / 255f, 1f, 0.85f), new Vector2(0, 0), new Vector2(2, 2)) };
  card.Add(under); underlines[card] = under;
  Add(card); return card;
 }

 void Legend((string btn, string text)[] items)
 {
  int x = 1824, y = 1012;
  for (int i = items.Length - 1; i >= 0; i--)
  {
   var t = Text(items[i].text, x, y, 0.62f, new Color(220 / 255f, 228 / 255f, 245 / 255f, 1), "right");
   x -= t.Size2D.Width + 46;
   Add(new ImageView { ResourceUrl = Root + "ps2/ui/btn-" + items[i].btn + ".png", Size2D = new Size2D(40, 40), Position2D = new Position2D(x, y - 2) });
   x -= 40;
  }
 }

 // ---------------------------------------------------------------- focus
 void UpdateFocus(bool animate = true)
 {
  focusAnimation?.Stop();
  if (animate) focusAnimation = new Animation(160);
  if (page == 2) { FocusSettings(animate); }
  else
  {
   for (int i = 0; i < slots.Count; i++)
   {
    var s = slots[i]; bool f = i == selected; float sc = f ? 1.10f : 1f;
    var pos = new Position(s.X + s.W / 2f, s.Y + s.H / 2f - (f ? 6 : 0), 0);
    s.Card.BorderlineColor = f ? EdgeFocus : EdgeIdle;
    s.Card.BoxShadow = f ? new Shadow(44f, new Color(70 / 255f, 170 / 255f, 1f, 0.42f), new Vector2(0, 0), new Vector2(0, 0)) : new Shadow(0f, new Color(0, 0, 0, 0), new Vector2(0, 0), new Vector2(0, 0));
    if (underlines.TryGetValue(s.Card, out var u)) { if (animate) focusAnimation.AnimateTo(u, "Opacity", f ? 1f : 0f); else u.Opacity = f ? 1 : 0; }
    if (s.Label != null) s.Label.Tint = f ? White : Soft;
    if (f) s.Card.RaiseToTop();
    if (animate) { focusAnimation.AnimateTo(s.Card, "Scale", new Vector3(sc, sc, 1)); focusAnimation.AnimateTo(s.Card, "Position", pos); }
    else { s.Card.Scale = new Vector3(sc, sc, 1); s.Card.Position = pos; }
   }
   if (slots.Count > 0)
   {
    var cur = slots[selected];
    if (title != null) { title.Set(cur.Name ?? ""); subtitle.Set(cur.Sub ?? ""); }
    if (detail != null) detail.Set(cur.Name + (string.IsNullOrEmpty(cur.Sub) ? "" : "  -  " + cur.Sub));
   }
  }
  focusAnimation?.Play();
  WriteState("focus");
 }

 void FocusSettings(bool animate)
 {
  for (int i = 0; i < slots.Count; i++)
  {
   var s = slots[i]; bool f = i == selected;
   ((Ps2Text)s.Card).Tint = f ? White : RowIdle; s.Label.Tint = f ? White : RowIdle;
   Retext(s.Label, SettingDisplay(s.Action), 1780, "right");
  }
  var y = slots[selected].Y;
  if (animate) focusAnimation.AnimateTo(bar, "PositionY", (float)y); else bar.PositionY = y;
  string id = slots[selected].Action;
  detail.Set(id == "volume" || id == "brightness" ? "Left / Right to change" : id == "reconnect" ? "Resets the HDMI 1 link if the PC shows no signal" : id == "controllers" || id == "network" || id == "advanced" ? "Enter to open" : "Enter to change");
 }

 string SettingDisplay(string id)
 {
  switch (id)
  {
   case "volume": return currentVolume < 0 ? "-" : currentVolume.ToString();
   case "brightness": return SettingValue("brightness") != "" ? SettingValue("brightness") : "-";
   case "game-mode": return SettingName("game-mode");
   case "sleeptimer": return SettingName("sleeptimer");
   case "mute": return currentMute < 0 ? "-" : currentMute == 1 ? "Off" : "On";
   case "music": return prefMusic ? "On" : "Off";
   case "sfx": return prefSfx ? "On" : "Off";
   case "boot": return prefBoot ? "On" : "Off";
   case "reconnect": return reconnectStatus == "" ? ">" : reconnectStatus;
   default: return ">";
  }
 }

 // ---------------------------------------------------------------- input
 void OnKey(object sender, Window.KeyEventArgs e)
 {
  if (e.Key == null || e.Key.State != Key.StateType.Down) return;
  string k = e.Key.KeyPressedName;
  long now = Environment.TickCount64; if (k == lastKey && now - lastKeyAt < 120) return; lastKey = k; lastKeyAt = now;
  try
  {
   if (booting) { if (k == "Return" || k == "KP_Enter" || k == "XF86Back" || k == "Back" || k == "Escape") EndBoot(true); return; }
   if (k == "XF86Home") { GoHome("home_key"); return; }
   if (k == "XF86SysMenu" || k == "XF86More") { GoPage(2, 0); return; }
   bool back = k == "XF86Back" || k == "Back" || k == "Escape" || k == "XF86Exit";
   bool enter = k == "Return" || k == "KP_Enter";
   if (page == 0)
   {
    if (k == "Left" || k == "Right") Move(selected + (k == "Left" ? -1 : 1));
    else if (enter) Activate();
    else if (back) { Sfx(theme.Back); SelectSource(lastSource); }
   }
   else if (page == 1)
   {
    if (back) { Sfx(theme.Back); GoPage(0, homeSelected); return; }
    if (enter) { Activate(); return; }
    if (library.Count == 0) return;
    int next = AppLibrary.Move(library.Count, libraryPage * AppLibrary.PageSize + selected, k);
    if (next < 0) return;
    int nextPage = next / AppLibrary.PageSize, local = next % AppLibrary.PageSize;
    if (nextPage != libraryPage) { libraryPage = nextPage; selected = local; Sfx(theme.Cursor); BuildPage(); }
    else Move(local);
   }
   else
   {
    if (back) { Sfx(theme.Back); GoPage(0, homeSelected); return; }
    if (k == "Up" || k == "Down") Move(selected + (k == "Up" ? -1 : 1));
    else if (k == "Left" || k == "Right") Adjust(k == "Left" ? -1 : 1);
    else if (enter) Activate();
   }
  }
  catch (Exception ex) { ShowError(ex.Message); }
 }

 void Move(int to)
 {
  if (to < 0 || to >= slots.Count || to == selected) return;
  selected = to; Sfx(theme.Cursor); UpdateFocus();
 }

 void GoHome(string why)
 {
  clockTimer?.Start(); RefreshSource();
  if (page != 0) { page = 0; selected = SourceIndex(); BuildPage(); }
  WriteState(why);
 }

 void GoPage(int p, int sel)
 {
  if (page == 0) homeSelected = selected;
  page = p; selected = sel; if (p == 1) libraryPage = 0;
  BuildPage();
 }

 void Activate()
 {
  if (slots.Count == 0) return;
  string a = slots[selected].Action; notice.Set("");
  if (a.StartsWith("hdmi:")) { Sfx(theme.Select); SelectSource(int.Parse(a.Substring(5))); return; }
  if (a.StartsWith("page:")) { Sfx(theme.Select); GoPage(int.Parse(a.Substring(5)), 0); return; }
  if (a.StartsWith("app:")) { Sfx(theme.Select); LaunchAny(a.Substring(4)); return; }
  switch (a)
  {
   case "reconnect": Sfx(theme.Select); ReconnectPc(); return;
   case "controllers": Sfx(theme.Select); Launch("org.tizen.menu", "add-bluetooth-keyboard"); return;
   case "network": Sfx(theme.Select); Launch("org.tizen.menu", "networksettings"); return;
   case "advanced": Sfx(theme.Select); Launch("org.tizen.menu"); return;
   case "volume": case "brightness": Sfx(theme.Select); return;
   default: Adjust(1, true); return;
  }
 }

 void Adjust(int d, bool viaEnter = false)
 {
  string a = slots[selected].Action;
  switch (a)
  {
   case "volume": ChangeVolume(d); break;
   case "brightness": if (!CanEdit("brightness")) { ShowError("Brightness is unavailable for the current input."); return; } ChangeBrightness(d); break;
   case "game-mode": RefreshSettings(); SetSetting(a, SettingValue(a) == "1" ? "0" : "1"); break;
   case "sleeptimer": RefreshSettings(); if (int.TryParse(SettingValue(a), out int n)) SetSetting(a, ((n + (d > 0 ? 1 : 4)) % 5).ToString()); break;
   case "mute": { int r = GetMute(out int m); if (r == 0) r = SetMute(m == 1 ? 0 : 1); if (r != 0) ShowError("Use the remote's mute button."); RefreshSettings(); break; }
   case "music": prefMusic = !prefMusic; SavePrefs(); if (prefMusic) StartAmbience(); else StopAmbience(); break;
   case "sfx": prefSfx = !prefSfx; SavePrefs(); break;
   case "boot": prefBoot = !prefBoot; SavePrefs(); break;
   default: return;
  }
  Sfx(viaEnter ? theme.Select : theme.Cursor);
  UpdateFocus(false);
 }

 // ---------------------------------------------------------------- native actions
 void RefreshSource() { try { if (vconf_get_int("memory/system/source_type", out int n) == 0 && n >= 13 && n <= 16) lastSource = n; } catch { } }
 int SourceIndex()
 {
  for (int i = 0; i < theme.Dock.Count; i++) if (theme.Dock[i].Action == "hdmi:" + lastSource) return i;
  return 0;
 }
 void SelectSource(int source)
 {
  int r = connect_source(source);
  if (r < 0) { ShowError("Input could not be switched."); WriteState("source_failed:" + r); return; }
  lastSource = source; Launch("org.tizen.tv-viewer");
 }
 void LaunchAny(string ids)
 {
  foreach (var id in ids.Split('|')) if (Launch(id, null, false) == 0) return;
  ShowError("Could not open the app.");
 }
 int Launch(string id, string menuItem = null, bool report = true)
 {
  int r = app_control_create(out IntPtr c); if (r != 0) { if (report) ShowError("Could not open the app."); return r; }
  try
  {
   r = app_control_set_app_id(c, id);
   if (r == 0) r = app_control_set_operation(c, "http://tizen.org/appcontrol/operation/default");
   if (r == 0 && id == "org.tizen.menu")
   {
    if (menuItem != null) { r = app_control_add_extra_data(c, "menuitem", menuItem); if (r == 0) r = app_control_add_extra_data(c, "launchoption", "pressed"); }
    if (r == 0) r = app_control_add_extra_data(c, "__K_EX_FORCE_BOOST", "enable");
    if (r == 0) r = app_control_add_extra_data(c, "__K_SPLASH_SCREEN", "LA_UI_VR_TRANSPARENT");
   }
   if (r == 0) r = app_control_send_launch_request(c, IntPtr.Zero, IntPtr.Zero);
   lastLaunch = id + (menuItem == null ? "" : ":" + menuItem) + ":" + r; WriteState("launch");
   if (r != 0 && report) ShowError("Could not open this screen.");
   return r;
  }
  finally { app_control_destroy(c); }
 }

 // Input Signal Plus is the settings item "uhd-color-hdmiN" (its old name, HDMI UHD Color).
 // Switching it off and on again makes the TV and the PC redo the HDMI handshake from scratch.
 string reconnectStatus = "";
 void ReconnectPc()
 {
  const string item = "uhd-color-hdmi1";
  string before = "";
  try { CSharpGetItemValue(item, out _, out before); } catch { }
  bool off = false; try { off = CSharpSetItemValue(item, "0"); } catch { }
  reconnectStatus = "Reconnecting PC..."; UpdateFocus(false); WriteState("reconnect_off:" + off + ":was=" + before);
  Defer(2500, () =>
  {
   bool on = false; try { on = CSharpSetItemValue(item, "1"); } catch { }
   string after = ""; try { CSharpGetItemValue(item, out _, out after); } catch { }
   reconnectStatus = on && after == "1" ? "Done - PC link reset" : "Could not reset the PC link";
   UpdateFocus(false); WriteState("reconnect_on:" + on + ":now=" + after);
   Defer(4000, () => { reconnectStatus = ""; if (page == 2) UpdateFocus(false); });
  });
 }

 void RefreshSettings()
 {
  currentVolume = GetVolume(out int v) == 0 ? v : -1;
  currentMute = GetMute(out int m) == 0 ? m : -1;
  foreach (var id in new[] { "brightness", "game-mode", "sleeptimer" })
  {
   try
   {
    if (CSharpGetItemValue(id, out var name, out var value) && !string.IsNullOrEmpty(value)) { settingValues[id] = value; settingNames[id] = name; }
    else { settingValues.Remove(id); settingNames.Remove(id); }
    settingEnabled[id] = CSharpGetItemEnable(id, out var enabled) && enabled;
   }
   catch { settingValues.Remove(id); settingNames.Remove(id); settingEnabled[id] = false; }
  }
  try { if (!CSharpGetItemMinMaxStepValue("brightness", out brightnessMin, out brightnessMax, out brightnessStep) || brightnessStep <= 0) { brightnessMin = 0; brightnessMax = 50; brightnessStep = 1; } } catch { }
 }
 string SettingValue(string id) => settingValues.TryGetValue(id, out var v) ? v : "";
 string SettingName(string id) => settingNames.TryGetValue(id, out var v) ? v : "-";
 bool CanEdit(string id) => settingEnabled.TryGetValue(id, out var e) && e && settingValues.ContainsKey(id);
 void SetSetting(string id, string value)
 {
  if (!CanEdit(id)) { ShowError("This setting is unavailable for the current input."); return; }
  bool ok = false; try { ok = CSharpSetItemValue(id, value); } catch { }
  RefreshSettings();
  if (!(ok && SettingValue(id) == value)) ShowError("The TV could not apply this setting.");
 }
 void ChangeBrightness(int d)
 {
  RefreshSettings();
  if (double.TryParse(SettingValue("brightness"), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
   SetSetting("brightness", Math.Max(brightnessMin, Math.Min(brightnessMax, n + d * brightnessStep)).ToString(CultureInfo.InvariantCulture));
 }
 void ChangeVolume(int d)
 {
  int r = GetVolume(out int n);
  if (r == 0) r = SetVolume(Math.Max(0, Math.Min(100, n + d)));
  if (r != 0) ShowError("Use the remote's volume buttons.");
  RefreshSettings();
 }

 // ---------------------------------------------------------------- prefs + diagnostics
 void LoadPrefs()
 {
  try { prefsPath = System.IO.Path.Combine(this.DirectoryInfo.Data, "ps2-prefs.txt"); } catch { prefsPath = "/tmp/quiet-home-prefs.txt"; }
  try
  {
   foreach (var line in File.ReadAllLines(prefsPath))
   {
    var p = line.Split('='); if (p.Length != 2) continue;
    bool on = p[1].Trim() == "1";
    if (p[0] == "music") prefMusic = on; else if (p[0] == "sfx") prefSfx = on; else if (p[0] == "boot") prefBoot = on;
   }
  }
  catch { }
 }
 void SavePrefs()
 {
  try { File.WriteAllText(prefsPath, "music=" + (prefMusic ? 1 : 0) + "\nsfx=" + (prefSfx ? 1 : 0) + "\nboot=" + (prefBoot ? 1 : 0) + "\n"); }
  catch (Exception e) { ShowError("Could not save: " + e.Message); }
 }
 static Color Hex(string hex)
 {
  try { int n = Convert.ToInt32(hex.TrimStart('#'), 16); return new Color(((n >> 16) & 255) / 255f, ((n >> 8) & 255) / 255f, (n & 255) / 255f, 1); }
  catch { return new Color(0, 0, 0, 1); }
 }
 static string WindowInfo()
 {
  try { var w = Window.Default.WindowSize; var sc = NUIApplication.GetScreenSize(); return w.Width + "x" + w.Height + " screen=" + sc.Width + "x" + sc.Height + " dpi=" + Window.Default.Dpi.X; }
  catch (Exception e) { return "error:" + e.GetType().Name; }
 }
 void ShowError(string text) { notice?.Set(text); WriteState("error:" + text); }
 void WriteState(string activity)
 {
  try
  {
   File.WriteAllText(StateFile, DateTime.UtcNow.ToString("o") + "\nactivity=" + activity + "\npage=" + page + "\nselected=" + selected
    + "\nselected_name=" + (selected >= 0 && selected < slots.Count ? slots[selected].Name : "") + "\nkey=" + lastKey + "\nbooting=" + booting
    + "\nunderlay=" + underlay + "\nvideo=" + videoStatus + "\nsound=" + Ps2Sound.Status + "\nambience_id=" + ambienceId
    + "\nprefs=" + prefsPath + " music=" + prefMusic + " sfx=" + prefSfx + " boot=" + prefBoot + "\ntheme_error=" + themeError
    + "\nlibrary_count=" + library.Count + "\nlibrary_error=" + libraryError + "\nlast_launch=" + lastLaunch + "\nsource=" + lastSource
    + "\nwindow=" + WindowInfo() + "\nvolume=" + currentVolume + "\nvolume_api=" + volumeApi + "\nmute=" + currentMute + "\nbuild_ms=" + buildMs + "\nprocess_id=" + Process.GetCurrentProcess().Id + "\n");
  }
  catch { }
 }
}
