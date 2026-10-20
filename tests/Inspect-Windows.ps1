param([string]$ProcessName = 'CornerTodo')
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public static class TodoWindowDiagnostics {
 public delegate bool Callback(IntPtr h, IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback c, IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, Callback c, IntPtr d);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h,int n);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,int a,out int v,int s);
 public struct Rect {public int Left,Top,Right,Bottom;}
 static HashSet<IntPtr> seen = new HashSet<IntPtr>();
 static List<string> result = new List<string>();
 static uint target;
 public static string[] Inspect(uint p) {
  target=p; seen.Clear(); result.Clear();
  EnumWindows(delegate(IntPtr h,IntPtr x) { InspectOne(h); EnumChildWindows(h,delegate(IntPtr ch,IntPtr y){ InspectOne(ch); return true;},IntPtr.Zero); return true;},IntPtr.Zero);
  return result.ToArray();
 }
 static void InspectOne(IntPtr h) {
  uint p; GetWindowThreadProcessId(h,out p); if(p!=target || !seen.Add(h))return;
  var cls=new StringBuilder(260);var title=new StringBuilder(512); GetClassName(h,cls,cls.Capacity);GetWindowText(h,title,title.Capacity);
  Rect r;GetWindowRect(h,out r);int cloak;DwmGetWindowAttribute(h,14,out cloak,4);
  var parent=GetParent(h);var pc=new StringBuilder(260);GetClassName(parent,pc,pc.Capacity);
  result.Add(String.Format("hwnd={0} parent={1} ({2}) visible={3} style=0x{4:X} exstyle=0x{5:X} cloak={6} rect={7},{8},{9},{10} class={11} title={12}",h,parent,pc,IsWindowVisible(h),GetWindowLongPtr(h,-16).ToInt64(),GetWindowLongPtr(h,-20).ToInt64(),cloak,r.Left,r.Top,r.Right,r.Bottom,cls,title));
 }
}
'@
Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | ForEach-Object {
    'Process {0} {1}' -f $_.Id,$_.Path
    [TodoWindowDiagnostics]::Inspect([uint32]$_.Id)
}
