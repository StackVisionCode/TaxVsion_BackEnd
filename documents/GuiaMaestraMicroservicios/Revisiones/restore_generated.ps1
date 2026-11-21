# Devuelve los generados sin cambio real a los bytes exactos del checkout y a las fechas del índice de git, para
# que `git status` no los marque. Prueba el blob tal cual (checkout en LF) y convertido a CRLF, según cuál
# coincida con el tamaño que guarda el índice. No toca el índice ni el historial.
param([string]$Repo, [string]$Dir, [string[]]$Keep = @())

Set-Location $Repo
$epoch = [DateTime]::new(1970, 1, 1, 0, 0, 0, [DateTimeKind]::Utc)
$restored = 0; $skipped = @()
$files = git ls-files -m -- $Dir
foreach ($f in $files) {
  if ($Keep | Where-Object { $f -like "*$_" }) { continue }
  $debug = git ls-files --debug -- $f
  $ctime = ($debug | Select-String 'ctime: (\d+):(\d+)').Matches[0]
  $mtime = ($debug | Select-String 'mtime: (\d+):(\d+)').Matches[0]
  $size = [int64]($debug | Select-String 'size: (\d+)').Matches[0].Groups[1].Value

  $psi = [System.Diagnostics.ProcessStartInfo]::new('git', "show `"HEAD:$f`"")
  $psi.RedirectStandardOutput = $true; $psi.UseShellExecute = $false; $psi.WorkingDirectory = $Repo
  $p = [System.Diagnostics.Process]::Start($psi)
  $ms = [System.IO.MemoryStream]::new(); $p.StandardOutput.BaseStream.CopyTo($ms); $p.WaitForExit()
  $blob = $ms.ToArray()

  if ($blob.Length -eq $size) {
    $bytes = $blob
  }
  else {
    $out = [System.Collections.Generic.List[byte]]::new($blob.Length + 256)
    for ($i = 0; $i -lt $blob.Length; $i++) {
      if ($blob[$i] -eq 10 -and ($i -eq 0 -or $blob[$i - 1] -ne 13)) { $out.Add(13) }
      $out.Add($blob[$i])
    }
    if ($out.Count -ne $size) { $skipped += "$f (esperado $size, blob $($blob.Length), crlf $($out.Count))"; continue }
    $bytes = $out.ToArray()
  }

  $full = Join-Path $Repo $f
  [System.IO.File]::WriteAllBytes($full, $bytes)
  $item = Get-Item $full
  $item.CreationTimeUtc = $epoch.AddSeconds([int64]$ctime.Groups[1].Value).AddTicks([int64]$ctime.Groups[2].Value / 100)
  $item.LastWriteTimeUtc = $epoch.AddSeconds([int64]$mtime.Groups[1].Value).AddTicks([int64]$mtime.Groups[2].Value / 100)
  $restored++
}
"restaurados: $restored"
if ($skipped.Count) { "omitidos:"; $skipped }
