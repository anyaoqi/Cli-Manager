$cases = [ordered]@{
  'A-exact-menu-string'  = 'echo PCT1=100%% & echo PCT2=D:\100%\dir\tool.exe & echo PCT3=%WINDIR%'
  'B-windir-only'        = 'echo X=%WINDIR%'
  'C-100pct-then-windir' = 'echo A=100% & echo B=%WINDIR%'
  'D-undefined-span'     = 'echo P=%NOSUCHVAR123% & echo Q=100%\x'
  'E-100pct-undef'       = 'echo A=100% & echo B=%NOSUCHVAR123%'
  'F-single-lone-pct'    = 'echo Z=D:\100%\dir\tool.exe'
}
foreach ($k in $cases.Keys) {
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = 'cmd.exe'
  $psi.Arguments = '/d /c ' + $cases[$k]
  $psi.UseShellExecute = $false
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $p = [System.Diagnostics.Process]::Start($psi)
  $out = $p.StandardOutput.ReadToEnd()
  $err = $p.StandardError.ReadToEnd()
  $p.WaitForExit()
  Write-Output ("=== " + $k + " ===")
  Write-Output ("IN : " + $cases[$k])
  Write-Output ("OUT: " + $out.Trim())
  if ($err.Trim()) { Write-Output ("ERR: " + $err.Trim()) }
  Write-Output ""
}
