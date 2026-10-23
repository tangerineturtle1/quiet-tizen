using System;
using System.Runtime.InteropServices;

// Menu sound effects and the looping menu ambience through Tizen's native wav player.
// Every call is guarded: if a library or symbol is missing on this firmware the launcher stays silent
// and records why in its state file instead of crashing.
static class Ps2Sound
{
 delegate void FocusChanged(IntPtr info, int mask, int state, int reason, int behavior, IntPtr extra, IntPtr data);
 [DllImport("libcapi-media-sound-manager.so.0")] static extern int sound_manager_create_stream_information(int type, FocusChanged callback, IntPtr data, out IntPtr info);
 [DllImport("libcapi-media-wav-player.so.0")] static extern int wav_player_start_new(string path, IntPtr info, IntPtr callback, IntPtr data, out int id);
 [DllImport("libcapi-media-wav-player.so.0")] static extern int wav_player_start_loop(string path, IntPtr info, uint count, IntPtr callback, IntPtr data, out int id);
 [DllImport("libcapi-media-wav-player.so.0")] static extern int wav_player_stop(int id);
 const int StreamMedia = 0;
 static readonly FocusChanged keepAlive = (a, b, c, d, e, f, g) => { };
 static IntPtr info;
 static bool failed;
 public static string Status = "idle";

 static bool Ready()
 {
  if (info != IntPtr.Zero) return true;
  if (failed) return false;
  try
  {
   int r = sound_manager_create_stream_information(StreamMedia, keepAlive, IntPtr.Zero, out info);
   Status = "stream:" + r;
   if (r != 0) { info = IntPtr.Zero; failed = true; }
  }
  catch (Exception e) { Status = "stream_error:" + e.GetType().Name; failed = true; }
  return info != IntPtr.Zero;
 }

 public static int Play(string path)
 {
  if (!Ready()) return -1;
  try { int r = wav_player_start_new(path, info, IntPtr.Zero, IntPtr.Zero, out int id); Status = "play:" + r; return r == 0 ? id : -1; }
  catch (Exception e) { Status = "play_error:" + e.GetType().Name; return -1; }
 }

 public static int Loop(string path)
 {
  if (!Ready()) return -1;
  try { int r = wav_player_start_loop(path, info, 0, IntPtr.Zero, IntPtr.Zero, out int id); Status = "loop:" + r; return r == 0 ? id : -1; }
  catch (Exception e) { Status = "loop_error:" + e.GetType().Name; return -1; }
 }

 public static void Stop(int id)
 {
  if (id < 0) return;
  try { wav_player_stop(id); } catch { }
 }
}
