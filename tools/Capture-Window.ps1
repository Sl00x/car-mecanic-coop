param(
  [string]$Exe = "C:\Users\alexu\Documents\Projects\CarMecaSim\launcher\bin\Release\net8.0-windows\CarMecaSim.Launcher.exe",
  [string]$Out = "C:\Users\alexu\AppData\Local\Temp\claude\C--Users-alexu-Documents-Projects-CarMecaSim\c52244c9-e0bb-442d-9287-f9b0eca3b7a0\scratchpad\launcher.png"
)
Add-Type -ReferencedAssemblies System.Drawing @"
using System; using System.Runtime.InteropServices; using System.Drawing;
public class Cap {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  public struct RECT { public int Left, Top, Right, Bottom; }
  public static void Shot(IntPtr h, string path) {
    RECT r; GetWindowRect(h, out r);
    int w = r.Right - r.Left, ht = r.Bottom - r.Top;
    using (var bmp = new Bitmap(w, ht))
    using (var g = Graphics.FromImage(bmp)) {
      IntPtr hdc = g.GetHdc();
      PrintWindow(h, hdc, 2); // PW_RENDERFULLCONTENT
      g.ReleaseHdc(hdc);
      bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
  }
}
"@
$p = Start-Process $Exe -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 30; $i++) { Start-Sleep -Milliseconds 300; $p.Refresh(); if ($p.MainWindowHandle -ne 0) { $h = $p.MainWindowHandle; break } }
if ($h -eq [IntPtr]::Zero) { Write-Output "no window handle"; exit 1 }
[Cap]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 800
[Cap]::Shot($h, $Out)
Start-Sleep -Milliseconds 200
Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Output "saved $Out ($((Get-Item $Out).Length) bytes)"
