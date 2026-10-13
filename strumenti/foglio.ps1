param([string]$Dir = "C:\Progetti\Missiva\prova-dati\lampi", [string]$Out = "C:\Progetti\Missiva\prova-dati\lampi-foglio.png", [string]$Filter = "*.png", [int]$Cols = 4, [double]$Scale = 0.3)
# Mette i fotogrammi di lampi.ps1 in un solo foglio, rimpiccioliti, con il nome sopra ciascuno.
Add-Type -AssemblyName System.Drawing
$files = Get-ChildItem $Dir -Filter $Filter | Sort-Object Name
$first = [System.Drawing.Image]::FromFile($files[0].FullName)
$tw = [int]($first.Width * $Scale); $th = [int]($first.Height * $Scale); $first.Dispose()
$rows = [math]::Ceiling($files.Count / $Cols)
$sheet = New-Object System.Drawing.Bitmap ($Cols * ($tw + 8)), ($rows * ($th + 30))
$g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear([System.Drawing.Color]::DimGray)
$font = New-Object System.Drawing.Font "Segoe UI", 11
for ($i = 0; $i -lt $files.Count; $i++) {
  $img = [System.Drawing.Image]::FromFile($files[$i].FullName)
  $x = ($i % $Cols) * ($tw + 8); $y = [math]::Floor($i / $Cols) * ($th + 30)
  $g.DrawString($files[$i].BaseName, $font, [System.Drawing.Brushes]::White, $x + 2, $y + 4)
  $g.DrawImage($img, $x, $y + 26, $tw, $th); $img.Dispose()
}
$sheet.Save($Out); $g.Dispose(); $sheet.Dispose()
"foglio: $Out ($($files.Count) fotogrammi)"
