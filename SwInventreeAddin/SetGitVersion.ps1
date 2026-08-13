$desc = git describe --tags --always 2>$null
if ([string]::IsNullOrEmpty($desc)) { $desc = 'unknown' }

if ($desc -match '^v?(\d+)\.(\d+)\.(\d+)(?:-(\d+))?') {
    $revision = if ($matches[4]) { $matches[4] } else { 0 }
    "$($matches[1]).$($matches[2]).$($matches[3]).$revision"
} else {
    '0.0.0.0'
}
